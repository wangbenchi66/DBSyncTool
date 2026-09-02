using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Desktop.Services;
using DBSync.Desktop.Models;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 主窗口 ViewModel，负责导航切换和状态栏管理
///</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// 应用设置存储
    ///</summary>
    private readonly IAppSettingsStore _appSettingsStore;

    /// <summary>
    /// 连接管理页面 ViewModel
    ///</summary>
    public ConnectionListViewModel ConnectionList { get; }

    /// <summary>
    /// 导出快照页面 ViewModel
    ///</summary>
    public ExportViewModel Export { get; }

    /// <summary>
    /// 加载比对页面 ViewModel
    ///</summary>
    public CompareViewModel Compare { get; }

    /// <summary>
    /// 历史记录页面 ViewModel
    ///</summary>
    public HistoryViewModel History { get; }

    /// <summary>
    /// 导航项列表
    ///</summary>
    public NavigationItemViewModel[] NavigationItems { get; }

    /// <summary>
    /// 当前选中的导航项
    ///</summary>
    [ObservableProperty]
    private NavigationItemViewModel? selectedNavigationItem;

    /// <summary>
    /// 当前显示的页面 ViewModel
    ///</summary>
    [ObservableProperty]
    private object? currentPage;

    /// <summary>
    /// 顶部状态栏文本
    ///</summary>
    [ObservableProperty]
    private string statusText = "就绪";

    /// <summary>
    /// 底部日志摘要
    ///</summary>
    [ObservableProperty]
    private string logSummary = "未记录操作";

    /// <summary>
    /// 是否有未完成的操作（关窗确认用）
    ///</summary>
    [ObservableProperty]
    private bool hasPendingOperation;

    /// <summary>
    /// 主窗口引用（用于关窗确认）
    ///</summary>
    private Window? _ownerWindow;

    /// <summary>
    /// 创建主窗口 ViewModel
    ///</summary>
    /// <param name="connectionList">连接管理 ViewModel</param>
    /// <param name="export">导出 ViewModel</param>
    /// <param name="compare">比对 ViewModel</param>
    /// <param name="history">历史 ViewModel</param>
    /// <param name="appSettingsStore">应用设置存储</param>
    public MainWindowViewModel(
        ConnectionListViewModel connectionList,
        ExportViewModel export,
        CompareViewModel compare,
        HistoryViewModel history,
        IAppSettingsStore appSettingsStore)
    {
        ConnectionList = connectionList;
        Export = export;
        Compare = compare;
        History = history;
        _appSettingsStore = appSettingsStore;

        NavigationItems =
        [
            new NavigationItemViewModel("connections", "连接管理", connectionList),
            new NavigationItemViewModel("export", "导出快照", export),
            new NavigationItemViewModel("compare", "加载比对", compare),
            new NavigationItemViewModel("history", "历史记录", history)
        ];

        // 监听各页面的 StatusText 变化
        connectionList.PropertyChanged += (_, e) => ForwardStatus(connectionList, e.PropertyName);
        export.PropertyChanged += (_, e) => ForwardStatus(export, e.PropertyName);
        compare.PropertyChanged += (_, e) => ForwardStatus(compare, e.PropertyName);
        history.PropertyChanged += (_, e) => ForwardStatus(history, e.PropertyName);

        // 监听导出/比对的 HasPendingOperation 变化
        export.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExportViewModel.HasPendingOperation))
                HasPendingOperation = export.HasPendingOperation || compare.HasPendingOperation;
        };
        compare.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CompareViewModel.HasPendingOperation))
                HasPendingOperation = export.HasPendingOperation || compare.HasPendingOperation;
        };

        // 设置历史记录的导航回调
        history.NavigateAndApplyHistory = OnNavigateFromHistory;

        // 恢复上次使用的页面
        var settings = appSettingsStore.Load();
        var lastPage = settings.LastPageName ?? "connections";
        var targetNav = NavigationItems.FirstOrDefault(n => n.Key == lastPage) ?? NavigationItems[0];
        SelectedNavigationItem = targetNav;
        CurrentPage = targetNav.PageViewModel;
    }

    /// <summary>
    /// 导航项选中变更时切换页面
    ///</summary>
    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
    {
        if (value is null)
            return;

        CurrentPage = value.PageViewModel;

        // 页面切换时刷新连接列表
        if (value.PageViewModel is ExportViewModel exportVm)
            exportVm.RefreshConnections();
        else if (value.PageViewModel is CompareViewModel compareVm)
            compareVm.RefreshCompareConnections();
        else if (value.PageViewModel is HistoryViewModel historyVm)
            historyVm.RefreshHistory();

        // 保存当前页面
        var settings = _appSettingsStore.Load();
        settings = settings with { LastPageName = value.Key };
        _appSettingsStore.Save(settings);
    }

    /// <summary>
    /// 设置主窗口引用（由 MainWindow 构造函数调用）
    ///</summary>
    /// <param name="ownerWindow">主窗口实例</param>
    public void AttachOwnerWindow(Window ownerWindow)
    {
        _ownerWindow = ownerWindow;
    }

    /// <summary>
    /// 导航到指定页面（供历史记录回调使用）
    ///</summary>
    /// <param name="pageKey">页面标识键</param>
    /// <param name="entry">历史条目</param>
    private void OnNavigateFromHistory(string pageKey, HistoryEntryViewModel entry)
    {
        var nav = NavigationItems.FirstOrDefault(n => n.Key == pageKey);
        if (nav is null)
            return;

        SelectedNavigationItem = nav;

        if (pageKey == "export" && !string.IsNullOrWhiteSpace(entry.Path))
        {
            Export.ExportPath = entry.Path;
        }
        else if (pageKey == "compare" && !string.IsNullOrWhiteSpace(entry.Path))
        {
            Compare.CompareSnapshotPath = entry.Path;
        }
    }

    /// <summary>
    /// 转发子页面的状态文本到主窗口状态栏
    ///</summary>
    private void ForwardStatus(ObservableObject source, string? propertyName)
    {
        if (source != CurrentPage)
            return;

        if (propertyName == "StatusText")
        {
            StatusText = source switch
            {
                ConnectionListViewModel vm => vm.StatusText,
                ExportViewModel vm => vm.StatusText,
                CompareViewModel vm => vm.StatusText,
                HistoryViewModel vm => vm.StatusText,
                _ => StatusText
            };
        }
        else if (propertyName == "LogSummary")
        {
            LogSummary = source switch
            {
                ConnectionListViewModel vm => vm.LogSummary,
                ExportViewModel vm => vm.LogSummary,
                CompareViewModel vm => vm.LogSummary,
                HistoryViewModel vm => vm.LogSummary,
                _ => LogSummary
            };
        }
    }
}

/// <summary>
/// 导航项 ViewModel
///</summary>
public sealed record NavigationItemViewModel(
    string Key,
    string DisplayName,
    object PageViewModel);
