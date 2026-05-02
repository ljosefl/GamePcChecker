using System.IO;
using System.Text.RegularExpressions;
using GamePcChecker.App.Models;
using Microsoft.Win32;

namespace GamePcChecker.App.Services;

/// <summary>Поиск папки установки PoE/PoE2 и классификация диска тома.</summary>
public static class GameInstallProbeService
{
    private static readonly Regex LibraryPathRx =
        new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly Dictionary<string, string[]> SteamFolderCandidates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["poe1"] = ["Path of Exile"],
        ["poe2"] = ["Path of Exile 2"],
    };

    private static readonly Dictionary<string, string[]> ExtraStandaloneRoots =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["poe1"] =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Grinding Gear Games", "Path of Exile"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Grinding Gear Games", "Path of Exile"),
            ],
            ["poe2"] =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Grinding Gear Games", "Path of Exile 2"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Grinding Gear Games", "Path of Exile 2"),
            ],
        };

    /// <param name="userInstallFolder">Необязательно: папка установки игры, если автоопределение не подходит.</param>
    public static GameInstallProbeResult Describe(
        string gameKey,
        IReadOnlyList<DiskVolume> disks,
        string? userInstallFolder = null)
    {
        if (!string.IsNullOrWhiteSpace(userInstallFolder))
        {
            string normalized;
            try
            {
                normalized = Path.GetFullPath(userInstallFolder.Trim());
            }
            catch
            {
                return NotFound("Некорректный путь к папке. Укажите полный путь к каталогу игры или очистите поле.");
            }

            if (!Directory.Exists(normalized))
            {
                return NotFound(
                    "Указанная папка не найдена. Проверьте путь или очистите поле для автоопределения.");
            }

            var markerOk = FileExistsGameMarker(normalized);
            return BuildFromFoundPath(normalized, disks, userSpecified: true, markerOk);
        }

        if (!SteamFolderCandidates.TryGetValue(gameKey, out var steamNames))
            steamNames = [];

        string? foundPath = null;

        foreach (var steamRoot in EnumerateSteamLibraryRoots())
        {
            foreach (var name in steamNames)
            {
                var candidate = Path.Combine(steamRoot, "steamapps", "common", name);
                if (Directory.Exists(candidate) && FileExistsGameMarker(candidate))
                {
                    foundPath = candidate;
                    break;
                }
            }

            if (foundPath != null)
                break;
        }

        if (foundPath == null && ExtraStandaloneRoots.TryGetValue(gameKey, out var standalones))
        {
            foreach (var p in standalones)
            {
                if (Directory.Exists(p) && FileExistsGameMarker(p))
                {
                    foundPath = p;
                    break;
                }
            }
        }

        if (foundPath == null)
        {
            return NotFound(
                "Автоопределение пути установки не удалось — укажите папку вручную выше или проверьте Steam/лаунчер.");
        }

        if (!FileExistsGameMarker(foundPath))
        {
            return NotFound(
                "Автоопределение нашло кандидата без типичных exe PoE — укажите папку установки вручную.");
        }

        return BuildFromFoundPath(foundPath, disks, userSpecified: false, markerOk: true);
    }

    private static GameInstallProbeResult NotFound(string summaryLine) =>
        new(
            false,
            null,
            null,
            null,
            "",
            false,
            false,
            summaryLine);

    private static GameInstallProbeResult BuildFromFoundPath(
        string foundPath,
        IReadOnlyList<DiskVolume> disks,
        bool userSpecified,
        bool markerOk)
    {
        var root = Path.GetPathRoot(foundPath);
        if (string.IsNullOrEmpty(root) || root.Length < 2 || !char.IsLetter(root[0]))
        {
            return new GameInstallProbeResult(
                true,
                foundPath,
                null,
                null,
                "",
                false,
                false,
                $"{(userSpecified ? "Папка" : "Найдено")}: {foundPath} (не удалось определить букву диска).");
        }

        var letter = char.ToUpperInvariant(root[0]);
        var free = TryFreeGb(disks, letter);
        var (diskLabel, isSsd, isNvme) = DriveMediaProbeService.DescribeDrive(letter);

        var freeText = free is { } gb ? $"свободно на томе ~{gb:F1} ГБ" : "свободное место на томе не сопоставлено";

        var prefix = userSpecified
            ? (markerOk
                ? "Папка указана вручную"
                : "Папка указана вручную (в корне не найдены PathOfExile*.exe / Client.exe — уточните путь, если это не каталог игры)")
            : "Найдено";

        var summary = $"{prefix}: {foundPath}. Том {letter}: {diskLabel}, {freeText}.";

        return new GameInstallProbeResult(
            true,
            foundPath,
            letter,
            free,
            diskLabel,
            isSsd,
            isNvme,
            summary);
    }

    private static bool FileExistsGameMarker(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
            {
                var n = Path.GetFileName(f);
                if (n.Contains("PathOfExile", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (n.Equals("Client.exe", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static double? TryFreeGb(IReadOnlyList<DiskVolume> disks, char letter)
    {
        var key = char.ToUpperInvariant(letter).ToString();
        foreach (var d in disks)
        {
            var vol = d.Letter.TrimEnd('\\').TrimEnd(':');
            if (vol.Equals(key, StringComparison.OrdinalIgnoreCase))
                return d.FreeGb;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSteamLibraryRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = k?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                var normalized = steamPath.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
                if (Directory.Exists(normalized))
                {
                    roots.Add(normalized);
                    TryAddLibraryFoldersVdf(Path.Combine(normalized, "steamapps", "libraryfolders.vdf"), roots);
                }
            }
        }
        catch
        {
            // ignore
        }

        foreach (var r in roots)
            yield return r;
    }

    private static void TryAddLibraryFoldersVdf(string vdfPath, HashSet<string> roots)
    {
        if (!File.Exists(vdfPath))
            return;

        string text;
        try
        {
            text = File.ReadAllText(vdfPath);
        }
        catch
        {
            return;
        }

        foreach (Match m in LibraryPathRx.Matches(text))
        {
            if (m.Groups.Count < 2)
                continue;
            var raw = m.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(raw))
                roots.Add(raw.TrimEnd(Path.DirectorySeparatorChar));
        }
    }

}
