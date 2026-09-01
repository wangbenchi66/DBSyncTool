namespace DBSync.Core.Models;

/// <summary>
/// 支持的数据库类型枚举
///</summary>
public enum DatabaseType
{
    /// <summary>Microsoft SQL Server</summary>
    SqlServer,
    /// <summary>MySQL / MariaDB</summary>
    MySql,
    /// <summary>PostgreSQL</summary>
    PostgreSql,
    /// <summary>SQLite</summary>
    Sqlite
}

/// <summary>
/// .dbsync 文件的 manifest.json 元数据
///</summary>
public sealed record SnapshotManifest
{
    /// <summary>
    /// 工具版本（用于兼容性检查）
    ///</summary>
    public required string Version { get; init; }

    /// <summary>
    /// 快照来源的数据库类型
    ///</summary>
    public required DatabaseType DbType { get; init; }

    /// <summary>
    /// 快照导出时间（本地时区）
    ///</summary>
    public required DateTimeOffset ExportedAt { get; init; }

    /// <summary>
    /// 快照中包含的表名列表
    ///</summary>
    public required IReadOnlyList<string> TableNames { get; init; }

    /// <summary>
    /// 明文密码提示（不参与加解密，可为空）
    ///</summary>
    public string? PasswordHint { get; init; }
}

/// <summary>
/// 从 .dbsync 文件解析得到的快照数据，包含结构元数据和数据指纹
///</summary>
public sealed record Snapshot
{
    /// <summary>
    /// 快照的元数据信息
    ///</summary>
    public required SnapshotManifest Manifest { get; init; }

    /// <summary>
    /// 表名到表结构定义的映射
    ///</summary>
    public required IReadOnlyDictionary<string, TableModel> Tables { get; init; }

    /// <summary>
    /// 表名到行哈希指纹集合的映射（已存在表的数据指纹）
    ///</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<RowHash>> DataFingerprints { get; init; }

    /// <summary>
    /// 表名到完整行数据的映射（仅新增表选择了"结构+数据"时存在）
    /// 每行数据为列名到字符串值的字典（null 表示该列为 NULL）
    ///</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> FullData { get; init; }
}
