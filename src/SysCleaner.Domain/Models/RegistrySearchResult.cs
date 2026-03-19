namespace SysCleaner.Domain.Models;

public sealed record RegistrySearchResult(
    string Id,
    string HiveName,
    string KeyPath,
    string ValueName,
    string ValueData,
    string ValueKind,
    string EntryType,
    string MatchTarget,
    bool CanEdit,
    bool CanDelete,
    string Metadata);