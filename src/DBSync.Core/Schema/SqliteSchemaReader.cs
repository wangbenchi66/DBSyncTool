using System.Data.Common;
using DBSync.Core;
using DBSync.Core.Models;
using Microsoft.Data.Sqlite;

namespace DBSync.Core.Schema;

public sealed class SqliteSchemaReader : ISchemaReader
{
    public async Task<IReadOnlyList<TableModel>> ReadAllTablesAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        EnsureSqlite(connection);
        await using var db = CreateConnection(connection);
        await db.OpenAsync(cancellationToken);

        var tables = await QueryAsync<TableRow>(db, Sql.Tables, cancellationToken);
        var result = new List<TableModel>(tables.Count);
        foreach (var table in tables)
        {
            var columns = await QueryAsync<ColumnRow>(db, Sql.TableInfo(table.Name), cancellationToken);
            var primaryKeys = columns
                .Where(c => c.PrimaryKeyOrdinal > 0)
                .OrderBy(c => c.PrimaryKeyOrdinal)
                .Select(c => new PrimaryKeyRow
                {
                    SchemaName = string.Empty,
                    TableName = table.Name,
                    ColumnName = c.Name,
                    OrdinalPosition = c.PrimaryKeyOrdinal
                })
                .ToList();

            var foreignKeys = await QueryAsync<ForeignKeyRow>(db, Sql.ForeignKeys(table.Name), cancellationToken);
            var indexes = await LoadIndexesAsync(db, table.Name, cancellationToken);

            result.Add(BuildTable(table, columns, primaryKeys, foreignKeys, indexes));
        }

        return result;
    }

    public async Task<TableModel?> ReadTableAsync(
        DatabaseConnection connection,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        var tables = await ReadAllTablesAsync(connection, cancellationToken);
        return tables.FirstOrDefault(t =>
            string.Equals(t.FullName, tableName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> TestConnectionAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection.DbType != DatabaseType.Sqlite)
            return false;

        try
        {
            await using var db = CreateConnection(connection);
            await db.OpenAsync(cancellationToken);
            await using var command = db.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static TableModel BuildTable(
        TableRow table,
        IReadOnlyList<ColumnRow> columns,
        IReadOnlyList<PrimaryKeyRow> primaryKeys,
        IReadOnlyList<ForeignKeyRow> foreignKeys,
        IReadOnlyList<IndexModel> indexes)
    {
        var createSql = table.CreateSql ?? string.Empty;
        return new TableModel
        {
            Name = table.Name,
            Schema = string.Empty,
            Comment = null,
            EstimatedRowCount = table.EstimatedRowCount,
            EstimatedDataSizeMb = table.EstimatedDataSizeMb,
            Columns = columns
                .OrderBy(c => c.OrdinalPosition)
                .Select(c => new ColumnModel
                {
                    Name = c.Name,
                    DbTypeName = c.Type,
                    ColumnType = DbDialectSupport.MapColumnType(c.Type, c.Type),
                    MaxLength = ParseMaxLength(c.Type),
                    Precision = null,
                    Scale = null,
                    IsNullable = c.NotNull == 0,
                    DefaultValue = c.DefaultValue,
                    Comment = null,
                    IsIdentity = false,
                    IsAutoIncrement = createSql.Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase) &&
                                       c.PrimaryKeyOrdinal > 0 &&
                                       IsIntegerType(c.Type),
                    OrdinalPosition = c.OrdinalPosition
                })
                .ToList(),
            PrimaryKeyColumns = primaryKeys.Select(pk => pk.ColumnName).ToList(),
            ForeignKeys = foreignKeys.Select(fk => new ForeignKeyModel
            {
                Name = $"FK_{table.Name}_{fk.Id}_{fk.Seq}",
                ColumnName = fk.From,
                ReferencedTable = fk.Table,
                ReferencedColumn = fk.To
            }).ToList(),
            Indexes = indexes
        };
    }

    private static async Task<IReadOnlyList<IndexModel>> LoadIndexesAsync(
        SqliteConnection db,
        string tableName,
        CancellationToken cancellationToken)
    {
        var list = await QueryAsync<IndexListRow>(db, Sql.Indexes(tableName), cancellationToken);
        var result = new List<IndexModel>();
        foreach (var index in list.Where(i => !string.Equals(i.Origin, "pk", StringComparison.OrdinalIgnoreCase)))
        {
            var columns = await QueryAsync<IndexInfoRow>(db, Sql.IndexInfo(index.Name), cancellationToken);
            result.Add(new IndexModel
            {
                Name = index.Name,
                ColumnNames = columns.OrderBy(c => c.Seqno).Select(c => c.Name).ToList(),
                IsUnique = index.Unique != 0,
                IsClustered = false,
                IsPrimaryKey = false
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<T>> QueryAsync<T>(
        SqliteConnection db,
        string sql,
        CancellationToken cancellationToken)
    {
        var result = new List<T>();
        await using var command = db.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadRow<T>(reader));
        return result;
    }

    private static T ReadRow<T>(DbDataReader reader)
    {
        var instance = Activator.CreateInstance<T>();
        foreach (var prop in typeof(T).GetProperties().Where(p => p.CanWrite))
        {
            var ordinal = GetOrdinal(reader, prop.Name);
            if (ordinal < 0)
                continue;

            var value = reader.GetValue(ordinal);
            if (value is DBNull)
                value = null;

            if (value is not null && prop.PropertyType != value.GetType())
                value = Convert.ChangeType(value, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);

            prop.SetValue(instance, value);
        }

        return instance;
    }

    private static int GetOrdinal(DbDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int? ParseMaxLength(string type)
    {
        var start = type.IndexOf('(');
        var end = type.IndexOf(')');
        if (start < 0 || end <= start)
            return null;

        var raw = type[(start + 1)..end];
        return int.TryParse(raw, out var length) ? length : null;
    }

    private static bool IsIntegerType(string type)
    {
        var lower = type.Trim().ToLowerInvariant();
        return lower is "integer" or "int" or "bigint" or "smallint" or "tinyint";
    }

    private static SqliteConnection CreateConnection(DatabaseConnection connection)
    {
        return new SqliteConnection(connection.ConnectionString);
    }

    private static void EnsureSqlite(DatabaseConnection connection)
    {
        if (connection.DbType != DatabaseType.Sqlite)
            throw new ArgumentException("SqliteSchemaReader 只支持 SQLite 连接。", nameof(connection));
    }

    private sealed class TableRow
    {
        public string Name { get; set; } = string.Empty;
        public string? CreateSql { get; set; }
        public long? EstimatedRowCount { get; set; }
        public decimal? EstimatedDataSizeMb { get; set; }
    }

    private sealed class ColumnRow
    {
        public int OrdinalPosition { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int NotNull { get; set; }
        public string? DefaultValue { get; set; }
        public int PrimaryKeyOrdinal { get; set; }
    }

    private sealed class PrimaryKeyRow
    {
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public int OrdinalPosition { get; set; }
    }

    private sealed class ForeignKeyRow
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string Table { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    private sealed class IndexListRow
    {
        public int Seq { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Unique { get; set; }
        public string Origin { get; set; } = string.Empty;
    }

    private sealed class IndexInfoRow
    {
        public int Seqno { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static class Sql
    {
        internal const string Tables = """
SELECT
    name AS Name,
    sql AS CreateSql,
    NULL AS EstimatedRowCount,
    NULL AS EstimatedDataSizeMb
FROM sqlite_master
WHERE type = 'table'
  AND name NOT LIKE 'sqlite_%'
ORDER BY name
""";

        internal static string TableInfo(string tableName) =>
            $"""
SELECT
    cid AS OrdinalPosition,
    name AS Name,
    type AS Type,
    notnull AS NotNull,
    dflt_value AS DefaultValue,
    pk AS PrimaryKeyOrdinal
FROM pragma_table_info({QuoteLiteral(tableName)})
ORDER BY cid
""";

        internal static string ForeignKeys(string tableName) =>
            $"""
SELECT
    id AS Id,
    seq AS Seq,
    "table" AS Table,
    "from" AS "From",
    "to" AS "To"
FROM pragma_foreign_key_list({QuoteLiteral(tableName)})
ORDER BY id, seq
""";

        internal static string Indexes(string tableName) =>
            $"""
SELECT
    seq AS Seq,
    name AS Name,
    "unique" AS Unique,
    origin AS Origin
FROM pragma_index_list({QuoteLiteral(tableName)})
ORDER BY seq
""";

        internal static string IndexInfo(string indexName) =>
            $"""
SELECT
    seqno AS Seqno,
    name AS Name
FROM pragma_index_info({QuoteLiteral(indexName)})
ORDER BY seqno
""";

        private static string QuoteLiteral(string value) => $"'{value.Replace("'", "''")}'";
    }
}
