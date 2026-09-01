namespace DBSync.Core.Models;

/// <summary>
/// 数据库表的外键约束定义
///</summary>
public sealed record ForeignKeyModel
{
    /// <summary>
    /// 外键约束名称
    ///</summary>
    public required string Name { get; init; }

    /// <summary>
    /// 本表中的外键列名
    ///</summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// 被引用的目标表名
    ///</summary>
    public required string ReferencedTable { get; init; }

    /// <summary>
    /// 被引用目标表中的列名
    ///</summary>
    public required string ReferencedColumn { get; init; }
}
