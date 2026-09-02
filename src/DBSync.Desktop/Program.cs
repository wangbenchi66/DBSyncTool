using Avalonia;
using DBSync.Desktop.Extensions;
using Easy.Serilog.Core;
using Microsoft.Extensions.Hosting;

namespace DBSync.Desktop;

/// <summary>
/// 应用程序入口
///</summary>
internal static class Program
{
    /// <summary>
    /// 主入口方法
    ///</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        var hostBuilder = Host.CreateDefaultBuilder(args);
        hostBuilder.AddSerilogHost();
        hostBuilder.ConfigureServices((_, services) =>
        {
            services.AddRegisterDependencies();
        });

        var host = hostBuilder.Build();

        App.Services = host.Services;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// 构建 Avalonia 应用
    ///</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
