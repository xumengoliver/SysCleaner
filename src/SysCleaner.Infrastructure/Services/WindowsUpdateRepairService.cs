using Microsoft.Win32;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;
using SysCleaner.Domain.Repair;
using System.Diagnostics;
using System.Text.Json;
using System.Text;

namespace SysCleaner.Infrastructure.Services;

public sealed class WindowsUpdateRepairService(IHistoryService historyService) : IWindowsUpdateRepairService
{
    private static readonly string[] CoreServices = ["wuauserv", "bits", "cryptsvc", "TrustedInstaller"];

    public Task<IReadOnlyList<SystemRepairItem>> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<SystemRepairItem>>(() =>
        {
            var services = CoreServices.Select(GetServiceState).ToList();
            var softwareDistributionCount = CountFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"));
            var downloaderCount = CountFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Network", "Downloader"));
            var pendingReboot = DetectPendingReboot();

            return WindowsUpdateDiagnostics.BuildItems(services, softwareDistributionCount, downloaderCount, pendingReboot);
        }, cancellationToken);
    }

    public async Task<WindowsUpdateOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var pendingReboot = DetectPendingReboot();
        var installedUpdatesTask = GetInstalledUpdatesAsync(cancellationToken);
        var eventsTask = GetUpdateEventsAsync(cancellationToken);

        await Task.WhenAll(installedUpdatesTask, eventsTask);

        var updateEvents = eventsTask.Result;
        var latestSuccess = updateEvents
            .Where(item => string.Equals(item.Result, "成功", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Timestamp)
            .FirstOrDefault();

        return new WindowsUpdateOverview(
            pendingReboot,
            latestSuccess?.Timestamp,
            latestSuccess?.Title ?? string.Empty,
            installedUpdatesTask.Result,
            updateEvents
                .Where(item => string.Equals(item.Result, "失败", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Timestamp)
                .Take(30)
                .ToList());
    }

    public async Task<OperationResult> UninstallUpdateAsync(WindowsInstalledUpdate update, CancellationToken cancellationToken = default)
    {
        if (!update.CanUninstall || string.IsNullOrWhiteSpace(update.KbId))
        {
            return new OperationResult(false, "当前更新项不支持卸载。");
        }

        try
        {
            var kbNumber = update.KbId.StartsWith("KB", StringComparison.OrdinalIgnoreCase) ? update.KbId[2..] : update.KbId;
            Process.Start(new ProcessStartInfo("wusa.exe", $"/uninstall /kb:{kbNumber}")
            {
                UseShellExecute = true
            });
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "WindowsUpdate", "UninstallUpdate", update.KbId, "Started", update.Title), cancellationToken);
            return new OperationResult(true, $"已启动 {update.KbId} 的卸载流程。");
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "WindowsUpdate", "UninstallUpdate", update.KbId, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public async Task<OperationResult> RestartCoreServicesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteRecipeAsync(
            "WindowsUpdate",
            "RestartCoreServices",
            "core-services",
            cancellationToken,
            [
                ("net.exe", "stop bits"),
                ("net.exe", "stop wuauserv"),
                ("net.exe", "stop cryptsvc"),
                ("net.exe", "start cryptsvc"),
                ("net.exe", "start bits"),
                ("net.exe", "start wuauserv"),
                ("net.exe", "start TrustedInstaller")
            ],
            "已执行 Windows Update 核心服务重启动作。");
    }

    public async Task<OperationResult> ResetWindowsUpdateComponentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stopResult = await ExecuteRecipeAsync(
                "WindowsUpdate",
                "ResetComponents-StopServices",
                "reset-components",
                cancellationToken,
                [
                    ("net.exe", "stop bits"),
                    ("net.exe", "stop wuauserv"),
                    ("net.exe", "stop cryptsvc")
                ],
                "已停止更新相关服务。",
                logResult: false);

            if (!stopResult.Success)
            {
                await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "WindowsUpdate", "ResetComponents", "reset-components", "Failed", stopResult.Message), cancellationToken);
                return stopResult;
            }

            var renamedTargets = new List<string>();
            ResetDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution"), renamedTargets);
            ResetDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "catroot2"), renamedTargets);

            var startResult = await ExecuteRecipeAsync(
                "WindowsUpdate",
                "ResetComponents-StartServices",
                "reset-components",
                cancellationToken,
                [
                    ("net.exe", "start cryptsvc"),
                    ("net.exe", "start bits"),
                    ("net.exe", "start wuauserv")
                ],
                "已重新启动更新服务。",
                logResult: false);

            if (!startResult.Success)
            {
                await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "WindowsUpdate", "ResetComponents", "reset-components", "Failed", startResult.Message), cancellationToken);
                return startResult;
            }

            var detail = renamedTargets.Count == 0
                ? "已执行 Windows Update 组件重置流程。"
                : $"已重置以下目录：{string.Join("; ", renamedTargets)}";
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "WindowsUpdate", "ResetComponents", "reset-components", "Success", detail), cancellationToken);
            return new OperationResult(true, detail);
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "WindowsUpdate", "ResetComponents", "reset-components", "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public async Task<OperationResult> RunDismRestoreHealthAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteRecipeAsync(
            "WindowsUpdate",
            "DISMRestoreHealth",
            "DISM",
            cancellationToken,
            [("DISM.exe", "/Online /Cleanup-Image /RestoreHealth")],
            "DISM RestoreHealth 执行完成。可能需要查看历史记录中的详细输出。",
            outputLimit: 1200);
    }

    public async Task<OperationResult> RunSfcScanAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteRecipeAsync(
            "WindowsUpdate",
            "SFCScan",
            "SFC",
            cancellationToken,
            [("sfc.exe", "/scannow")],
            "SFC 系统文件扫描执行完成。可能需要查看历史记录中的详细输出。",
            outputLimit: 1200);
    }

    private async Task<OperationResult> ExecuteRecipeAsync(
        string module,
        string action,
        string target,
        CancellationToken cancellationToken,
        IReadOnlyList<(string FileName, string Arguments)> commands,
        string successMessage,
        bool logResult = true,
        int outputLimit = 600)
    {
        var output = new StringBuilder();

        foreach (var command in commands)
        {
            var (success, commandOutput) = await RunProcessAsync(command.FileName, command.Arguments, cancellationToken);
            if (!string.IsNullOrWhiteSpace(commandOutput))
            {
                if (output.Length > 0)
                {
                    output.AppendLine();
                }

                output.AppendLine(commandOutput.Trim());
            }

            if (!success)
            {
                var failure = TrimOutput(output.ToString(), outputLimit);
                if (logResult)
                {
                    await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, module, action, target, "Failed", failure), cancellationToken);
                }

                return new OperationResult(false, failure);
            }
        }

        var detail = TrimOutput(string.IsNullOrWhiteSpace(output.ToString()) ? successMessage : output.ToString(), outputLimit);
        if (logResult)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, module, action, target, "Success", detail), cancellationToken);
        }

        return new OperationResult(true, successMessage);
    }

    private static void ResetDirectory(string directoryPath, List<string> renamedTargets)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        var backupPath = directoryPath + ".syscleaner.bak." + DateTime.Now.ToString("yyyyMMddHHmmss");
        Directory.Move(directoryPath, backupPath);
        renamedTargets.Add(backupPath);
    }

    private static int CountFiles(string directoryPath)
    {
        try
        {
            return Directory.Exists(directoryPath)
                ? Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).Take(5000).Count()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static async Task<IReadOnlyList<WindowsInstalledUpdate>> GetInstalledUpdatesAsync(CancellationToken cancellationToken)
    {
        const string command = "Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 50 HotFixID, Description, InstalledOn, InstalledBy | ConvertTo-Json -Compress";
        var (success, output) = await RunPowerShellAsync(command, cancellationToken);
        if (!success || string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var items = new List<WindowsInstalledUpdate>();
            foreach (var element in EnumerateJsonItems(document.RootElement))
            {
                items.Add(new WindowsInstalledUpdate(
                    GetJsonString(element, "HotFixID"),
                    GetJsonString(element, "Description"),
                    ClassifyUpdateType(GetJsonString(element, "Description"), GetJsonString(element, "HotFixID")),
                    IsSecurityUpdate(GetJsonString(element, "Description"), GetJsonString(element, "HotFixID")),
                    GetJsonDateTime(element, "InstalledOn"),
                    GetJsonString(element, "InstalledBy"),
                    CanUninstall(GetJsonString(element, "HotFixID"))));
            }

            return items
                .Where(item => !string.IsNullOrWhiteSpace(item.KbId) || !string.IsNullOrWhiteSpace(item.Title))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<WindowsUpdateEventRecord>> GetUpdateEventsAsync(CancellationToken cancellationToken)
    {
        const string command = @"
$events = @()
try {
  $events = Get-WinEvent -LogName 'Microsoft-Windows-WindowsUpdateClient/Operational' -MaxEvents 200 -ErrorAction Stop |
    Where-Object { $_.Id -in 19,20 } |
    Select-Object -First 80 TimeCreated, Id, Message
} catch {
  $events = Get-WinEvent -FilterHashtable @{ LogName='System'; ProviderName='Microsoft-Windows-WindowsUpdateClient'; Id=19,20 } -MaxEvents 200 -ErrorAction SilentlyContinue |
    Select-Object -First 80 TimeCreated, Id, Message
}
$events | ConvertTo-Json -Compress";

        var (success, output) = await RunPowerShellAsync(command, cancellationToken);
        if (!success || string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var items = new List<WindowsUpdateEventRecord>();
            foreach (var element in EnumerateJsonItems(document.RootElement))
            {
                var message = GetJsonString(element, "Message");
                var eventId = GetJsonInt32(element, "Id");
                items.Add(new WindowsUpdateEventRecord(
                    GetJsonDateTime(element, "TimeCreated"),
                    WindowsUpdateEventParser.GetResult(eventId),
                    WindowsUpdateEventParser.BuildTitle(message),
                    ExtractErrorCode(message),
                    message));
            }

            return items;
        }
        catch
        {
            return [];
        }
    }

    private static WindowsUpdateServiceState GetServiceState(string serviceName)
    {
        var queryOutput = RunProcess("sc.exe", $"query {serviceName}");
        if (queryOutput.ExitCode != 0)
        {
            return new WindowsUpdateServiceState(serviceName, Exists: false, IsRunning: false, IsAutoStart: false);
        }

        var configOutput = RunProcess("sc.exe", $"qc {serviceName}");
        var running = queryOutput.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        var autoStart = configOutput.Output.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase);
        return new WindowsUpdateServiceState(serviceName, Exists: true, IsRunning: running, IsAutoStart: autoStart);
    }

    private static bool DetectPendingReboot()
    {
        try
        {
            return Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is not null
                || Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null
                || Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager")?.GetValue("PendingFileRenameOperations") is not null;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(bool Success, string Output)> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);

        var output = string.Join(Environment.NewLine, new[] { await stdOutTask, await stdErrTask }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        return (process.ExitCode == 0, output);
    }

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            var output = string.Join(Environment.NewLine, new[] { stdOut, stdErr }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
            return (process.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private static async Task<(bool Success, string Output)> RunPowerShellAsync(string command, CancellationToken cancellationToken)
    {
        return await RunProcessAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"", cancellationToken);
    }

    private static IEnumerable<JsonElement> EnumerateJsonItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
        }
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : string.Empty;
    }

    private static int GetJsonInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
            _ => 0
        };
    }

    private static DateTime? GetJsonDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String && DateTime.TryParse(property.GetString(), out var value))
        {
            return value;
        }

        return null;
    }

    private static string ExtractErrorCode(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var match = System.Text.RegularExpressions.Regex.Match(message, @"0x[0-9a-fA-F]{8}");
        return match.Success ? match.Value : string.Empty;
    }

    private static string ClassifyUpdateType(string description, string kbId)
    {
        var source = $"{description} {kbId}";
        if (source.Contains("Security", StringComparison.OrdinalIgnoreCase) || source.Contains("安全", StringComparison.OrdinalIgnoreCase))
        {
            return "安全更新";
        }

        if (source.Contains("Feature", StringComparison.OrdinalIgnoreCase) || source.Contains("功能", StringComparison.OrdinalIgnoreCase))
        {
            return "功能更新";
        }

        if (source.Contains("Cumulative", StringComparison.OrdinalIgnoreCase) || source.Contains("累计", StringComparison.OrdinalIgnoreCase))
        {
            return "累计更新";
        }

        if (source.Contains("Service Pack", StringComparison.OrdinalIgnoreCase))
        {
            return "服务包";
        }

        return "常规更新";
    }

    private static bool IsSecurityUpdate(string description, string kbId)
    {
        return string.Equals(ClassifyUpdateType(description, kbId), "安全更新", StringComparison.Ordinal);
    }

    private static bool CanUninstall(string kbId)
    {
        return !string.IsNullOrWhiteSpace(kbId) && kbId.StartsWith("KB", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimOutput(string output, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        return output.Length <= maxLength ? output : output[..maxLength] + "...";
    }
}