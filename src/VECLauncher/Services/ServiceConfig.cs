using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using VECLauncher.ViewModels;

namespace VECLauncher.Services;

/// <summary>
/// Central registration of all services, repositories, and ViewModels for the DI container.
/// </summary>
public static class ServiceConfig
{
    public static IServiceProvider Configure()
    {
        var services = new ServiceCollection();

        // ── Infrastructure ──
        services.AddSingleton(App.Http);

        // ── Core services (static wrappers) ──
        services.AddSingleton<VersionService>();
        services.AddSingleton<DownloadManager>();
        services.AddSingleton<MicrosoftAuthService>();
        services.AddSingleton<JavaService>();
        services.AddSingleton<SkinService>();
        services.AddSingleton<GameLauncher>();
        services.AddSingleton<ModLoaderService>();
        services.AddSingleton<ServerPingService>();
        services.AddSingleton<ModService>();
        services.AddSingleton<ModpackService>();
        services.AddSingleton<RamMonitor>();

        // ── Service interfaces (replace static calls) ──
        services.AddSingleton<IInstanceService, InstanceServiceAdapter>();
        services.AddSingleton<IOfflineSkinService, OfflineSkinServiceAdapter>();
        services.AddSingleton<IMaintenanceService, MaintenanceServiceAdapter>();
        services.AddSingleton<IImageCacheService, ImageCacheServiceAdapter>();
        services.AddSingleton<LaunchService>();

        // ── State helpers ──
        services.AddSingleton<GameStatistics>();
        services.AddSingleton<FavoriteInstances>();

        // ── Toast service ──
        services.AddSingleton<IToastService, ToastService>();

        // ── Event Aggregator (decoupled VM communication) ──
        services.AddSingleton<EventAggregator>();

        // ── Repositories (abstract data access) ──
        services.AddSingleton<IAccountRepository, AccountRepository>();
        services.AddSingleton<IInstanceRepository, InstanceRepository>();

        // ── Settings (auto-save on property change) ──
        services.AddSingleton<ISettingsService, SettingsServiceDI>();
        services.AddSingleton(sp => sp.GetRequiredService<ISettingsService>().Settings);

        // ── ViewModels ──
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<IAccountState>(sp => sp.GetRequiredService<MainWindowViewModel>());
        services.AddSingleton<AccountViewModel>();
        services.AddSingleton<InstancesViewModel>();
        services.AddSingleton<ModsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // ── View ──
        services.AddTransient<Views.MainWindow>();

        return services.BuildServiceProvider();
    }
}
