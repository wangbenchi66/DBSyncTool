using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Desktop.Services;
using DBSync.Desktop.Models;
using SukiUI.Toasts;

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
    /// 直连比对页面 ViewModel
    ///</summary>
    public DirectCompareViewModel DirectCompare { get; }

    /// <summary>
    /// 同步工作台 ViewModel（组合导出 + 比对）
    ///</summary>
    public SyncWorkflowViewModel SyncWorkflow { get; }

    /// <summary>
    /// 历史记录页面 ViewModel
    ///</summary>
    public HistoryViewModel History { get; }

    /// <summary>
    /// 仪表盘页面 ViewModel
    ///</summary>
    public DashboardViewModel Dashboard { get; }

    /// <summary>
    /// 设置页面 ViewModel
    ///</summary>
    public SettingsViewModel Settings { get; }

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
    /// 状态文本是否表示错误（用于红色高亮）
    ///</summary>
    [ObservableProperty]
    private bool statusIsError;

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
    /// 浮动提示管理器
    ///</summary>
    public ISukiToastManager ToastManager { get; } = new SukiToastManager();

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
        DirectCompareViewModel directCompare,
        HistoryViewModel history,
        DashboardViewModel dashboard,
        SettingsViewModel settingsVm,
        IAppSettingsStore appSettingsStore)
    {
        ConnectionList = connectionList;
        Export = export;
        Compare = compare;
        DirectCompare = directCompare;
        History = history;
        Dashboard = dashboard;
        Settings = settingsVm;
        _appSettingsStore = appSettingsStore;

        SyncWorkflow = new SyncWorkflowViewModel(export, compare, directCompare);

        ConnectionList.ConnectionsChanged += OnConnectionsChanged;

        NavigationItems =
        [
            new NavigationItemViewModel("dashboard", "仪表盘", dashboard),
            new NavigationItemViewModel("connections", "连接管理", connectionList),
            new NavigationItemViewModel("sync", "同步工作台", SyncWorkflow),
            new NavigationItemViewModel("history", "历史记录", history),
            new NavigationItemViewModel("settings", "设置", settingsVm)
        ];

        // 仪表盘快捷操作的导航回调
        dashboard.NavigateToPage = OnDashboardNavigate;

        // 监听各页面的 StatusText 变化
        connectionList.PropertyChanged += (_, e) => ForwardStatus(connectionList, e.PropertyName);
        SyncWorkflow.PropertyChanged += (_, e) => ForwardStatus(SyncWorkflow, e.PropertyName);
        history.PropertyChanged += (_, e) => ForwardStatus(history, e.PropertyName);
        settingsVm.PropertyChanged += (_, e) => ForwardStatus(settingsVm, e.PropertyName);

        // 监听同步工作台的 HasPendingOperation 变化
        SyncWorkflow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SyncWorkflowViewModel.HasPendingOperation))
                HasPendingOperation = SyncWorkflow.HasPendingOperation;
        };

        // 设置历史记录的导航回调
        history.NavigateAndApplyHistory = OnNavigateFromHistory;

        // 恢复上次使用的页面
        var settings = appSettingsStore.Load();
        var lastPage = settings.LastPageName ?? "dashboard";
        // 兼容旧设置：export 和 compare 都映射到 sync
        if (lastPage == "export" || lastPage == "compare")
            lastPage = "sync";
        var targetNav = NavigationItems.FirstOrDefault(n => n.Key == lastPage) ?? NavigationItems[0];
        selectedNavigationItem = targetNav;
        currentPage = targetNav.PageViewModel;
        OnPropertyChanged(nameof(SelectedNavigationItem));
        OnPropertyChanged(nameof(CurrentPage));
    }

    /// <summary>
    /// 导航项选中变更时切换页面
    ///</summary>
    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
    {
        if (value is null)
            return;

        CurrentPage = value.PageViewModel;

        // 页面切换时刷新数据
        if (value.PageViewModel is DashboardViewModel dashVm)
            dashVm.Refresh();
        else if (value.PageViewModel is SyncWorkflowViewModel syncVm)
        {
            if (syncVm.SelectedTabIndex == 0)
                syncVm.ActivateExportTab();
            else if (syncVm.SelectedTabIndex == 2)
                syncVm.ActivateDirectCompareTab();
            else
                syncVm.ActivateCompareTab();
        }
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
        // 兼容旧的 pageKey
        if (pageKey == "export" || pageKey == "compare")
            pageKey = "sync";

        var nav = NavigationItems.FirstOrDefault(n => n.Key == pageKey);
        if (nav is null)
            return;

        SelectedNavigationItem = nav;

        if (pageKey != "sync")
            return;

        // 根据历史条目类型切换到对应 Tab
        var isExportEntry = entry.Kind is "导出快照" or "快照";
        if (isExportEntry)
        {
            SyncWorkflow.ActivateExportTab();
            if (!string.IsNullOrWhiteSpace(entry.Path))
                Export.SetExportPath(entry.Path);
            if (!string.IsNullOrWhiteSpace(entry.ConnectionName))
            {
                var conn = Export.Connections.FirstOrDefault(c =>
                    string.Equals(c.Name, entry.ConnectionName, StringComparison.OrdinalIgnoreCase));
                if (conn is not null)
                    Export.SelectedConnection = conn;
            }
        }
        else
        {
            SyncWorkflow.ActivateCompareTab();
            if (!string.IsNullOrWhiteSpace(entry.Path))
                Compare.CompareSnapshotPath = entry.Path;
            if (!string.IsNullOrWhiteSpace(entry.ConnectionName))
            {
                var conn = Compare.CompareConnections.FirstOrDefault(c =>
                    string.Equals(c.Name, entry.ConnectionName, StringComparison.OrdinalIgnoreCase));
                if (conn is not null)
                    Compare.SelectedCompareConnection = conn;
            }
        }
    }

    /// <summary>
    /// 连接列表发生变更后，刷新依赖连接列表的页面
    ///</summary>
    private void OnConnectionsChanged()
    {
        Dashboard.Refresh();
        Export.RefreshConnections();
        Compare.RefreshCompareConnections();
        DirectCompare.RefreshConnections();
    }

    /// <summary>
    /// 仪表盘快捷操作导航回调
    ///</summary>
    /// <param name="target">目标页面标识（sync-export / sync-compare）</param>
    private void OnDashboardNavigate(string target)
    {
        var syncNav = NavigationItems.FirstOrDefault(n => n.Key == "sync");
        if (syncNav is null)
            return;

        SelectedNavigationItem = syncNav;

        if (target == "sync-export")
            SyncWorkflow.ActivateExportTab();
        else if (target == "sync-compare")
            SyncWorkflow.ActivateCompareTab();
    }

    /// <summary>
    /// 转发子页面的状态文本到主窗口状态栏
    ///</summary>
    private void ForwardStatus(ObservableObject source, string? propertyName)
    {
        if (source != CurrentPage || source is not IPageViewModel page)
            return;

        if (propertyName == nameof(IPageViewModel.StatusText))
        {
            StatusText = page.StatusText;
            StatusIsError = StatusText.Contains("失败", StringComparison.Ordinal);
            if (ShouldToastStatus(StatusText))
                ShowToast(StatusText, string.Empty, StatusIsError);
        }
        else if (propertyName == nameof(IPageViewModel.LogSummary))
        {
            LogSummary = page.LogSummary;
            ShowToast(StatusText, LogSummary, StatusIsError);
        }
    }

    /// <summary>
    /// 发送浮动提示
    ///</summary>
    /// <param name="title">标题</param>
    /// <param name="content">内容</param>
    /// <param name="isError">是否错误</param>
    private void ShowToast(string title, string? content, bool isError)
    {
        title = title.Trim();
        content = content?.Trim();

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content))
            return;

        if (!string.IsNullOrWhiteSpace(title) &&
            title.StartsWith("正在", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var builder = FluentSukiToastBuilder.CreateSimpleInfoToast(ToastManager)
            .WithTitle(string.IsNullOrWhiteSpace(title) ? "提示" : title)
            .OfType(ResolveToastType(title, isError));

        if (!string.IsNullOrWhiteSpace(content) &&
            !string.Equals(content, title, StringComparison.Ordinal))
        {
            builder = builder.WithContent(content);
        }

        builder.Queue();
    }

    /// <summary>
    /// 根据状态文本推断提示类型
    ///</summary>
    /// <param name="text">状态文本</param>
    /// <param name="isError">是否错误</param>
    /// <returns>提示类型</returns>
    private static NotificationType ResolveToastType(string text, bool isError)
    {
        if (isError || text.Contains("失败", StringComparison.Ordinal) || text.Contains("错误", StringComparison.Ordinal))
            return NotificationType.Error;

        if (text.Contains("必须", StringComparison.Ordinal) ||
            text.StartsWith("请", StringComparison.Ordinal) ||
            text.StartsWith("没有", StringComparison.Ordinal) ||
            text.StartsWith("未", StringComparison.Ordinal))
            return NotificationType.Warning;

        if (text.Contains("成功", StringComparison.Ordinal) ||
            text.Contains("完成", StringComparison.Ordinal) ||
            text.Contains("已", StringComparison.Ordinal))
            return NotificationType.Success;

        return NotificationType.Information;
    }

    /// <summary>
    /// 判断状态文本是否需要单独弹出提示
    ///</summary>
    /// <param name="text">状态文本</param>
    /// <returns>是否弹出</returns>
    private static bool ShouldToastStatus(string text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            text == "就绪" ||
            text.StartsWith("正在", StringComparison.Ordinal))
        {
            return false;
        }

        if (text.Contains("警告", StringComparison.Ordinal) ||
            text.StartsWith("请", StringComparison.Ordinal) ||
            text.StartsWith("没有", StringComparison.Ordinal) ||
            text.StartsWith("未", StringComparison.Ordinal))
        {
            return true;
        }

        return text.StartsWith("已", StringComparison.Ordinal) &&
               !text.Contains("完成", StringComparison.Ordinal) &&
               !text.Contains("取消", StringComparison.Ordinal);
    }
}

/// <summary>
/// 导航项 ViewModel
///</summary>
/// <param name="Key">页面标识键（如 connections、sync）</param>
/// <param name="DisplayName">侧边栏显示名称</param>
/// <param name="PageViewModel">对应的页面 ViewModel 实例</param>
public sealed record NavigationItemViewModel(
    string Key,
    string DisplayName,
    object PageViewModel);
