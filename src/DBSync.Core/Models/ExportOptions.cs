namespace DBSync.Core.Models;

/// <summary>
/// 单张表的导出选项配置
///</summary>
public sealed record TableExportOptions
{
    /// <summary>
    /// 表名
    ///</summary>
    public required string TableName { get; init; }

    /// <summary>
    /// 是否同步结构（DDL），默认为 true
    ///</summary>
    public bool SyncSchema { get; init; } = true;

    /// <summary>
    /// 是否同步数据（仅对新增表的完整数据导出有效）
    ///</summary>
    public bool SyncData { get; init; }

    /// <summary>
    /// 用户自定义 WHERE 子句过滤条件（不含 WHERE 关键字，可为空）
    ///</summary>
    public string? WhereClause { get; init; }
}

/// <summary>
/// 快照导出操作的整体配置
///</summary>
public sealed record ExportOptions
{
    /// <summary>
    /// AES-256 加密密码（不存储，仅在内存中使用）
    ///</summary>
    public required string Password { get; init; }

    /// <summary>
    /// 明文密码提示（存储在 manifest.json 中，不参与加密，可为空）
    ///</summary>
    public string? PasswordHint { get; init; }

    /// <summary>
    /// 各表的导出选项列表
    ///</summary>
    public required IReadOnlyList<TableExportOptions> Tables { get; init; }

    /// <summary>
    /// 新增表完整数据导出的行数警告阈值，超出后弹出确认对话框（默认 10 万行）
    ///</summary>
    public int RowCountWarningThreshold { get; init; } = 100_000;
}
