namespace SysCleaner.Domain.Models;

public sealed record RegistrySearchOptions(
    string Query,
    bool SearchKeyPath = true,
    bool SearchValueName = true,
    bool SearchValueData = true,
    int MaxResults = 500);