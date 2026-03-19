using SysCleaner.Contracts.Interfaces;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;

namespace SysCleaner.Infrastructure.Services;

public sealed class ResidueAnalysisService : IResidueAnalysisService
{
    public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(InstalledApp app, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<CleanupCandidate>>(() =>
        {
            var candidates = new List<CleanupCandidate>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                app.DisplayName,
                Path.GetFileName(app.InstallLocation),
                app.Publisher
            };

            foreach (var name in names.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddDirectoryCandidates(candidates, app, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), name));
                AddDirectoryCandidates(candidates, app, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name));
                AddDirectoryCandidates(candidates, app, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name));
                AddDirectoryCandidates(candidates, app, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", name));
            }

            return candidates;
        }, cancellationToken);
    }

    private static void AddDirectoryCandidates(List<CleanupCandidate> candidates, InstalledApp app, string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return;
        }

        candidates.Add(new CleanupCandidate(
            Guid.NewGuid().ToString("N"),
            Directory.Exists(path) ? CleanupCategory.ResidualFolder : CleanupCategory.ResidualFile,
            Path.GetFileName(path),
            path,
            "ResidueScan",
            "按软件名称与常见残留目录规则命中",
            app.IsProtected ? ItemHealth.Protected : ItemHealth.Review,
            app.IsProtected ? RiskLevel.Protected : RiskLevel.Review,
            false,
            !app.IsProtected,
            true,
            app.Id));
    }
}