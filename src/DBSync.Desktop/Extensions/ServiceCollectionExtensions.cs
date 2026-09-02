using DBSync.Core.Extensions;
using DBSync.Desktop.Services;
using DBSync.Desktop.Storage;
using DBSync.Desktop.ViewModels;
using DBSync.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DBSync.Desktop.Extensions;

/// <summary>
/// 桌面端依赖注入扩展方法
///</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册桌面端所有依赖
    ///</summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddRegisterDependencies(this IServiceCollection services)
    {
        // Core 层服务
        services.AddDbSyncCore();

        // 基础服务
        services.AddSingleton<IConnectionEncryption, ConnectionEncryptionFactory>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<IConnectionStore, LocalConnectionStore>();
        services.AddSingleton<DiffReportExporter>();
        services.AddSingleton<WindowProvider>();
        services.AddSingleton<IWindowProvider>(sp => sp.GetRequiredService<WindowProvider>());

        // 页面 ViewModel（Singleton）
        services.AddSingleton<ConnectionListViewModel>();
        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<CompareViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        // 窗口
        services.AddSingleton<MainWindow>();

        return services;
    }
}
