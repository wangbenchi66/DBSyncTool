using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Desktop.Models;
using DBSync.Desktop.Services;
using System.Collections.ObjectModel;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 仪表盘页面的 ViewModel，显示统计概览、快捷操作和最近活动
///</summary>
public sealed partial class DashboardViewModel : ObservableObject, IPageViewModel
{
    /// <summary>
    /// 连接存储服务
    ///</summary>
    private readonly IConnectionStore _connectionStore;

    /// <summary>
    /// 应用设置存储
    ///</summary>
    private readonly IAppSettingsStore _appSettingsStore;

    /// <summary>
    /// 页面状态文本
    ///</summary>
    [ObservableProperty]
    private string statusText = "就绪";

    /// <summary>
    /// 日志摘要
    ///</summary>
    [ObservableProperty]
    private string logSummary = "";

    /// <summary>
    /// 连接总数
    ///</summary>
    [ObservableProperty]
    private int connectionCount;

    /// <summary>
    /// 本周快照数
    ///</summary>
    [ObservableProperty]
    private int weeklySnapshotCount;

    /// <summary>
    /// 本周比对数
    ///</summary>
    [ObservableProperty]
    private int weeklyCompareCount;

    /// <summary>
    /// 本周脚本数
    ///</summary>
    [ObservableProperty]
    private int weeklyScriptCount;

    /// <summary>
    /// 连接列表（用于卡片概览）
    ///</summary>
    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = new();

    /// <summary>
    /// 最近活动列表（取前 8 条）
    ///</summary>
    public ObservableCollection<HistoryEntryViewModel> RecentActivities { get; } = new();

    /// <summary>
    /// 导航到同步工作台的回调（由 MainWindowViewModel 设置）
    ///</summary>
    public Action<string>? NavigateToPage { get; set; }

    /// <summary>
    /// 创建仪表盘 ViewModel
    ///</summary>
    /// <param name="connectionStore">连接存储</param>
    /// <param name="appSettingsStore">应用设置存储</param>
    public DashboardViewModel(
        IConnectionStore connectionStore,
        IAppSettingsStore appSettingsStore)
    {
        _connectionStore = connectionStore;
        _appSettingsStore = appSettingsStore;
        Refresh();
    }

    /// <summary>
    /// 刷新仪表盘数据
    ///</summary>
    public void Refresh()
    {
        RefreshConnections();
        RefreshStatistics();
        RefreshRecentActivities();
    }

    /// <summary>
    /// 快捷操作：跳转到导出快照
    ///</summary>
    [RelayCommand]
    private void GoToExport()
    {
        NavigateToPage?.Invoke("sync-export");
    }

    /// <summary>
    /// 快捷操作：跳转到快照比对
    ///</summary>
    [RelayCommand]
    private void GoToCompare()
    {
        NavigateToPage?.Invoke("sync-compare");
    }

    /// <summary>
    /// 刷新连接列表
    ///</summary>
    private void RefreshConnections()
    {
        Connections.Clear();
        foreach (var conn in _connectionStore.Load())
            Connections.Add(ConnectionItemViewModel.FromDatabaseConnection(conn));
        ConnectionCount = Connections.Count;
    }

    /// <summary>
    /// 从历史记录中统计本周数据
    ///</summary>
    private void RefreshStatistics()
    {
        var settings = _appSettingsStore.Load();
        var weekStart = DateTimeOffset.Now.AddDays(-7);
        var recentItems = settings.RecentHistoryItems
            .Where(i => i.CreatedAt >= weekStart)
            .ToList();

        WeeklySnapshotCount = recentItems.Count(i =>
            i.Kind.Contains("快照", StringComparison.Ordinal) ||
            i.Kind.Contains("导出", StringComparison.Ordinal));
        WeeklyCompareCount = recentItems.Count(i =>
            i.Kind.Contains("比对", StringComparison.Ordinal));
        WeeklyScriptCount = recentItems.Count(i =>
            i.Kind.Contains("脚本", StringComparison.Ordinal));
    }

    /// <summary>
    /// 加载最近活动（取前 8 条）
    ///</summary>
    private void RefreshRecentActivities()
    {
        RecentActivities.Clear();
        var settings = _appSettingsStore.Load();
        foreach (var item in settings.RecentHistoryItems
                     .OrderByDescending(x => x.CreatedAt)
                     .Take(8))
        {
            RecentActivities.Add(new HistoryEntryViewModel(
                item.Kind,
                item.Title,
                item.Path,
                item.ConnectionName,
                item.CreatedAt));
        }
    }
}
