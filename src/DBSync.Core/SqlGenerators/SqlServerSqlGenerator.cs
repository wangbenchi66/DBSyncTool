using System.Text;
using DBSync.Core.Comparers;
using DBSync.Core.Models;

namespace DBSync.Core.SqlGenerators;

/// <summary>
/// SQL Server SQL 语句生成器。
///</summary>
public sealed class SqlServerSqlGenerator : ISqlGenerator
{
    public string GenerateUpgradeScript(
        DatabaseType dbType,
        SchemaDiff schemaDiff,
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>? fullData = null,
        bool useTransaction = true)
    {
        if (dbType != DatabaseType.SqlServer)
            throw new ArgumentException("SqlServerSqlGenerator 只支持 SQL Server。", nameof(dbType));

        return GenerateUpgradeScript(schemaDiff, dataDiffs, fullData, useTransaction);
    }

    public string GenerateCreateTable(DatabaseType dbType, TableModel table)
    {
        if (dbType != DatabaseType.SqlServer)
            throw new ArgumentException("SqlServerSqlGenerator 只支持 SQL Server。", nameof(dbType));

        return GenerateCreateTable(table);
    }

    public string GenerateDropTable(DatabaseType dbType, TableModel table)
    {
        if (dbType != DatabaseType.SqlServer)
            throw new ArgumentException("SqlServerSqlGenerator 只支持 SQL Server。", nameof(dbType));

        return GenerateDropTable(table);
    }

    public IReadOnlyList<string> GenerateAlterTable(DatabaseType dbType, TableDiff diff)
    {
        if (dbType != DatabaseType.SqlServer)
            throw new ArgumentException("SqlServerSqlGenerator 只支持 SQL Server。", nameof(dbType));

        return GenerateAlterTable(diff);
    }

    public IReadOnlyList<string> GenerateInsertStatements(
        DatabaseType dbType,
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        if (dbType != DatabaseType.SqlServer)
            throw new ArgumentException("SqlServerSqlGenerator 只支持 SQL Server。", nameof(dbType));

        return GenerateInsertStatements(table, rows);
    }

    /// <summary>
    /// 根据结构差异和数据差异生成升级脚本。
    /// </summary>
    /// <param name="schemaDiff">结构差异</param>
    /// <param name="dataDiffs">数据差异</param>
    /// <param name="fullData">新增表完整数据</param>
    /// <returns>升级脚本</returns>
    public string GenerateUpgradeScript(
        SchemaDiff schemaDiff,
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>? fullData = null,
        bool useTransaction = true)
    {
        var script = new StringBuilder();
        script.AppendLine("-- DBSyncTool Upgrade.sql");
        script.AppendLine($"-- 生成时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        script.AppendLine("-- 工具版本: DBSyncTool");
        script.AppendLine($"-- 影响表数量: {schemaDiff.AddedTables.Count + schemaDiff.ModifiedTables.Count + dataDiffs.Count}");
        script.AppendLine($"-- 预计影响行数: {dataDiffs.Values.Sum(d => d.RowsToInsert.Count)}");
        script.AppendLine();

        if (useTransaction)
        {
            script.AppendLine("SET XACT_ABORT ON;");
            script.AppendLine("BEGIN TRANSACTION;");
            script.AppendLine();
        }

        foreach (var sql in GenerateDdlStatements(schemaDiff, includeDropTables: false))
        {
            script.AppendLine(sql);
            script.AppendLine();
        }

        var dataTables = schemaDiff.AddedTables.Concat(schemaDiff.ModifiedTables.Select(t => t.SourceTable));
        var sortedDataTables = FkTopologicalSorter.Sort(dataTables).Sorted;
        foreach (var table in sortedDataTables)
        {
            if (!dataDiffs.TryGetValue(table.FullName, out var diff) || diff.Skipped || diff.RowsToInsert.Count == 0)
                continue;

            var rows = SqlGeneratorRows.ResolveRowsToInsert(table, diff, fullData);
            foreach (var insert in GenerateInsertStatements(table, rows))
            {
                script.AppendLine(insert);
                script.AppendLine();
            }
        }

        if (useTransaction)
            script.AppendLine("COMMIT TRANSACTION;");
        script.AppendLine("GO");

        return script.ToString().TrimEnd();
    }

    /// <summary>
    /// 生成完整有序 DDL 语句序列。
    /// </summary>
    /// <param name="schemaDiff">结构差异</param>
    /// <returns>DDL 语句序列</returns>
    public IReadOnlyList<string> GenerateDdlScript(SchemaDiff schemaDiff)
    {
        return GenerateDdlStatements(schemaDiff, includeDropTables: true);
    }

    /// <summary>
    /// 生成单张表的 CREATE TABLE 语句。
    /// </summary>
    /// <param name="table">表元数据</param>
    /// <returns>CREATE TABLE SQL 字符串</returns>
    public string GenerateCreateTable(TableModel table)
    {
        var definitions = table.Columns
            .OrderBy(c => c.OrdinalPosition)
            .Select(column => FormatColumnDefinition(column, includeIdentity: true, includeDefault: true))
            .ToList();

        if (table.PrimaryKeyColumns.Count > 0)
            definitions.Add($"CONSTRAINT {QuoteIdentifier(PrimaryKeyName(table))} PRIMARY KEY ({FormatColumnList(table.PrimaryKeyColumns)})");

        definitions.AddRange(table.ForeignKeys.Select(fk =>
            $"CONSTRAINT {QuoteIdentifier(fk.Name)} FOREIGN KEY ({QuoteIdentifier(fk.ColumnName)}) REFERENCES {QuoteTableName(table.Schema, fk.ReferencedTable)} ({QuoteIdentifier(fk.ReferencedColumn)})"));

        var body = string.Join($",{Environment.NewLine}", definitions.Select(d => $"    {d}"));
        var script = new StringBuilder();
        script.AppendLine($"CREATE TABLE {QuoteName(table)}");
        script.AppendLine("(");
        script.AppendLine(body);
        script.Append(");");

        foreach (var index in table.Indexes.Where(i => !i.IsPrimaryKey))
        {
            script.AppendLine();
            script.Append(GenerateCreateIndex(table, index));
        }

        foreach (var comment in GenerateCommentStatements(table))
        {
            script.AppendLine();
            script.Append(comment);
        }

        return script.ToString();
    }

    /// <summary>
    /// 生成单张表的 DROP TABLE 语句。
    /// </summary>
    /// <param name="table">表元数据</param>
    /// <returns>DROP TABLE SQL 字符串</returns>
    public string GenerateDropTable(TableModel table)
    {
        return $"DROP TABLE IF EXISTS {QuoteName(table)};";
    }

    /// <summary>
    /// 根据表结构差异生成 ALTER TABLE 语句组。
    /// </summary>
    /// <param name="diff">单张表的结构差异</param>
    /// <returns>ALTER TABLE SQL 语句列表</returns>
    public IReadOnlyList<string> GenerateAlterTable(TableDiff diff)
    {
        var result = new List<string>();
        var tableName = QuoteName(diff.SourceTable);

        foreach (var columnDiff in diff.ColumnDiffs)
        {
            if (columnDiff.DiffType == ColumnDiffType.Added && columnDiff.After is not null)
            {
                result.Add($"ALTER TABLE {tableName} ADD {FormatColumnDefinition(columnDiff.After, includeIdentity: true, includeDefault: true)};");
                result.AddRange(GenerateCommentStatements(diff.SourceTable, columnDiff.After));
            }

            if (columnDiff.DiffType == ColumnDiffType.Removed && columnDiff.Before is not null)
                result.Add($"ALTER TABLE {tableName} DROP COLUMN {QuoteIdentifier(columnDiff.Before.Name)};");

            if (columnDiff.DiffType == ColumnDiffType.Modified && columnDiff.After is not null)
            {
                result.Add($"ALTER TABLE {tableName} ALTER COLUMN {FormatColumnDefinition(columnDiff.After, includeIdentity: false, includeDefault: false)};");
                result.AddRange(GenerateCommentStatements(diff.SourceTable, columnDiff.After));
            }
        }

        if (diff.CommentChanged)
            result.AddRange(GenerateCommentStatements(diff.SourceTable, includeColumns: false));

        if (diff.PrimaryKeyChanged)
        {
            result.Add($"ALTER TABLE {tableName} DROP CONSTRAINT {QuoteIdentifier(PrimaryKeyName(diff.BaselineTable))};");
            if (diff.SourceTable.PrimaryKeyColumns.Count > 0)
                result.Add($"ALTER TABLE {tableName} ADD CONSTRAINT {QuoteIdentifier(PrimaryKeyName(diff.SourceTable))} PRIMARY KEY ({FormatColumnList(diff.SourceTable.PrimaryKeyColumns)});");
        }

        foreach (var indexDiff in diff.IndexDiffs)
        {
            if ((indexDiff.DiffType is IndexDiffType.Removed or IndexDiffType.Modified) && indexDiff.Before is not null)
                result.Add(GenerateDropIndex(diff.SourceTable, indexDiff.Before));

            if ((indexDiff.DiffType is IndexDiffType.Added or IndexDiffType.Modified) && indexDiff.After is not null)
                result.Add(GenerateCreateIndex(diff.SourceTable, indexDiff.After));
        }

        return result;
    }

    /// <summary>
    /// 根据完整行数据生成 INSERT 语句组。
    /// </summary>
    /// <param name="table">表元数据</param>
    /// <param name="rows">行数据列表</param>
    /// <returns>INSERT SQL 语句列表</returns>
    public IReadOnlyList<string> GenerateInsertStatements(
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        if (rows.Count == 0)
            return [];

        var columns = table.Columns.OrderBy(c => c.OrdinalPosition).ToList();
        var columnNames = string.Join(", ", columns.Select(c => QuoteIdentifier(c.Name)));
        var statements = new List<string>();
        var hasIdentity = columns.Any(c => c.IsIdentity);
        if (hasIdentity)
            statements.Add($"SET IDENTITY_INSERT {QuoteName(table)} ON;");

        statements.AddRange(rows.Select(row => BuildInsertStatement(table, columnNames, columns, row)));

        if (hasIdentity)
            statements.Add($"SET IDENTITY_INSERT {QuoteName(table)} OFF;");

        return statements;
    }

    /// <summary>
    /// 格式化列定义。
    /// </summary>
    /// <param name="column">列模型</param>
    /// <param name="includeIdentity">是否输出 IDENTITY 声明</param>
    /// <param name="includeDefault">是否输出默认值声明</param>
    /// <returns>列定义 SQL</returns>
    private static string FormatColumnDefinition(ColumnModel column, bool includeIdentity, bool includeDefault)
    {
        var identity = includeIdentity && column.IsIdentity ? " IDENTITY(1,1)" : string.Empty;
        var defaultValue = includeDefault && !string.IsNullOrWhiteSpace(column.DefaultValue) ? $" DEFAULT {column.DefaultValue}" : string.Empty;
        var nullable = column.IsNullable ? "NULL" : "NOT NULL";

        return $"{QuoteIdentifier(column.Name)} {FormatColumnType(column)}{identity} {nullable}{defaultValue}";
    }

    /// <summary>
    /// 格式化列类型。
    /// </summary>
    /// <param name="column">列模型</param>
    /// <returns>列类型 SQL</returns>
    private static string FormatColumnType(ColumnModel column)
    {
        var typeName = column.DbTypeName;
        var lowerTypeName = typeName.ToLowerInvariant();

        if (lowerTypeName is "char" or "nchar" or "varchar" or "nvarchar" or "binary" or "varbinary")
            return $"{typeName}({(column.MaxLength.HasValue ? column.MaxLength.Value.ToString() : "max")})";

        if (lowerTypeName is "decimal" or "numeric")
            return $"{typeName}({column.Precision ?? 18},{column.Scale ?? 2})";

        return typeName;
    }

    /// <summary>
    /// 生成创建索引语句。
    /// </summary>
    /// <param name="table">表模型</param>
    /// <param name="index">索引模型</param>
    /// <returns>创建索引 SQL</returns>
    private static string GenerateCreateIndex(TableModel table, IndexModel index)
    {
        var unique = index.IsUnique ? "UNIQUE " : string.Empty;
        var clustered = index.IsClustered ? "CLUSTERED " : "NONCLUSTERED ";

        return $"CREATE {unique}{clustered}INDEX {QuoteIdentifier(index.Name)} ON {QuoteName(table)} ({FormatColumnList(index.ColumnNames)});";
    }

    private static IEnumerable<string> GenerateCommentStatements(TableModel table, bool includeColumns = true)
    {
        if (!string.IsNullOrWhiteSpace(table.Comment))
            yield return GenerateCommentStatement(table, null, table.Comment);

        if (!includeColumns)
            yield break;

        foreach (var column in table.Columns.Where(c => !string.IsNullOrWhiteSpace(c.Comment)))
            yield return GenerateCommentStatement(table, column, column.Comment);
    }

    private static IEnumerable<string> GenerateCommentStatements(TableModel table, ColumnModel column)
    {
        if (!string.IsNullOrWhiteSpace(column.Comment))
            yield return GenerateCommentStatement(table, column, column.Comment);
    }

    private static string GenerateCommentStatement(TableModel table, ColumnModel? column, string? comment)
    {
        var level2 = column is null
            ? string.Empty
            : $", @level2type=N'COLUMN', @level2name=N'{EscapeSqlLiteral(column.Name)}'";

        return $"EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{EscapeSqlLiteral(comment!)}', @level0type=N'SCHEMA', @level0name=N'{EscapeSqlLiteral(table.Schema)}', @level1type=N'TABLE', @level1name=N'{EscapeSqlLiteral(table.Name)}'{level2};";
    }

    /// <summary>
    /// 生成删除索引语句。
    /// </summary>
    /// <param name="table">表模型</param>
    /// <param name="index">索引模型</param>
    /// <returns>删除索引 SQL</returns>
    private static string GenerateDropIndex(TableModel table, IndexModel index)
    {
        return $"DROP INDEX {QuoteIdentifier(index.Name)} ON {QuoteName(table)};";
    }

    /// <summary>
    /// 生成 DDL 语句序列。
    /// </summary>
    /// <param name="schemaDiff">结构差异</param>
    /// <param name="includeDropTables">是否输出 DROP TABLE 语句</param>
    /// <returns>DDL 语句序列</returns>
    private IReadOnlyList<string> GenerateDdlStatements(SchemaDiff schemaDiff, bool includeDropTables)
    {
        var result = new List<string>();

        foreach (var cycle in schemaDiff.CyclicDependencyGroups)
            result.Add($"-- 检测到循环外键依赖，需要手动处理: {string.Join(", ", cycle)}");

        var (removedTables, _) = FkTopologicalSorter.Sort(schemaDiff.RemovedTables);
        foreach (var table in removedTables.Reverse())
        {
            result.Add(includeDropTables
                ? GenerateDropTable(table)
                : $"-- 警告: 源库缺少表 {QuoteName(table)}，默认不生成 DROP TABLE。");
        }

        var (addedTables, _) = FkTopologicalSorter.Sort(schemaDiff.AddedTables);
        result.AddRange(addedTables.Select(GenerateCreateTable));

        foreach (var tableDiff in schemaDiff.ModifiedTables)
            result.AddRange(GenerateAlterTable(tableDiff));

        return result;
    }

    /// <summary>
    /// 格式化列名列表。
    /// </summary>
    /// <param name="columnNames">列名列表</param>
    /// <returns>列名 SQL 片段</returns>
    private static string FormatColumnList(IEnumerable<string> columnNames)
    {
        return string.Join(", ", columnNames.Select(QuoteIdentifier));
    }

    /// <summary>
    /// 生成主键约束名。
    /// </summary>
    /// <param name="table">表模型</param>
    /// <returns>主键约束名</returns>
    private static string PrimaryKeyName(TableModel table)
    {
        return $"PK_{table.Name}";
    }

    /// <summary>
    /// 引用表名。
    /// </summary>
    /// <param name="table">表模型</param>
    /// <returns>带方括号的表名</returns>
    private static string QuoteName(TableModel table)
    {
        return QuoteTableName(table.Schema, table.Name);
    }

    /// <summary>
    /// 引用表名。
    /// </summary>
    /// <param name="schema">Schema 名</param>
    /// <param name="tableName">表名</param>
    /// <returns>带方括号的表名</returns>
    private static string QuoteTableName(string schema, string tableName)
    {
        return string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(tableName)
            : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(tableName)}";
    }

    /// <summary>
    /// 引用标识符。
    /// </summary>
    /// <param name="name">标识符名称</param>
    /// <returns>带方括号的标识符</returns>
    private static string QuoteIdentifier(string name)
    {
        return $"[{name.Replace("]", "]]")}]";
    }

    /// <summary>
    /// 生成单条 INSERT 语句。
    /// </summary>
    private static string BuildInsertStatement(
        TableModel table,
        string columnNames,
        IReadOnlyList<ColumnModel> columns,
        IReadOnlyDictionary<string, string?> row)
    {
        var values = columns.Select(column => FormatSqlLiteral(row.TryGetValue(column.Name, out var value) ? value : null));
        return $"INSERT INTO {QuoteName(table)} ({columnNames}) VALUES ({string.Join(", ", values)});";
    }

    /// <summary>
    /// 格式化 SQL 字面量。
    /// </summary>
    private static string FormatSqlLiteral(string? value)
    {
        return value is null
            ? "NULL"
            : $"N'{value.Replace("'", "''")}'";
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''");
}
