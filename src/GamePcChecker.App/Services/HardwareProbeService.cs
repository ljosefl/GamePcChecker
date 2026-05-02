using System.Management;
using GamePcChecker.App.Models;
using Vortice.DXGI;

namespace GamePcChecker.App.Services;

public sealed class HardwareProbeService
{
    private const string DxgiSource = "DXGI";
    private const string WmiSource = "WMI";

    public MachineSnapshot Probe()
    {
        var osCaption = QueryScalar("Win32_OperatingSystem", "Caption") ?? "Windows";
        var ramGb = QueryRamGb();

        var cpuName = "";
        var cores = 0;
        var logical = 0;
        var mhz = 0;
        using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
        {
            foreach (ManagementObject mo in searcher.Get())
            {
                cpuName = mo["Name"]?.ToString() ?? "";
                cores = Convert.ToInt32(mo["NumberOfCores"] ?? 0);
                logical = Convert.ToInt32(mo["NumberOfLogicalProcessors"] ?? 0);
                mhz = Convert.ToInt32(mo["MaxClockSpeed"] ?? 0);
                break;
            }
        }

        var dxgi = BuildDxgiList();
        var wmiGpus = BuildWmiGpuList();
        var mergedGpus = MergeGpus(dxgi, wmiGpus);

        var d3dProbe = GraphicsFeatureProbe.DescribeMaxD3D11FeatureLevel();
        var vkProbe = GraphicsFeatureProbe.DescribeVulkanLoaderPresence();
        var graphicsRuntimeSummary =
            $"Графика: максимальный feature level (проба Direct3D 11): {d3dProbe}. {vkProbe}. " +
            "Объём видеопамяти дискретной карты берётся из DXGI при сопоставлении с WMI — одинаково для NVIDIA, AMD и Intel (Arc / дискрет). " +
            "Игры на DirectX 9–11 используют совместимость через уровни feature level; Vulkan — отдельный API (проверка наличия загрузчика).";

        var runtimePrereqs = RuntimeProbeService.Probe();
        var systemHealth = SystemHealthProbeService.BuildSummary(ramGb);

        var disks = new List<DiskVolume>();
        using (var s = new ManagementObjectSearcher("SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3"))
        {
            foreach (ManagementObject mo in s.Get())
            {
                var id = mo["DeviceID"]?.ToString() ?? "";
                var size = ToUlong(mo["Size"]);
                var free = ToUlong(mo["FreeSpace"]);
                if (size == 0)
                    continue;
                disks.Add(new DiskVolume(
                    id.TrimEnd('\\'),
                    Math.Round(size / 1_073_741_824.0, 1),
                    Math.Round(free / 1_073_741_824.0, 1)));
            }
        }

        return new MachineSnapshot(
            osCaption,
            cpuName,
            cores,
            logical,
            mhz,
            ramGb,
            mergedGpus,
            disks,
            graphicsRuntimeSummary,
            runtimePrereqs,
            systemHealth,
            GameInstall: null,
            QuickGpuStress: null);
    }

    private static IReadOnlyList<GpuCard> MergeGpus(
        IReadOnlyList<DxgiEntry> dxgi,
        IReadOnlyList<WmiGpu> wmi)
    {
        if (wmi.Count == 0)
            return [];

        var dxSorted = dxgi.OrderByDescending(x => x.DedicatedVideoMemory).ToList();
        var wmiSorted = wmi.OrderByDescending(x => x.AdapterRamBytes).ToList();

        var result = new List<GpuCard>(wmiSorted.Count);
        var usedDx = new bool[dxSorted.Count];
        for (var i = 0; i < wmiSorted.Count; i++)
        {
            var w = wmiSorted[i];
            double vramGb;
            string source;

            var match = FindDxgiForWmi(dxSorted, usedDx, w.Name, i);
            string vendorName;
            if (match != null)
            {
                vramGb = Math.Round(match.DedicatedVideoMemory / 1_073_741_824.0, 2);
                source = DxgiSource;
                vendorName = VendorIdToName(match.VendorId);
            }
            else
            {
                vramGb = Math.Round(w.AdapterRamBytes / 1_073_741_824.0, 2);
                source = WmiSource;
                vendorName = InferVendorFromAdapterName(w.Name);
            }

            result.Add(new GpuCard(w.Name, vramGb, w.DriverVersion, source, vendorName));
        }

        result.Sort((a, b) => b.VramGb.CompareTo(a.VramGb));
        return result;
    }

    private static DxgiEntry? FindDxgiForWmi(
        List<DxgiEntry> dxSorted,
        bool[] usedDx,
        string wmiName,
        int preferIndex)
    {
        if (dxSorted.Count == 0)
            return null;

        for (var j = 0; j < dxSorted.Count; j++)
        {
            if (usedDx[j])
                continue;
            if (NameLikelyMatch(wmiName, dxSorted[j].Description))
            {
                usedDx[j] = true;
                return dxSorted[j];
            }
        }

        if (preferIndex < dxSorted.Count && !usedDx[preferIndex])
        {
            usedDx[preferIndex] = true;
            return dxSorted[preferIndex];
        }

        for (var j = 0; j < dxSorted.Count; j++)
        {
            if (!usedDx[j])
            {
                usedDx[j] = true;
                return dxSorted[j];
            }
        }

        return null;
    }

    private static bool NameLikelyMatch(string wmiName, string dxgiDescription)
    {
        var a = Normalize(wmiName).ToLowerInvariant();
        var b = Normalize(dxgiDescription).ToLowerInvariant();
        if (a.Length == 0 || b.Length == 0)
            return false;
        if (b.Contains(a, StringComparison.Ordinal) || a.Contains(b, StringComparison.Ordinal))
            return true;

        // Одинаковая модель в стиле "rx 580" / "radeon rx 580 series"
        foreach (var model in new[] { "rx 580", "rx 570", "rx 560", "rx 590", "rtx ", "gtx ", "arc a", "intel arc" })
        {
            if (!a.Contains(model, StringComparison.Ordinal))
                continue;
            if (b.Contains(model, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Normalize(string s)
    {
        return string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private sealed record DxgiEntry(string Description, ulong DedicatedVideoMemory, uint VendorId);

    private static string VendorIdToName(uint vendorId) =>
        vendorId switch
        {
            0x10DE => "NVIDIA",
            0x1002 => "AMD",
            0x1022 => "AMD",
            0x8086 => "Intel",
            0x1A03 => "ASPEED",
            0x1414 => "Microsoft",
            _ => $"PCI {vendorId:X4}",
        };

    private static string InferVendorFromAdapterName(string adapterName)
    {
        var n = adapterName.ToLowerInvariant();
        if (n.Contains("nvidia", StringComparison.Ordinal) || n.Contains("geforce", StringComparison.Ordinal)
            || n.Contains("rtx", StringComparison.Ordinal) || n.Contains("gtx", StringComparison.Ordinal))
            return "NVIDIA (по имени устройства)";
        if (n.Contains("radeon", StringComparison.Ordinal) || n.Contains("rx ", StringComparison.Ordinal))
            return "AMD (по имени устройства)";
        if (n.Contains("intel", StringComparison.Ordinal) || n.Contains("arc", StringComparison.Ordinal))
            return "Intel (по имени устройства)";
        return "не определён";
    }

    private sealed record WmiGpu(string Name, ulong AdapterRamBytes, string DriverVersion);

    private static List<DxgiEntry> BuildDxgiList()
    {
        var list = new List<DxgiEntry>();
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; ; i++)
            {
                var hr = factory.EnumAdapters1(i, out var adapter);
                if (hr.Failure)
                    break;

                using (adapter)
                {
                    var d = adapter.Description1;
                    if (d.Flags.HasFlag(AdapterFlags.Software))
                        continue;
                    if (d.VendorId == 0x1414)
                        continue;

                    var desc = d.Description.TrimEnd('\0').Trim();
                    if (desc.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                        continue;

                    list.Add(new DxgiEntry(desc, d.DedicatedVideoMemory, d.VendorId));
                }
            }
        }
        catch
        {
            // DXGI недоступен — останется только WMI
        }

        return list;
    }

    private static List<WmiGpu> BuildWmiGpuList()
    {
        var list = new List<WmiGpu>();
        using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");
        foreach (ManagementObject mo in searcher.Get())
        {
            var name = mo["Name"]?.ToString() ?? "";
            if (IsIgnoredAdapter(name))
                continue;

            var ramObj = mo["AdapterRAM"];
            ulong ram = 0;
            if (ramObj != null)
            {
                try
                {
                    ram = Convert.ToUInt64(ramObj);
                }
                catch
                {
                    ram = (ulong)(uint)(ramObj);
                }
            }

            list.Add(new WmiGpu(name, ram, mo["DriverVersion"]?.ToString() ?? ""));
        }

        return list;
    }

    private static bool IsIgnoredAdapter(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("microsoft basic render")
               || n.Contains("microsoft basic display")
               || (n.Contains("parsec", StringComparison.Ordinal) && n.Contains("virtual", StringComparison.Ordinal));
    }

    private static double QueryRamGb()
    {
        using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
        foreach (ManagementObject mo in searcher.Get())
        {
            var bytes = ToUlong(mo["TotalPhysicalMemory"]);
            if (bytes > 0)
                return Math.Round(bytes / 1_073_741_824.0, 2);
        }

        return 0;
    }

    private static string? QueryScalar(string wmiClass, string property)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
        foreach (ManagementObject mo in searcher.Get())
            return mo[property]?.ToString();
        return null;
    }

    private static ulong ToUlong(object? o)
    {
        if (o == null)
            return 0;
        try
        {
            return Convert.ToUInt64(o);
        }
        catch
        {
            return (ulong)Convert.ToInt64(o);
        }
    }
}
