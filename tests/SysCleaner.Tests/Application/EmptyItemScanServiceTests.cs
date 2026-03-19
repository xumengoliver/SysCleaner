using SysCleaner.Contracts.Interfaces;
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