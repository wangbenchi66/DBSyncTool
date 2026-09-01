using DBSync.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DBSync.Desktop.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册桌面端依赖。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddRegisterDependencies(this IServiceCollection services)
    {
        services.AddDbSyncCore();
        return services;
    }
}
