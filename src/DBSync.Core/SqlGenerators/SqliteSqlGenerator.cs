using DBSync.Core;
using System.Text;
using DBSync.Core.Comparers;
using DBSync.Core.Models;

namespace DBSync.Core.SqlGenerators;

public sealed class SqliteSqlGenerator : ISqlGenerator
{
    public string GenerateUpgradeScript(
        DatabaseType dbType,
        SchemaDiff schemaDiff,
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>? fullData = null,
        bool useTransaction = true)
    {
        if (dbType != DatabaseType.Sqlite)
            throw new ArgumentException("SqliteSqlGenerator 只支持 SQLite。", nameof(dbType));

        var script = new StringBuilder();
        script.AppendLine("-- DBSyncTool Upgrade.sql");
        script.AppendLine($"-- 生成时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        script.AppendLine("-- 工具版本: DBSyncTool");
        script.AppendLine($"-- 影响表数量: {schemaDiff.AddedTables.Count + schemaDiff.ModifiedTables.Count + dataDiffs.Count}");
        script.AppendLine($"-- 预计影响行数: {dataDiffs.Values.Sum(d => d.RowsToInsert.Count)}");
        script.AppendLine();

        if (useTransaction)
        {
            script.AppendLine("BEGIN;");
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
        if (dbType != DatabaseType.Sqlite)
            throw new ArgumentException("SqliteSqlGenerator 只支持 SQLite。", nameof(dbType));

        var definitions = table.Columns
            .OrderBy(c => c.OrdinalPosition)
            .Select(column => FormatColumnDefinition(table, column))
            .ToList();

        if (table.PrimaryKeyColumns.Count == 1 &&
            !table.Columns.Any(c => string.Equals(c.Name, table.PrimaryKeyColumns[0], StringComparison.OrdinalIgnoreCase) &&
                                    (c.IsIdentity || c.IsAutoIncrement) &&
                                    IsIntegerType(c)))
        {
            definitions.Add($"PRIMARY KEY ({FormatColumnList(table.PrimaryKeyColumns)})");
        }
        else if (table.PrimaryKeyColumns.Count > 1)
        {
            definitions.Add($"PRIMARY KEY ({FormatColumnList(table.PrimaryKeyColumns)})");
        }

        definitions.AddRange(table.ForeignKeys.Select(fk =>
            $"FOREIGN KEY ({QuoteIdentifier(fk.ColumnName)}) REFERENCES {QuoteTableName(table.Schema, fk.ReferencedTable)} ({QuoteIdentifier(fk.ReferencedColumn)})"));

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

        return script.ToString();
    }

    public string GenerateDropTable(DatabaseType dbType, TableModel table)
    {
        if (dbType != DatabaseType.Sqlite)
            throw new ArgumentException("SqliteSqlGenerator 只支持 SQLite。", nameof(dbType));

        return $"DROP TABLE IF EXISTS {QuoteName(table)};";
    }

    public IReadOnlyList<string> GenerateAlterTable(DatabaseType dbType, TableDiff diff)
    {
        if (dbType != DatabaseType.Sqlite)
            throw new ArgumentException("SqliteSqlGenerator 只支持 SQLite。", nameof(dbType));

        var result = new List<string>();
        var tableName = QuoteName(diff.SourceTable);

        foreach (var columnDiff in diff.ColumnDiffs)
        {
            if (columnDiff.DiffType == ColumnDiffType.Added && columnDiff.After is not null)
            {
                result.Add($"ALTER TABLE {tableName} ADD COLUMN {FormatColumnDefinition(diff.SourceTable, columnDiff.After)};");
                continue;
            }

            if (columnDiff.DiffType == ColumnDiffType.Removed && columnDiff.Before is not null)
            {
                result.Add($"-- SQLite 需要重建表才能删除列：{columnDiff.Before.Name}");
                continue;
            }

            if (columnDiff.DiffType == ColumnDiffType.Modified && columnDiff.After is not null)
                result.Add($"-- SQLite 需要重建表才能修改列：{columnDiff.After.Name}");
        }

        if (diff.PrimaryKeyChanged)
            result.Add("-- SQLite 需要重建表才能修改主键。");

        foreach (var indexDiff in diff.IndexDiffs)
        {
            if ((indexDiff.DiffType is IndexDiffType.Removed or IndexDiffType.Modified) && indexDiff.Before is not null)
                result.Add(GenerateDropIndex(indexDiff.Before));

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
        if (dbType != DatabaseType.Sqlite)
            throw new ArgumentException("SqliteSqlGenerator 只支持 SQLite。", nameof(dbType));

        return GenerateInsertStatements(table, rows);
    }

    public string GenerateCreateTable(TableModel table) => GenerateCreateTable(DatabaseType.Sqlite, table);

    public string GenerateDropTable(TableModel table) => GenerateDropTable(DatabaseType.Sqlite, table);

    public IReadOnlyList<string> GenerateAlterTable(TableDiff diff) => GenerateAlterTable(DatabaseType.Sqlite, diff);

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

    private static string FormatColumnDefinition(TableModel table, ColumnModel column)
    {
        var pkColumn = table.PrimaryKeyColumns.Count == 1 &&
                       string.Equals(table.PrimaryKeyColumns[0], column.Name, StringComparison.OrdinalIgnoreCase);
        var identity = (column.IsIdentity || column.IsAutoIncrement) && pkColumn && IsIntegerType(column)
            ? " PRIMARY KEY AUTOINCREMENT"
            : string.Empty;
        var nullable = column.IsNullable && identity.Length == 0 ? "NULL" : "NOT NULL";
        var defaultValue = !string.IsNullOrWhiteSpace(column.DefaultValue) ? $" DEFAULT {column.DefaultValue}" : string.Empty;

        return $"{QuoteIdentifier(column.Name)} {FormatColumnType(column)}{identity} {nullable}{defaultValue}";
    }

    private static bool IsIntegerType(ColumnModel column)
    {
        return column.ColumnType == DbColumnType.Integer ||
               column.DbTypeName.Equals("integer", StringComparison.OrdinalIgnoreCase) ||
               column.DbTypeName.Equals("int", StringComparison.OrdinalIgnoreCase);
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

    private static string GenerateDropIndex(IndexModel index)
    {
        return $"DROP INDEX IF EXISTS {QuoteIdentifier(index.Name)};";
    }

    private static string FormatColumnList(IEnumerable<string> columnNames)
    {
        return string.Join(", ", columnNames.Select(QuoteIdentifier));
    }

    private static string QuoteName(TableModel table)
    {
        return DbDialectSupport.QuoteTableName(DatabaseType.Sqlite, table.Schema, table.Name);
    }

    private static string QuoteTableName(string schema, string tableName)
    {
        return DbDialectSupport.QuoteTableName(DatabaseType.Sqlite, schema, tableName);
    }

    private static string QuoteIdentifier(string name)
    {
        return DbDialectSupport.QuoteSqliteIdentifier(name);
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

    public IReadOnlyList<string> GenerateUpdateStatements(TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        var result = new List<string>();
        var tableName = QuoteName(table);
        var pkCols = table.PrimaryKeyColumns;
        foreach (var row in rows)
        {
            var sets = row.Where(kv => !pkCols.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                .Select(kv => $"{QuoteIdentifier(kv.Key)} = {FormatSqlLiteral(kv.Value)}");
            var wheres = pkCols.Select(pk => $"{QuoteIdentifier(pk)} = {FormatSqlLiteral(row.GetValueOrDefault(pk))}");
            result.Add($"UPDATE {tableName} SET {string.Join(", ", sets)} WHERE {string.Join(" AND ", wheres)};");
        }
        return result;
    }

    public IReadOnlyList<string> GenerateUpdateStatements(DatabaseType dbType, TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows) => GenerateUpdateStatements(table, rows);

    public IReadOnlyList<string> GenerateDeleteStatements(TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> primaryKeyValues)
    {
        var result = new List<string>();
        var tableName = QuoteName(table);
        var pkCols = table.PrimaryKeyColumns;
        foreach (var row in primaryKeyValues)
        {
            var wheres = pkCols.Select(pk => $"{QuoteIdentifier(pk)} = {FormatSqlLiteral(row.GetValueOrDefault(pk))}");
            result.Add($"DELETE FROM {tableName} WHERE {string.Join(" AND ", wheres)};");
        }
        return result;
    }

    public IReadOnlyList<string> GenerateDeleteStatements(DatabaseType dbType, TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> primaryKeyValues) => GenerateDeleteStatements(table, primaryKeyValues);
}
