using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Matching;
using SysCleaner.Domain.Models;
using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace SysCleaner.Infrastructure.Services;

public sealed class ScheduledTaskService(IInstalledAppService installedAppService, IHistoryService historyService) : ITaskSchedulerService
{
    public Task<IReadOnlyList<CleanupCandidate>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var results = new List<CleanupCandidate>();
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType is null)
            {
                return (IReadOnlyList<CleanupCandidate>)results;
            }

            var installedApps = await installedAppService.GetInstalledAppsAsync(cancellationToken);
            var scheduler = Activator.CreateInstance(schedulerType);
            if (scheduler is null)
            {
                return (IReadOnlyList<CleanupCandidate>)results;
            }

            InvokeComMethod(scheduler, "Connect");
            var rootFolder = InvokeComMethod(scheduler, "GetFolder", "\\");
            if (rootFolder is not null)
            {
                EnumerateFolder(rootFolder, results, installedApps, cancellationToken);
            }

            return (IReadOnlyList<CleanupCandidate>)results
                .OrderByDescending(candidate => candidate.Health)
                .ThenBy(candidate => candidate.Title)
                .ToList();
        }, cancellationToken);
    }

    public Task<OperationResult> DisableAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default)
    {
        var taskPath = ExtractTaskPath(candidate);
        if (string.IsNullOrWhiteSpace(taskPath))
        {
            return Task.FromResult(new OperationResult(false, "未识别到计划任务路径。"));
        }

        return ExecuteSchtasksAsync("ScheduledTask", "Disable", taskPath, "已禁用计划任务。", cancellationToken, "/change", "/tn", taskPath, "/disable");
    }

    public Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default)
    {
        var taskPath = ExtractTaskPath(candidate);
        if (string.IsNullOrWhiteSpace(taskPath))
        {
            return Task.FromResult(new OperationResult(false, "未识别到计划任务路径。"));
        }

        return ExecuteSchtasksAsync("ScheduledTask", "Delete", taskPath, "已删除计划任务。", cancellationToken, "/delete", "/tn", taskPath, "/f");
    }

    private static void EnumerateFolder(object folder, List<CleanupCandidate> results, IReadOnlyList<InstalledApp> installedApps, CancellationToken cancellationToken)
    {
        foreach (var task in EnumerateComCollection(InvokeComMethod(folder, "GetTasks", 1)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var title = GetComProperty<string>(task, "Name");
            var taskPath = GetComProperty<string>(task, "Path");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(taskPath))
            {
                continue;
            }

            var enabled = GetComProperty<bool>(task, "Enabled");
            var actions = ReadActions(task);
            var actionPaths = actions
                .Select(action => Environment.ExpandEnvironmentVariables(action.Path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var missingActionCount = actionPaths.Count(path => !ScanUtilities.PathExists(path));
            var protectedEntry = taskPath.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase)
                || actions.Any(action => ScanUtilities.IsProtectedPublisher(action.Description, title));
            InstalledAppMatcher.TryMatch(installedApps, [title, taskPath, .. actions.Select(action => action.Description)], out var matchedApp);

            var health = protectedEntry
                ? ItemHealth.Protected
                : missingActionCount > 0
                    ? ItemHealth.Broken
                    : actionPaths.Count == 0
                        ? ItemHealth.Review
                        : enabled
                            ? ItemHealth.Healthy
                            : ItemHealth.Review;

            var risk = protectedEntry
                ? RiskLevel.Protected
                : missingActionCount > 0
                    ? RiskLevel.Review
                    : enabled
                        ? RiskLevel.Review
                        : RiskLevel.Safe;

            var evidence = missingActionCount > 0
                ? $"检测到 {missingActionCount} 个动作目标不存在"
                : actionPaths.Count == 0
                    ? "未解析出执行文件，建议人工复核"
                    : enabled
                        ? "计划任务动作目标存在"
                        : "计划任务当前处于禁用状态";

            var candidate = new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.ScheduledTask,
                title,
                actionPaths.FirstOrDefault() ?? string.Empty,
                taskPath,
                evidence,
                health,
                risk,
                !protectedEntry,
                !protectedEntry,
                true,
                matchedApp?.Id ?? string.Empty,
                $"task|{taskPath}");

            if (candidate.Health == ItemHealth.Broken || !protectedEntry || !string.IsNullOrWhiteSpace(candidate.RelatedAppId))
            {
                results.Add(candidate);
            }
        }

        foreach (var childFolder in EnumerateComCollection(InvokeComMethod(folder, "GetFolders", 0)))
        {
            EnumerateFolder(childFolder, results, installedApps, cancellationToken);
        }
    }

    private static IReadOnlyList<ScheduledTaskAction> ReadActions(object task)
    {
        var actions = new List<ScheduledTaskAction>();
        var definition = GetComProperty<object>(task, "Definition");
        var actionCollection = definition is null ? null : GetComProperty<object>(definition, "Actions");
        foreach (var action in EnumerateComCollection(actionCollection))
        {
            var path = GetComProperty<string>(action, "Path");
            var arguments = GetComProperty<string>(action, "Arguments");
            var workingDirectory = GetComProperty<string>(action, "WorkingDirectory");
            var description = string.Join(" ", new[] { path, arguments, workingDirectory }.Where(value => !string.IsNullOrWhiteSpace(value)));
            actions.Add(new ScheduledTaskAction(path ?? string.Empty, description));
        }

        return actions;
    }

    private async Task<OperationResult> ExecuteSchtasksAsync(string module, string action, string target, string successMessage, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "schtasks.exe";
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

            var output = string.Join(Environment.NewLine, new[] { await stdOutTask, await stdErrTask }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
            if (process.ExitCode == 0)
            {
                await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, module, action, target, "Success", successMessage), cancellationToken);
                return new OperationResult(true, successMessage);
            }

            var failure = string.IsNullOrWhiteSpace(output) ? "schtasks 执行失败。" : output;
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, module, action, target, "Failed", failure), cancellationToken);
            return new OperationResult(false, failure);
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, module, action, target, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    private static string ExtractTaskPath(CleanupCandidate candidate)
    {
        const string prefix = "task|";
        return candidate.Metadata.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? candidate.Metadata[prefix.Length..]
            : string.Empty;
    }

    private static IEnumerable<object> EnumerateComCollection(object? collection)
    {
        if (collection is not IEnumerable enumerable)
        {
            yield break;
        }

        foreach (var item in enumerable)
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static object? InvokeComMethod(object instance, string methodName, params object[] arguments)
    {
        try
        {
            return instance.GetType().InvokeMember(methodName, BindingFlags.InvokeMethod, null, instance, arguments);
        }
        catch
        {
            return null;
        }
    }

    private static T? GetComProperty<T>(object instance, string propertyName)
    {
        try
        {
            var value = instance.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, instance, null);
            if (value is null)
            {
                return default;
            }

            if (value is T typed)
            {
                return typed;
            }

            if (typeof(T) == typeof(object))
            {
                return (T)value;
            }

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(value, targetType);
        }
        catch
        {
            return default;
        }
    }

    private sealed record ScheduledTaskAction(string Path, string Description);
}