namespace DBSync.Core.Models;

/// <summary>
/// 列差异类型枚举
///</summary>
public enum ColumnDiffType
{
    /// <summary>列在源库中新增</summary>
    Added,
    /// <summary>列在源库中被删除</summary>
    Removed,
    /// <summary>列的定义发生变更（类型、长度、可空性等）</summary>
    Modified
}

/// <summary>
/// 单列的差异记录
///</summary>
public sealed record ColumnDiff
{
    /// <summary>
    /// 基线（目标库）中的列定义，Added 时为 null
    ///</summary>
    public required ColumnModel? Before { get; init; }

    /// <summary>
    /// 源库中的列定义，Removed 时为 null
    ///</summary>
    public required ColumnModel? After { get; init; }

    /// <summary>
    /// 差异类型
    ///</summary>
    public required ColumnDiffType DiffType { get; init; }
}

/// <summary>
/// 索引差异类型枚举
///</summary>
public enum IndexDiffType
{
    /// <summary>索引在源库中新增</summary>
    Added,
    /// <summary>索引在源库中被删除</summary>
    Removed,
    /// <summary>索引的定义发生变更（列、唯一性等）</summary>
    Modified
}

/// <summary>
/// 单个索引的差异记录
///</summary>
public sealed record IndexDiff
{
    /// <summary>
    /// 基线中的索引定义，Added 时为 null
    ///</summary>
    public required IndexModel? Before { get; init; }

    /// <summary>
    /// 源库中的索引定义，Removed 时为 null
    ///</summary>
    public required IndexModel? After { get; init; }

    /// <summary>
    /// 差异类型
    ///</summary>
    public required IndexDiffType DiffType { get; init; }
}

/// <summary>
/// 单张表的结构差异汇总
///</summary>
public sealed record TableDiff
{
    /// <summary>
    /// 基线中该表的结构定义
    ///</summary>
    public required TableModel BaselineTable { get; init; }

    /// <summary>
    /// 源库中该表的当前结构定义
    ///</summary>
    public required TableModel SourceTable { get; init; }

    /// <summary>
    /// 列级别的差异列表
    ///</summary>
    public required IReadOnlyList<ColumnDiff> ColumnDiffs { get; init; }

    /// <summary>
    /// 索引级别的差异列表
    ///</summary>
    public required IReadOnlyList<IndexDiff> IndexDiffs { get; init; }

    /// <summary>
    /// 主键列是否发生变更
    ///</summary>
    public bool PrimaryKeyChanged { get; init; }

    /// <summary>
    /// 表注释是否发生变更
    ///</summary>
    public bool CommentChanged { get; init; }

    /// <summary>
    /// 该表是否存在任何结构差异
    ///</summary>
    public bool HasChanges => ColumnDiffs.Count > 0 || IndexDiffs.Count > 0 || PrimaryKeyChanged || CommentChanged;
}

/// <summary>
/// 整个快照与源库之间的结构差异汇总
///</summary>
public sealed record SchemaDiff
{
    /// <summary>
    /// 新增的表（基线有、源库无）
    ///</summary>
    public required IReadOnlyList<TableModel> AddedTables { get; init; }

    /// <summary>
    /// 删除的表（基线无、源库有），默认不纳入脚本，仅展示警告
    ///</summary>
    public required IReadOnlyList<TableModel> RemovedTables { get; init; }

    /// <summary>
    /// 结构发生变更的表
    ///</summary>
    public required IReadOnlyList<TableDiff> ModifiedTables { get; init; }

    /// <summary>
    /// 外键依赖中检测到循环依赖的表组（每组含相互依赖的表名列表）
    ///</summary>
    public required IReadOnlyList<IReadOnlyList<string>> CyclicDependencyGroups { get; init; }

    /// <summary>
    /// 是否存在任何结构差异（不含删除表）
    ///</summary>
    public bool HasChanges =>
        AddedTables.Count > 0 || ModifiedTables.Any(t => t.HasChanges);
}
