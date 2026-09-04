namespace DBSync.Core.Models;

/// <summary>
/// 数据库连接的环境标识
///</summary>
public enum ConnectionEnvironment
{
    /// <summary>未设置</summary>
    Unspecified,
    /// <summary>开发</summary>
    Development,
    /// <summary>测试</summary>
    Testing,
    /// <summary>预发</summary>
    Staging,
    /// <summary>生产</summary>
    Production
}
