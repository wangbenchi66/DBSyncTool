using Avalonia;
using Avalonia.Threading;
using DBSync.Desktop.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace DBSync.Desktop;

/// <summary>
/// 应用程序入口。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 主入口方法。
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // 日志目录统一放到用户数据目录（%APPDATA%\DBSyncTool\logs），
            // 以支持安装版部署到 Program Files（程序目录对普通用户不可写）。
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DBSyncTool", "logs");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(logDirectory, "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}")
                .CreateLogger();

            RegisterGlobalExceptionHandlers();

            var hostBuilder = Host.CreateDefaultBuilder(args);
            hostBuilder.ConfigureServices((_, services) =>
            {
                services.AddRegisterDependencies();
            });

            var host = hostBuilder.Build();

            App.Services = host.Services;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "应用程序发生未处理异常");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
                Log.Error(exception, "应用程序发生未处理异常");
            else
                Log.Error("应用程序发生未处理异常：{ExceptionObject}", e.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "后台任务发生未观察异常");
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error(e.Exception, "UI 线程发生未处理异常");
        };
    }

    /// <summary>
    /// 构建 Avalonia 应用。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
