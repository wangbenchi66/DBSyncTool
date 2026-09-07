using System.Text.Json;
using DBSync.Core.Models;

namespace DBSync.Desktop.Storage;

/// <summary>
/// 同步项目文件的读写服务
///</summary>
public static class ProjectStore
{
    /// <summary>
    /// JSON 序列化上下文（Source Generator）
    ///</summary>
    private static readonly StorageJsonContext JsonContext = StorageJsonContext.Default;

    /// <summary>
    /// 保存项目到 .dbsync-project 文件
    ///</summary>
    /// <param name="path">文件路径</param>
    /// <param name="project">项目配置</param>
    public static async Task SaveAsync(string path, SyncProject project)
    {
        var updated = project with { UpdatedAt = DateTimeOffset.Now };
        var json = JsonSerializer.Serialize(updated, JsonContext.SyncProject);
        await File.WriteAllTextAsync(path, json);
    }

    /// <summary>
    /// 从 .dbsync-project 文件加载项目
    ///</summary>
    /// <param name="path">文件路径</param>
    /// <returns>项目配置</returns>
    public static async Task<SyncProject> LoadAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize(json, JsonContext.SyncProject) ?? new SyncProject();
    }
}
