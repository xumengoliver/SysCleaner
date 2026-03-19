namespace SysCleaner.Domain.Models;

public sealed record WindowsUpdateEventRecord(
    DateTime? Timestamp,
    string Result,
    string Title,
    string ErrorCode,
    string Details);