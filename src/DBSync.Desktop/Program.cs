using Avalonia;
using DBSync.Core.Schema;
using DBSync.Desktop.Services;
using DBSync.Desktop.Storage;
using DBSync.Desktop.ViewModels;
using DBSync.Desktop.Views;
using DBSync.Desktop.Extensions;
using Easy.Serilog.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WBC66.Autofac.Core;

namespace DBSync.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var hostBuilder = Host.CreateDefaultBuilder(args);
        hostBuilder.AddSerilogHost();
        hostBuilder.ConfigureServices((_, services) =>
        {
            services.AddRegisterDependencies();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
            //hostBuilder.AddAutofacHostSetup(services, AutofacSetup.AddAutofacModule);//上边已经注入了
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
