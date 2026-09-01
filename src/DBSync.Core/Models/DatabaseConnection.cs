namespace DBSync.Core.Models;

/// <summary>
/// 数据库连接配置（内存中存储明文连接字符串，持久化时加密）
///</summary>
public sealed record DatabaseConnection
{
    /// <summary>
    /// 连接的显示名称（用户自定义，如"生产-SqlServer"）
    ///</summary>
    public required string Name { get; init; }

    /// <summary>
    /// 数据库类型
    ///</summary>
    public required DatabaseType DbType { get; init; }

    /// <summary>
    /// 连接字符串（内存中为明文，写入配置文件时加密）
    ///</summary>
    public required string ConnectionString { get; init; }
}
