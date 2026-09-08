using DBSync.Core.Models;

namespace DBSync.Core.SqlGenerators;

public static class SqlGeneratorRows
{
    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> ResolveRowsToInsert(
        TableModel table,
        DataDiff diff,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>? fullData)
    {
        if (fullData is null || !fullData.TryGetValue(table.FullName, out var fullRows))
            return diff.RowsToInsert.Select(r => r.PrimaryKeyValues).ToList();

        var keys = diff.RowsToInsert
            .Select(r => r.PrimaryKeyString)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matched = fullRows
            .Where(row => keys.Contains(BuildPrimaryKeyString(table, row)))
            .ToList();

        // 当完整数据匹配失败时，回退到主键数据
        return matched.Count > 0 ? matched : diff.RowsToInsert.Select(r => r.PrimaryKeyValues).ToList();
    }

    /// <summary>
    /// 解析所有需要生成数据 DML 的表：结构变更的表 + 纯数据差异的表
    /// </summary>
    public static IEnumerable<TableModel> ResolveDataTables(
        SchemaDiff schemaDiff,
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        IReadOnlyDictionary<string, TableModel>? allTables)
    {
        var schemaTables = schemaDiff.AddedTables
            .Concat(schemaDiff.ModifiedTables.Select(t => t.SourceTable))
            .ToList();

        var schemaTableNames = new HashSet<string>(
            schemaTables.Select(t => t.FullName), StringComparer.OrdinalIgnoreCase);

        // 纯数据差异的表（不在结构变更中）
        if (allTables is not null)
        {
            foreach (var tableName in dataDiffs.Keys)
            {
                if (!schemaTableNames.Contains(tableName) &&
                    allTables.TryGetValue(tableName, out var table))
                {
                    schemaTables.Add(table);
                }
            }
        }

        return schemaTables;
    }

    private static string BuildPrimaryKeyString(TableModel table, IReadOnlyDictionary<string, string?> row)
    {
        return string.Join("|", table.PrimaryKeyColumns
            .OrderBy(name => name)
            .Select(name => $"{name}={(row.TryGetValue(name, out var value) ? value : null) ?? "NULL"}"));
    }
}
