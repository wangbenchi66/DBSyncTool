namespace DBSync.Core.Models;

/// <summary>
/// 单张表的数据差异汇总，数据同步仅处理新增行
///</summary>
public sealed record DataDiff
{
    /// <summary>
    /// 新增行（源库有、基线无）——将生成 INSERT 语句
    ///</summary>
    public required IReadOnlyList<RowHash> RowsToInsert { get; init; }

    /// <summary>
    /// 删除行（基线有、源库无）——仅记录在差异报告中，不生成 SQL
    ///</summary>
    public required IReadOnlyList<RowHash> DeletedRows { get; init; }

    /// <summary>
    /// 变更行（主键相同，哈希不同）——仅记录在差异报告中，不生成 SQL
    ///</summary>
    public required IReadOnlyList<RowHash> ChangedRows { get; init; }

    /// <summary>
    /// 是否因表无主键而跳过了数据比对
    ///</summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// 无任何差异的空结果
    ///</summary>
    public static DataDiff Empty => new()
    {
        RowsToInsert = [],
        DeletedRows = [],
        ChangedRows = []
    };

    /// <summary>
    /// 因无主键跳过数据比对的结果
    ///</summary>
    public static DataDiff NoPrimaryKey => new()
    {
        RowsToInsert = [],
        DeletedRows = [],
        ChangedRows = [],
        Skipped = true
    };
}
