using LibreHardwareMonitor.Hardware;

namespace GamePcChecker.App.Services;

/// <summary>
/// Чтение датчиков через LibreHardwareMonitor (CPU/GPU температура, мощность GPU при наличии).
/// Потребление блока питания из ОС не доступно — только то, что отдаёт драйвер/EC.
/// </summary>
public sealed class LibreHardwareSensorService : IDisposable
{
    private readonly Computer _computer;
    private bool _disposed;

    public LibreHardwareSensorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsPowerMonitorEnabled = true,
            IsMemoryEnabled = false,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false,
        };
        try
        {
            _computer.Open();
        }
        catch
        {
            _computer.Close();
            throw;
        }
    }

    public void Refresh()
    {
        static void Visit(IHardware h)
        {
            h.Update();
            foreach (var sub in h.SubHardware)
                Visit(sub);
        }

        foreach (var h in _computer.Hardware)
            Visit(h);
    }

    /// <summary>Снимок значений после <see cref="Refresh"/>.</summary>
    public SensorSnapshot ReadSnapshot()
    {
        double? cpuT = null;
        double? gpuT = null;
        double? gpuW = null;

        foreach (var hardware in _computer.Hardware)
            CollectFromHardware(hardware, ref cpuT, ref gpuT, ref gpuW);

        return new SensorSnapshot(cpuT, gpuT, gpuW);
    }

    private static void CollectFromHardware(IHardware hardware, ref double? cpuT, ref double? gpuT, ref double? gpuW)
    {
        switch (hardware.HardwareType)
        {
            case HardwareType.Cpu:
                // Температуры часто висят только на дочерних узлах (ядра CCD); берём разумный максимум по всему дереву.
                CollectCpuTempTree(hardware, ref cpuT);
                break;

            case HardwareType.Motherboard:
                // Super I/O и др. — не всегда HardwareType.Motherboard; обходим всё поддерево с корня.
                WalkMotherboardForCpuTemp(hardware, ref cpuT);
                break;

            case HardwareType.SuperIO:
                WalkMotherboardForCpuTemp(hardware, ref cpuT);
                break;

            case HardwareType.EmbeddedController:
                WalkEmbeddedForCpuTemp(hardware, ref cpuT);
                break;

            case HardwareType.PowerMonitor:
                foreach (var s in hardware.Sensors)
                {
                    if (s.SensorType != SensorType.Power || !s.Value.HasValue)
                        continue;
                    ConsiderGpuPower(s.Name, s.Value.Value, ref gpuW);
                }
                break;

            case HardwareType.GpuNvidia:
            case HardwareType.GpuAmd:
            case HardwareType.GpuIntel:
                foreach (var s in hardware.Sensors)
                {
                    if (!s.Value.HasValue)
                        continue;
                    if (s.SensorType == SensorType.Temperature && IsGpuDieTemp(s.Name))
                    {
                        var v = s.Value.Value;
                        if (!gpuT.HasValue || v > gpuT)
                            gpuT = v;
                    }
                    else if (s.SensorType == SensorType.Power)
                    {
                        ConsiderGpuPower(s.Name, s.Value.Value, ref gpuW);
                    }
                }
                break;
        }

        foreach (var sub in hardware.SubHardware)
            CollectFromHardware(sub, ref cpuT, ref gpuT, ref gpuW);
    }

    private static void WalkMotherboardForCpuTemp(IHardware hardware, ref double? cpuT)
    {
        foreach (var s in hardware.Sensors)
        {
            if (s.SensorType != SensorType.Temperature || !s.Value.HasValue)
                continue;
            var v = s.Value.Value;
            if (!IsPlausibleCpuTemp(v))
                continue;
            if (!IsMotherboardCpuTemp(s.Name))
                continue;
            if (!cpuT.HasValue || v > cpuT)
                cpuT = v;
        }

        foreach (var sub in hardware.SubHardware)
            WalkMotherboardForCpuTemp(sub, ref cpuT);
    }

    /// <summary>Ноутбуки / EC: температура CPU часто только здесь.</summary>
    private static void WalkEmbeddedForCpuTemp(IHardware hardware, ref double? cpuT)
    {
        foreach (var s in hardware.Sensors)
        {
            if (s.SensorType != SensorType.Temperature || !s.Value.HasValue)
                continue;
            var v = s.Value.Value;
            if (!IsPlausibleCpuTemp(v))
                continue;
            if (!LooksLikeCpuTempLabel(s.Name))
                continue;
            if (!cpuT.HasValue || v > cpuT)
                cpuT = v;
        }

        foreach (var sub in hardware.SubHardware)
            WalkEmbeddedForCpuTemp(sub, ref cpuT);
    }

    private static bool LooksLikeCpuTempLabel(string name)
    {
        return name.Contains("CPU", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Package", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Core", StringComparison.OrdinalIgnoreCase)
               || name.Contains("CCD", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Tdie", StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectCpuTempTree(IHardware hardware, ref double? cpuT)
    {
        foreach (var s in hardware.Sensors)
        {
            if (s.SensorType != SensorType.Temperature || !s.Value.HasValue)
                continue;
            var v = s.Value.Value;
            if (!IsPlausibleCpuTemp(v))
                continue;
            if (ShouldIgnoreCpuTempLabel(s.Name))
                continue;
            if (!cpuT.HasValue || v > cpuT)
                cpuT = v;
        }

        foreach (var sub in hardware.SubHardware)
            CollectCpuTempTree(sub, ref cpuT);
    }

    private static bool IsPlausibleCpuTemp(double c) => c is >= 3 and <= 125;

    private static bool ShouldIgnoreCpuTempLabel(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        // не температура в °C
        if (name.Contains("TjMax", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Contains("Distance to", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Contains("Throttle", StringComparison.OrdinalIgnoreCase) && !name.Contains("Temp", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool IsMotherboardCpuTemp(string name)
    {
        return name.Contains("CPU", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Socket", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
               || name.Contains("CPUTIN", StringComparison.OrdinalIgnoreCase)
               || name.Contains("PECI", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Diode", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGpuDieTemp(string name)
    {
        if (name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Contains("Memory", StringComparison.OrdinalIgnoreCase) && !name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
            return false;
        return name.Contains("GPU", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Core", StringComparison.OrdinalIgnoreCase)
               || name.Contains("D3D", StringComparison.OrdinalIgnoreCase);
    }

    private static void ConsiderGpuPower(string name, double watts, ref double? gpuW)
    {
        if (watts is < 0.5 or > 650)
            return;

        if (name.Contains("Power Limit", StringComparison.OrdinalIgnoreCase))
            return;
        if (name.Contains("Performance Limit", StringComparison.OrdinalIgnoreCase))
            return;

        var n = name;
        var looksGpu =
            n.Contains("GPU", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Graphics", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Board", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Package", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Chip", StringComparison.OrdinalIgnoreCase)
            || n.Contains("PCIe", StringComparison.OrdinalIgnoreCase)
            || n.Contains("VGA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n.Trim(), "Power", StringComparison.OrdinalIgnoreCase);

        if (!looksGpu)
            return;

        if (!gpuW.HasValue || watts > gpuW)
            gpuW = watts;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _computer.Close();
        }
        catch
        {
            // ignore
        }
    }
}

public readonly record struct SensorSnapshot(double? CpuCelsius, double? GpuCelsius, double? GpuPowerWatts);
