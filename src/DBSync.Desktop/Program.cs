using Avalonia;
using DBSync.Core.Schema;
using DBSync.Desktop.Services;
using DBSync.Desktop.Storage;
using DBSync.Desktop.ViewModels;
using DBSync.Desktop.Views;
using Easy.Serilog.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DBSync.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var hostBuilder = Host.CreateDefaultBuilder(args);
        hostBuilder.AddSerilogHost();
        hostBuilder.ConfigureServices(services =>
        {
            services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
            services.AddSingleton<IConnectionStore, LocalConnectionStore>();
            services.AddSingleton<IConnectionEncryption, ConnectionEncryptionFactory>();
            services.AddSingleton<ISchemaReader, SqlServerSchemaReader>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
        });

        var host = hostBuilder.Build();

        App.Services = host.Services;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
