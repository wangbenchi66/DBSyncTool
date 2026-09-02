using System.Diagnostics;
using DBSync.Desktop.Models;
using DBSync.Desktop.Services;

namespace DBSync.Desktop.Helpers;

/// <summary>
/// ViewModel 层共享工具方法
///</summary>
public static class ViewModelHelpers
{
    /// <summary>
    /// 格式化文件大小为 KB 或 MB 字符串
    ///</summary>
    /// <param name="bytes">字节数</param>
    /// <returns>格式化后的字符串</returns>
    public static string FormatFileSize(long bytes)
    {
        return bytes < 1024 * 1024
            ? $"{bytes / 1024.0:F1} KB"
            : $"{bytes / 1024.0 / 1024.0:F1} MB";
    }

    /// <summary>
    /// 用系统默认程序打开文件
    ///</summary>
    /// <param name="path">文件路径</param>
    public static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // 打开失败时静默处理（如文件不存在或无关联程序）
        }
    }

    /// <summary>
    /// 保存最近操作历史记录
    ///</summary>
    /// <param name="appSettingsStore">设置存储</param>
    /// <param name="settings">当前设置（会被更新）</param>
    /// <param name="kind">记录类型</param>
    /// <param name="title">记录标题</param>
    /// <param name="path">文件路径</param>
    /// <param name="connectionName">关联的连接名称</param>
    /// <returns>更新后的 AppSettings</returns>
    public static AppSettings SaveRecentHistory(
        IAppSettingsStore appSettingsStore,
        AppSettings settings,
        string kind,
        string title,
        string path,
        string? connectionName = null)
    {
        var record = new RecentHistoryItem
        {
            Kind = kind,
            Title = title,
            Path = path,
            ConnectionName = connectionName,
            CreatedAt = DateTimeOffset.Now
        };

        var items = settings.RecentHistoryItems
            .Where(item => !string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CreatedAt)
            .Take(19)
            .ToList();
        items.Insert(0, record);

        var updated = settings with { RecentHistoryItems = items };
        appSettingsStore.Save(updated);
        return updated;
    }
}
