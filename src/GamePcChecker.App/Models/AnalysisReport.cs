namespace GamePcChecker.App.Models;

public sealed record AnalysisReport(
    string GameTitle,
    string GameNotes,
    MachineSnapshot Snapshot,
    double CpuScore,
    double GpuScore,
    GpuCard? PrimaryGpu,
    /// <summary>Подпись к строке свободного места: «Свободно на томе установки игры» или «Макс. свободно на томе».</summary>
    string StorageFreeSpaceHeading,
    string MaxFreeVolumeLetter,
    double MaxFreeGb,
    IReadOnlyList<VerdictItem> Verdicts,
    IReadOnlyList<string> UpgradeSteps,
    IReadOnlyList<string> PeripheralTips,
    string Footnote,
    StressTestSessionRecord? StressSession)
{
    public bool HasPrimaryGpu => PrimaryGpu is not null;

    public bool HasStressSession => StressSession is not null;

    public bool HasGameInstallDetail => Snapshot.GameInstall is { Found: true };

    /// <summary>Секция «диск игры» в отчёте: после сканирования заполнена эвристикой (найдено или нет).</summary>
    public bool HasGameInstallSection => Snapshot.GameInstall is not null;

    public bool HasQuickGpuStressOk => Snapshot.QuickGpuStress is { Success: true };
}
