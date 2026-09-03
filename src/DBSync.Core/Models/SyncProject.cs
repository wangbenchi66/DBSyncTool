namespace DBSync.Core.Models;

/// <summary>
/// 同步项目配置文件模型，保存到 .dbsync-project JSON 文件
///</summary>
public sealed record SyncProject
{
    /// <summary>
    /// 项目名称
    ///</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// 源库连接名称
    ///</summary>
    public string? SourceConnectionName { get; init; }

    /// <summary>
    /// 目标库连接名称
    ///</summary>
    public string? TargetConnectionName { get; init; }

    /// <summary>
    /// 快照文件路径
    ///</summary>
    public string? SnapshotPath { get; init; }

    /// <summary>
    /// 过滤规则
    ///</summary>
    public FilterOptions Filters { get; init; } = new();

    /// <summary>
    /// 是否启用事务
    ///</summary>
    public bool UseTransaction { get; init; } = true;

    /// <summary>
    /// 导出目录
    ///</summary>
    public string? ExportDirectory { get; init; }

    /// <summary>
    /// 创建时间
    ///</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// 更新时间
    ///</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
}
