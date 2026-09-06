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

    private static string BuildPrimaryKeyString(TableModel table, IReadOnlyDictionary<string, string?> row)
    {
        return string.Join("|", table.PrimaryKeyColumns
            .OrderBy(name => name)
            .Select(name => $"{name}={(row.TryGetValue(name, out var value) ? value : null) ?? "NULL"}"));
    }
}
