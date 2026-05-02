using System.IO;
using System.Text.Json;

namespace GamePcChecker.App.Configuration;

/// <summary>
/// Репозиторий GitHub для проверки релизов. Задаётся через <c>github-update.json</c> рядом с exe
/// или переменную среды <c>GAMEPCCHECKER_GITHUB_UPDATE</c> (формат <c>владелец/репо</c>).
/// </summary>
public static class GitHubUpdateConfig
{
    private const string ConfigFileName = "github-update.json";
    private const string EnvVarName = "GAMEPCCHECKER_GITHUB_UPDATE";

    public static GitHubRepoTarget? TryLoad()
    {
        try
        {
            var env = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(env))
            {
                var parts = env.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && IsValidSegment(parts[0]) && IsValidSegment(parts[1]))
                    return new GitHubRepoTarget(parts[0], parts[1]);
            }

            var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<ConfigDto>(json, ConfigJsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Owner) || string.IsNullOrWhiteSpace(dto.Repo))
                return null;
            if (!IsValidSegment(dto.Owner) || !IsValidSegment(dto.Repo))
                return null;
            return new GitHubRepoTarget(dto.Owner.Trim(), dto.Repo.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static bool IsValidSegment(string s)
    {
        if (s.Length is < 1 or > 100)
            return false;
        return s.All(static c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');
    }

    private sealed class ConfigDto
    {
        public string? Owner { get; set; }
        public string? Repo { get; set; }
    }
}

public readonly record struct GitHubRepoTarget(string Owner, string Repo);
