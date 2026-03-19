namespace SysCleaner.Domain.Models;

public sealed record RegistryReplaceOptions(
    string OldValue,
    string NewValue,
    bool MatchCase,
    bool MatchWholeWord);