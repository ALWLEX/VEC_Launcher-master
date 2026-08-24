using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using VECLauncher.Services;

namespace VECLauncher;

#if DEBUG
internal sealed class BindingErrorListener : TraceListener
{
    public override void Write(string? message) { }

    public override void WriteLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Console.WriteLine("[BINDING] " + message);
        Log.Warn("Binding error: " + message);
    }
}
#endif

public partial class App : Application
{
    public static HttpClient Http { get; private set; } = null!;

    /// <summary>Global DI service provider — use to resolve services anywhere.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (SelfUpdateService.RunSelfReplacementIfNeeded())
        {
            Shutdown();
            return;
        }

        ServicePointManager.DefaultConnectionLimit = 64;
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 32,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true
        };

        Http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("VEC Launcher/1.0 (+windows)");
        Http.DefaultRequestHeaders.Accept.ParseAdd("*/*");

#if DEBUG
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorListener());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
#endif

        LauncherPaths.EnsureAll();
        Log.Info("VEC Launcher started");

        // ── Build DI container (all services, VMs registered here) ──
        Services = ServiceConfig.Configure();

        try
        {
            var s = Services.GetRequiredService<LauncherSettings>();
            ThemeService.ApplyTheme(s.Theme);
            ThemeService.ApplyAccent(s.AccentColor);
        }
        catch (Exception ex) { Log.Warn("Failed to apply theme: " + ex.Message); }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled domain exception: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unhandled task exception: " + args.Exception);
            args.SetObserved();
        };

        // SplashWindow.xaml is the StartupUri — it resolves MainWindow from DI
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("UI error", e.Exception);
        MessageBox.Show(
            "An error occurred:\n\n" + e.Exception.Message +
            "\n\nDetails logged to:\n" + LauncherPaths.LauncherLogFile,
            "VEC Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Shutting down");
        Http?.Dispose();
        base.OnExit(e);
    }
}
