using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using GamePcChecker.App.Configuration;
using GamePcChecker.App.Models;

namespace GamePcChecker.App.Services;

/// <summary>
/// Сравнивает текущую версию с последним GitHub Release (публичный API, без токена).
/// </summary>
public sealed class GitHubReleaseUpdateChecker
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"GamePcChecker/{AppVersionInfo.DisplayVersion} (Windows; +https://github.com/)");
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public async Task<UpdateCheckResult?> CheckForNewerReleaseAsync(Version current, CancellationToken cancellationToken)
    {
        var target = GitHubUpdateConfig.TryLoad();
        if (target is null)
            return null;

        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(target.Value.Owner)}/{Uri.EscapeDataString(target.Value.Repo)}/releases/latest";
        using var resp = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var rel = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (rel is null || string.IsNullOrWhiteSpace(rel.tag_name))
            return null;

        if (!TryParseReleaseVersion(rel.tag_name, out var latest))
            return null;

        if (latest <= current)
        {
            return new UpdateCheckResult(
                false,
                latest,
                rel.name ?? rel.tag_name,
                rel.html_url ?? string.Empty,
                null,
                null);
        }

        var (assetUrl, assetName) = PickPreferredAsset(rel.assets);

        return new UpdateCheckResult(
            true,
            latest,
            rel.name ?? rel.tag_name,
            rel.html_url ?? $"https://github.com/{target.Value.Owner}/{target.Value.Repo}/releases/latest",
            assetUrl,
            assetName);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static bool TryParseReleaseVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        var s = tag.Trim().TrimStart('v', 'V');
        if (Version.TryParse(s, out var v))
        {
            version = v;
            return true;
        }

        var dash = s.IndexOf('-');
        if (dash > 0 && Version.TryParse(s[..dash], out v))
        {
            version = v;
            return true;
        }

        return false;
    }

    private static (string? Url, string? Name) PickPreferredAsset(GitHubAssetDto[]? assets)
    {
        if (assets is null || assets.Length == 0)
            return (null, null);

        static int Score(string fileName)
        {
            var n = fileName.ToLowerInvariant();
            var score = 0;
            if (n.EndsWith(".zip", StringComparison.Ordinal))
                score += 40;
            if (n.Contains("win", StringComparison.Ordinal))
                score += 15;
            if (n.Contains("x64", StringComparison.Ordinal))
                score += 10;
            if (n.Contains("portable", StringComparison.Ordinal))
                score += 8;
            if (n.EndsWith(".exe", StringComparison.Ordinal))
                score += 25;
            if (n.Contains("gamepchecker", StringComparison.Ordinal))
                score += 5;
            return score;
        }

        GitHubAssetDto? best = null;
        var bestScore = -1;
        foreach (var a in assets)
        {
            if (string.IsNullOrEmpty(a.browser_download_url) || string.IsNullOrEmpty(a.name))
                continue;
            var sc = Score(a.name);
            if (sc > bestScore)
            {
                bestScore = sc;
                best = a;
            }
        }

        if (best != null)
            return (best.browser_download_url, best.name);

        var first = assets[0];
        return (first.browser_download_url, first.name);
    }

    private sealed class GitHubReleaseDto
    {
        public string? tag_name { get; set; }
        public string? name { get; set; }
        public string? html_url { get; set; }
        public GitHubAssetDto[]? assets { get; set; }
    }

    private sealed class GitHubAssetDto
    {
        public string? name { get; set; }
        public string? browser_download_url { get; set; }
    }
}
