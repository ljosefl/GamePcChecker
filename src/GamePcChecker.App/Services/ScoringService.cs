using System.Text.RegularExpressions;

namespace GamePcChecker.App.Services;

public static class ScoringService
{
    private static readonly (Regex Pattern, double Score)[] GpuRules =
    [
        (new Regex(@"4090", RegexOptions.IgnoreCase), 220.0),
        (new Regex(@"4080", RegexOptions.IgnoreCase), 200.0),
        (new Regex(@"4070\s*ti", RegexOptions.IgnoreCase), 155.0),
        (new Regex(@"4070", RegexOptions.IgnoreCase), 140.0),
        (new Regex(@"4060\s*ti", RegexOptions.IgnoreCase), 115.0),
        (new Regex(@"4060", RegexOptions.IgnoreCase), 100.0),
        (new Regex(@"3080\s*ti", RegexOptions.IgnoreCase), 150.0),
        (new Regex(@"3080", RegexOptions.IgnoreCase), 135.0),
        (new Regex(@"3070\s*ti", RegexOptions.IgnoreCase), 125.0),
        (new Regex(@"3070", RegexOptions.IgnoreCase), 110.0),
        (new Regex(@"3060\s*ti", RegexOptions.IgnoreCase), 105.0),
        (new Regex(@"3060", RegexOptions.IgnoreCase), 95.0),
        (new Regex(@"3050", RegexOptions.IgnoreCase), 75.0),
        (new Regex(@"2060", RegexOptions.IgnoreCase), 95.0),
        (new Regex(@"1660\s*ti", RegexOptions.IgnoreCase), 85.0),
        (new Regex(@"1660", RegexOptions.IgnoreCase), 75.0),
        (new Regex(@"1650", RegexOptions.IgnoreCase), 55.0),
        (new Regex(@"1080\s*ti", RegexOptions.IgnoreCase), 115.0),
        (new Regex(@"1080", RegexOptions.IgnoreCase), 100.0),
        (new Regex(@"1070\s*ti", RegexOptions.IgnoreCase), 95.0),
        (new Regex(@"1070", RegexOptions.IgnoreCase), 88.0),
        (new Regex(@"1060\s*6", RegexOptions.IgnoreCase), 70.0),
        (new Regex(@"1060", RegexOptions.IgnoreCase), 62.0),
        (new Regex(@"1050\s*ti", RegexOptions.IgnoreCase), 45.0),
        (new Regex(@"1050", RegexOptions.IgnoreCase), 38.0),
        (new Regex(@"rx\s*7900", RegexOptions.IgnoreCase), 200.0),
        (new Regex(@"rx\s*7800", RegexOptions.IgnoreCase), 160.0),
        (new Regex(@"rx\s*7700", RegexOptions.IgnoreCase), 140.0),
        (new Regex(@"rx\s*7600", RegexOptions.IgnoreCase), 110.0),
        (new Regex(@"rx\s*6900", RegexOptions.IgnoreCase), 170.0),
        (new Regex(@"rx\s*6800\s*xt", RegexOptions.IgnoreCase), 155.0),
        (new Regex(@"rx\s*6800", RegexOptions.IgnoreCase), 145.0),
        (new Regex(@"rx\s*6700\s*xt", RegexOptions.IgnoreCase), 125.0),
        (new Regex(@"rx\s*6700", RegexOptions.IgnoreCase), 115.0),
        (new Regex(@"rx\s*6650", RegexOptions.IgnoreCase), 95.0),
        (new Regex(@"rx\s*6600\s*xt", RegexOptions.IgnoreCase), 95.0),
        (new Regex(@"rx\s*6600", RegexOptions.IgnoreCase), 88.0),
        (new Regex(@"rx\s*6500", RegexOptions.IgnoreCase), 58.0),
        (new Regex(@"rx\s*6400", RegexOptions.IgnoreCase), 50.0),
        (new Regex(@"rx\s*5700\s*xt", RegexOptions.IgnoreCase), 105.0),
        (new Regex(@"rx\s*5700", RegexOptions.IgnoreCase), 98.0),
        (new Regex(@"rx\s*5600\s*xt", RegexOptions.IgnoreCase), 95.0),
        (new Regex(@"rx\s*5600", RegexOptions.IgnoreCase), 88.0),
        (new Regex(@"rx\s*590", RegexOptions.IgnoreCase), 72.0),
        (new Regex(@"rx\s*580", RegexOptions.IgnoreCase), 68.0),
        (new Regex(@"rx\s*570", RegexOptions.IgnoreCase), 55.0),
        (new Regex(@"rx\s*560", RegexOptions.IgnoreCase), 45.0),
        (new Regex(@"rx\s*550", RegexOptions.IgnoreCase), 35.0),
        (new Regex(@"rx\s*480", RegexOptions.IgnoreCase), 62.0),
        (new Regex(@"rx\s*470", RegexOptions.IgnoreCase), 55.0),
        (new Regex(@"r9\s*fury", RegexOptions.IgnoreCase), 85.0),
        (new Regex(@"radeon\s*r.?vii", RegexOptions.IgnoreCase), 120.0),
        (new Regex(@"gtx\s*960", RegexOptions.IgnoreCase), 50.0),
        (new Regex(@"gtx\s*950", RegexOptions.IgnoreCase), 42.0),
        (new Regex(@"gtx\s*780\s*ti", RegexOptions.IgnoreCase), 70.0),
        (new Regex(@"gtx\s*770", RegexOptions.IgnoreCase), 52.0),
        (new Regex(@"gtx\s*760", RegexOptions.IgnoreCase), 48.0),
        (new Regex(@"gtx\s*750\s*ti", RegexOptions.IgnoreCase), 35.0),
        (new Regex(@"hd\s*7970", RegexOptions.IgnoreCase), 58.0),
        (new Regex(@"hd\s*7950", RegexOptions.IgnoreCase), 52.0),
        (new Regex(@"hd\s*7870", RegexOptions.IgnoreCase), 40.0),
        (new Regex(@"hd\s*7850", RegexOptions.IgnoreCase), 25.0),
        (new Regex(@"arc\s*a770", RegexOptions.IgnoreCase), 105.0),
        (new Regex(@"arc\s*a750", RegexOptions.IgnoreCase), 95.0),
        (new Regex(@"arc\s*a580", RegexOptions.IgnoreCase), 85.0),
        (new Regex(@"arc\s*a380", RegexOptions.IgnoreCase), 55.0),
    ];

    public static double GpuScore(string name, double vramGb)
    {
        var n = name ?? "";
        var baseScore = 30.0;
        foreach (var (pattern, score) in GpuRules)
        {
            if (pattern.IsMatch(n))
            {
                baseScore = score;
                break;
            }
        }

        if (vramGb <= 2)
            baseScore *= 0.92;
        else if (vramGb >= 8)
            baseScore *= 1.05;

        return Math.Round(baseScore, 1);
    }

    public static double CpuScore(string name, int cores, int logical, int mhz)
    {
        var n = (name ?? "").ToLowerInvariant();
        var mult = 1.0;
        if (n.Contains("ryzen 9", StringComparison.Ordinal) || n.Contains("i9", StringComparison.Ordinal))
            mult = 1.12;
        else if (n.Contains("ryzen 7", StringComparison.Ordinal) || n.Contains("i7", StringComparison.Ordinal))
            mult = 1.06;
        else if (n.Contains("ryzen 5", StringComparison.Ordinal) || n.Contains("i5", StringComparison.Ordinal))
            mult = 1.0;
        else if (n.Contains("ryzen 3", StringComparison.Ordinal) || n.Contains("i3", StringComparison.Ordinal))
            mult = 0.94;

        var ghz = mhz > 0 ? mhz / 1000.0 : 3.0;
        return Math.Round(cores * ghz * mult * 8.0 + logical * 2.0, 1);
    }
}
