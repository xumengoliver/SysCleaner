using SysCleaner.Contracts.Interfaces;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;
using SysCleaner.Infrastructure.Interop;
using System.Diagnostics;

namespace SysCleaner.Infrastructure.Services;

public sealed class LockDetectionService : ILockDetectionService
{
    public Task<IReadOnlyList<LockInfo>> DetectLocksAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<LockInfo>>(() =>
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return [];
            }

            var sessionKey = Guid.NewGuid().ToString("N");
            var result = RestartManagerInterop.RmStartSession(out var handle, 0, sessionKey);
            if (result != 0)
            {
                return [new LockInfo(Guid.NewGuid().ToString("N"), targetPath, "未知占用", 0, string.Empty, string.Empty, LockKind.Unknown, RiskLevel.Review, false, false, "无法使用 Restart Manager 精确识别。", "可尝试关闭相关程序后重试")];
            }

            try
            {
                RestartManagerInterop.RmRegisterResources(handle, 1, [targetPath], 0, IntPtr.Zero, 0, null);
                uint needed;
                uint count = 0;
                uint reason = RestartManagerInterop.RmRebootReasonNone;
                var res = RestartManagerInterop.RmGetList(handle, out needed, ref count, null, ref reason);
                if (res == RestartManagerInterop.ErrorMoreData)
                {
                    var infos = new RestartManagerInterop.RmProcessInfo[needed];
                    count = needed;
                    res = RestartManagerInterop.RmGetList(handle, out needed, ref count, infos, ref reason);
                    if (res == 0)
                    {
                        return infos.Take((int)count).Select(Map).ToList();
                    }
                }

                return [];
            }
            finally
            {
                RestartManagerInterop.RmEndSession(handle);
            }
        }, cancellationToken);
    }

    private static LockInfo Map(RestartManagerInterop.RmProcessInfo info)
    {
        try
        {
            using var process = Process.GetProcessById(info.Process.dwProcessId);
            var path = string.Empty;
            try
            {
                path = process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
            }

            var kind = info.ApplicationType switch
            {
                RestartManagerInterop.RM_APP_TYPE.RmService => LockKind.Service,
                RestartManagerInterop.RM_APP_TYPE.RmExplorer => LockKind.Shell,
                RestartManagerInterop.RM_APP_TYPE.RmCritical => LockKind.Protected,
                _ => LockKind.Process
            };

            var protectedProcess = kind == LockKind.Protected || (path.Contains("windows", StringComparison.OrdinalIgnoreCase) && path.Contains("system32", StringComparison.OrdinalIgnoreCase));
            return new LockInfo(
                Guid.NewGuid().ToString("N"),
                string.Empty,
                info.strAppName,
                info.Process.dwProcessId,
                path,
                string.Empty,
                protectedProcess ? LockKind.Protected : kind,
                protectedProcess ? RiskLevel.Protected : kind == LockKind.Service ? RiskLevel.High : RiskLevel.Review,
                !protectedProcess && kind is LockKind.Process or LockKind.Shell,
                !protectedProcess && kind == LockKind.Service,
                kind == LockKind.Shell ? "可尝试重启资源管理器后重试。" : "可先关闭占用进程后重试。",
                info.strServiceShortName);
        }
        catch
        {
            return new LockInfo(Guid.NewGuid().ToString("N"), string.Empty, info.strAppName, info.Process.dwProcessId, string.Empty, string.Empty, LockKind.Unknown, RiskLevel.Review, false, false, "无法解析更多进程细节。", string.Empty);
        }
    }
}