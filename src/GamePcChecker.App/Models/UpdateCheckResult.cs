namespace GamePcChecker.App.Models;

public sealed record UpdateCheckResult(
    bool HasUpdate,
    Version? LatestVersion,
    string ReleaseTitle,
    string ReleasePageUrl,
    string? PreferredDownloadUrl,
    string? PreferredAssetName);
