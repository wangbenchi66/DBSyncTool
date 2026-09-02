using DBSync.Core;
using System.Text;
using DBSync.Core.Comparers;
using DBSync.Core.Models;

namespace DBSync.Core.SqlGenerators;

public sealed class MySqlSqlGenerator : ISqlGenerator
{
    public string GenerateUpgradeScript(
        DatabaseType dbType,
        SchemaDiff schemaDiff,
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>? fullData = null,
        bool useTransaction = true)
    {
        if (dbType != DatabaseType.MySql)
            throw new ArgumentException("MySqlSqlGenerator 只支持 MySQL。", nameof(dbType));

        var script = new StringBuilder();
        script.AppendLine("-- DBSyncTool Upgrade.sql");
        script.AppendLine($"-- 生成时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        script.AppendLine("-- 工具版本: DBSyncTool");
        script.AppendLine($"-- 影响表数量: {schemaDiff.AddedTables.Count + schemaDiff.ModifiedTables.Count + dataDiffs.Count}");
        script.AppendLine($"-- 预计影响行数: {dataDiffs.Values.Sum(d => d.RowsToInsert.Count)}");
        script.AppendLine();

        if (useTransaction)
        {
            script.AppendLine("START TRANSACTION;");
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
            script.AppendLine("COMMIT;");
        return script.ToString().TrimEnd();
    }

    public string GenerateCreateTable(DatabaseType dbType, TableModel table)
    {
        if (dbType != DatabaseType.MySql)
            throw new ArgumentException("MySqlSqlGenerator 只支持 MySQL。", nameof(dbType));

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
        script.Append(") ENGINE=InnoDB");
        if (!string.IsNullOrWhiteSpace(table.Comment))
            script.Append($" COMMENT='{EscapeSqlLiteral(table.Comment)}'");
        script.AppendLine(";");

        foreach (var index in table.Indexes.Where(i => !i.IsPrimaryKey))
        {
            script.AppendLine();
            script.Append(GenerateCreateIndex(table, index));
        }

        return script.ToString();
    }

    public string GenerateDropTable(DatabaseType dbType, TableModel table)
    {
        if (dbType != DatabaseType.MySql)
            throw new ArgumentException("MySqlSqlGenerator 只支持 MySQL。", nameof(dbType));

        return $"DROP TABLE IF EXISTS {QuoteName(table)};";
    }

    public IReadOnlyList<string> GenerateAlterTable(DatabaseType dbType, TableDiff diff)
    {
        if (dbType != DatabaseType.MySql)
            throw new ArgumentException("MySqlSqlGenerator 只支持 MySQL。", nameof(dbType));

        var result = new List<string>();
        var tableName = QuoteName(diff.SourceTable);

        foreach (var columnDiff in diff.ColumnDiffs)
        {
            if (columnDiff.DiffType == ColumnDiffType.Added && columnDiff.After is not null)
                result.Add($"ALTER TABLE {tableName} ADD COLUMN {FormatColumnDefinition(columnDiff.After, includeIdentity: true, includeDefault: true)};");

            if (columnDiff.DiffType == ColumnDiffType.Removed && columnDiff.Before is not null)
                result.Add($"ALTER TABLE {tableName} DROP COLUMN {QuoteIdentifier(columnDiff.Before.Name)};");

            if (columnDiff.DiffType == ColumnDiffType.Modified && columnDiff.After is not null)
                result.Add($"ALTER TABLE {tableName} MODIFY COLUMN {FormatColumnDefinition(columnDiff.After, includeIdentity: true, includeDefault: true)};");
        }

        if (diff.CommentChanged)
            result.Add($"ALTER TABLE {tableName} COMMENT = '{EscapeSqlLiteral(diff.SourceTable.Comment ?? string.Empty)}';");

        if (diff.PrimaryKeyChanged)
        {
            result.Add($"ALTER TABLE {tableName} DROP PRIMARY KEY;");
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

    public IReadOnlyList<string> GenerateInsertStatements(
        DatabaseType dbType,
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        if (dbType != DatabaseType.MySql)
            throw new ArgumentException("MySqlSqlGenerator 只支持 MySQL。", nameof(dbType));

        return GenerateInsertStatements(table, rows);
    }

    public string GenerateCreateTable(TableModel table) => GenerateCreateTable(DatabaseType.MySql, table);

    public string GenerateDropTable(TableModel table) => GenerateDropTable(DatabaseType.MySql, table);

    public IReadOnlyList<string> GenerateAlterTable(TableDiff diff) => GenerateAlterTable(DatabaseType.MySql, diff);

    public IReadOnlyList<string> GenerateInsertStatements(
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        if (rows.Count == 0)
            return [];

        var columns = table.Columns.OrderBy(c => c.OrdinalPosition).ToList();
        var columnNames = string.Join(", ", columns.Select(c => QuoteIdentifier(c.Name)));
        return rows.Select(row => BuildInsertStatement(table, columnNames, columns, row)).ToList();
    }

    private IReadOnlyList<string> GenerateDdlStatements(SchemaDiff schemaDiff, bool includeDropTables)
    {
        var result = new List<string>();

        foreach (var cycle in schemaDiff.CyclicDependencyGroups)
            result.Add($"-- 检测到循环外键依赖，需要手动处理: {string.Join(", ", cycle)}");

        var (removedTables, _) = FkTopologicalSorter.Sort(schemaDiff.RemovedTables);
        foreach (var table in removedTables.Reverse())
        {
            result.Add(includeDropTables
                ? $"DROP TABLE IF EXISTS {QuoteName(table)};"
                : $"-- 警告: 源库缺少表 {QuoteName(table)}，默认不生成 DROP TABLE。");
        }

        var (addedTables, _) = FkTopologicalSorter.Sort(schemaDiff.AddedTables);
        result.AddRange(addedTables.Select(GenerateCreateTable));

        foreach (var tableDiff in schemaDiff.ModifiedTables)
            result.AddRange(GenerateAlterTable(tableDiff));

        return result;
    }

    private static string FormatColumnDefinition(ColumnModel column, bool includeIdentity, bool includeDefault)
    {
        var identity = includeIdentity && (column.IsIdentity || column.IsAutoIncrement) ? " AUTO_INCREMENT" : string.Empty;
        var defaultValue = includeDefault && !string.IsNullOrWhiteSpace(column.DefaultValue) ? $" DEFAULT {column.DefaultValue}" : string.Empty;
        var comment = !string.IsNullOrWhiteSpace(column.Comment) ? $" COMMENT '{EscapeSqlLiteral(column.Comment)}'" : string.Empty;
        var nullable = column.IsNullable ? "NULL" : "NOT NULL";

        return $"{QuoteIdentifier(column.Name)} {FormatColumnType(column)}{identity} {nullable}{defaultValue}{comment}";
    }

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

    private static string GenerateCreateIndex(TableModel table, IndexModel index)
    {
        var unique = index.IsUnique ? "UNIQUE " : string.Empty;
        return $"CREATE {unique}INDEX {QuoteIdentifier(index.Name)} ON {QuoteName(table)} ({FormatColumnList(index.ColumnNames)});";
    }

    private static string GenerateDropIndex(TableModel table, IndexModel index)
    {
        return $"DROP INDEX {QuoteIdentifier(index.Name)} ON {QuoteName(table)};";
    }

    private static string FormatColumnList(IEnumerable<string> columnNames)
    {
        return string.Join(", ", columnNames.Select(QuoteIdentifier));
    }

    private static string PrimaryKeyName(TableModel table)
    {
        return $"PK_{table.Name}";
    }

    private static string QuoteName(TableModel table)
    {
        return DbDialectSupport.QuoteTableName(DatabaseType.MySql, table.Schema, table.Name);
    }

    private static string QuoteTableName(string schema, string tableName)
    {
        return DbDialectSupport.QuoteTableName(DatabaseType.MySql, schema, tableName);
    }

    private static string QuoteIdentifier(string name)
    {
        return DbDialectSupport.QuoteMySqlIdentifier(name);
    }

    private static string BuildInsertStatement(
        TableModel table,
        string columnNames,
        IReadOnlyList<ColumnModel> columns,
        IReadOnlyDictionary<string, string?> row)
    {
        var values = columns.Select(column => FormatSqlLiteral(row.TryGetValue(column.Name, out var value) ? value : null));
        return $"INSERT INTO {QuoteName(table)} ({columnNames}) VALUES ({string.Join(", ", values)});";
    }

    private static string FormatSqlLiteral(string? value)
    {
        return value is null
            ? "NULL"
            : $"'{value.Replace("'", "''")}'";
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''");
}
