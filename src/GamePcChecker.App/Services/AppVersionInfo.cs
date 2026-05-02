using System.Reflection;

namespace GamePcChecker.App.Services;

public static class AppVersionInfo
{
    public static string InformationalVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>Сокращённая строка для UI, без суффикса +hash при сборке.</summary>
    public static string DisplayVersion
    {
        get
        {
            var v = InformationalVersion;
            var plus = v.IndexOf('+');
            if (plus >= 0)
                v = v[..plus];
            return v.Trim();
        }
    }

    public static Version ParseVersionOrFallback()
    {
        try
        {
            var s = DisplayVersion;
            if (Version.TryParse(s, out var ver))
                return ver;
        }
        catch
        {
            // ignore
        }

        return new Version(0, 0, 0);
    }
}
