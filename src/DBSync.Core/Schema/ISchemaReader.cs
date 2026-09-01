using DBSync.Core.Models;

namespace DBSync.Core.Schema;

/// <summary>
/// 数据库结构读取器接口，各数据库类型分别实现
///</summary>
public interface ISchemaReader
{
    /// <summary>
    /// 读取指定数据库连接中的所有表结构元数据
    /// </summary>
    /// <param name="connection">数据库连接配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有表的元数据列表</returns>
    Task<IReadOnlyList<TableModel>> ReadAllTablesAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取指定数据库连接中指定表的结构元数据
    /// </summary>
    /// <param name="connection">数据库连接配置</param>
    /// <param name="tableName">表名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表的元数据，表不存在时返回 null</returns>
    Task<TableModel?> ReadTableAsync(
        DatabaseConnection connection,
        string tableName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试数据库连接是否可用
    /// </summary>
    /// <param name="connection">数据库连接配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接可用时返回 true</returns>
    Task<bool> TestConnectionAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default);
}
