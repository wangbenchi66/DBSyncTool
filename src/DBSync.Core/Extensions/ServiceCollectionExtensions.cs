using DBSync.Core.Schema;
using DBSync.Core.SqlGenerators;
using Microsoft.Extensions.DependencyInjection;

namespace DBSync.Core.Extensions;

/// <summary>
/// DBSync.Core 的依赖注入注册扩展
///</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 DBSync.Core 提供的所有核心服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合（用于链式调用）</returns>
    public static IServiceCollection AddDbSyncCore(this IServiceCollection services)
    {
        services.AddSingleton<ISchemaReader, SqlServerSchemaReader>();
        services.AddSingleton<ISqlGenerator, SqlServerSqlGenerator>();

        return services;
    }
}
