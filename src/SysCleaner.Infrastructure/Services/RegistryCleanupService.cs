using Microsoft.Win32;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;
using SysCleaner.Domain.Registry;
using System.Text;

namespace SysCleaner.Infrastructure.Services;

public sealed class RegistryCleanupService(IHistoryService historyService) : IRegistryCleanupService
{
    private static readonly (string HiveName, RegistryKey Root)[] SearchRoots =
    [
        ("HKEY_CURRENT_USER", Registry.CurrentUser),
        ("HKEY_LOCAL_MACHINE", Registry.LocalMachine),
        ("HKEY_CLASSES_ROOT", Registry.ClassesRoot)
    ];

    private static readonly (RegistryKey Root, string Path, string Label)[] BrokenValueRoots =
    [
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "当前用户启动项"),
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "当前用户一次性启动项"),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "系统启动项"),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "系统一次性启动项")
    ];

    private static readonly (RegistryKey Root, string Path, string Label)[] BrokenKeyRoots =
    [
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\App Paths", "当前用户 App Paths"),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\App Paths", "系统 App Paths"),
        (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths", "32 位 App Paths")
    ];

    private static readonly (RegistryKey Root, string Path, string Label)[] BrokenShellCommandRoots =
    [
        (Registry.ClassesRoot, @"*\shell", "所有文件右键命令"),
        (Registry.ClassesRoot, @"AllFilesystemObjects\shell", "文件系统对象右键命令"),
        (Registry.ClassesRoot, @"Directory\shell", "目录右键命令"),
        (Registry.ClassesRoot, @"Directory\Background\shell", "目录背景右键命令"),
        (Registry.ClassesRoot, @"Drive\shell", "磁盘右键命令")
    ];

    private static readonly (RegistryKey Root, string Path, string Label)[] BrokenShellHandlerRoots =
    [
        (Registry.ClassesRoot, @"*\shellex\ContextMenuHandlers", "所有文件 Shell 扩展"),
        (Registry.ClassesRoot, @"AllFilesystemObjects\shellex\ContextMenuHandlers", "文件系统对象 Shell 扩展"),
        (Registry.ClassesRoot, @"Directory\shellex\ContextMenuHandlers", "目录 Shell 扩展"),
        (Registry.ClassesRoot, @"Directory\Background\shellex\ContextMenuHandlers", "目录背景 Shell 扩展"),
        (Registry.ClassesRoot, @"Drive\shellex\ContextMenuHandlers", "磁盘 Shell 扩展")
    ];

    private static readonly (RegistryKey Root, string Path, string Label)[] BrokenBrowserHelperObjectRoots =
    [
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects", "当前用户 BHO"),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects", "系统 BHO"),
        (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects", "32 位 BHO")
    ];

    private static readonly (RegistryKey Root, string Path, string Label)[] BrokenMuiCacheRoots =
    [
        (Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", "当前用户 MuiCache"),
        (Registry.CurrentUser, @"Software\Microsoft\Windows\ShellNoRoam\MUICache", "旧版 MuiCache")
    ];

    private static readonly string[] FileAssociationVerbs = ["open", "print", "edit", "preview", "play"];

    public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(InstalledApp app, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<CleanupCandidate>>(() =>
        {
            var candidates = new List<CleanupCandidate>();
            ScanRunEntries(candidates, app, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run");
            ScanRunEntries(candidates, app, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run");
            return candidates;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<CleanupCandidate>> ScanBrokenEntriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<CleanupCandidate>>(() =>
        {
            var candidates = new List<CleanupCandidate>();
            foreach (var (root, path, label) in BrokenValueRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanBrokenValueEntries(candidates, root, path, label);
            }

            foreach (var (root, path, label) in BrokenKeyRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanBrokenAppPathEntries(candidates, root, path, label);
            }

            foreach (var (root, path, label) in BrokenShellCommandRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanBrokenShellCommandEntries(candidates, root, path, label);
            }

            foreach (var (root, path, label) in BrokenShellHandlerRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanBrokenShellHandlerEntries(candidates, root, path, label);
            }

            foreach (var (root, path, label) in BrokenBrowserHelperObjectRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanBrokenBrowserHelperObjects(candidates, root, path, label);
            }

            foreach (var (root, path, label) in BrokenMuiCacheRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanBrokenMuiCacheEntries(candidates, root, path, label);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ScanBrokenFileAssociationCommands(candidates, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            ScanBrokenProtocolHandlers(candidates, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            ScanBrokenSharedDlls(candidates);

            cancellationToken.ThrowIfCancellationRequested();
            ScanBrokenUninstallEntries(candidates);

            return candidates
                .GroupBy(candidate => candidate.Metadata, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(candidate => candidate.Health)
                .ThenBy(candidate => candidate.Title)
                .ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<RegistrySearchResult>> SearchAsync(RegistrySearchOptions options, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<RegistrySearchResult>>(() =>
        {
            var query = options.Query.Trim();
            if (query.Length < 2)
            {
                return [];
            }

            var results = new List<RegistrySearchResult>();
            foreach (var (hiveName, root) in SearchRoots)
            {
                SearchKey(root, hiveName, string.Empty, query, options, results, cancellationToken);
                if (results.Count >= options.MaxResults)
                {
                    break;
                }
            }

            return results
                .OrderBy(item => item.HiveName)
                .ThenBy(item => item.KeyPath)
                .ThenBy(item => item.ValueName)
                .ToList();
        }, cancellationToken);
    }

    public async Task<OperationResult> DeleteSearchResultsAsync(IReadOnlyList<RegistrySearchResult> results, CancellationToken cancellationToken = default)
    {
        if (results.Count == 0)
        {
            return new OperationResult(false, "未选择任何注册表结果。");
        }

        try
        {
            var deleted = 0;
            foreach (var result in results.DistinctBy(item => item.Metadata))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DeleteSearchResult(result))
                {
                    deleted++;
                }
            }

            var message = $"已删除 {deleted} 个注册表结果。";
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "RegistrySearch", "BatchDelete", "registry-search", "Success", message), cancellationToken);
            return new OperationResult(true, message);
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "RegistrySearch", "BatchDelete", "registry-search", "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public async Task<OperationResult> UpdateSearchResultsAsync(IReadOnlyList<RegistrySearchResult> results, string newValue, CancellationToken cancellationToken = default)
    {
        var editableItems = results.Where(item => item.CanEdit).DistinctBy(item => item.Metadata).ToList();
        if (editableItems.Count == 0)
        {
            return new OperationResult(false, "选中项中没有可编辑的注册表值。");
        }

        try
        {
            var updated = 0;
            foreach (var item in editableItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (UpdateSearchResult(item, newValue))
                {
                    updated++;
                }
            }

            var message = $"已批量更新 {updated} 个注册表值。";
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "RegistrySearch", "BatchUpdate", "registry-search", "Success", message), cancellationToken);
            return new OperationResult(true, message);
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "RegistrySearch", "BatchUpdate", "registry-search", "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public async Task<OperationResult> ReplaceInSearchResultsAsync(IReadOnlyList<RegistrySearchResult> results, RegistryReplaceOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.OldValue))
        {
            return new OperationResult(false, "替换前内容不能为空。");
        }

        var editableItems = results.Where(item => item.CanEdit).DistinctBy(item => item.Metadata).ToList();
        if (editableItems.Count == 0)
        {
            return new OperationResult(false, "选中项中没有可编辑的注册表值。");
        }

        try
        {
            var updated = 0;
            foreach (var item in editableItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var replacedValue = RegistryReplaceEngine.Replace(item.ValueData, options);
                if (string.Equals(replacedValue, item.ValueData, StringComparison.Ordinal))
                {
                    continue;
                }

                if (UpdateSearchResult(item, replacedValue))
                {
                    updated++;
                }
            }

            var message = $"已按替换模式更新 {updated} 个注册表值。";
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "RegistrySearch", "BatchReplace", "registry-search", "Success", message), cancellationToken);
            return new OperationResult(true, message);
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "RegistrySearch", "BatchReplace", "registry-search", "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    private static void ScanRunEntries(List<CleanupCandidate> candidates, InstalledApp app, RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        if (key is null)
        {
            return;
        }

        foreach (var name in key.GetValueNames())
        {
            var value = key.GetValue(name)?.ToString() ?? string.Empty;
            if (!value.Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase)
                && !value.Contains(app.InstallLocation, StringComparison.OrdinalIgnoreCase)
                && !name.Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            candidates.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.RegistryEntry,
                name,
                $"{root.Name}\\{path}",
                "RegistryRun",
                $"检测到与 {app.DisplayName} 关联的启动注册表值",
                app.IsProtected ? ItemHealth.Protected : ItemHealth.Review,
                app.IsProtected ? RiskLevel.Protected : RiskLevel.Review,
                false,
                !app.IsProtected,
                true,
                app.Id,
                $"registry-value|{root.Name}\\{path}|{name}"));
        }
    }

    private static void ScanBrokenValueEntries(List<CleanupCandidate> candidates, RegistryKey root, string path, string label)
    {
        using var key = root.OpenSubKey(path);
        if (key is null)
        {
            return;
        }

        foreach (var name in key.GetValueNames())
        {
            var value = key.GetValue(name)?.ToString() ?? string.Empty;
            var executablePath = ScanUtilities.ExtractExecutablePath(value);
            if (string.IsNullOrWhiteSpace(executablePath) || ScanUtilities.PathExists(executablePath))
            {
                continue;
            }

            var fullKeyPath = $"{root.Name}\\{path}";
            candidates.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.RegistryEntry,
                string.IsNullOrWhiteSpace(name) ? "(默认)" : name,
                fullKeyPath,
                label,
                $"注册表值指向的目标不存在：{executablePath}",
                ItemHealth.Broken,
                RiskLevel.Safe,
                false,
                true,
                true,
                Metadata: $"registry-value|{fullKeyPath}|{name}"));
        }
    }

    private static void ScanBrokenAppPathEntries(List<CleanupCandidate> candidates, RegistryKey root, string path, string label)
    {
        using var baseKey = root.OpenSubKey(path);
        if (baseKey is null)
        {
            return;
        }

        foreach (var subKeyName in baseKey.GetSubKeyNames())
        {
            using var subKey = baseKey.OpenSubKey(subKeyName);
            if (subKey is null)
            {
                continue;
            }

            var executablePath = ScanUtilities.ExtractExecutablePath(subKey.GetValue(null)?.ToString());
            if (string.IsNullOrWhiteSpace(executablePath) || ScanUtilities.PathExists(executablePath))
            {
                continue;
            }

            var fullKeyPath = $"{root.Name}\\{path}\\{subKeyName}";
            candidates.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.RegistryEntry,
                subKeyName,
                fullKeyPath,
                label,
                $"App Paths 注册的程序不存在：{executablePath}",
                ItemHealth.Broken,
                RiskLevel.Safe,
                false,
                true,
                true,
                Metadata: $"registry-key|{fullKeyPath}"));
        }
    }

    private static void ScanBrokenShellCommandEntries(List<CleanupCandidate> candidates, RegistryKey root, string path, string label)
    {
        using var baseKey = root.OpenSubKey(path);
        if (baseKey is null)
        {
            return;
        }

        foreach (var subKeyName in baseKey.GetSubKeyNames())
        {
            using var commandKey = baseKey.OpenSubKey(subKeyName + "\\command");
            if (commandKey is null)
            {
                continue;
            }

            var rawCommand = commandKey.GetValue(null)?.ToString() ?? string.Empty;
            var executablePath = ResolvePathCandidate(rawCommand);
            if (string.IsNullOrWhiteSpace(executablePath) || ScanUtilities.PathExists(executablePath))
            {
                continue;
            }

            var fullKeyPath = $"{root.Name}\\{path}\\{subKeyName}\\command";
            candidates.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.RegistryEntry,
                subKeyName,
                fullKeyPath,
                label,
                $"右键命令指向的程序不存在：{executablePath}",
                ItemHealth.Broken,
                RiskLevel.Safe,
                false,
                true,
                true,
                Metadata: $"registry-key|{fullKeyPath}"));
        }
    }

    private static void ScanBrokenShellHandlerEntries(List<CleanupCandidate> candidates, RegistryKey root, string path, string label)
    {
        using var baseKey = root.OpenSubKey(path);
        if (baseKey is null)
        {
            return;
        }

        foreach (var subKeyName in baseKey.GetSubKeyNames())
        {
            using var handlerKey = baseKey.OpenSubKey(subKeyName);
            var clsidValue = handlerKey?.GetValue(null)?.ToString() ?? string.Empty;
            if (!Guid.TryParse(clsidValue, out _))
            {
                continue;
            }

            var serverPath = ResolveClsidServerPath(clsidValue);
            if (string.IsNullOrWhiteSpace(serverPath) || ScanUtilities.PathExists(serverPath))
            {
                continue;
            }

            var fullKeyPath = $"{root.Name}\\{path}\\{subKeyName}";
            candidates.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.RegistryEntry,
                subKeyName,
                fullKeyPath,
                label,
                $"Shell 扩展 CLSID {clsidValue} 指向的组件不存在：{serverPath}",
                ItemHealth.Broken,
                RiskLevel.Safe,
                false,
                true,
                true,
                Metadata: $"registry-key|{fullKeyPath}"));
        }
    }

    private static void ScanBrokenBrowserHelperObjects(List<CleanupCandidate> candidates, RegistryKey root, string path, string label)
    {
        using var baseKey = root.OpenSubKey(path);
        if (baseKey is null)
        {
            return;
        }

        foreach (var subKeyName in baseKey.GetSubKeyNames())
        {
            var serverPath = ResolveClsidServerPath(subKeyName);
            if (string.IsNullOrWhiteSpace(serverPath) || ScanUtilities.PathExists(serverPath))
            {
                continue;
            }

            var fullKeyPath = $"{root.Name}\\{path}\\{subKeyName}";
            candidates.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.RegistryEntry,
                subKeyName,
                fullKeyPath,
                label,
                $"BHO 组件指向的模块不存在：{serverPath}",
                ItemHealth.Broken,
                RiskLevel.Safe,
                false,
                true,
                true,
                Metadata: $"registry-key|{fullKeyPath}"));
        }
    }

    private static void ScanBrokenFileAssociationCommands(List<CleanupCandidate> candidates, CancellationToken cancellationToken)
    {
        using var classesRoot = Registry.ClassesRoot;
        foreach (var extension in classesRoot.GetSubKeyNames().Where(name => name.StartsWith(".", StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var extensionKey = classesRoot.OpenSubKey(extension);
            var progId = extensionKey?.GetValue(null)?.ToString();
            if (string.IsNullOrWhiteSpace(progId))
            {
                continue;
            }

            foreach (var verb in FileAssociationVerbs)
            {
                ScanFileAssociationCommand(candidates, progId, extension, verb);
            }
        }
    }

    private static void ScanBrokenProtocolHandlers(List<CleanupCandidate> candidates, CancellationToken cancellationToken)
    {
        using var classesRoot = Registry.ClassesRoot;
        foreach (var keyName in classesRoot.GetSubKeyNames().Where(name => !name.StartsWith(".", StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key = classesRoot.OpenSubKey(keyName);
            var hasProtocol = key?.GetValueNames().Any(name => name.Equals("URL Protocol", StringComparison.OrdinalIgnoreCase)) == true;
            if (!hasProtocol)
            {
                continue;
            }

            using var commandKey = classesRoot.OpenSubKey($"{keyName}\\shell\\open\\command");
            var executablePath = ResolvePathCandidate(commandKey?.GetValue(null)?.ToString());
            if (string.IsNullOrWhiteSpace(executablePath) || ScanUtilities.PathExists(executablePath))
            {
                continue;
            }

            var fullKeyPath = $"HKEY_CLASSES_ROOT\\{keyName}\\shell\\open\\command";
            candidates.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.RegistryEntry,
                keyName,
                fullKeyPath,
                "协议处理器",
                $"协议处理器指向的程序不存在：{executablePath}",
                ItemHealth.Broken,
                RiskLevel.Safe,
                false,
                true,
                true,
                Metadata: $"registry-key|{fullKeyPath}"));
        }
    }

    private static void ScanBrokenSharedDlls(List<CleanupCandidate> candidates)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDlls");
        if (key is null)
        {
            return;
        }

        foreach (var valueName in key.GetValueNames())
        {
            var path = ResolvePathCandidate(valueName);
            if (string.IsNullOrWhiteSpace(path) || ScanUtilities.PathExists(path))
            {
                continue;
            }

            candidates.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.RegistryEntry,
                Path.GetFileName(path),
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDlls",
                "SharedDlls",
                $"SharedDlls 记录的模块不存在：{path}",
                ItemHealth.Broken,
                ScanUtilities.IsPathUnderWindowsDirectory(path) ? RiskLevel.Review : RiskLevel.Safe,
                false,
                true,
                true,
                Metadata: $"registry-value|HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\SharedDlls|{valueName}"));
        }
    }

    private static void ScanBrokenMuiCacheEntries(List<CleanupCandidate> candidates, RegistryKey root, string path, string label)
    {
        using var key = root.OpenSubKey(path);
        if (key is null)
        {
            return;
        }

        foreach (var valueName in key.GetValueNames())
        {
            var pathCandidate = ResolvePathCandidate(valueName);
            if (string.IsNullOrWhiteSpace(pathCandidate) || ScanUtilities.PathExists(pathCandidate))
            {
                continue;
            }

            var fullKeyPath = $"{root.Name}\\{path}";
            candidates.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.RegistryEntry,
                Path.GetFileName(pathCandidate),
                fullKeyPath,
                label,
                $"MuiCache 缓存的程序已不存在：{pathCandidate}",
                ItemHealth.Broken,
                RiskLevel.Safe,
                false,
                true,
                true,
                Metadata: $"registry-value|{fullKeyPath}|{valueName}"));
        }
    }

    private static void ScanFileAssociationCommand(List<CleanupCandidate> candidates, string progId, string extension, string verb)
    {
        using var commandKey = Registry.ClassesRoot.OpenSubKey($"{progId}\\shell\\{verb}\\command");
        if (commandKey is null)
        {
            return;
        }

        var rawCommand = commandKey.GetValue(null)?.ToString();
        var executablePath = ResolvePathCandidate(rawCommand);
        if (string.IsNullOrWhiteSpace(executablePath) || ScanUtilities.PathExists(executablePath))
        {
            return;
        }

        var fullKeyPath = $"HKEY_CLASSES_ROOT\\{progId}\\shell\\{verb}\\command";
        candidates.Add(new CleanupCandidate(
            Guid.NewGuid().ToString("N"),
            CleanupCategory.RegistryEntry,
            $"{extension} -> {progId} ({verb})",
            fullKeyPath,
            "文件关联命令",
            $"文件关联命令指向的程序不存在：{executablePath}",
            ItemHealth.Broken,
            RiskLevel.Safe,
            false,
            true,
            true,
            Metadata: $"registry-key|{fullKeyPath}"));
    }

    private static void ScanBrokenUninstallEntries(List<CleanupCandidate> candidates)
    {
        foreach (var (root, path) in ScanUtilities.GetUninstallRoots())
        {
            using var baseKey = root.OpenSubKey(path);
            if (baseKey is null)
            {
                continue;
            }

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                using var subKey = baseKey.OpenSubKey(subKeyName);
                if (subKey is null)
                {
                    continue;
                }

                var displayName = ScanUtilities.SafeGetString(subKey, "DisplayName");
                var uninstallString = ScanUtilities.SafeGetString(subKey, "UninstallString");
                var quietUninstallString = ScanUtilities.SafeGetString(subKey, "QuietUninstallString");
                var installLocation = ScanUtilities.SafeGetString(subKey, "InstallLocation");
                var systemComponent = ScanUtilities.SafeGetString(subKey, "SystemComponent") == "1";
                var isProtected = systemComponent || ScanUtilities.IsProtectedPublisher(ScanUtilities.SafeGetString(subKey, "Publisher"), displayName);
                if (isProtected)
                {
                    continue;
                }

                var uninstallPath = ResolvePathCandidate(uninstallString);
                var quietUninstallPath = ResolvePathCandidate(quietUninstallString);
                var installExists = string.IsNullOrWhiteSpace(installLocation) || Directory.Exists(installLocation);
                var uninstallExists = ScanUtilities.PathExists(uninstallPath) || ScanUtilities.PathExists(quietUninstallPath);
                if (uninstallExists || installExists)
                {
                    continue;
                }

                var fullKeyPath = $"{root.Name}\\{path}\\{subKeyName}";
                candidates.Add(new CleanupCandidate(
                    Guid.NewGuid().ToString("N"),
                    CleanupCategory.RegistryEntry,
                    string.IsNullOrWhiteSpace(displayName) ? subKeyName : displayName,
                    fullKeyPath,
                    "失效卸载条目",
                    $"卸载命令与安装目录都无效：{(string.IsNullOrWhiteSpace(uninstallPath) ? quietUninstallPath : uninstallPath)}",
                    ItemHealth.Broken,
                    RiskLevel.Safe,
                    false,
                    true,
                    true,
                    Metadata: $"registry-key|{fullKeyPath}"));
            }
        }
    }

    private static void SearchKey(
        RegistryKey key,
        string hiveName,
        string currentPath,
        string query,
        RegistrySearchOptions options,
        List<RegistrySearchResult> results,
        CancellationToken cancellationToken)
    {
        if (results.Count >= options.MaxResults)
        {
            return;
        }

        var fullPath = string.IsNullOrWhiteSpace(currentPath) ? hiveName : $"{hiveName}\\{currentPath}";
        if (options.SearchKeyPath && fullPath.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new RegistrySearchResult(
                Guid.NewGuid().ToString("N"),
                hiveName,
                currentPath,
                string.Empty,
                string.Empty,
                string.Empty,
                "Key",
                "键路径",
                CanEdit: false,
                CanDelete: !string.IsNullOrWhiteSpace(currentPath),
                Metadata: $"reg-key|{Escape(hiveName)}|{Escape(currentPath)}"));
        }

        try
        {
            foreach (var valueName in key.GetValueNames())
            {
                if (results.Count >= options.MaxResults)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var valueData = FormatRegistryValue(key.GetValue(valueName));
                var displayName = string.IsNullOrEmpty(valueName) ? "(默认)" : valueName;
                var matchesName = options.SearchValueName && displayName.Contains(query, StringComparison.OrdinalIgnoreCase);
                var matchesData = options.SearchValueData && valueData.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!matchesName && !matchesData)
                {
                    continue;
                }

                var kind = key.GetValueKind(valueName);
                results.Add(new RegistrySearchResult(
                    Guid.NewGuid().ToString("N"),
                    hiveName,
                    currentPath,
                    displayName,
                    valueData,
                    kind.ToString(),
                    "Value",
                    matchesName && matchesData ? "值名称/数据" : matchesName ? "值名称" : "值数据",
                    CanEdit: IsEditable(kind),
                    CanDelete: true,
                    Metadata: $"reg-value|{Escape(hiveName)}|{Escape(currentPath)}|{Escape(valueName)}|{kind}"));
            }
        }
        catch
        {
        }

        try
        {
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                if (results.Count >= options.MaxResults)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                using var subKey = key.OpenSubKey(subKeyName);
                if (subKey is null)
                {
                    continue;
                }

                var nextPath = string.IsNullOrWhiteSpace(currentPath) ? subKeyName : $"{currentPath}\\{subKeyName}";
                SearchKey(subKey, hiveName, nextPath, query, options, results, cancellationToken);
            }
        }
        catch
        {
        }
    }

    private static string FormatRegistryValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            string[] texts => string.Join("; ", texts),
            byte[] bytes => Convert.ToHexString(bytes),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool IsEditable(RegistryValueKind kind)
    {
        return kind is RegistryValueKind.String or RegistryValueKind.ExpandString or RegistryValueKind.MultiString or RegistryValueKind.DWord or RegistryValueKind.QWord;
    }

    private static string ResolveClsidServerPath(string clsid)
    {
        var normalized = clsid.Trim();
        using var inprocServer = Registry.ClassesRoot.OpenSubKey($"CLSID\\{normalized}\\InprocServer32");
        var inprocPath = ResolvePathCandidate(inprocServer?.GetValue(null)?.ToString());
        if (!string.IsNullOrWhiteSpace(inprocPath))
        {
            return inprocPath;
        }

        using var localServer = Registry.ClassesRoot.OpenSubKey($"CLSID\\{normalized}\\LocalServer32");
        return ResolvePathCandidate(localServer?.GetValue(null)?.ToString());
    }

    private static string ResolvePathCandidate(string? rawValue)
    {
        var executablePath = ScanUtilities.ExtractExecutablePath(rawValue);
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            return executablePath;
        }

        return ScanUtilities.ExpandPath(rawValue);
    }

    private static bool DeleteSearchResult(RegistrySearchResult result)
    {
        var parts = result.Metadata.Split('|');
        if (parts.Length < 3)
        {
            return false;
        }

        var root = OpenHive(Unescape(parts[1]));
        var keyPath = Unescape(parts[2]);
        if (parts[0] == "reg-key")
        {
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                return false;
            }

            var split = keyPath.LastIndexOf('\\');
            var parentPath = split >= 0 ? keyPath[..split] : string.Empty;
            var childName = split >= 0 ? keyPath[(split + 1)..] : keyPath;
            using var parent = string.IsNullOrWhiteSpace(parentPath) ? root : root.OpenSubKey(parentPath, writable: true);
            parent?.DeleteSubKeyTree(childName, throwOnMissingSubKey: false);
            return true;
        }

        if (parts[0] == "reg-value" && parts.Length >= 4)
        {
            using var key = string.IsNullOrWhiteSpace(keyPath) ? root : root.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(Unescape(parts[3]), throwOnMissingValue: false);
            return true;
        }

        return false;
    }

    private static bool UpdateSearchResult(RegistrySearchResult result, string newValue)
    {
        var parts = result.Metadata.Split('|');
        if (parts.Length < 5 || parts[0] != "reg-value")
        {
            return false;
        }

        var root = OpenHive(Unescape(parts[1]));
        var keyPath = Unescape(parts[2]);
        var valueName = Unescape(parts[3]);
        if (!Enum.TryParse<RegistryValueKind>(parts[4], out var kind))
        {
            return false;
        }

        using var key = string.IsNullOrWhiteSpace(keyPath) ? root : root.OpenSubKey(keyPath, writable: true);
        if (key is null)
        {
            return false;
        }

        var converted = ConvertEditedValue(kind, newValue);
        key.SetValue(valueName, converted, kind);
        return true;
    }

    private static object ConvertEditedValue(RegistryValueKind kind, string newValue)
    {
        return kind switch
        {
            RegistryValueKind.String => newValue,
            RegistryValueKind.ExpandString => newValue,
            RegistryValueKind.MultiString => newValue.Split([Environment.NewLine, ";"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            RegistryValueKind.DWord => ParseInteger(newValue),
            RegistryValueKind.QWord => ParseLong(newValue),
            _ => newValue
        };
    }

    private static int ParseInteger(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt32(value[2..], 16);
        }

        return int.Parse(value);
    }

    private static long ParseLong(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt64(value[2..], 16);
        }

        return long.Parse(value);
    }

    private static RegistryKey OpenHive(string hiveName)
    {
        return hiveName switch
        {
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            _ => Registry.CurrentUser
        };
    }

    private static string Escape(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string Unescape(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
}