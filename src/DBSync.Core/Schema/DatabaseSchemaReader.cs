using DBSync.Core.Models;

namespace DBSync.Core.Schema;

public sealed class DatabaseSchemaReader(
    SqlServerSchemaReader sqlServer,
    MySqlSchemaReader mySql,
    PostgresSchemaReader postgreSql,
    SqliteSchemaReader sqlite) : ISchemaReader
{
    public Task<IReadOnlyList<TableModel>> ReadAllTablesAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        return connection.DbType switch
        {
            DatabaseType.SqlServer => sqlServer.ReadAllTablesAsync(connection, cancellationToken),
            DatabaseType.MySql => mySql.ReadAllTablesAsync(connection, cancellationToken),
            DatabaseType.PostgreSql => postgreSql.ReadAllTablesAsync(connection, cancellationToken),
            DatabaseType.Sqlite => sqlite.ReadAllTablesAsync(connection, cancellationToken),
            _ => throw new NotSupportedException($"不支持的数据库类型：{connection.DbType}")
        };
    }

    public Task<TableModel?> ReadTableAsync(
        DatabaseConnection connection,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        return connection.DbType switch
        {
            DatabaseType.SqlServer => sqlServer.ReadTableAsync(connection, tableName, cancellationToken),
            DatabaseType.MySql => mySql.ReadTableAsync(connection, tableName, cancellationToken),
            DatabaseType.PostgreSql => postgreSql.ReadTableAsync(connection, tableName, cancellationToken),
            DatabaseType.Sqlite => sqlite.ReadTableAsync(connection, tableName, cancellationToken),
            _ => throw new NotSupportedException($"不支持的数据库类型：{connection.DbType}")
        };
    }

    public Task<bool> TestConnectionAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        return connection.DbType switch
        {
            DatabaseType.SqlServer => sqlServer.TestConnectionAsync(connection, cancellationToken),
            DatabaseType.MySql => mySql.TestConnectionAsync(connection, cancellationToken),
            DatabaseType.PostgreSql => postgreSql.TestConnectionAsync(connection, cancellationToken),
            DatabaseType.Sqlite => sqlite.TestConnectionAsync(connection, cancellationToken),
            _ => throw new NotSupportedException($"不支持的数据库类型：{connection.DbType}")
        };
    }
}
