namespace SysCleaner.Contracts.Models;

public sealed record EmptyItemScanProgress(int ScannedDirectories, int FoundCandidates, string CurrentPath, bool ReachedResultLimit);