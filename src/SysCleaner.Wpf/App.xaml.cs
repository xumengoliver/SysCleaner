using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using SysCleaner.Application.Services;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Infrastructure;
using SysCleaner.Wpf.ViewModels;

namespace SysCleaner.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
	private IHost? _host;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		_host = Host.CreateDefaultBuilder()
			.ConfigureServices(services =>
			{
				services.AddSysCleanerInfrastructure();
				services.AddSingleton<DashboardService>();
				services.AddSingleton<SoftwarePanoramaService>();
				services.AddSingleton<DashboardViewModel>();
				services.AddSingleton<InstalledAppsViewModel>();
				services.AddSingleton<SoftwarePanoramaViewModel>();
				services.AddSingleton<BrokenEntriesViewModel>();
				services.AddSingleton<StartupViewModel>();
				services.AddSingleton<ScheduledTasksViewModel>();
				services.AddSingleton<SystemServicesViewModel>();
				services.AddSingleton<SystemRepairViewModel>();
				services.AddSingleton<WindowsUpdateRepairViewModel>();
				services.AddSingleton<ContextMenuViewModel>();
				services.AddSingleton<UnlockAssistantViewModel>();
				services.AddSingleton<EmptyCleanupViewModel>();
				services.AddSingleton<ResidueViewModel>();
				services.AddSingleton<RegistryViewModel>();
				services.AddSingleton<RegistrySearchViewModel>();
				services.AddSingleton<HistoryViewModel>();
				services.AddSingleton<ShellViewModel>();
				services.AddSingleton<MainWindow>();
			})
			.Build();

		await _host.StartAsync();

		var history = _host.Services.GetRequiredService<IHistoryService>();
		await history.InitializeAsync();

		var shell = _host.Services.GetRequiredService<ShellViewModel>();
		var window = _host.Services.GetRequiredService<MainWindow>();
		window.DataContext = shell;
		window.Show();

		await shell.InitializeAsync();
	}

	protected override async void OnExit(ExitEventArgs e)
	{
		if (_host is not null)
		{
			await _host.StopAsync();
			_host.Dispose();
		}

		base.OnExit(e);
	}
}

