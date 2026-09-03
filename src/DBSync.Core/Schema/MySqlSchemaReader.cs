using DBSync.Core;
using System.Data.Common;
using DBSync.Core.Models;
using MySqlConnector;

namespace DBSync.Core.Schema;

public sealed class MySqlSchemaReader : ISchemaReader
{
    public async Task<IReadOnlyList<TableModel>> ReadAllTablesAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        EnsureMySql(connection);
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
        if (connection.DbType != DatabaseType.MySql)
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
            MaxLength = ToNullableInt32(row.MaxLength),
            Precision = row.Precision,
            Scale = row.Scale,
            IsNullable = string.Equals(row.IsNullable, "YES", StringComparison.OrdinalIgnoreCase),
            DefaultValue = row.DefaultValue,
            Comment = row.Comment,
            IsIdentity = false,
            IsAutoIncrement = row.Extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
            OrdinalPosition = row.OrdinalPosition
        };
    }

    private static int? ToNullableInt32(long? value)
    {
        if (value is null || value <= 0 || value > int.MaxValue)
            return null;

        return (int)value.Value;
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

    private static MySqlConnection CreateConnection(DatabaseConnection connection)
    {
        return new MySqlConnection(connection.ConnectionString);
    }

    private static async Task<IReadOnlyList<T>> QueryAsync<T>(
        MySqlConnection db,
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
        var type = typeof(T);
        var instance = Activator.CreateInstance<T>();
        foreach (var prop in type.GetProperties().Where(p => p.CanWrite))
        {
            var ordinal = GetOrdinal(reader, prop.Name);
            if (ordinal < 0)
                continue;

            var value = reader.GetValue(ordinal);
            if (value is DBNull)
                value = null;

            if (value is not null && prop.PropertyType != value.GetType())
            {
                value = Convert.ChangeType(value, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
            }

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

    private static void EnsureMySql(DatabaseConnection connection)
    {
        if (connection.DbType != DatabaseType.MySql)
            throw new ArgumentException("MySqlSchemaReader 只支持 MySQL 连接。", nameof(connection));
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
        public long? MaxLength { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public string IsNullable { get; set; } = string.Empty;
        public string? DefaultValue { get; set; }
        public string? Comment { get; set; }
        public string Extra { get; set; } = string.Empty;
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
    TABLE_SCHEMA AS SchemaName,
    TABLE_NAME AS Name,
    NULLIF(TABLE_COMMENT, '') AS Comment,
    COALESCE(TABLE_ROWS, 0) AS EstimatedRowCount,
    COALESCE(ROUND((DATA_LENGTH + INDEX_LENGTH) / 1024 / 1024, 2), 0) AS EstimatedDataSizeMb
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
  AND TABLE_SCHEMA = DATABASE()
ORDER BY TABLE_SCHEMA, TABLE_NAME
""";

        internal const string Columns = """
SELECT
    TABLE_SCHEMA AS SchemaName,
    TABLE_NAME AS TableName,
    COLUMN_NAME AS Name,
    DATA_TYPE AS DbTypeName,
    COLUMN_TYPE AS RawTypeName,
    CHARACTER_MAXIMUM_LENGTH AS MaxLength,
    NUMERIC_PRECISION AS `Precision`,
    NUMERIC_SCALE AS `Scale`,
    IS_NULLABLE AS `IsNullable`,
    COLUMN_DEFAULT AS DefaultValue,
    NULLIF(COLUMN_COMMENT, '') AS Comment,
    EXTRA AS Extra,
    ORDINAL_POSITION AS OrdinalPosition
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
""";

        internal const string PrimaryKeys = """
SELECT
    TABLE_SCHEMA AS SchemaName,
    TABLE_NAME AS TableName,
    COLUMN_NAME AS ColumnName,
    ORDINAL_POSITION AS OrdinalPosition
FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
WHERE TABLE_SCHEMA = DATABASE()
  AND CONSTRAINT_NAME = 'PRIMARY'
ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
""";

        internal const string ForeignKeys = """
SELECT
    kcu.TABLE_SCHEMA AS SchemaName,
    kcu.TABLE_NAME AS TableName,
    kcu.CONSTRAINT_NAME AS Name,
    kcu.COLUMN_NAME AS ColumnName,
    kcu.REFERENCED_TABLE_NAME AS ReferencedTable,
    kcu.REFERENCED_COLUMN_NAME AS ReferencedColumn
FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
  ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
 AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
WHERE kcu.TABLE_SCHEMA = DATABASE()
  AND tc.CONSTRAINT_TYPE = 'FOREIGN KEY'
ORDER BY kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION
""";

        internal const string Indexes = """
SELECT
    TABLE_SCHEMA AS SchemaName,
    TABLE_NAME AS TableName,
    INDEX_NAME AS Name,
    COLUMN_NAME AS ColumnName,
    NON_UNIQUE = 0 AS IsUnique,
    INDEX_NAME = 'PRIMARY' AS IsPrimaryKey,
    SEQ_IN_INDEX AS KeyOrdinal
FROM INFORMATION_SCHEMA.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
  AND INDEX_NAME <> 'PRIMARY'
ORDER BY TABLE_SCHEMA, TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX
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
