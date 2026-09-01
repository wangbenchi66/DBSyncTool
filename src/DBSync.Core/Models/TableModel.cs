namespace DBSync.Core.Models;

/// <summary>
/// 数据库表的完整元数据定义
///</summary>
public sealed record TableModel
{
    /// <summary>
    /// 表名（不含 Schema 前缀）
    ///</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Schema 名称（SQL Server 的 dbo、PostgreSQL 的 public 等）
    ///</summary>
    public required string Schema { get; init; }

    /// <summary>
    /// 列定义列表（按 OrdinalPosition 排序）
    ///</summary>
    public required IReadOnlyList<ColumnModel> Columns { get; init; }

    /// <summary>
    /// 主键列名列表（复合主键时含多个）
    ///</summary>
    public required IReadOnlyList<string> PrimaryKeyColumns { get; init; }

    /// <summary>
    /// 外键约束列表
    ///</summary>
    public required IReadOnlyList<ForeignKeyModel> ForeignKeys { get; init; }

    /// <summary>
    /// 非主键索引列表
    ///</summary>
    public required IReadOnlyList<IndexModel> Indexes { get; init; }

    /// <summary>
    /// 预估行数
    ///</summary>
    public long? EstimatedRowCount { get; init; }

    /// <summary>
    /// 预估数据大小（MB）
    ///</summary>
    public decimal? EstimatedDataSizeMb { get; init; }

    /// <summary>
    /// 表是否有主键
    ///</summary>
    public bool HasPrimaryKey => PrimaryKeyColumns.Count > 0;

    /// <summary>
    /// 含 Schema 前缀的完整表名（如 dbo.Users）
    ///</summary>
    public string FullName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";
}
