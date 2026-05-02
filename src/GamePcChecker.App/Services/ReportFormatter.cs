using System.Text;
using GamePcChecker.App.Models;

namespace GamePcChecker.App.Services;

public static class ReportFormatter
{
    public static string ToPlainText(AnalysisReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {r.GameTitle} ===");
        sb.AppendLine(r.GameNotes);
        sb.AppendLine();
        sb.AppendLine("--- Ваше железо ---");
        sb.AppendLine($"ОС: {r.Snapshot.OsCaption}");
        sb.AppendLine($"CPU: {r.Snapshot.CpuName} ({r.Snapshot.CpuCores} ядер / {r.Snapshot.CpuLogical} потоков, ~{r.Snapshot.CpuMhz} МГц)");
        sb.AppendLine($"Оценка CPU (условная): {r.CpuScore}");
        if (r.PrimaryGpu != null)
        {
            sb.AppendLine(
                $"GPU: {r.PrimaryGpu.Name}, вендор: {r.PrimaryGpu.GpuVendorName}, VRAM: {r.PrimaryGpu.VramGb:F1} ГБ [{r.PrimaryGpu.VramMeasuredBy}], драйвер {r.PrimaryGpu.DriverVersion}");
            sb.AppendLine($"Оценка GPU (условная): {r.GpuScore}");
        }
        else
            sb.AppendLine("GPU: не определена.");

        sb.AppendLine($"RAM: {r.Snapshot.RamGb:F1} ГБ");
        sb.AppendLine(r.Snapshot.GraphicsRuntimeSummary);
        sb.AppendLine(r.Snapshot.SystemHealthSummary);
        sb.AppendLine($"Макс. свободно на одном томе: {r.MaxFreeVolumeLetter} {r.MaxFreeGb:F1} ГБ");
        sb.AppendLine();
        sb.AppendLine("--- Среда и рантаймы (эвристика) ---");
        foreach (var p in r.Snapshot.RuntimePrerequisites)
        {
            sb.AppendLine($"• {p.Title} [{p.StatusLabel}]: {p.Detail}");
            if (!string.IsNullOrEmpty(p.InstallUrl))
                sb.AppendLine($"  {p.InstallUrl}");
        }

        sb.AppendLine();
        if (r.StressSession != null)
        {
            sb.AppendLine("--- Стресс-тест (последняя сессия, синтетика) ---");
            sb.AppendLine(r.StressSession.ShortSummary);
            sb.AppendLine(
                $"Подробности: длительность {r.StressSession.Duration:mm\\:ss}, кадров {r.StressSession.FrameCount}, средн. FPS ≈ {r.StressSession.AvgFps:F1}, min {r.StressSession.MinFps:F1}, max {r.StressSession.MaxFps:F1}.");
            sb.AppendLine();
        }

        sb.AppendLine("--- Вердикт ---");
        foreach (var v in r.Verdicts)
        {
            var tag = v.Severity switch
            {
                VerdictSeverity.Ok => "[+]",
                VerdictSeverity.Warn => "[!]",
                _ => "[X]",
            };
            sb.AppendLine($"{tag} {v.Text}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Что улучшить ---");
        foreach (var u in r.UpgradeSteps)
            sb.AppendLine($"• {u}");

        sb.AppendLine();
        sb.AppendLine("--- Периферия ---");
        foreach (var p in r.PeripheralTips)
            sb.AppendLine($"• {p}");

        sb.AppendLine();
        sb.AppendLine(r.Footnote);
        return sb.ToString();
    }
}
