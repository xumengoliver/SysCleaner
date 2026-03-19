namespace SysCleaner.Domain.Models;

public sealed record OperationLogEntry(
    long Id,
    DateTime Timestamp,
    string Module,
    string Action,
    string Target,
    string Result,
    string Details);