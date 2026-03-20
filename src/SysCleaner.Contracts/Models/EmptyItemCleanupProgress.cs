namespace SysCleaner.Contracts.Models;

public sealed record EmptyItemCleanupProgress(int ProcessedCandidates, int TotalCandidates, string CurrentPath, int CascadeDeletedCount);