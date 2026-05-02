namespace GamePcChecker.App.Models;

/// <summary>Результат стресс-теста GPU во время сканирования (одна или несколько фаз).</summary>
public sealed record QuickGpuStressMetrics(
    bool Success,
    string? Error,
    double MinFps,
    double MaxFps,
    double AvgFps,
    TimeSpan Duration,
    long FrameCount,
    IReadOnlyList<ScanStressPhaseMetrics>? Phases = null,
    string? SuiteSummary = null)
{
    public static QuickGpuStressMetrics Failed(string? error) =>
        new(false, error, 0, 0, 0, TimeSpan.Zero, 0, null, null);

    /// <summary>Достаточная длительность для выводов (трёхфазный тест ~75 с или fallback).</summary>
    public bool IsMeaningful =>
        Success &&
        FrameCount > 400 &&
        (Phases is { Count: >= 3 } || Duration.TotalSeconds >= 45);

    /// <summary>Одна строка для компактного UI.</summary>
    public string ShortSummary =>
        Success
            ? $"мин {MinFps:F0} / ср {AvgFps:F0} / макс {MaxFps:F0} FPS · {Duration.TotalSeconds:F0} с · кадров {FrameCount}"
            : $"не выполнен{(string.IsNullOrEmpty(Error) ? "" : $": {Error}")}";

    /// <summary>Текст для отчёта: полная сводка по фазам или короткая строка.</summary>
    public string ReportSummaryText =>
        !string.IsNullOrWhiteSpace(SuiteSummary) ? SuiteSummary : ShortSummary;

    public static QuickGpuStressMetrics FromPhaseSuite(IReadOnlyList<ScanStressPhaseMetrics> phases)
    {
        if (phases.Count == 0)
            return Failed("Нет данных по фазам");

        long totalFrames = 0;
        long ticks = 0;
        foreach (var p in phases)
        {
            totalFrames += p.FrameCount;
            ticks += p.Duration.Ticks;
        }

        var totalDuration = TimeSpan.FromTicks(ticks);
        var rawMin = phases.Min(p => p.MinFps);
        var overallMax = phases.Max(p => p.MaxFps);
        var overallAvg = totalDuration.TotalSeconds > 0 ? totalFrames / totalDuration.TotalSeconds : 0;
        // Не опускаем общий «мин» ниже разумной доли от среднего — убираем хвост от смены фаз / первых кадров.
        var overallMin = Math.Max(rawMin, overallAvg * 0.42);

        var suiteSummary = BuildSuiteSummary(phases, overallMin, overallAvg, overallMax, totalFrames, totalDuration);

        return new QuickGpuStressMetrics(
            true,
            null,
            overallMin,
            overallMax,
            overallAvg,
            totalDuration,
            totalFrames,
            phases,
            suiteSummary);
    }

    private static string BuildSuiteSummary(
        IReadOnlyList<ScanStressPhaseMetrics> phases,
        double overallMin,
        double overallAvg,
        double overallMax,
        long totalFrames,
        TimeSpan totalDuration)
    {
        var lines = new List<string>
        {
            "Итог стресс-теста при сканировании (3×25 с): GPU (DirectX 11, шейдер) + нагрузка на все логические ядра CPU. Синтетика — не FPS игры:",
        };

        foreach (var p in phases)
        {
            lines.Add(
                $"• {p.Label} (интенсивность ~{p.IntensityPercent}%): мин {p.MinFps:F0} / ср {p.AvgFps:F0} / макс {p.MaxFps:F0} FPS · {p.Duration.TotalSeconds:F0} с · кадров {p.FrameCount}");
        }

        lines.Add(
            $"Общий итог за {totalDuration.TotalSeconds:F0} с: мин {overallMin:F0} / ср {overallAvg:F0} / макс {overallMax:F0} FPS, всего кадров {totalFrames}.");

        return string.Join(Environment.NewLine, lines);
    }
}
