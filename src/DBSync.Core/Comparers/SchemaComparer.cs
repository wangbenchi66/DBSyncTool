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
    /// <param name="filter">过滤选项，控制忽略哪些差异</param>
    /// <returns>结构差异汇总，含新增表、删除表、变更表及循环依赖组</returns>
    public static SchemaDiff Compare(
        IEnumerable<TableModel> baseline,
        IEnumerable<TableModel> source,
        FilterOptions? filter = null)
    {
        // 表名配对不区分大小写（多数数据库表名大小写不敏感，两端书写可能不同，应视为同一张表）
        var baselineMap = ToKeyedDictionary(baseline, t => t.FullName, StringComparer.OrdinalIgnoreCase);
        var sourceMap = ToKeyedDictionary(source, t => t.FullName, StringComparer.OrdinalIgnoreCase);

        var added = sourceMap.Keys.Except(baselineMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(n => sourceMap[n]).ToList();
        var removed = baselineMap.Keys.Except(sourceMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(n => baselineMap[n]).ToList();

        var modified = baselineMap.Keys
            .Intersect(sourceMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(name => DiffTable(baselineMap[name], sourceMap[name], filter))
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
    private static TableDiff DiffTable(TableModel baseline, TableModel source, FilterOptions? filter = null)
    {
        // 列键按精确大小写存放：大小写敏感（CS）排序规则的库中，同名不同大小写的两列是真实不同的列，
        // 必须各自保留（不能按忽略大小写建字典，否则会撞键抛异常）；配对查找时再忽略大小写。
        var baselineCols = ToKeyedDictionary(baseline.Columns, c => c.Name, StringComparer.Ordinal);
        var sourceCols = ToKeyedDictionary(source.Columns, c => c.Name, StringComparer.Ordinal);

        var columnDiffs = new List<ColumnDiff>();
        var matchedBaselineCols = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, s) in sourceCols.Select(kv => (kv.Key, kv.Value)))
        {
            var bKey = FirstUnmatchedKey(baselineCols, matchedBaselineCols, name);
            if (bKey is null)
            {
                columnDiffs.Add(new ColumnDiff { Before = null, After = s, DiffType = ColumnDiffType.Added });
                continue;
            }
            matchedBaselineCols.Add(bKey);
            var b = baselineCols[bKey];
            var ignoreCommentInCols = filter?.IgnoreTableComments ?? false;
            if (!ColumnsEqual(b, s, ignoreCommentInCols))
                columnDiffs.Add(new ColumnDiff { Before = b, After = s, DiffType = ColumnDiffType.Modified });
        }

        // 基线上未在源库匹配到的剩余列 -> 已删除
        foreach (var (name, b) in baselineCols.Select(kv => (kv.Key, kv.Value)))
        {
            if (!matchedBaselineCols.Contains(name))
                columnDiffs.Add(new ColumnDiff { Before = b, After = null, DiffType = ColumnDiffType.Removed });
        }

        var ignoreIndexNames = filter?.IgnoreIndexNames ?? false;
        var baselineIdxMap = ToKeyedDictionary(baseline.Indexes, i => i.Name, StringComparer.Ordinal);
        var sourceIdxMap = ToKeyedDictionary(source.Indexes, i => i.Name, StringComparer.Ordinal);

        var indexDiffs = new List<IndexDiff>();

        if (!ignoreIndexNames)
        {
            var matchedBaselineIdx = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (name, s) in sourceIdxMap.Select(kv => (kv.Key, kv.Value)))
            {
                var bKey = FirstUnmatchedKey(baselineIdxMap, matchedBaselineIdx, name);
                if (bKey is null)
                {
                    indexDiffs.Add(new IndexDiff { Before = null, After = s, DiffType = IndexDiffType.Added });
                    continue;
                }
                matchedBaselineIdx.Add(bKey);
                var b = baselineIdxMap[bKey];
                if (!IndexesEqual(b, s))
                    indexDiffs.Add(new IndexDiff { Before = b, After = s, DiffType = IndexDiffType.Modified });
            }

            foreach (var (name, b) in baselineIdxMap.Select(kv => (kv.Key, kv.Value)))
            {
                if (!matchedBaselineIdx.Contains(name))
                    indexDiffs.Add(new IndexDiff { Before = b, After = null, DiffType = IndexDiffType.Removed });
            }
        }

        var ignoreComments = filter?.IgnoreTableComments ?? false;

        return new TableDiff
        {
            BaselineTable = baseline,
            SourceTable = source,
            ColumnDiffs = columnDiffs,
            IndexDiffs = indexDiffs,
            PrimaryKeyChanged = !baseline.PrimaryKeyColumns.SequenceEqual(source.PrimaryKeyColumns, StringComparer.OrdinalIgnoreCase),
            CommentChanged = !ignoreComments && !string.Equals(NormalizeComment(baseline.Comment), NormalizeComment(source.Comment), StringComparison.Ordinal)
        };
    }

    /// <summary>
    /// 判断两列定义是否结构相同
    /// </summary>
    /// <param name="a">第一列</param>
    /// <param name="b">第二列</param>
    /// <returns>结构完全相同时返回 true</returns>
    private static bool ColumnsEqual(ColumnModel a, ColumnModel b, bool ignoreComments = false) =>
        a.ColumnType == b.ColumnType &&
        a.MaxLength == b.MaxLength &&
        a.Precision == b.Precision &&
        a.Scale == b.Scale &&
        a.IsNullable == b.IsNullable &&
        a.IsIdentity == b.IsIdentity &&
        string.Equals(a.DefaultValue, b.DefaultValue, StringComparison.OrdinalIgnoreCase) &&
        (ignoreComments || string.Equals(NormalizeComment(a.Comment), NormalizeComment(b.Comment), StringComparison.Ordinal));

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

    /// <summary>
    /// 在按精确大小写键建的字典中，为指定名称查找首个尚未被占用的匹配键（比较时忽略大小写）。
    /// 这样既能容纳大小写敏感库中“仅大小写不同的多列/索引”，也能让两端书写大小写不同时正常配对。
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="map">精确键字典</param>
    /// <param name="matchedKeys">已参与配对的键集合</param>
    /// <param name="name">待匹配的名称</param>
    /// <returns>命中的键；未找到或全部被占用时返回 null</returns>
    private static string? FirstUnmatchedKey<T>(Dictionary<string, T> map, HashSet<string> matchedKeys, string name)
    {
        foreach (var key in map.Keys)
        {
            if (!matchedKeys.Contains(key) && string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return key;
        }
        return null;
    }

    /// <summary>
    /// 以指定键比较器建立字典；遇到字节级重复键时保留首个，避免抛“已添加相同键”异常
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="items">数据源</param>
    /// <param name="keySelector">键选择器</param>
    /// <param name="comparer">键比较器（表名通常忽略大小写；同端列/索引用精确大小写以容纳真实变体）</param>
    /// <returns>键值字典（重复键仅保留首个）</returns>
    private static Dictionary<string, T> ToKeyedDictionary<T>(IEnumerable<T> items, Func<T, string> keySelector, StringComparer comparer)
    {
        var map = new Dictionary<string, T>(comparer);
        foreach (var item in items)
        {
            var key = keySelector(item);
            if (!map.ContainsKey(key))
                map[key] = item;
        }
        return map;
    }

    private static string NormalizeComment(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
