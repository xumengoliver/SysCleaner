using SysCleaner.Domain.Models;
using System.Text.RegularExpressions;

namespace SysCleaner.Domain.Registry;

public static class RegistryReplaceEngine
{
    public static string Replace(string source, RegistryReplaceOptions options)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(options.OldValue))
        {
            return source;
        }

        var pattern = Regex.Escape(options.OldValue);
        if (options.MatchWholeWord)
        {
            pattern = $@"(?<![\p{{L}}\p{{N}}_]){pattern}(?![\p{{L}}\p{{N}}_])";
        }

        var regexOptions = options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        return Regex.Replace(source, pattern, options.NewValue, regexOptions);
    }
}