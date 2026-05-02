using System.IO;
using GamePcChecker.App.Models;
using Microsoft.Win32;

namespace GamePcChecker.App.Services;

/// <summary>
/// Эвристическая проверка распространённых компонент Windows для игр (PoE / др.).
/// </summary>
public static class RuntimeProbeService
{
    private const string UrlVcRedistX64 =
        "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    private const string UrlNetFx48 =
        "https://dotnet.microsoft.com/download/dotnet-framework/net48";

    private const string UrlWebView2Bootstrap =
        "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private const string UrlDotNetDesktop8 =
        "https://dotnet.microsoft.com/download/dotnet/8.0/runtime";

    public static IReadOnlyList<RuntimePrerequisite> Probe()
    {
        var list = new List<RuntimePrerequisite>
        {
            CheckVc2015_2022_X64(),
            CheckNetFramework48(),
            CheckWebView2Runtime(),
            CheckDotNetDesktopRuntime(),
            PoeJavaNote(),
        };

        return list;
    }

    /// <summary>Microsoft Visual C++ 2015–2022 Redistributable (x64).</summary>
    private static RuntimePrerequisite CheckVc2015_2022_X64()
    {
        // Основной ключ установленного VC++ 14.x runtime (64-bit view).
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            var installed = k?.GetValue("Installed");
            if (installed is int i && i != 0)
                return new RuntimePrerequisite(
                    "Visual C++ 2015–2022 (x64)",
                    RuntimeCheckStatus.Ok,
                    "Запись реестра VC++ 14.x Runtimes\\x64 найдена.",
                    null);

            if (installed is string s && (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)))
                return new RuntimePrerequisite(
                    "Visual C++ 2015–2022 (x64)",
                    RuntimeCheckStatus.Ok,
                    "Запись реестра VC++ 14.x Runtimes\\x64 найдена.",
                    null);
        }
        catch
        {
            // ignored — считаем проверку неудачной ниже
        }

        return new RuntimePrerequisite(
            "Visual C++ 2015–2022 (x64)",
            RuntimeCheckStatus.Missing,
            "Часто нужен для клиента игр на свежей ОС. При ошибках вида «не найден VCRUNTIME140.dll» установите redistributable.",
            UrlVcRedistX64);
    }

    /// <summary>.NET Framework 4.8 (полная установка).</summary>
    private static RuntimePrerequisite CheckNetFramework48()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            var releaseObj = k?.GetValue("Release");
            if (releaseObj != null)
            {
                var release = Convert.ToInt32(releaseObj);
                // >= 528040 соответствует .NET Framework 4.8 на поддерживаемых ОС.
                if (release >= 528040)
                    return new RuntimePrerequisite(
                        ".NET Framework 4.8",
                        RuntimeCheckStatus.Ok,
                        $"Release={release} (4.8+).",
                        null);
            }
        }
        catch
        {
            // fallthrough to Missing
        }

        return new RuntimePrerequisite(
            ".NET Framework 4.8",
            RuntimeCheckStatus.Missing,
            "Рекомендуется для многих приложений Windows и некоторых лаунчеров.",
            UrlNetFx48);
    }

    /// <summary>Microsoft Edge WebView2 Runtime (лаунчеры / оверлеи).</summary>
    private static RuntimePrerequisite CheckWebView2Runtime()
    {
        var edgeWebViewRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft",
            "EdgeWebView",
            "Application");
        try
        {
            if (Directory.Exists(edgeWebViewRoot))
            {
                var any = Directory.GetDirectories(edgeWebViewRoot);
                if (any.Length > 0)
                    return new RuntimePrerequisite(
                        "WebView2 Runtime",
                        RuntimeCheckStatus.Ok,
                        "Каталог Edge WebView2 найден.",
                        null);
            }
        }
        catch
        {
            // ignored
        }

        return new RuntimePrerequisite(
            "WebView2 Runtime",
            RuntimeCheckStatus.Missing,
            "Может понадобиться лаунчерам и оверлеям. Игра без них часто запускается — ставьте при ошибках про WebView2.",
            UrlWebView2Bootstrap);
    }

    /// <summary>.NET (Desktop) — не всегда обязателен для PoE, но полезен для инструментов.</summary>
    private static RuntimePrerequisite CheckDotNetDesktopRuntime()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "shared",
            "Microsoft.WindowsDesktop.App");
        try
        {
            if (Directory.Exists(root))
            {
                var ver = Directory.GetDirectories(root);
                if (ver.Length > 0)
                    return new RuntimePrerequisite(
                        ".NET Desktop Runtime (6+ / 8+)",
                        RuntimeCheckStatus.Ok,
                        $"Найдено: {ver.Length} версия(й) в {root}.",
                        null);
            }
        }
        catch
        {
            // ignored
        }

        return new RuntimePrerequisite(
            ".NET Desktop Runtime (6+ / 8+)",
            RuntimeCheckStatus.Info,
            "Клиенту Path of Exile / PoE 2 обычно не требуется. Нужен отдельным лаунчерам/оверлеям/утилитам на .NET 6+.",
            UrlDotNetDesktop8);
    }

    private static RuntimePrerequisite PoeJavaNote()
    {
        return new RuntimePrerequisite(
            "Java",
            RuntimeCheckStatus.Info,
            "Самой игре Path of Exile / PoE 2 Java не нужна. Может требоваться сторонним фильтрам/браузерным тулзам — по ситуации.",
            "https://adoptium.net/temurin/releases/?version=17");
    }
}
