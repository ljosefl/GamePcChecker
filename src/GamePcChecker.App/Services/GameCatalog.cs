using GamePcChecker.App.Models;

namespace GamePcChecker.App.Services;

public static class GameCatalog
{
    public static IReadOnlyList<GameProfileRecord> All { get; } =
    [
        new(
            Key: "poe1",
            Title: "Path of Exile",
            Notes: "",
            MinRamGb: 8,
            RecRamGb: 16,
            MinCpuCores: 4,
            RecCpuCores: 4,
            MinCpuGhz: 2.6,
            RecCpuGhz: 3.2,
            MinVramGb: 2.0,
            RecVramGb: 4.0,
            MinGpuScore: 25.0,
            RecGpuScore: 45.0,
            StorageNeedGb: 40,
            SsdRecommended: true,
            DirectXMin: 11),
        new(
            Key: "poe2",
            Title: "Path of Exile 2",
            Notes: "",
            MinRamGb: 8,
            RecRamGb: 16,
            MinCpuCores: 4,
            RecCpuCores: 6,
            MinCpuGhz: 3.0,
            RecCpuGhz: 3.4,
            MinVramGb: 3.0,
            RecVramGb: 6.0,
            MinGpuScore: 55.0,
            RecGpuScore: 95.0,
            StorageNeedGb: 100,
            SsdRecommended: true,
            DirectXMin: 12),
    ];

    public static GameProfileRecord Get(string key) =>
        All.First(g => g.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
