using Microsoft.Win32;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Domain.Models;
using SysCleaner.Infrastructure.Services;

namespace SysCleaner.Tests.Application;

public sealed class RegistryCleanupServiceTests
{
    [Fact]
    public async Task ScanBrokenEntriesAsync_FindsBrokenRunValueAndBrokenAppPathKey()
    {
        var unique = "SysCleaner-Test-" + Guid.NewGuid().ToString("N");
        var runValueName = unique + "-Run";
        var appPathKeyName = unique + ".exe";
        var missingRunPath = Path.Combine(Path.GetTempPath(), unique, "missing-run.exe");
        var missingAppPath = Path.Combine(Path.GetTempPath(), unique, "missing-app.exe");

        using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        using var appPathsRoot = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths", writable: true);
        using var appPathKey = appPathsRoot?.CreateSubKey(appPathKeyName, writable: true);

        runKey?.SetValue(runValueName, $"\"{missingRunPath}\"");
        appPathKey?.SetValue(null, missingAppPath);

        try
        {
            var service = new RegistryCleanupService(new FakeHistoryService());

            var results = await service.ScanBrokenEntriesAsync();

            Assert.Contains(results, item => item.Title == runValueName && item.Metadata == $"registry-value|HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run|{runValueName}");
            Assert.Contains(results, item => item.Title == appPathKeyName && item.Metadata == $"registry-key|HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\{appPathKeyName}");
        }
        finally
        {
            runKey?.DeleteValue(runValueName, throwOnMissingValue: false);
            appPathsRoot?.DeleteSubKeyTree(appPathKeyName, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task ScanBrokenEntriesAsync_FindsBrokenShellCommandAndShellHandler()
    {
        var unique = "SysCleaner-Test-" + Guid.NewGuid().ToString("N");
        var commandKeyName = unique + "-Command";
        var handlerKeyName = unique + "-Handler";
        var clsid = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
        var missingCommandPath = Path.Combine(Path.GetTempPath(), unique, "missing-context.exe");
        var missingDllPath = Path.Combine(Path.GetTempPath(), unique, "missing-handler.dll");

        using var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\*\shell\{commandKeyName}\command", writable: true);
        using var handlerKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\*\shellex\ContextMenuHandlers\{handlerKeyName}", writable: true);
        using var clsidKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{clsid}\InprocServer32", writable: true);

        commandKey?.SetValue(null, $"\"{missingCommandPath}\" \"%1\"");
        handlerKey?.SetValue(null, clsid);
        clsidKey?.SetValue(null, missingDllPath);

        try
        {
            var service = new RegistryCleanupService(new FakeHistoryService());

            var results = await service.ScanBrokenEntriesAsync();

            Assert.Contains(results, item => item.Title == commandKeyName && item.Metadata == $"registry-key|HKEY_CLASSES_ROOT\\*\\shell\\{commandKeyName}\\command");
            Assert.Contains(results, item => item.Title == handlerKeyName && item.Metadata == $"registry-key|HKEY_CLASSES_ROOT\\*\\shellex\\ContextMenuHandlers\\{handlerKeyName}");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\*\shell\{commandKeyName}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\*\shellex\ContextMenuHandlers\{handlerKeyName}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{clsid}", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task ScanBrokenEntriesAsync_FindsBrokenFileAssociationCommand()
    {
        var unique = "SysCleaner-Test-" + Guid.NewGuid().ToString("N");
        var extension = "." + unique;
        var progId = unique + ".File";
        var missingPath = Path.Combine(Path.GetTempPath(), unique, "missing-open.exe");

        using var extensionKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}", writable: true);
        using var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\open\command", writable: true);

        extensionKey?.SetValue(null, progId);
        commandKey?.SetValue(null, $"\"{missingPath}\" \"%1\"");

        try
        {
            var service = new RegistryCleanupService(new FakeHistoryService());

            var results = await service.ScanBrokenEntriesAsync();

            Assert.Contains(results, item => item.Title == $"{extension} -> {progId} (open)" && item.Source == "文件关联命令");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{extension}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{progId}", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task ScanBrokenEntriesAsync_FindsBrokenUninstallEntryAndBho()
    {
        var unique = "SysCleaner-Test-" + Guid.NewGuid().ToString("N");
        var uninstallKeyName = unique + "-Uninstall";
        var clsid = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
        var missingUninstallPath = Path.Combine(Path.GetTempPath(), unique, "missing-uninstall.exe");
        var missingInstallDir = Path.Combine(Path.GetTempPath(), unique, "MissingInstall");
        var missingBhoPath = Path.Combine(Path.GetTempPath(), unique, "missing-bho.dll");

        using var uninstallKey = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{uninstallKeyName}", writable: true);
        using var bhoKey = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects\{clsid}", writable: true);
        using var clsidKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{clsid}\InprocServer32", writable: true);

        uninstallKey?.SetValue("DisplayName", uninstallKeyName);
        uninstallKey?.SetValue("UninstallString", $"\"{missingUninstallPath}\"");
        uninstallKey?.SetValue("InstallLocation", missingInstallDir);
        clsidKey?.SetValue(null, missingBhoPath);

        try
        {
            var service = new RegistryCleanupService(new FakeHistoryService());

            var results = await service.ScanBrokenEntriesAsync();

            Assert.Contains(results, item => item.Title == uninstallKeyName && item.Source == "失效卸载条目");
            Assert.Contains(results, item => item.Title == clsid && item.Source == "当前用户 BHO");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{uninstallKeyName}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects\{clsid}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{clsid}", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task ScanBrokenEntriesAsync_FindsBrokenProtocolHandlerAndMuiCache()
    {
        var unique = "SysCleaner-Test-" + Guid.NewGuid().ToString("N");
        var protocolName = unique + "-protocol";
        var missingProtocolPath = Path.Combine(Path.GetTempPath(), unique, "missing-protocol.exe");
        var missingMuiPath = Path.Combine(Path.GetTempPath(), unique, "missing-mui.exe");

        using var protocolKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocolName}", writable: true);
        using var protocolCommandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocolName}\shell\open\command", writable: true);
        using var muiKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", writable: true);

        protocolKey?.SetValue("URL Protocol", string.Empty);
        protocolCommandKey?.SetValue(null, $"\"{missingProtocolPath}\" \"%1\"");
        muiKey?.SetValue(missingMuiPath, "Missing App");

        try
        {
            var service = new RegistryCleanupService(new FakeHistoryService());

            var results = await service.ScanBrokenEntriesAsync();

            Assert.Contains(results, item => item.Title == protocolName && item.Source == "协议处理器");
            Assert.Contains(results, item => item.Source == "当前用户 MuiCache" && item.Evidence.Contains(missingMuiPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{protocolName}", throwOnMissingSubKey: false);
            muiKey?.DeleteValue(missingMuiPath, throwOnMissingValue: false);
        }
    }

    [Fact]
    public async Task ScanBrokenEntriesAsync_FindsBrokenSharedDll()
    {
        var unique = "SysCleaner-Test-" + Guid.NewGuid().ToString("N");
        var missingDllPath = Path.Combine(Path.GetTempPath(), unique, "missing-shared.dll");

        RegistryKey? key;
        try
        {
            key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDlls", writable: true);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        key?.SetValue(missingDllPath, 1, RegistryValueKind.DWord);

        try
        {
            var service = new RegistryCleanupService(new FakeHistoryService());

            var results = await service.ScanBrokenEntriesAsync();

            Assert.Contains(results, item => item.Source == "SharedDlls" && item.Evidence.Contains(missingDllPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            key?.DeleteValue(missingDllPath, throwOnMissingValue: false);
        }
    }

    [Fact]
    public async Task DeleteAsync_DeletesRegistryValueAndRegistryKeyCandidates()
    {
        var unique = "SysCleaner-Test-" + Guid.NewGuid().ToString("N");
        var runValueName = unique + "-Delete";
        var appPathKeyName = unique + ".exe";

        using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        using var appPathsRoot = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths", writable: true);
        using var appPathKey = appPathsRoot?.CreateSubKey(appPathKeyName, writable: true);

        runKey?.SetValue(runValueName, @"C:\Missing\ghost.exe");
        appPathKey?.SetValue(null, @"C:\Missing\ghost.exe");

        var cleanup = new CleanupExecutionService(new FakeHistoryService());
        var valueCandidate = new CleanupCandidate(
            "1",
            SysCleaner.Domain.Enums.CleanupCategory.RegistryEntry,
            runValueName,
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            "当前用户启动项",
            "测试值",
            SysCleaner.Domain.Enums.ItemHealth.Broken,
            SysCleaner.Domain.Enums.RiskLevel.Safe,
            false,
            true,
            true,
            Metadata: $"registry-value|HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run|{runValueName}");
        var keyCandidate = new CleanupCandidate(
            "2",
            SysCleaner.Domain.Enums.CleanupCategory.RegistryEntry,
            appPathKeyName,
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\App Paths\" + appPathKeyName,
            "当前用户 App Paths",
            "测试键",
            SysCleaner.Domain.Enums.ItemHealth.Broken,
            SysCleaner.Domain.Enums.RiskLevel.Safe,
            false,
            true,
            true,
            Metadata: $"registry-key|HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\{appPathKeyName}");

        try
        {
            var valueResult = await cleanup.DeleteAsync(valueCandidate);
            var keyResult = await cleanup.DeleteAsync(keyCandidate);

            Assert.True(valueResult.Success);
            Assert.True(keyResult.Success);
            Assert.Null(runKey?.GetValue(runValueName));
            Assert.Null(appPathsRoot?.OpenSubKey(appPathKeyName));
        }
        finally
        {
            runKey?.DeleteValue(runValueName, throwOnMissingValue: false);
            appPathsRoot?.DeleteSubKeyTree(appPathKeyName, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task DeleteAsync_DeletesReadOnlyFileCandidate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SysCleaner.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "locked.txt");
        File.WriteAllText(filePath, "content");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        var cleanup = new CleanupExecutionService(new FakeHistoryService());
        var candidate = new CleanupCandidate(
            "readonly-file",
            SysCleaner.Domain.Enums.CleanupCategory.ResidualFile,
            "locked.txt",
            filePath,
            directory,
            "测试只读文件",
            SysCleaner.Domain.Enums.ItemHealth.Broken,
            SysCleaner.Domain.Enums.RiskLevel.Safe,
            false,
            true,
            false);

        try
        {
            var result = await cleanup.DeleteAsync(candidate);

            Assert.True(result.Success);
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class FakeHistoryService : IHistoryService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LogAsync(OperationLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OperationLogEntry>> GetRecentAsync(int take = 200, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OperationLogEntry>>([]);
    }
}