using DBSync.Core.Data;
using DBSync.Core.Execution;
using DBSync.Core.Schema;
using DBSync.Core.Snapshot;
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
        services.AddSingleton<SqlServerSchemaReader>();
        services.AddSingleton<MySqlSchemaReader>();
        services.AddSingleton<PostgresSchemaReader>();
        services.AddSingleton<SqliteSchemaReader>();
        services.AddSingleton<ISchemaReader, DatabaseSchemaReader>();

        services.AddSingleton<SqlServerDataFingerprinter>();
        services.AddSingleton<MySqlDataFingerprinter>();
        services.AddSingleton<PostgresDataFingerprinter>();
        services.AddSingleton<SqliteDataFingerprinter>();
        services.AddSingleton<IDataFingerprinter, DatabaseDataFingerprinter>();

        services.AddSingleton<SqlServerSqlGenerator>();
        services.AddSingleton<MySqlSqlGenerator>();
        services.AddSingleton<PostgresSqlGenerator>();
        services.AddSingleton<SqliteSqlGenerator>();
        services.AddSingleton<ISqlGenerator, DatabaseSqlGenerator>();

        services.AddSingleton<ISnapshotExporter, SnapshotExporter>();
        services.AddSingleton<ISnapshotLoader, SnapshotLoader>();

        services.AddSingleton<IScriptExecutor, ScriptExecutor>();

        return services;
    }
}
