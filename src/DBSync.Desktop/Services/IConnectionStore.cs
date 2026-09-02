using DBSync.Core.Models;

namespace DBSync.Desktop.Services;

/// <summary>
/// 数据库连接配置的持久化存储接口
///</summary>
public interface IConnectionStore
{
    /// <summary>
    /// 从持久化存储加载所有已保存的连接配置
    ///</summary>
    /// <returns>连接配置列表</returns>
    IReadOnlyList<DatabaseConnection> Load();

    /// <summary>
    /// 将连接配置列表保存到持久化存储
    ///</summary>
    /// <param name="connections">要保存的连接配置列表</param>
    void Save(IReadOnlyList<DatabaseConnection> connections);
}
