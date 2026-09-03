using DBSync.Core.Models;
using Easy.SqlSugar.Core.Common;
using SqlSugar;

namespace DBSync.Core.Schema;

/// <summary>
/// SQL Server 数据库结构读取器。
///</summary>
public sealed class SqlServerSchemaReader : ISchemaReader
{
    /// <summary>
    /// 读取 SQL Server 连接中的所有用户表结构。
    /// </summary>
    /// <param name="connection">数据库连接配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有用户表的结构元数据</returns>
    public async Task<IReadOnlyList<TableModel>> ReadAllTablesAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        EnsureSqlServer(connection);
        cancellationToken.ThrowIfCancellationRequested();

        using var db = CreateClient(connection);

        var tables = await db.Ado.SqlQueryAsync<TableRow>(Sql.Tables);
        var columns = await db.Ado.SqlQueryAsync<ColumnRow>(Sql.Columns);
        var primaryKeys = await db.Ado.SqlQueryAsync<PrimaryKeyRow>(Sql.PrimaryKeys);
        var foreignKeys = await db.Ado.SqlQueryAsync<ForeignKeyRow>(Sql.ForeignKeys);
        var indexes = await db.Ado.SqlQueryAsync<IndexRow>(Sql.Indexes);

        cancellationToken.ThrowIfCancellationRequested();

        return tables
            .Select(table => BuildTable(table, columns, primaryKeys, foreignKeys, indexes))
            .ToList();
    }

    /// <summary>
    /// 读取 SQL Server 连接中指定表的结构。
    /// </summary>
    /// <param name="connection">数据库连接配置</param>
    /// <param name="tableName">表名或完整表名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表结构元数据，表不存在时返回 null</returns>
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

    /// <summary>
    /// 测试 SQL Server 连接是否可用。
    /// </summary>
    /// <param name="connection">数据库连接配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接可用时返回 true</returns>
    public async Task<bool> TestConnectionAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection.DbType != DatabaseType.SqlServer)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var db = CreateClient(connection);
            await db.Ado.SqlQueryAsync<int>("SELECT 1");
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

    /// <summary>
    /// 根据元数据行组装表模型。
    /// </summary>
    /// <param name="table">表元数据行</param>
    /// <param name="columns">列元数据行集合</param>
    /// <param name="primaryKeys">主键元数据行集合</param>
    /// <param name="foreignKeys">外键元数据行集合</param>
    /// <param name="indexes">索引元数据行集合</param>
    /// <returns>表模型</returns>
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

    /// <summary>
    /// 将列元数据行转换为列模型。
    /// </summary>
    /// <param name="row">列元数据行</param>
    /// <returns>列模型</returns>
    private static ColumnModel ToColumnModel(ColumnRow row)
    {
        return new ColumnModel
        {
            Name = row.Name,
            DbTypeName = row.DbTypeName,
            ColumnType = MapColumnType(row.DbTypeName),
            MaxLength = row.MaxLength is > 0 ? row.MaxLength : null,
            Precision = row.Precision,
            Scale = row.Scale,
            IsNullable = string.Equals(row.IsNullable, "YES", StringComparison.OrdinalIgnoreCase),
            DefaultValue = row.DefaultValue,
            Comment = row.Comment,
            IsIdentity = row.IsIdentity == 1,
            IsAutoIncrement = false,
            OrdinalPosition = row.OrdinalPosition
        };
    }

    /// <summary>
    /// 将索引元数据分组转换为索引模型。
    /// </summary>
    /// <param name="group">同一索引的元数据行分组</param>
    /// <returns>索引模型</returns>
    private static IndexModel ToIndexModel(IGrouping<string, IndexRow> group)
    {
        var rows = group.OrderBy(i => i.KeyOrdinal).ToList();
        var first = rows[0];

        return new IndexModel
        {
            Name = first.Name,
            ColumnNames = rows.Select(i => i.ColumnName).ToList(),
            IsUnique = first.IsUnique,
            IsClustered = first.IsClustered,
            IsPrimaryKey = first.IsPrimaryKey
        };
    }

    /// <summary>
    /// 将 SQL Server 原生类型映射为通用列类型。
    /// </summary>
    /// <param name="dbTypeName">SQL Server 原生类型名</param>
    /// <returns>通用列类型</returns>
    private static DbColumnType MapColumnType(string dbTypeName)
    {
        return dbTypeName.ToLowerInvariant() switch
        {
            "char" or "nchar" or "varchar" or "nvarchar" or "text" or "ntext" => DbColumnType.Text,
            "tinyint" or "smallint" or "int" or "bigint" => DbColumnType.Integer,
            "decimal" or "numeric" or "money" or "smallmoney" => DbColumnType.Decimal,
            "float" or "real" => DbColumnType.Float,
            "bit" => DbColumnType.Boolean,
            "date" or "time" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => DbColumnType.DateTime,
            "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => DbColumnType.Binary,
            "xml" => DbColumnType.Xml,
            _ => DbColumnType.Other
        };
    }

    /// <summary>
    /// 判断元数据行是否属于指定表。
    /// </summary>
    /// <param name="schemaName">元数据行的 Schema 名</param>
    /// <param name="tableName">元数据行的表名</param>
    /// <param name="table">目标表</param>
    /// <returns>属于同一张表时返回 true</returns>
    private static bool SameTable(string schemaName, string tableName, TableRow table)
    {
        return string.Equals(schemaName, table.SchemaName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(tableName, table.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 创建 SqlSugar 客户端。
    /// </summary>
    /// <param name="connection">数据库连接配置</param>
    /// <returns>SqlSugar 客户端</returns>
    private static SqlSugarClient CreateClient(DatabaseConnection connection)
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connection.ConnectionString.CheckTrustServerCertificate().CheckEncrypt(),
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true
        });
    }

    /// <summary>
    /// 确认连接配置为 SQL Server。
    /// </summary>
    /// <param name="connection">数据库连接配置</param>
    private static void EnsureSqlServer(DatabaseConnection connection)
    {
        if (connection.DbType != DatabaseType.SqlServer)
            throw new ArgumentException("SqlServerSchemaReader 只支持 SQL Server 连接。", nameof(connection));
    }

    /// <summary>
    /// 表查询结果行。
    ///</summary>
    private sealed class TableRow
    {
        /// <summary>
        /// Schema 名称
        ///</summary>
        public string SchemaName { get; set; } = string.Empty;

        /// <summary>
        /// 表名
        ///</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 表注释
        ///</summary>
        public string? Comment { get; set; }

        /// <summary>
        /// 预估行数
        ///</summary>
        public long EstimatedRowCount { get; set; }

        /// <summary>
        /// 预估数据大小（MB）
        ///</summary>
        public decimal EstimatedDataSizeMb { get; set; }
    }

    /// <summary>
    /// 列查询结果行。
    ///</summary>
    private sealed class ColumnRow
    {
        /// <summary>
        /// Schema 名称
        ///</summary>
        public string SchemaName { get; set; } = string.Empty;

        /// <summary>
        /// 表名
        ///</summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 列名
        ///</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 数据库原生类型名
        ///</summary>
        public string DbTypeName { get; set; } = string.Empty;

        /// <summary>
        /// 最大长度
        ///</summary>
        public int? MaxLength { get; set; }

        /// <summary>
        /// 数值精度
        ///</summary>
        public int? Precision { get; set; }

        /// <summary>
        /// 小数位数
        ///</summary>
        public int? Scale { get; set; }

        /// <summary>
        /// 是否允许 NULL
        ///</summary>
        public string IsNullable { get; set; } = string.Empty;

        /// <summary>
        /// 默认值表达式
        ///</summary>
        public string? DefaultValue { get; set; }

        /// <summary>
        /// 列注释
        ///</summary>
        public string? Comment { get; set; }

        /// <summary>
        /// 是否 IDENTITY 列
        ///</summary>
        public int IsIdentity { get; set; }

        /// <summary>
        /// 列顺序
        ///</summary>
        public int OrdinalPosition { get; set; }
    }

    /// <summary>
    /// 主键查询结果行。
    ///</summary>
    private sealed class PrimaryKeyRow
    {
        /// <summary>
        /// Schema 名称
        ///</summary>
        public string SchemaName { get; set; } = string.Empty;

        /// <summary>
        /// 表名
        ///</summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 主键列名
        ///</summary>
        public string ColumnName { get; set; } = string.Empty;

        /// <summary>
        /// 主键列顺序
        ///</summary>
        public int OrdinalPosition { get; set; }
    }

    /// <summary>
    /// 外键查询结果行。
    ///</summary>
    private sealed class ForeignKeyRow
    {
        /// <summary>
        /// Schema 名称
        ///</summary>
        public string SchemaName { get; set; } = string.Empty;

        /// <summary>
        /// 表名
        ///</summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 外键名称
        ///</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 本表外键列名
        ///</summary>
        public string ColumnName { get; set; } = string.Empty;

        /// <summary>
        /// 被引用表名
        ///</summary>
        public string ReferencedTable { get; set; } = string.Empty;

        /// <summary>
        /// 被引用列名
        ///</summary>
        public string ReferencedColumn { get; set; } = string.Empty;
    }

    /// <summary>
    /// 索引查询结果行。
    ///</summary>
    private sealed class IndexRow
    {
        /// <summary>
        /// Schema 名称
        ///</summary>
        public string SchemaName { get; set; } = string.Empty;

        /// <summary>
        /// 表名
        ///</summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 索引名称
        ///</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 索引列名
        ///</summary>
        public string ColumnName { get; set; } = string.Empty;

        /// <summary>
        /// 是否唯一索引
        ///</summary>
        public bool IsUnique { get; set; }

        /// <summary>
        /// 是否聚集索引
        ///</summary>
        public bool IsClustered { get; set; }

        /// <summary>
        /// 是否主键索引
        ///</summary>
        public bool IsPrimaryKey { get; set; }

        /// <summary>
        /// 索引列顺序
        ///</summary>
        public int KeyOrdinal { get; set; }
    }

    /// <summary>
    /// SQL Server 元数据查询语句。
    ///</summary>
    private static class Sql
    {
        /// <summary>
        /// 读取用户表列表
        ///</summary>
internal const string Tables = """
SELECT
    TABLE_SCHEMA AS SchemaName,
    TABLE_NAME AS Name,
    CAST(ep.value AS nvarchar(max)) AS Comment,
    ISNULL(ps.EstimatedRowCount, 0) AS EstimatedRowCount,
    ISNULL(ps.EstimatedDataSizeMb, 0) AS EstimatedDataSizeMb
FROM INFORMATION_SCHEMA.TABLES
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = OBJECT_ID(QUOTENAME(TABLE_SCHEMA) + '.' + QUOTENAME(TABLE_NAME))
    AND ep.minor_id = 0
    AND ep.name = 'MS_Description'
OUTER APPLY
(
    SELECT
        SUM(CASE WHEN p.index_id IN (0, 1) THEN p.row_count ELSE 0 END) AS EstimatedRowCount,
        CAST(SUM(a.total_pages) * 8.0 / 1024.0 AS decimal(18, 2)) AS EstimatedDataSizeMb
    FROM sys.dm_db_partition_stats p
    LEFT JOIN sys.allocation_units a
        ON p.partition_id = a.container_id
    WHERE p.object_id = OBJECT_ID(QUOTENAME(TABLE_SCHEMA) + '.' + QUOTENAME(TABLE_NAME))
) ps
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_SCHEMA, TABLE_NAME
""";

        /// <summary>
        /// 读取列列表
        ///</summary>
        internal const string Columns = """
SELECT
    c.TABLE_SCHEMA AS SchemaName,
    c.TABLE_NAME AS TableName,
    c.COLUMN_NAME AS Name,
    c.DATA_TYPE AS DbTypeName,
    c.CHARACTER_MAXIMUM_LENGTH AS MaxLength,
    c.NUMERIC_PRECISION AS Precision,
    c.NUMERIC_SCALE AS Scale,
    c.IS_NULLABLE AS IsNullable,
    c.COLUMN_DEFAULT AS DefaultValue,
    CAST(ep.value AS nvarchar(max)) AS Comment,
    COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity,
    c.ORDINAL_POSITION AS OrdinalPosition
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN sys.columns sc
    ON sc.object_id = OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME))
    AND sc.name = c.COLUMN_NAME
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = sc.object_id
    AND ep.minor_id = sc.column_id
    AND ep.name = 'MS_Description'
ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
""";

        /// <summary>
        /// 读取主键列列表
        ///</summary>
        internal const string PrimaryKeys = """
SELECT
    kcu.TABLE_SCHEMA AS SchemaName,
    kcu.TABLE_NAME AS TableName,
    kcu.COLUMN_NAME AS ColumnName,
    kcu.ORDINAL_POSITION AS OrdinalPosition
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
    ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
    AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
ORDER BY kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.ORDINAL_POSITION
""";

        /// <summary>
        /// 读取外键关系列表
        ///</summary>
        internal const string ForeignKeys = """
SELECT
    s.name AS SchemaName,
    tp.name AS TableName,
    fk.name AS Name,
    cp.name AS ColumnName,
    tr.name AS ReferencedTable,
    cr.name AS ReferencedColumn
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc
    ON fk.object_id = fkc.constraint_object_id
JOIN sys.tables tp
    ON fkc.parent_object_id = tp.object_id
JOIN sys.schemas s
    ON tp.schema_id = s.schema_id
JOIN sys.columns cp
    ON fkc.parent_object_id = cp.object_id
    AND fkc.parent_column_id = cp.column_id
JOIN sys.tables tr
    ON fkc.referenced_object_id = tr.object_id
JOIN sys.columns cr
    ON fkc.referenced_object_id = cr.object_id
    AND fkc.referenced_column_id = cr.column_id
ORDER BY s.name, tp.name, fk.name, fkc.constraint_column_id
""";

        /// <summary>
        /// 读取非主键索引列表
        ///</summary>
        internal const string Indexes = """
SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    i.name AS Name,
    c.name AS ColumnName,
    CONVERT(bit, i.is_unique) AS IsUnique,
    CONVERT(bit, CASE WHEN i.type = 1 THEN 1 ELSE 0 END) AS IsClustered,
    CONVERT(bit, i.is_primary_key) AS IsPrimaryKey,
    ic.key_ordinal AS KeyOrdinal
FROM sys.indexes i
JOIN sys.index_columns ic
    ON i.object_id = ic.object_id
    AND i.index_id = ic.index_id
JOIN sys.columns c
    ON ic.object_id = c.object_id
    AND ic.column_id = c.column_id
JOIN sys.tables t
    ON i.object_id = t.object_id
JOIN sys.schemas s
    ON t.schema_id = s.schema_id
WHERE i.name IS NOT NULL
    AND i.is_hypothetical = 0
    AND i.is_primary_key = 0
    AND ic.key_ordinal > 0
ORDER BY s.name, t.name, i.name, ic.key_ordinal
""";
    }

    /// <summary>
    /// 读取所有数据库对象（视图、存储过程、函数、触发器）
    ///</summary>
    public Task<IReadOnlyList<DatabaseObjectModel>> ReadAllObjectsAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        // v2.5 存根实现，后续填充 SQL Server 查询
        return Task.FromResult<IReadOnlyList<DatabaseObjectModel>>([]);
    }
}
