using System.Data.Common;
using DBSync.Core;
using DBSync.Core.Models;
using Npgsql;

namespace DBSync.Core.Schema;

public sealed class PostgresSchemaReader : ISchemaReader
{
    public async Task<IReadOnlyList<TableModel>> ReadAllTablesAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        EnsurePostgres(connection);
        await using var db = CreateConnection(connection);
        await db.OpenAsync(cancellationToken);

        var tables = await QueryAsync<TableRow>(db, Sql.Tables, cancellationToken);
        var columns = await QueryAsync<ColumnRow>(db, Sql.Columns, cancellationToken);
        var primaryKeys = await QueryAsync<PrimaryKeyRow>(db, Sql.PrimaryKeys, cancellationToken);
        var foreignKeys = await QueryAsync<ForeignKeyRow>(db, Sql.ForeignKeys, cancellationToken);
        var indexes = await QueryAsync<IndexRow>(db, Sql.Indexes, cancellationToken);

        return tables
            .Select(table => BuildTable(table, columns, primaryKeys, foreignKeys, indexes))
            .ToList();
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
        if (connection.DbType != DatabaseType.PostgreSql)
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
        IReadOnlyList<IndexRow> indexes)
    {
        return new TableModel
        {
            Name = table.Name,
            Schema = table.SchemaName,
            Comment = table.Comment,
            EstimatedRowCount = table.EstimatedRowCount,
            EstimatedDataSizeMb = table.EstimatedDataSizeMb,
            Columns = columns
                .Where(c => SameTable(c.SchemaName, c.TableName, table))
                .OrderBy(c => c.OrdinalPosition)
                .Select(ToColumnModel)
                .ToList(),
            PrimaryKeyColumns = primaryKeys
                .Where(pk => SameTable(pk.SchemaName, pk.TableName, table))
                .OrderBy(pk => pk.OrdinalPosition)
                .Select(pk => pk.ColumnName)
                .ToList(),
            ForeignKeys = foreignKeys
                .Where(fk => SameTable(fk.SchemaName, fk.TableName, table))
                .Select(fk => new ForeignKeyModel
                {
                    Name = fk.Name,
                    ColumnName = fk.ColumnName,
                    ReferencedTable = fk.ReferencedTable,
                    ReferencedColumn = fk.ReferencedColumn
                })
                .ToList(),
            Indexes = indexes
                .Where(i => SameTable(i.SchemaName, i.TableName, table))
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToIndexModel)
                .ToList()
        };
    }

    private static ColumnModel ToColumnModel(ColumnRow row)
    {
        return new ColumnModel
        {
            Name = row.Name,
            DbTypeName = row.DbTypeName,
            ColumnType = DbDialectSupport.MapColumnType(row.DbTypeName, row.RawTypeName),
            MaxLength = row.MaxLength is > 0 ? row.MaxLength : null,
            Precision = row.Precision,
            Scale = row.Scale,
            IsNullable = row.IsNullable,
            DefaultValue = row.DefaultValue,
            Comment = row.Comment,
            IsIdentity = row.IsIdentity,
            IsAutoIncrement = row.IsIdentity,
            OrdinalPosition = row.OrdinalPosition
        };
    }

    private static IndexModel ToIndexModel(IGrouping<string, IndexRow> group)
    {
        var rows = group.OrderBy(i => i.KeyOrdinal).ToList();
        var first = rows[0];

        return new IndexModel
        {
            Name = first.Name,
            ColumnNames = rows.Select(i => i.ColumnName).ToList(),
            IsUnique = first.IsUnique,
            IsClustered = false,
            IsPrimaryKey = first.IsPrimaryKey
        };
    }

    private static bool SameTable(string schemaName, string tableName, TableRow table)
    {
        return string.Equals(schemaName, table.SchemaName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(tableName, table.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static NpgsqlConnection CreateConnection(DatabaseConnection connection)
    {
        return new NpgsqlConnection(connection.ConnectionString);
    }

    private static async Task<IReadOnlyList<T>> QueryAsync<T>(
        NpgsqlConnection db,
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

    private static void EnsurePostgres(DatabaseConnection connection)
    {
        if (connection.DbType != DatabaseType.PostgreSql)
            throw new ArgumentException("PostgresSchemaReader 只支持 PostgreSQL 连接。", nameof(connection));
    }

    private sealed class TableRow
    {
        public string SchemaName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Comment { get; set; }
        public long EstimatedRowCount { get; set; }
        public decimal EstimatedDataSizeMb { get; set; }
    }

    private sealed class ColumnRow
    {
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DbTypeName { get; set; } = string.Empty;
        public string RawTypeName { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool IsNullable { get; set; }
        public string? DefaultValue { get; set; }
        public string? Comment { get; set; }
        public bool IsIdentity { get; set; }
        public int OrdinalPosition { get; set; }
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
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public string ReferencedTable { get; set; } = string.Empty;
        public string ReferencedColumn { get; set; } = string.Empty;
    }

    private sealed class IndexRow
    {
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public bool IsUnique { get; set; }
        public bool IsPrimaryKey { get; set; }
        public int KeyOrdinal { get; set; }
    }

    private static class Sql
    {
        internal const string Tables = """
SELECT
    n.nspname AS SchemaName,
    c.relname AS Name,
    obj_description(c.oid, 'pg_class') AS Comment,
    COALESCE(c.reltuples::bigint, 0) AS EstimatedRowCount,
    COALESCE(ROUND(pg_total_relation_size(c.oid) / 1024.0 / 1024.0, 2), 0) AS EstimatedDataSizeMb
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname NOT IN ('pg_catalog', 'information_schema')
ORDER BY n.nspname, c.relname
""";

        internal const string Columns = """
SELECT
    table_schema AS SchemaName,
    table_name AS TableName,
    column_name AS Name,
    data_type AS DbTypeName,
    udt_name AS RawTypeName,
    CASE WHEN character_maximum_length > 0 THEN character_maximum_length ELSE NULL END AS MaxLength,
    numeric_precision AS Precision,
    numeric_scale AS Scale,
    is_nullable = 'YES' AS IsNullable,
    column_default AS DefaultValue,
    col_description(format('%I.%I', table_schema, table_name)::regclass::oid, ordinal_position) AS Comment,
    is_identity = 'YES' AS IsIdentity,
    ordinal_position AS OrdinalPosition
FROM information_schema.columns
WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
ORDER BY table_schema, table_name, ordinal_position
""";

        internal const string PrimaryKeys = """
SELECT
    kcu.table_schema AS SchemaName,
    kcu.table_name AS TableName,
    kcu.column_name AS ColumnName,
    kcu.ordinal_position AS OrdinalPosition
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage kcu
    ON tc.constraint_schema = kcu.constraint_schema
    AND tc.constraint_name = kcu.constraint_name
    AND tc.table_schema = kcu.table_schema
    AND tc.table_name = kcu.table_name
WHERE tc.constraint_type = 'PRIMARY KEY'
ORDER BY kcu.table_schema, kcu.table_name, kcu.ordinal_position
""";

        internal const string ForeignKeys = """
SELECT
    n.nspname AS SchemaName,
    rel.relname AS TableName,
    c.conname AS Name,
    a.attname AS ColumnName,
    frel.relname AS ReferencedTable,
    fa.attname AS ReferencedColumn
FROM pg_constraint c
JOIN pg_class rel ON rel.oid = c.conrelid
JOIN pg_namespace n ON n.oid = rel.relnamespace
JOIN LATERAL unnest(c.conkey) WITH ORDINALITY AS ck(attnum, ordinality) ON TRUE
JOIN pg_attribute a ON a.attrelid = rel.oid AND a.attnum = ck.attnum
JOIN pg_class frel ON frel.oid = c.confrelid
JOIN LATERAL unnest(c.confkey) WITH ORDINALITY AS fk(attnum, ordinality) ON fk.ordinality = ck.ordinality
JOIN pg_attribute fa ON fa.attrelid = frel.oid AND fa.attnum = fk.attnum
WHERE c.contype = 'f'
ORDER BY n.nspname, rel.relname, c.conname, ck.ordinality
""";

        internal const string Indexes = """
SELECT
    n.nspname AS SchemaName,
    t.relname AS TableName,
    i.relname AS Name,
    a.attname AS ColumnName,
    idx.indisunique AS IsUnique,
    false AS IsPrimaryKey,
    ck.ordinality AS KeyOrdinal
FROM pg_index idx
JOIN pg_class t ON t.oid = idx.indrelid
JOIN pg_namespace n ON n.oid = t.relnamespace
JOIN pg_class i ON i.oid = idx.indexrelid
JOIN LATERAL unnest(idx.indkey) WITH ORDINALITY AS ck(attnum, ordinality) ON TRUE
JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ck.attnum
WHERE idx.indisprimary = false
ORDER BY n.nspname, t.relname, i.relname, ck.ordinality
""";
    }

    /// <summary>
    /// 读取所有数据库对象（视图、存储过程、函数、触发器）
    ///</summary>
    public Task<IReadOnlyList<DatabaseObjectModel>> ReadAllObjectsAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DatabaseObjectModel>>([]);
    }
}
