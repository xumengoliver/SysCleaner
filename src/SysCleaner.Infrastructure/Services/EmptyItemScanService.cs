using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;

namespace SysCleaner.Infrastructure.Services;

public sealed class EmptyItemScanService(IHistoryService historyService) : IEmptyItemScanService
{
    private static readonly string[] ProtectedRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    ];

    public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(string rootPath, bool includeSubfolders = true, int maxResults = int.MaxValue, IProgress<EmptyItemScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<CleanupCandidate>>(() =>
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return [];
            }

            var candidates = new List<CleanupCandidate>();
            var scanState = new ScanState(candidates, maxResults, progress);
            if (includeSubfolders)
            {
                Traverse(rootPath, rootPath, scanState, cancellationToken);
            }
            else
            {
                ScanCurrentLevel(rootPath, scanState, cancellationToken);
            }

            return candidates.OrderBy(x => x.TargetPath).ToList();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<CleanupCandidate>> ExecuteAsync(string rootPath, IReadOnlyList<CleanupCandidate> selectedCandidates, CancellationToken cancellationToken = default)
    {
        var cascadeDeleted = new List<CleanupCandidate>();
        var deletedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in selectedCandidates.OrderByDescending(x => x.TargetPath.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deletedPaths.Contains(candidate.TargetPath))
            {
                continue;
            }

            var deletion = await TryDeleteAsync(candidate.TargetPath, cancellationToken);
            if (!deletion.Success)
            {
                await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "EmptyCleanup", "Delete", candidate.TargetPath, "Failed", deletion.Message), cancellationToken);
                continue;
            }

            deletedPaths.Add(candidate.TargetPath);
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "EmptyCleanup", "Delete", candidate.TargetPath, "Success", deletion.Message), cancellationToken);
            await CascadeParentsAsync(rootPath, candidate.TargetPath, cascadeDeleted, deletedPaths, cancellationToken);
        }

        return cascadeDeleted;
    }

    private static bool Traverse(string scanRoot, string current, ScanState scanState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        scanState.ReportDirectory(current);
        if (scanState.ReachedLimit)
        {
            return true;
        }

        if (ShouldSkipScanPath(scanRoot, current) || IsReparsePoint(current))
        {
            return true;
        }

        var directories = ScanUtilities.EnumerateSafeDirectories(current).ToList();
        var files = ScanUtilities.EnumerateSafeFiles(current).ToList();
        var hasNonEmptyContent = false;

        foreach (var directory in directories)
        {
            if (Traverse(scanRoot, directory, scanState, cancellationToken))
            {
                hasNonEmptyContent = true;
            }

            if (scanState.ReachedLimit)
            {
                return true;
            }
        }

        foreach (var file in files)
        {
            if (IsZeroLengthFile(file))
            {
                scanState.TryAddCandidate(new CleanupCandidate(Guid.NewGuid().ToString("N"), CleanupCategory.EmptyFile, Path.GetFileName(file), file, scanRoot, "零字节文件", ItemHealth.Healthy, RiskLevel.Safe, false, true, false));
                continue;
            }

            hasNonEmptyContent = true;
        }

        if (current != scanRoot && !hasNonEmptyContent)
        {
            scanState.TryAddCandidate(new CleanupCandidate(Guid.NewGuid().ToString("N"), CleanupCategory.EmptyFolder, Path.GetFileName(current), current, scanRoot, "空文件夹", ItemHealth.Healthy, RiskLevel.Safe, false, true, false));
        }

        return hasNonEmptyContent;
    }

    internal static bool ShouldSkipScanPath(string scanRoot, string current)
    {
        var normalizedScanRoot = Path.GetFullPath(scanRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCurrent = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return !string.Equals(normalizedScanRoot, normalizedCurrent, StringComparison.OrdinalIgnoreCase)
            && IsProtected(current);
    }

    private static void ScanCurrentLevel(string scanRoot, ScanState scanState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        scanState.ReportDirectory(scanRoot);

        foreach (var file in ScanUtilities.EnumerateSafeFiles(scanRoot))
        {
            if (IsZeroLengthFile(file))
            {
                scanState.TryAddCandidate(new CleanupCandidate(Guid.NewGuid().ToString("N"), CleanupCategory.EmptyFile, Path.GetFileName(file), file, scanRoot, "零字节文件", ItemHealth.Healthy, RiskLevel.Safe, false, true, false));
                if (scanState.ReachedLimit)
                {
                    return;
                }
            }
        }

        foreach (var directory in ScanUtilities.EnumerateSafeDirectories(scanRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanState.ReportDirectory(directory);
            if (IsProtected(directory) || IsReparsePoint(directory))
            {
                continue;
            }

            var hasAnyEntries = ScanUtilities.EnumerateSafeDirectories(directory).Any() || ScanUtilities.EnumerateSafeFiles(directory).Any();
            if (!hasAnyEntries)
            {
                scanState.TryAddCandidate(new CleanupCandidate(Guid.NewGuid().ToString("N"), CleanupCategory.EmptyFolder, Path.GetFileName(directory), directory, scanRoot, "空文件夹", ItemHealth.Healthy, RiskLevel.Safe, false, true, false));
                if (scanState.ReachedLimit)
                {
                    return;
                }
            }
        }
    }

    private static async Task CascadeParentsAsync(string rootPath, string targetPath, List<CleanupCandidate> cascadeDeleted, HashSet<string> deletedPaths, CancellationToken cancellationToken)
    {
        var parent = Directory.GetParent(targetPath);
        while (parent is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(parent.FullName, rootPath, StringComparison.OrdinalIgnoreCase) || IsProtected(parent.FullName))
            {
                return;
            }

            if (deletedPaths.Contains(parent.FullName))
            {
                parent = parent.Parent;
                continue;
            }

            var hasAnyEntries = ScanUtilities.EnumerateSafeDirectories(parent.FullName).Any() || ScanUtilities.EnumerateSafeFiles(parent.FullName).Any();
            if (hasAnyEntries)
            {
                return;
            }

            var deletion = await TryDeleteAsync(parent.FullName, cancellationToken);
            if (!deletion.Success)
            {
                return;
            }

            deletedPaths.Add(parent.FullName);
            cascadeDeleted.Add(new CleanupCandidate(Guid.NewGuid().ToString("N"), CleanupCategory.EmptyFolder, parent.Name, parent.FullName, rootPath, "子项删除后级联变为空目录", ItemHealth.Healthy, RiskLevel.Safe, false, true, false));
            parent = parent.Parent;
        }
    }

    private static Task<PathDeletionResult> TryDeleteAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return PathDeletionHelper.DeleteAsync(path, recursive: false, cancellationToken);
        }

        if (Directory.Exists(path))
        {
            return PathDeletionHelper.DeleteAsync(path, recursive: false, cancellationToken);
        }

        return Task.FromResult(new PathDeletionResult(PathDeletionStatus.Missing, "目标不存在。"));
    }

    private static bool IsZeroLengthFile(string path)
    {
        try
        {
            return new FileInfo(path).Length == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProtected(string path)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var root in ProtectedRoots.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.Equals(normalizedRoot, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch
        {
            return true;
        }
    }

    private sealed class ScanState(List<CleanupCandidate> candidates, int maxResults, IProgress<EmptyItemScanProgress>? progress)
    {
        private readonly List<CleanupCandidate> _candidates = candidates;
        private readonly int _maxResults = maxResults > 0 ? maxResults : int.MaxValue;
        private readonly IProgress<EmptyItemScanProgress>? _progress = progress;

        public int ScannedDirectories { get; private set; }

        public bool ReachedLimit => _candidates.Count >= _maxResults;

        public void ReportDirectory(string currentPath)
        {
            ScannedDirectories++;
            _progress?.Report(new EmptyItemScanProgress(ScannedDirectories, _candidates.Count, currentPath, ReachedLimit));
        }

        public bool TryAddCandidate(CleanupCandidate candidate)
        {
            if (ReachedLimit)
            {
                return false;
            }

            _candidates.Add(candidate);
            _progress?.Report(new EmptyItemScanProgress(ScannedDirectories, _candidates.Count, candidate.TargetPath, ReachedLimit));
            return true;
        }
    }
}