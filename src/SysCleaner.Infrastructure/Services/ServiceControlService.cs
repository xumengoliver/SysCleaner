using Microsoft.Win32;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Matching;
using SysCleaner.Domain.Models;
using System.Diagnostics;

namespace SysCleaner.Infrastructure.Services;

public sealed class ServiceControlService(IInstalledAppService installedAppService, IHistoryService historyService) : IServiceControlService
{
    private const string ServicesRegistryPath = @"SYSTEM\CurrentControlSet\Services";
    private const int Win32OwnProcess = 0x10;
    private const int Win32ShareProcess = 0x20;

    public Task<IReadOnlyList<CleanupCandidate>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var results = new List<CleanupCandidate>();
            var installedApps = await installedAppService.GetInstalledAppsAsync(cancellationToken);
            using var servicesKey = Registry.LocalMachine.OpenSubKey(ServicesRegistryPath);
            if (servicesKey is null)
            {
                return (IReadOnlyList<CleanupCandidate>)results;
            }

            foreach (var serviceName in servicesKey.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var serviceKey = servicesKey.OpenSubKey(serviceName);
                if (serviceKey is null || !IsWin32Service(serviceKey))
                {
                    continue;
                }

                var candidate = CreateCandidate(serviceName, serviceKey, installedApps);
                if (candidate is not null)
                {
                    results.Add(candidate);
                }
            }

            return (IReadOnlyList<CleanupCandidate>)results
                .OrderByDescending(candidate => candidate.Health)
                .ThenBy(candidate => candidate.Title)
                .ToList();
        }, cancellationToken);
    }

    public async Task<OperationResult> StopAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return new OperationResult(false, "未识别到服务名。");
        }

        try
        {
            var stopResult = await RunScAsync(cancellationToken, "stop", serviceName);
            if (stopResult.ExitCode == 0 || IsIgnorableStopResult(stopResult.Output))
            {
                var message = stopResult.ExitCode == 0
                    ? $"已向服务 {serviceName} 发送停止请求。"
                    : $"服务 {serviceName} 当前未运行，无需停止。";
                await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Service", "Stop", serviceName, "Success", message), cancellationToken);
                return new OperationResult(true, message);
            }

            var failure = string.IsNullOrWhiteSpace(stopResult.Output) ? "sc stop 执行失败。" : stopResult.Output;
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Service", "Stop", serviceName, "Failed", failure), cancellationToken);
            return new OperationResult(false, failure);
        }
        catch (Exception ex)
        {
            var message = ScanUtilities.DescribeException(ex);
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Service", "Stop", serviceName, "Failed", message), cancellationToken);
            return new OperationResult(false, message);
        }
    }

    public async Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default)
    {
        var serviceName = ExtractServiceName(candidate);
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return new OperationResult(false, "未识别到服务名。");
        }

        if (!candidate.CanDelete)
        {
            return new OperationResult(false, "受保护服务不能卸载。请先确认该服务是否为第三方服务。");
        }

        try
        {
            var stopResult = await RunScAsync(cancellationToken, "stop", serviceName);
            if (stopResult.ExitCode != 0 && !IsIgnorableStopResult(stopResult.Output))
            {
                await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Service", "Delete", serviceName, "Failed", stopResult.Output), cancellationToken);
                return new OperationResult(false, stopResult.Output);
            }

            var deleteResult = await RunScAsync(cancellationToken, "delete", serviceName);
            if (deleteResult.ExitCode == 0)
            {
                var message = $"已提交服务 {serviceName} 的卸载请求。若服务仍在运行，系统会在停止后完成删除。";
                await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Service", "Delete", serviceName, "Success", message), cancellationToken);
                return new OperationResult(true, message);
            }

            var failure = string.IsNullOrWhiteSpace(deleteResult.Output) ? "sc delete 执行失败。" : deleteResult.Output;
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Service", "Delete", serviceName, "Failed", failure), cancellationToken);
            return new OperationResult(false, failure);
        }
        catch (Exception ex)
        {
            var message = ScanUtilities.DescribeException(ex);
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Service", "Delete", serviceName, "Failed", message), cancellationToken);
            return new OperationResult(false, message);
        }
    }

    private static CleanupCandidate? CreateCandidate(string serviceName, RegistryKey serviceKey, IReadOnlyList<InstalledApp> installedApps)
    {
        var displayName = ScanUtilities.SafeGetString(serviceKey, "DisplayName");
        var description = ScanUtilities.SafeGetString(serviceKey, "Description");
        var imagePath = ScanUtilities.SafeGetString(serviceKey, "ImagePath");
        var executablePath = ScanUtilities.ExtractExecutablePath(imagePath);
        var serviceDllPath = ReadServiceDllPath(serviceKey);
        var targetPath = !string.IsNullOrWhiteSpace(serviceDllPath) ? serviceDllPath : executablePath;
        var targetExists = ScanUtilities.PathExists(targetPath) || ScanUtilities.PathExists(executablePath);
        var sharedHost = IsSharedHost(executablePath);
        var protectedEntry = IsProtectedService(serviceName, displayName, targetPath, executablePath, serviceDllPath);
        InstalledAppMatcher.TryMatch(installedApps, [serviceName, displayName, description, targetPath, imagePath], out var matchedApp);

        var health = protectedEntry
            ? ItemHealth.Protected
            : !string.IsNullOrWhiteSpace(targetPath) && !targetExists
                ? ItemHealth.Broken
                : string.IsNullOrWhiteSpace(targetPath)
                    ? ItemHealth.Review
                    : ItemHealth.Healthy;

        var evidence = protectedEntry
            ? "系统目录或系统命名空间下的服务，默认保护。"
            : !string.IsNullOrWhiteSpace(targetPath) && !targetExists
                ? "服务注册存在，但目标文件缺失。"
                : string.IsNullOrWhiteSpace(targetPath)
                    ? "未解析出服务二进制，建议人工复核后再处理。"
                    : sharedHost
                        ? "服务由共享宿主加载，删除前请确认不是系统组件。"
                        : "检测到第三方服务注册，可按需卸载。";

        var title = string.IsNullOrWhiteSpace(displayName) ? serviceName : displayName;
        var source = string.IsNullOrWhiteSpace(description)
            ? $"服务名：{serviceName}"
            : $"服务名：{serviceName} | {description}";

        var candidate = new CleanupCandidate(
            Guid.NewGuid().ToString("N"),
            CleanupCategory.Service,
            title,
            targetPath,
            source,
            evidence,
            health,
            protectedEntry ? RiskLevel.Protected : sharedHost ? RiskLevel.Review : ScanUtilities.ToRisk(health),
            false,
            !protectedEntry,
            false,
            matchedApp?.Id ?? string.Empty,
            $"service|{serviceName}");

        if (candidate.Health == ItemHealth.Broken || !protectedEntry || !string.IsNullOrWhiteSpace(candidate.RelatedAppId))
        {
            return candidate;
        }

        return null;
    }

    private static bool IsWin32Service(RegistryKey serviceKey)
    {
        var typeValue = serviceKey.GetValue("Type");
        if (typeValue is not int type)
        {
            return false;
        }

        return (type & Win32OwnProcess) != 0 || (type & Win32ShareProcess) != 0;
    }

    private static string ReadServiceDllPath(RegistryKey serviceKey)
    {
        using var parametersKey = serviceKey.OpenSubKey("Parameters");
        if (parametersKey is null)
        {
            return string.Empty;
        }

        var rawPath = ScanUtilities.SafeGetString(parametersKey, "ServiceDll");
        return ScanUtilities.ExtractExecutablePath(rawPath);
    }

    private static bool IsSharedHost(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        return fileName.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("services.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProtectedService(string serviceName, string displayName, string targetPath, string executablePath, string serviceDllPath)
    {
        return ScanUtilities.IsProtectedPublisher(displayName, serviceName)
            || ScanUtilities.IsPathUnderWindowsDirectory(targetPath)
            || ScanUtilities.IsPathUnderWindowsDirectory(executablePath)
            || ScanUtilities.IsPathUnderWindowsDirectory(serviceDllPath);
    }

    private static string ExtractServiceName(CleanupCandidate candidate)
    {
        const string prefix = "service|";
        return candidate.Metadata.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? candidate.Metadata[prefix.Length..]
            : string.Empty;
    }

    private static bool IsIgnorableStopResult(string output)
    {
        return output.Contains("1062", StringComparison.OrdinalIgnoreCase)
            || output.Contains("service has not been started", StringComparison.OrdinalIgnoreCase)
            || output.Contains("SERVICE_NOT_ACTIVE", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int ExitCode, string Output)> RunScAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = "sc.exe";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var output = string.Join(Environment.NewLine, new[] { await stdOutTask, await stdErrTask }
            .Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
        return (process.ExitCode, output);
    }
}