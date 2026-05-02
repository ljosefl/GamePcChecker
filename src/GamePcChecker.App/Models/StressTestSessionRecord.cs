namespace GamePcChecker.App.Models;

/// <summary>Результат последней завершённой сессии синтетического стресс-теста.</summary>
public sealed record StressTestSessionRecord(
    DateTime EndedAtUtc,
    TimeSpan Duration,
    long FrameCount,
    double AvgFps,
    double MinFps,
    double MaxFps,
    int IntensityPercent,
    bool Vsync,
    bool CpuStress)
{
    /// <summary>Достаточная длительность для грубых выводов.</summary>
    public bool IsMeaningful => Duration.TotalSeconds >= 20;

    public string ShortSummary =>
        $"Длительность {Duration:mm\\:ss}, средн. FPS ≈ {AvgFps:F0} (min {MinFps:F0} / max {MaxFps:F0}), интенсивность {IntensityPercent}%" +
        (CpuStress ? ", нагрузка CPU" : "") +
        (Vsync ? ", V-Sync" : "");
}
