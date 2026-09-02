using DBSync.Core.Models;

namespace DBSync.Core.Data;

public sealed class DatabaseDataFingerprinter(
    SqlServerDataFingerprinter sqlServer,
    MySqlDataFingerprinter mySql,
    PostgresDataFingerprinter postgreSql,
    SqliteDataFingerprinter sqlite) : IDataFingerprinter
{
    public IAsyncEnumerable<RowHash> ReadRowHashesAsync(
        DatabaseConnection connection,
        TableModel table,
        string? whereClause = null,
        CancellationToken cancellationToken = default)
    {
        return connection.DbType switch
        {
            DatabaseType.SqlServer => sqlServer.ReadRowHashesAsync(connection, table, whereClause, cancellationToken),
            DatabaseType.MySql => mySql.ReadRowHashesAsync(connection, table, whereClause, cancellationToken),
            DatabaseType.PostgreSql => postgreSql.ReadRowHashesAsync(connection, table, whereClause, cancellationToken),
            DatabaseType.Sqlite => sqlite.ReadRowHashesAsync(connection, table, whereClause, cancellationToken),
            _ => throw new NotSupportedException($"不支持的数据库类型：{connection.DbType}")
        };
    }
}
