using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Desktop.Models;
using DBSync.Desktop.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 历史记录页面的 ViewModel
///</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    /// <summary>
    /// 应用设置存储
    ///</summary>
    private readonly IAppSettingsStore _appSettingsStore;

    /// <summary>
    /// 当前状态文本
    ///</summary>
    [ObservableProperty]
    private string statusText = "就绪";

    /// <summary>
    /// 日志摘要
    ///</summary>
    [ObservableProperty]
    private string logSummary = "";

    /// <summary>
    /// 最近历史记录条目列表
    ///</summary>
    public ObservableCollection<HistoryEntryViewModel> RecentHistoryEntries { get; } = new();

    /// <summary>
    /// 导航回调（页面类型, 历史条目），由 MainWindowViewModel 设置
    ///</summary>
    public Action<string, HistoryEntryViewModel>? NavigateAndApplyHistory { get; set; }

    /// <summary>
    /// 创建历史记录 ViewModel
    ///</summary>
    /// <param name="appSettingsStore">应用设置存储</param>
    public HistoryViewModel(IAppSettingsStore appSettingsStore)
    {
        _appSettingsStore = appSettingsStore;
        RefreshHistory();
    }

    /// <summary>
    /// 打开或使用历史记录条目
    ///</summary>
    /// <param name="entry">历史记录条目</param>
    [RelayCommand]
    private void OpenHistoryEntry(HistoryEntryViewModel? entry)
    {
        if (entry is null)
            return;

        if (entry.Kind.Contains("脚本", StringComparison.Ordinal) || entry.Kind.Contains("报告", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(entry.Path) && File.Exists(entry.Path))
            {
                OpenFile(entry.Path);
                StatusText = "已打开历史文件";
                LogSummary = entry.Path;
            }
            return;
        }

        if (entry.Kind.Contains("导出", StringComparison.Ordinal))
        {
            NavigateAndApplyHistory?.Invoke("export", entry);
            StatusText = "已恢复导出路径";
            LogSummary = entry.Path;
        }
        else if (entry.Kind.Contains("快照", StringComparison.Ordinal) || entry.Kind.Contains("比对", StringComparison.Ordinal))
        {
            NavigateAndApplyHistory?.Invoke("compare", entry);
            StatusText = "已恢复快照路径";
            LogSummary = entry.Path;
        }
    }

    /// <summary>
    /// 刷新历史记录列表
    ///</summary>
    public void RefreshHistory()
    {
        RecentHistoryEntries.Clear();

        var settings = _appSettingsStore.Load();
        foreach (var item in settings.RecentHistoryItems
                     .OrderByDescending(x => x.CreatedAt)
                     .Take(20))
        {
            RecentHistoryEntries.Add(new HistoryEntryViewModel(
                item.Kind,
                item.Title,
                item.Path,
                item.ConnectionName,
                item.CreatedAt));
        }
    }

    /// <summary>
    /// 用系统默认程序打开文件
    ///</summary>
    /// <param name="path">文件路径</param>
    private static void OpenFile(string path)
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
        }
    }
}
