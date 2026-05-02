namespace GamePcChecker.App.Models;

/// <summary>Эвристика: папка установки выбранной игры и тип диска тома.</summary>
public sealed record GameInstallProbeResult(
    bool Found,
    string? InstallPath,
    char? DriveLetter,
    double? FreeGbOnVolume,
    string DiskKindLabel,
    bool IsSsdClass,
    bool IsNvme,
    string SummaryLine);
