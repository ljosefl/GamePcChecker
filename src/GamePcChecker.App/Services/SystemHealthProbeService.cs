using System.Management;
using Microsoft.Win32;

namespace GamePcChecker.App.Services;

/// <summary>
/// Доп. эвристики состояния системы без датчиков температуры и ваттметра.
/// </summary>
public static class SystemHealthProbeService
{
    public static string BuildSummary(double ramTotalGb)
    {
        var parts = new List<string>();

        try
        {
            double freeGb = 0;
            using var os = new ManagementObjectSearcher(
                "SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject mo in os.Get())
            {
                var freeKb = Convert.ToDouble(mo["FreePhysicalMemory"] ?? 0);
                freeGb = Math.Round(freeKb / 1_048_576.0, 2);
                break;
            }

            if (ramTotalGb > 0 && freeGb >= 0)
            {
                var usedPct = (1.0 - freeGb / ramTotalGb) * 100.0;
                var hint = usedPct > 92
                    ? " ОЗУ почти заполнено — закройте лишнее перед игрой."
                    : usedPct > 85
                        ? " Мало свободной ОЗУ — возможны микрофризы."
                        : "";
                parts.Add($"Свободно ОЗУ сейчас: ~{freeGb:F1} ГБ из ~{ramTotalGb:F1} ГБ.{hint}");
            }
        }
        catch
        {
            parts.Add("Свободная ОЗУ: не удалось считать (WMI).");
        }

        try
        {
            using var pf = new ManagementObjectSearcher("SELECT AllocatedBaseSize FROM Win32_PageFileUsage");
            foreach (ManagementObject mo in pf.Get())
            {
                var kb = Convert.ToUInt64(mo["AllocatedBaseSize"] ?? 0);
                if (kb > 0)
                {
                    var gb = Math.Round(kb / 1_048_576.0, 2);
                    parts.Add($"Файл подкачки (сумма размеров): ~{gb:F1} ГБ.");
                }

                break;
            }
        }
        catch
        {
            // ignore
        }

        var plan = TryGetActivePowerPlanLabel();
        if (!string.IsNullOrEmpty(plan))
            parts.Add($"Схема питания (активная): {plan}.");

        try
        {
            var n = 0;
            using var na = new ManagementObjectSearcher(
                "SELECT NetEnabled, PhysicalAdapter FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE AND NetEnabled=TRUE");
            foreach (ManagementObject mo in na.Get())
            {
                if (mo["NetEnabled"] is true)
                    n++;
            }

            if (n > 0)
                parts.Add($"Сетевые адаптеры (включены): {n} — для онлайн-игры при возможности используйте Ethernet.");
        }
        catch
        {
            // ignore
        }

        try
        {
            using var vc = new ManagementObjectSearcher(
                "SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController WHERE CurrentHorizontalResolution IS NOT NULL");
            foreach (ManagementObject mo in vc.Get())
            {
                var w = mo["CurrentHorizontalResolution"]?.ToString();
                var h = mo["CurrentVerticalResolution"]?.ToString();
                if (!string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(h))
                {
                    parts.Add($"Текущее разрешение дисплея (WMI): {w}×{h}.");
                    break;
                }
            }
        }
        catch
        {
            // ignore
        }

        if (parts.Count == 0)
            return "Доп. проверки системы: данные недоступны.";

        return string.Join(" ", parts);
    }

    private static string? TryGetActivePowerPlanLabel()
    {
        try
        {
            using var schemes = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
            var active = schemes?.GetValue("ActivePowerScheme")?.ToString()?.Trim();
            if (string.IsNullOrEmpty(active))
                return null;

            using var planKey = schemes?.OpenSubKey(active);
            var friendly = planKey?.GetValue("FriendlyName")?.ToString();
            if (!string.IsNullOrEmpty(friendly) && friendly.Length < 200)
                return friendly;

            return $"GUID {active[..Math.Min(8, active.Length)]}… (Параметры → Система → Питание)";
        }
        catch
        {
            return null;
        }
    }
}
