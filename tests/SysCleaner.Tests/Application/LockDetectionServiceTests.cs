using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;
using SysCleaner.Infrastructure.Services;
using System.Diagnostics;
using System.Reflection;

namespace SysCleaner.Tests.Application;

public sealed class LockDetectionServiceTests
{
    [Fact]
    public void Map_ExplorerLock_UsesRestartExplorerInsteadOfTerminate()
    {
        var mapped = MapCurrentProcess("Explorer Host", "", "RmExplorer");

        Assert.Equal(LockKind.Shell, mapped.Kind);
        Assert.False(mapped.CanTerminate);
        Assert.False(mapped.CanStopService);
        Assert.Contains("重启资源管理器", mapped.Recommendation);
    }

    [Fact]
    public void Map_ServiceLock_AllowsStoppingService()
    {
        var mapped = MapCurrentProcess("Vendor Service", "VendorSvc", "RmService");

        Assert.Equal(LockKind.Service, mapped.Kind);
        Assert.False(mapped.CanTerminate);
        Assert.True(mapped.CanStopService);
        Assert.Equal("VendorSvc", mapped.Notes);
    }

    private static LockInfo MapCurrentProcess(string appName, string serviceShortName, string applicationTypeName)
    {
        var assembly = typeof(LockDetectionService).Assembly;
        var interopType = assembly.GetType("SysCleaner.Infrastructure.Interop.RestartManagerInterop", throwOnError: true)!;
        var processInfoType = assembly.GetType("SysCleaner.Infrastructure.Interop.RestartManagerInterop+RmProcessInfo", throwOnError: true)!;
        var uniqueProcessType = assembly.GetType("SysCleaner.Infrastructure.Interop.RestartManagerInterop+RmUniqueProcess", throwOnError: true)!;
        var appType = assembly.GetType("SysCleaner.Infrastructure.Interop.RestartManagerInterop+RM_APP_TYPE", throwOnError: true)!;
        var mapMethod = typeof(LockDetectionService).GetMethod("Map", BindingFlags.NonPublic | BindingFlags.Static)!;

        var uniqueProcess = Activator.CreateInstance(uniqueProcessType)!;
        uniqueProcessType.GetField("dwProcessId")!.SetValue(uniqueProcess, Process.GetCurrentProcess().Id);

        var processInfo = Activator.CreateInstance(processInfoType)!;
        processInfoType.GetField("Process")!.SetValue(processInfo, uniqueProcess);
        processInfoType.GetField("strAppName")!.SetValue(processInfo, appName);
        processInfoType.GetField("strServiceShortName")!.SetValue(processInfo, serviceShortName);
        processInfoType.GetField("ApplicationType")!.SetValue(processInfo, Enum.Parse(appType, applicationTypeName));

        return (LockInfo)mapMethod.Invoke(null, [processInfo])!;
    }
}