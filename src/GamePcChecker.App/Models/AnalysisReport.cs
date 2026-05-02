namespace GamePcChecker.App.Models;

public sealed record AnalysisReport(
    string GameTitle,
    string GameNotes,
    MachineSnapshot Snapshot,
    double CpuScore,
    double GpuScore,
    GpuCard? PrimaryGpu,
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
}
