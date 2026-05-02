namespace GamePcChecker.App.Models;

/// <param name="VramMeasuredBy">DXGI — объём как у DXGI/драйвера (NVIDIA/AMD/Intel); WMI — из Win32 (может ошибаться для &gt;4 ГБ).</param>
/// <param name="GpuVendorName">PCI Vendor из DXGI при сопоставлении: NVIDIA, AMD, Intel и т.д.</param>
public sealed record GpuCard(string Name, double VramGb, string DriverVersion, string VramMeasuredBy, string GpuVendorName);
