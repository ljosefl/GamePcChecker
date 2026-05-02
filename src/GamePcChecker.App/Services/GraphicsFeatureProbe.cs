using System.IO;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace GamePcChecker.App.Services;

/// <summary>
/// Быстрые пробы графической среды: одинаково для GPU NVIDIA, AMD, Intel.
/// </summary>
public static class GraphicsFeatureProbe
{
    public static string DescribeMaxD3D11FeatureLevel()
    {
        try
        {
            // Только уровни D3D11 (12.x задаётся через D3D12 — здесь не создаём).
            var levels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0,
            };

            var hr = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                levels,
                out var device,
                out var fl,
                out var context);

            if (hr.Failure || device == null)
                return "D3D11: устройство не создано (нет драйвера GPU / WARP)";

            var label = fl.ToString().Replace("Level_", " ").Trim();
            context?.Dispose();
            device.Dispose();
            return label;
        }
        catch (Exception ex)
        {
            return $"D3D11: {ex.Message}";
        }
    }

    /// <summary>
    /// Наличие системного Vulkan loader (не гарантирует поддержку конкретным GPU).
    /// </summary>
    public static string DescribeVulkanLoaderPresence()
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, "vulkan-1.dll");
            return File.Exists(path)
                ? "Vulkan: загрузчик в System32 найден"
                : "Vulkan: vulkan-1.dll в System32 не найден (возможна поддержка через установку драйвера или runtime игры)";
        }
        catch
        {
            return "Vulkan: не удалось проверить";
        }
    }
}
