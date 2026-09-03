using DBSync.Core.Models;

namespace DBSync.Core.SqlGenerators;

public sealed class DatabaseSqlGenerator(
    SqlServerSqlGenerator sqlServer,
    MySqlSqlGenerator mySql,
    PostgresSqlGenerator postgreSql,
    SqliteSqlGenerator sqlite) : ISqlGenerator
{
    public string GenerateUpgradeScript(
        DatabaseType dbType,
        SchemaDiff schemaDiff,
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>? fullData = null,
        bool useTransaction = true)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => sqlServer.GenerateUpgradeScript(dbType, schemaDiff, dataDiffs, fullData, useTransaction),
            DatabaseType.MySql => mySql.GenerateUpgradeScript(dbType, schemaDiff, dataDiffs, fullData, useTransaction),
            DatabaseType.PostgreSql => postgreSql.GenerateUpgradeScript(dbType, schemaDiff, dataDiffs, fullData, useTransaction),
            DatabaseType.Sqlite => sqlite.GenerateUpgradeScript(dbType, schemaDiff, dataDiffs, fullData, useTransaction),
            _ => throw new NotSupportedException($"不支持的数据库类型：{dbType}")
        };
    }

    public IReadOnlyList<string> GenerateDdlScript(SchemaDiff schemaDiff)
    {
        return sqlServer.GenerateDdlScript(schemaDiff);
    }

    public string GenerateCreateTable(DatabaseType dbType, TableModel table)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => sqlServer.GenerateCreateTable(table),
            DatabaseType.MySql => mySql.GenerateCreateTable(table),
            DatabaseType.PostgreSql => postgreSql.GenerateCreateTable(table),
            DatabaseType.Sqlite => sqlite.GenerateCreateTable(table),
            _ => throw new NotSupportedException($"不支持的数据库类型：{dbType}")
        };
    }

    public string GenerateDropTable(DatabaseType dbType, TableModel table)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => sqlServer.GenerateDropTable(table),
            DatabaseType.MySql => mySql.GenerateDropTable(table),
            DatabaseType.PostgreSql => postgreSql.GenerateDropTable(table),
            DatabaseType.Sqlite => sqlite.GenerateDropTable(table),
            _ => throw new NotSupportedException($"不支持的数据库类型：{dbType}")
        };
    }

    public IReadOnlyList<string> GenerateAlterTable(DatabaseType dbType, TableDiff diff)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => sqlServer.GenerateAlterTable(diff),
            DatabaseType.MySql => mySql.GenerateAlterTable(diff),
            DatabaseType.PostgreSql => postgreSql.GenerateAlterTable(diff),
            DatabaseType.Sqlite => sqlite.GenerateAlterTable(diff),
            _ => throw new NotSupportedException($"不支持的数据库类型：{dbType}")
        };
    }

    public IReadOnlyList<string> GenerateInsertStatements(
        DatabaseType dbType,
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => sqlServer.GenerateInsertStatements(table, rows),
            DatabaseType.MySql => mySql.GenerateInsertStatements(table, rows),
            DatabaseType.PostgreSql => postgreSql.GenerateInsertStatements(table, rows),
            DatabaseType.Sqlite => sqlite.GenerateInsertStatements(table, rows),
            _ => throw new NotSupportedException($"不支持的数据库类型：{dbType}")
        };
    }

    public IReadOnlyList<string> GenerateUpdateStatements(
        DatabaseType dbType,
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => sqlServer.GenerateUpdateStatements(table, rows),
            DatabaseType.MySql => mySql.GenerateUpdateStatements(table, rows),
            DatabaseType.PostgreSql => postgreSql.GenerateUpdateStatements(table, rows),
            DatabaseType.Sqlite => sqlite.GenerateUpdateStatements(table, rows),
            _ => throw new NotSupportedException($"不支持的数据库类型：{dbType}")
        };
    }

    public IReadOnlyList<string> GenerateDeleteStatements(
        DatabaseType dbType,
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> primaryKeyValues)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => sqlServer.GenerateDeleteStatements(table, primaryKeyValues),
            DatabaseType.MySql => mySql.GenerateDeleteStatements(table, primaryKeyValues),
            DatabaseType.PostgreSql => postgreSql.GenerateDeleteStatements(table, primaryKeyValues),
            DatabaseType.Sqlite => sqlite.GenerateDeleteStatements(table, primaryKeyValues),
            _ => throw new NotSupportedException($"不支持的数据库类型：{dbType}")
        };
    }
}
