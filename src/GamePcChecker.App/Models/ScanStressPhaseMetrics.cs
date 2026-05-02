namespace GamePcChecker.App.Models;

/// <summary>Одна фаза стресс-теста при сканировании (фиксированная интенсивность шейдера).</summary>
public sealed record ScanStressPhaseMetrics(
    string Label,
    int IntensityPercent,
    double MinFps,
    double AvgFps,
    double MaxFps,
    TimeSpan Duration,
    long FrameCount);
