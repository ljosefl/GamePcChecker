using System.Management;
using System.Text.RegularExpressions;

namespace GamePcChecker.App.Services;

/// <summary>Определяет тип носителя (NVMe / SSD / HDD) по букве тома через CIM и запасной путь Win32_DiskDrive.</summary>
public static class DriveMediaProbeService
{
    // MSFT_PhysicalDisk: https://docs.microsoft.com/previous-versions/windows/desktop/storports/msft-physicaldisk
    private const uint BusTypeNvme = 17;
    private const uint MediaTypeHdd = 3;
    private const uint MediaTypeSsd = 4;

    /// <summary>
    /// По имени/модели без слова «SSD»: типичные коды SATA SSD (Kingston SHFS…, A400…), Samsung MZ-, WD SN и т.д.
    /// Win32_DiskDrive.MediaType для SSD часто такой же «Fixed hard disk media», как у HDD — одного fixed недостаточно.
    /// </summary>
    private static bool LooksLikeSsdModel(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Solid State", StringComparison.OrdinalIgnoreCase))
            return true;

        // Модельные ряды без подстроки "SSD" в строке
        return SsdModelHintRx.IsMatch(name);
    }

    private static readonly Regex SsdModelHintRx = new(
        @"\b(" +
        @"SHFS|SKC|SKH|SA400|SUV[0-9]|SV300|SNV|SFY|KC[0-9]{4}|" + // Kingston и др.
        @"MZ[-]?[VNM]|PM[89]|SM[-]?[0-9]|Samsung\s+SSD|" +       // Samsung
        @"WDS\d|WD[_ ]?Blue\s+SN|WD_BLACK\s+SN|" +               // WD NVMe/SATA SSD
        @"CT[0-9]{3,4}|BX\d{3}|MX\d{3}|" +                     // Crucial
        @"SSDSC[WK]|SSDPE|Intel\s+SSD" +                      // Intel
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    public static (string Label, bool IsSsdClass, bool IsNvme) DescribeDrive(char driveLetter)
    {
        var root = char.ToUpperInvariant(driveLetter);
        if (root is < 'A' or > 'Z')
            return ("неизвестно", false, false);

        var diskIndex = TryGetDiskIndexForVolume(root);
        if (diskIndex is not { } idx)
            return ("не определено (нет WMI)", false, false);

        var msftOk = TryFromMsftPhysicalDisk(idx, out var mLabel, out var mSsd, out var mNvme);
        var winOk = TryFromWin32DiskDrive(idx, out var wLabel, out var wSsd, out var wNvme);

        if (!msftOk && !winOk)
            return ("не определено (нет WMI)", false, false);

        var nvme = (msftOk && mNvme) || (winOk && wNvme);
        var ssdClass = nvme || (msftOk && mSsd) || (winOk && wSsd);

        var label = PickMergedLabel(msftOk, mLabel, winOk, wLabel, ssdClass);
        return (label, ssdClass, nvme);
    }

    private static string PickMergedLabel(
        bool msftOk,
        string mLabel,
        bool winOk,
        string wLabel,
        bool ssdClass)
    {
        if (ssdClass && msftOk && mLabel.Contains("HDD", StringComparison.OrdinalIgnoreCase) && winOk &&
            !string.IsNullOrWhiteSpace(wLabel))
            return wLabel;

        if (msftOk && !string.IsNullOrWhiteSpace(mLabel))
            return mLabel;
        if (winOk && !string.IsNullOrWhiteSpace(wLabel))
            return wLabel;
        return msftOk ? mLabel : wLabel;
    }

    private static uint? TryGetDiskIndexForVolume(char letter)
    {
        var id = $"{letter}:";
        try
        {
            using var s = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{id}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");
            foreach (ManagementObject part in s.Get())
            {
                try
                {
                    return Convert.ToUInt32(part["DiskIndex"] ?? 0u);
                }
                finally
                {
                    part.Dispose();
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static bool TryFromMsftPhysicalDisk(
        uint diskIndex,
        out string label,
        out bool isSsdClass,
        out bool isNvme)
    {
        label = "";
        isSsdClass = false;
        isNvme = false;

        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            using var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk"));
            foreach (ManagementObject mo in s.Get())
            {
                try
                {
                    var num = ReadUInt(mo, "DeviceNumber");
                    if (num != diskIndex)
                        continue;

                    var bus = ReadUInt(mo, "BusType");
                    var media = ReadUInt(mo, "MediaType");
                    var friendly = mo["FriendlyName"]?.ToString() ?? "";

                    isNvme = bus == BusTypeNvme;
                    var isHdd = media == MediaTypeHdd;
                    // MediaType «не задан» (0): раньше ошибочно не считали SSD; явный HDD только при MediaTypeHdd.
                    // Дополнительно — по FriendlyName (напр. KINGSTON SHFS… без слова SSD).
                    isSsdClass = isNvme ||
                                 media == MediaTypeSsd ||
                                 (!isHdd && LooksLikeSsdModel(friendly));

                    var treatAsHdd = isHdd;
                    label = BuildLabel(bus, media, friendly, isNvme, treatAsHdd, isSsdClass);
                    return true;
                }
                finally
                {
                    mo.Dispose();
                }
            }
        }
        catch
        {
            // Storage CIM может быть недоступен
        }

        return false;
    }

    private static bool TryFromWin32DiskDrive(
        uint diskIndex,
        out string label,
        out bool isSsdClass,
        out bool isNvme)
    {
        label = "";
        isSsdClass = false;
        isNvme = false;

        try
        {
            using var s = new ManagementObjectSearcher($"SELECT * FROM Win32_DiskDrive WHERE Index = {diskIndex}");
            foreach (ManagementObject dd in s.Get())
            {
                try
                {

                    var model = dd["Model"]?.ToString() ?? "";
                    var iface = dd["InterfaceType"]?.ToString() ?? "";
                    var media = dd["MediaType"]?.ToString() ?? "";

                    isNvme = model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
                             || iface.Equals("SCSI", StringComparison.OrdinalIgnoreCase) &&
                             (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                              model.Contains("NVME", StringComparison.OrdinalIgnoreCase));

                    var looksSsd = media.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                                   || model.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                                   || model.Contains("Solid State", StringComparison.OrdinalIgnoreCase)
                                   || LooksLikeSsdModel(model);

                    isSsdClass = isNvme || looksSsd;
                    // «Fixed hard disk media» бывает у SSD и HDD — HDD только по явным признакам.
                    var looksHdd = !isNvme && !looksSsd &&
                                   (media.Contains("Rotating", StringComparison.OrdinalIgnoreCase)
                                    || media.Contains("HDD", StringComparison.OrdinalIgnoreCase));

                    label = isNvme
                        ? "NVMe SSD"
                        : looksSsd
                            ? "SSD"
                            : looksHdd
                                ? "HDD"
                                : $"{iface} · {model}".Trim();
                    return true;
                }
                finally
                {
                    dd.Dispose();
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static string BuildLabel(
        uint bus,
        uint media,
        string friendly,
        bool isNvme,
        bool isHdd,
        bool isSsdClass)
    {
        string busName = bus switch
        {
            7 => "SATA",
            11 => "USB",
            12 => "SAS",
            17 => "NVMe",
            _ => $"шина {bus}",
        };

        string mediaName = media switch
        {
            MediaTypeHdd => "HDD",
            MediaTypeSsd => "SSD",
            _ => "накопитель",
        };

        if (isNvme)
            return string.IsNullOrEmpty(friendly) ? "NVMe SSD" : $"NVMe · {friendly}";

        if (isSsdClass && !isHdd)
            return string.IsNullOrEmpty(friendly) ? $"{busName} SSD" : $"{busName} SSD · {friendly}";

        if (isHdd)
            return string.IsNullOrEmpty(friendly) ? "HDD" : $"HDD · {friendly}";

        return string.IsNullOrEmpty(friendly) ? $"{busName} · {mediaName}" : $"{busName} · {friendly}";
    }

    private static uint ReadUInt(ManagementObject mo, string name)
    {
        var o = mo[name];
        if (o == null)
            return 0;
        return Convert.ToUInt32(o);
    }
}
