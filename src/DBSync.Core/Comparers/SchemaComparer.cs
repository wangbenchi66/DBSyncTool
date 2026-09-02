using DBSync.Core.Models;

namespace DBSync.Core.Comparers;

/// <summary>
/// 纯函数结构比较器，对比两组表元数据产生 SchemaDiff
///</summary>
public static class SchemaComparer
{
    /// <summary>
    /// 比较基线（目标库）和源库的表结构，返回差异汇总
    /// </summary>
    /// <param name="baseline">基线表集合（来自目标库快照，通常为生产库）</param>
    /// <param name="source">源库当前表集合（通常为测试库）</param>
    /// <returns>结构差异汇总，含新增表、删除表、变更表及循环依赖组</returns>
    public static SchemaDiff Compare(
        IEnumerable<TableModel> baseline,
        IEnumerable<TableModel> source)
    {
        var baselineMap = baseline.ToDictionary(t => t.FullName, t => t, StringComparer.OrdinalIgnoreCase);
        var sourceMap = source.ToDictionary(t => t.FullName, t => t, StringComparer.OrdinalIgnoreCase);

        var added = sourceMap.Keys.Except(baselineMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(n => sourceMap[n]).ToList();
        var removed = baselineMap.Keys.Except(sourceMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(n => baselineMap[n]).ToList();

        var modified = baselineMap.Keys
            .Intersect(sourceMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(name => DiffTable(baselineMap[name], sourceMap[name]))
            .Where(d => d.HasChanges)
            .ToList();

        var (_, cycles) = FkTopologicalSorter.Sort(sourceMap.Values);

        return new SchemaDiff
        {
            AddedTables = added,
            RemovedTables = removed,
            ModifiedTables = modified,
            CyclicDependencyGroups = cycles
        };
    }

    /// <summary>
    /// 比较单张表的基线与源库版本，返回该表的结构差异
    /// </summary>
    /// <param name="baseline">基线版本的表结构</param>
    /// <param name="source">源库版本的表结构</param>
    /// <returns>TableDiff，若无差异则 HasChanges 为 false</returns>
    private static TableDiff DiffTable(TableModel baseline, TableModel source)
    {
        var baselineCols = baseline.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var sourceCols = source.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        var columnDiffs = new List<ColumnDiff>();

        foreach (var name in sourceCols.Keys.Except(baselineCols.Keys, StringComparer.OrdinalIgnoreCase))
            columnDiffs.Add(new ColumnDiff { Before = null, After = sourceCols[name], DiffType = ColumnDiffType.Added });

        foreach (var name in baselineCols.Keys.Except(sourceCols.Keys, StringComparer.OrdinalIgnoreCase))
            columnDiffs.Add(new ColumnDiff { Before = baselineCols[name], After = null, DiffType = ColumnDiffType.Removed });

        foreach (var name in baselineCols.Keys.Intersect(sourceCols.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var b = baselineCols[name];
            var s = sourceCols[name];
            if (!ColumnsEqual(b, s))
                columnDiffs.Add(new ColumnDiff { Before = b, After = s, DiffType = ColumnDiffType.Modified });
        }

        var baselineIdxMap = baseline.Indexes.ToDictionary(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);
        var sourceIdxMap = source.Indexes.ToDictionary(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);

        var indexDiffs = new List<IndexDiff>();

        foreach (var name in sourceIdxMap.Keys.Except(baselineIdxMap.Keys, StringComparer.OrdinalIgnoreCase))
            indexDiffs.Add(new IndexDiff { Before = null, After = sourceIdxMap[name], DiffType = IndexDiffType.Added });

        foreach (var name in baselineIdxMap.Keys.Except(sourceIdxMap.Keys, StringComparer.OrdinalIgnoreCase))
            indexDiffs.Add(new IndexDiff { Before = baselineIdxMap[name], After = null, DiffType = IndexDiffType.Removed });

        foreach (var name in baselineIdxMap.Keys.Intersect(sourceIdxMap.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var b = baselineIdxMap[name];
            var s = sourceIdxMap[name];
            if (!IndexesEqual(b, s))
                indexDiffs.Add(new IndexDiff { Before = b, After = s, DiffType = IndexDiffType.Modified });
        }

        return new TableDiff
        {
            BaselineTable = baseline,
            SourceTable = source,
            ColumnDiffs = columnDiffs,
            IndexDiffs = indexDiffs,
            PrimaryKeyChanged = !baseline.PrimaryKeyColumns.SequenceEqual(source.PrimaryKeyColumns, StringComparer.OrdinalIgnoreCase),
            CommentChanged = !string.Equals(NormalizeComment(baseline.Comment), NormalizeComment(source.Comment), StringComparison.Ordinal)
        };
    }

    /// <summary>
    /// 判断两列定义是否结构相同
    /// </summary>
    /// <param name="a">第一列</param>
    /// <param name="b">第二列</param>
    /// <returns>结构完全相同时返回 true</returns>
    private static bool ColumnsEqual(ColumnModel a, ColumnModel b) =>
        a.ColumnType == b.ColumnType &&
        a.MaxLength == b.MaxLength &&
        a.Precision == b.Precision &&
        a.Scale == b.Scale &&
        a.IsNullable == b.IsNullable &&
        a.IsIdentity == b.IsIdentity &&
        string.Equals(a.DefaultValue, b.DefaultValue, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(NormalizeComment(a.Comment), NormalizeComment(b.Comment), StringComparison.Ordinal);

    /// <summary>
    /// 判断两索引定义是否结构相同
    /// </summary>
    /// <param name="a">第一索引</param>
    /// <param name="b">第二索引</param>
    /// <returns>结构完全相同时返回 true</returns>
    private static bool IndexesEqual(IndexModel a, IndexModel b) =>
        a.IsUnique == b.IsUnique &&
        a.IsClustered == b.IsClustered &&
        a.IsPrimaryKey == b.IsPrimaryKey &&
        a.ColumnNames.SequenceEqual(b.ColumnNames, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeComment(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
