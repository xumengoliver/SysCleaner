using SysCleaner.Domain.Models;

namespace SysCleaner.Domain.Matching;

public static class InstalledAppMatcher
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "app",
        "co",
        "company",
        "corp",
        "corporation",
        "inc",
        "ltd",
        "llc",
        "software",
        "studio",
        "system",
        "systems",
        "technology",
        "technologies",
        "tool"
    };

    public static bool Matches(InstalledApp app, params string?[] values)
    {
        var haystacks = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.ToLowerInvariant())
            .ToArray();

        if (haystacks.Length == 0)
        {
            return false;
        }

        foreach (var token in BuildTokens(app))
        {
            if (haystacks.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryMatch(IEnumerable<InstalledApp> apps, IEnumerable<string?> values, out InstalledApp? matchedApp)
    {
        var haystacks = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        foreach (var app in apps)
        {
            if (Matches(app, haystacks))
            {
                matchedApp = app;
                return true;
            }
        }

        matchedApp = null;
        return false;
    }

    public static IReadOnlyList<string> BuildTokens(InstalledApp app)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddValueTokens(tokens, app.DisplayName);
        AddValueTokens(tokens, app.Publisher);
        AddValueTokens(tokens, Path.GetFileName(app.InstallLocation));
        AddValueTokens(tokens, Path.GetFileNameWithoutExtension(app.InstallLocation));
        AddValueTokens(tokens, ExtractFileName(app.UninstallString));
        AddValueTokens(tokens, ExtractFileName(app.QuietUninstallString));
        return tokens.ToList();
    }

    private static void AddValueTokens(HashSet<string> tokens, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var raw = value.Trim();
        if (raw.Length >= 3 && !StopWords.Contains(raw))
        {
            tokens.Add(raw.ToLowerInvariant());
        }

        foreach (var part in raw.Split([' ', '-', '_', '.', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length < 3 || StopWords.Contains(part))
            {
                continue;
            }

            tokens.Add(part.ToLowerInvariant());
        }
    }

    private static string ExtractFileName(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var trimmed = command.Trim().Trim('"');
        var separator = trimmed.IndexOf(' ');
        var candidate = separator > 0 ? trimmed[..separator] : trimmed;
        return Path.GetFileNameWithoutExtension(candidate);
    }
}