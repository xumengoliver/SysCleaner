using Microsoft.Extensions.DependencyInjection;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Infrastructure.Persistence;
using SysCleaner.Infrastructure.Services;

namespace SysCleaner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSysCleanerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IHistoryService, SqliteHistoryService>();
        services.AddSingleton<IInstalledAppService, InstalledAppService>();
        services.AddSingleton<IResidueAnalysisService, ResidueAnalysisService>();
        services.AddSingleton<IRegistryCleanupService, RegistryCleanupService>();
        services.AddSingleton<ICleanupExecutionService, CleanupExecutionService>();
        services.AddSingleton<IStartupItemService, StartupItemService>();
        services.AddSingleton<IContextMenuService, ContextMenuService>();
        services.AddSingleton<ITaskSchedulerService, ScheduledTaskService>();
        services.AddSingleton<IServiceControlService, ServiceControlService>();
        services.AddSingleton<ISystemRepairService, SystemRepairService>();
        services.AddSingleton<IWindowsUpdateRepairService, WindowsUpdateRepairService>();
        services.AddSingleton<ILockDetectionService, LockDetectionService>();
        services.AddSingleton<IUnlockAssistanceService, UnlockAssistanceService>();
        services.AddSingleton<IEmptyItemScanService, EmptyItemScanService>();
        return services;
    }
}