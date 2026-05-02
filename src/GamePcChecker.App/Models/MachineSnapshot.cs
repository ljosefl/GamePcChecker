namespace GamePcChecker.App.Models;

public sealed record MachineSnapshot(
    string OsCaption,
    string CpuName,
    int CpuCores,
    int CpuLogical,
    int CpuMhz,
    double RamGb,
    IReadOnlyList<GpuCard> Gpus,
    IReadOnlyList<DiskVolume> Disks,
    string GraphicsRuntimeSummary,
    IReadOnlyList<RuntimePrerequisite> RuntimePrerequisites,
    string SystemHealthSummary);
