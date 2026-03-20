using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;
using SysCleaner.Infrastructure.Services;

namespace SysCleaner.Tests.Application;

public sealed class EmptyItemScanServiceTests
{
    [Fact]
    public async Task ScanAsync_FindsEmptyFileAndEmptyFolder()
    {
        using var fixture = new TempDirectoryFixture();
        File.WriteAllText(Path.Combine(fixture.RootPath, "keep.txt"), "x");
        File.WriteAllText(Path.Combine(fixture.RootPath, "empty.txt"), string.Empty);
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "empty-folder"));

        var service = new EmptyItemScanService(new FakeHistoryService());

        var results = await service.ScanAsync(fixture.RootPath);

        Assert.Contains(results, x => x.TargetPath.EndsWith("empty.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, x => x.TargetPath.EndsWith("empty-folder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_FindsEmptyItemsInNestedSubfolders()
    {
        using var fixture = new TempDirectoryFixture();
        var parent = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "parent"));
        var child = Directory.CreateDirectory(Path.Combine(parent.FullName, "child"));
        var emptyFile = Path.Combine(child.FullName, "empty.txt");
        File.WriteAllText(emptyFile, string.Empty);

        var service = new EmptyItemScanService(new FakeHistoryService());

        var results = await service.ScanAsync(fixture.RootPath);

        Assert.Contains(results, x => x.TargetPath == emptyFile);
        Assert.Contains(results, x => x.TargetPath == child.FullName);
        Assert.Contains(results, x => x.TargetPath == parent.FullName);
    }

    [Fact]
    public async Task ScanAsync_DoesNotScanNestedSubfoldersWhenDisabled()
    {
        using var fixture = new TempDirectoryFixture();
        var parent = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "parent"));
        var child = Directory.CreateDirectory(Path.Combine(parent.FullName, "child"));
        var emptyFile = Path.Combine(child.FullName, "empty.txt");
        File.WriteAllText(emptyFile, string.Empty);

        var service = new EmptyItemScanService(new FakeHistoryService());

        var results = await service.ScanAsync(fixture.RootPath, includeSubfolders: false);

        Assert.DoesNotContain(results, x => x.TargetPath == emptyFile);
        Assert.DoesNotContain(results, x => x.TargetPath == child.FullName);
        Assert.DoesNotContain(results, x => x.TargetPath == parent.FullName);
    }

    [Fact]
    public void ShouldSkipScanPath_AllowsProtectedRootWhenItIsSelectedScanRoot()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var shouldSkipRoot = EmptyItemScanService.ShouldSkipScanPath(userProfile, userProfile);
        var shouldSkipChild = EmptyItemScanService.ShouldSkipScanPath(userProfile, Path.Combine(userProfile, "Documents"));

        Assert.False(shouldSkipRoot);
        Assert.False(shouldSkipChild);
    }

    [Fact]
    public void ShouldSkipScanPath_AllowsProtectedInstallPathChildrenWhenRootIsExplicitlySelected()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.False(string.IsNullOrWhiteSpace(programFiles));

        var child = Path.Combine(programFiles, "Vendor", "Empty");

        var shouldSkip = EmptyItemScanService.ShouldSkipScanPath(programFiles, child);

        Assert.False(shouldSkip);
    }

    [Fact]
    public void ShouldSkipScanPath_StillSkipsProtectedInstallPathWhenScanningDriveRoot()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.False(string.IsNullOrWhiteSpace(programFiles));

        var driveRoot = Path.GetPathRoot(programFiles);
        Assert.False(string.IsNullOrWhiteSpace(driveRoot));

        var shouldSkip = EmptyItemScanService.ShouldSkipScanPath(driveRoot!, Path.Combine(programFiles, "Vendor"));

        Assert.True(shouldSkip);
    }

    [Fact]
    public async Task ScanAsync_StopsAtConfiguredResultLimit()
    {
        using var fixture = new TempDirectoryFixture();
        for (var index = 0; index < 10; index++)
        {
            File.WriteAllText(Path.Combine(fixture.RootPath, $"empty-{index}.txt"), string.Empty);
        }

        var progressEvents = new List<EmptyItemScanProgress>();
        var progress = new Progress<EmptyItemScanProgress>(item => progressEvents.Add(item));
        var service = new EmptyItemScanService(new FakeHistoryService());

        var results = await service.ScanAsync(fixture.RootPath, includeSubfolders: true, maxResults: 3, progress: progress);

        Assert.Equal(3, results.Count);
        Assert.Contains(progressEvents, item => item.ReachedResultLimit);
    }

    [Fact]
    public async Task ExecuteAsync_CascadeDeletesParentFolderWhenItBecomesEmpty()
    {
        using var fixture = new TempDirectoryFixture();
        var parent = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "parent"));
        var child = Directory.CreateDirectory(Path.Combine(parent.FullName, "child"));

        var service = new EmptyItemScanService(new FakeHistoryService());
        var candidates = await service.ScanAsync(fixture.RootPath);
        var selected = candidates.Where(x => x.TargetPath == child.FullName).ToList();

        var cascaded = await service.ExecuteAsync(fixture.RootPath, selected);

        Assert.False(Directory.Exists(child.FullName));
        Assert.False(Directory.Exists(parent.FullName));
        Assert.Contains(cascaded, x => x.TargetPath == parent.FullName);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesReadOnlyEmptyFile()
    {
        using var fixture = new TempDirectoryFixture();
        var emptyFile = Path.Combine(fixture.RootPath, "readonly-empty.txt");
        File.WriteAllText(emptyFile, string.Empty);
        File.SetAttributes(emptyFile, FileAttributes.ReadOnly);

        var service = new EmptyItemScanService(new FakeHistoryService());
        var candidates = await service.ScanAsync(fixture.RootPath);
        var selected = candidates.Where(x => x.TargetPath == emptyFile).ToList();

        await service.ExecuteAsync(fixture.RootPath, selected);

        Assert.False(File.Exists(emptyFile));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgress()
    {
        using var fixture = new TempDirectoryFixture();
        File.WriteAllText(Path.Combine(fixture.RootPath, "empty-1.txt"), string.Empty);
        File.WriteAllText(Path.Combine(fixture.RootPath, "empty-2.txt"), string.Empty);

        var progressEvents = new List<EmptyItemCleanupProgress>();
        var progress = new Progress<EmptyItemCleanupProgress>(item => progressEvents.Add(item));
        var service = new EmptyItemScanService(new FakeHistoryService());
        var candidates = await service.ScanAsync(fixture.RootPath, includeSubfolders: false);

        await service.ExecuteAsync(fixture.RootPath, candidates, progress);

        Assert.NotEmpty(progressEvents);
        Assert.Contains(progressEvents, item => item.ProcessedCandidates == 0 && item.TotalCandidates == candidates.Count);
        Assert.Contains(progressEvents, item => item.ProcessedCandidates == candidates.Count);
    }

    private sealed class FakeHistoryService : IHistoryService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogAsync(OperationLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<OperationLogEntry>> GetRecentAsync(int take = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OperationLogEntry>>([]);
    }

    private sealed class TempDirectoryFixture : IDisposable
    {
        public TempDirectoryFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "SysCleaner.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                foreach (var file in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(RootPath, true);
            }
        }
    }
}