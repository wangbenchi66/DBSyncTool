using DBSync.Core.Models;
using SnapshotModel = DBSync.Core.Models.Snapshot;

namespace DBSync.Core.Snapshot;

/// <summary>
/// 快照加载器接口，负责读取并解密 .dbsync 文件
///</summary>
public interface ISnapshotLoader
{
    /// <summary>
    /// 从流中读取 manifest.json 的密码提示，无需完整解密（用于在输入密码前显示提示）
    /// </summary>
    /// <param name="inputStream">.dbsync 文件流（可定位）</param>
    /// <returns>密码提示字符串，未设置时返回 null</returns>
    Task<string?> ReadPasswordHintAsync(Stream inputStream);

    /// <summary>
    /// 解密并完整加载 .dbsync 文件为 Snapshot 对象
    /// </summary>
    /// <param name="inputStream">.dbsync 文件流</param>
    /// <param name="password">解密密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解析得到的 Snapshot 对象</returns>
    /// <exception cref="InvalidOperationException">密码错误或文件损坏时抛出</exception>
    Task<SnapshotModel> LoadAsync(
        Stream inputStream,
        string password,
        CancellationToken cancellationToken = default);
}
