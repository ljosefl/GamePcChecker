namespace GamePcChecker.App.Models;

public sealed record GameProfileRecord(
    string Key,
    string Title,
    string Notes,
    int MinRamGb,
    int RecRamGb,
    int MinCpuCores,
    int RecCpuCores,
    double MinCpuGhz,
    double RecCpuGhz,
    double MinVramGb,
    double RecVramGb,
    double MinGpuScore,
    double RecGpuScore,
    int StorageNeedGb,
    bool SsdRecommended,
    int DirectXMin);
