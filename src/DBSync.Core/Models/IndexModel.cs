namespace DBSync.Core.Models;

/// <summary>
/// 数据库表的索引定义
///</summary>
public sealed record IndexModel
{
    /// <summary>
    /// 索引名称
    ///</summary>
    public required string Name { get; init; }

    /// <summary>
    /// 索引包含的列名列表（按索引顺序）
    ///</summary>
    public required IReadOnlyList<string> ColumnNames { get; init; }

    /// <summary>
    /// 是否为唯一索引
    ///</summary>
    public bool IsUnique { get; init; }

    /// <summary>
    /// 是否为聚集索引（SQL Server 专有概念）
    ///</summary>
    public bool IsClustered { get; init; }

    /// <summary>
    /// 是否为主键索引
    ///</summary>
    public bool IsPrimaryKey { get; init; }
}
