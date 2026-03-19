using System.Text.RegularExpressions;

namespace SysCleaner.Domain.Repair;

public static partial class WindowsUpdateEventParser
{
    [GeneratedRegex(@"KB\d+", RegexOptions.IgnoreCase)]
    private static partial Regex KbRegex();

    public static string GetResult(int eventId) => eventId switch
    {
        19 => "成功",
        20 => "失败",
        _ => "复核"
    };

    public static string ExtractKbId(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var match = KbRegex().Match(message);
        return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
    }

    public static string BuildTitle(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var kbId = ExtractKbId(message);
        if (!string.IsNullOrWhiteSpace(kbId))
        {
            return kbId;
        }

        var firstLine = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return firstLine.Length <= 96 ? firstLine : firstLine[..96] + "...";
    }
}