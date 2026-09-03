namespace DBSync.Desktop.Models;

/// <summary>
/// 应用全局设置（持久化到 settings.json）
///</summary>
public sealed record AppSettings
{
    /// <summary>
    /// 数据导出时的行数警告阈值（默认 10 万行）
    ///</summary>
    public int RowCountWarningThreshold { get; init; } = 100_000;

    /// <summary>
    /// 上次使用的连接名称
    ///</summary>
    public string? LastConnectionName { get; init; }

    /// <summary>
    /// 上次使用的导出路径
    ///</summary>
    public string? LastExportPath { get; init; }

    /// <summary>
    /// 上次使用的快照文件路径
    ///</summary>
    public string? LastSnapshotPath { get; init; }

    /// <summary>
    /// 最近操作的历史记录列表
    ///</summary>
    public List<RecentHistoryItem> RecentHistoryItems { get; init; } = [];

    /// <summary>
    /// 上次使用的导航页面标识
    ///</summary>
    public string? LastPageName { get; init; }

    /// <summary>
    /// 默认导出目录
    ///</summary>
    public string? DefaultExportDirectory { get; init; }

    /// <summary>
    /// 默认启用加密导出
    ///</summary>
    public bool DefaultEncrypt { get; init; } = true;

    /// <summary>
    /// 默认启用事务包裹
    ///</summary>
    public bool DefaultUseTransaction { get; init; } = true;
}
