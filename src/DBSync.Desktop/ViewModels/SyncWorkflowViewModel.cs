using CommunityToolkit.Mvvm.ComponentModel;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 同步工作台 ViewModel，薄包装层，组合导出、快照比对和直连比对三个子 ViewModel
///</summary>
public partial class SyncWorkflowViewModel : ObservableObject, IPageViewModel
{
    /// <summary>
    /// 导出快照子 ViewModel
    ///</summary>
    public ExportViewModel Export { get; }

    /// <summary>
    /// 快照比对子 ViewModel
    ///</summary>
    public CompareViewModel Compare { get; }

    /// <summary>
    /// 直连比对子 ViewModel
    ///</summary>
    public DirectCompareViewModel DirectCompare { get; }

    /// <summary>
    /// 当前选中的 Tab 索引（0=导出快照，1=快照比对，2=直连比对）
    ///</summary>
    [ObservableProperty]
    private int selectedTabIndex;

    /// <summary>
    /// 页面状态文本（转发自当前活跃 Tab）
    ///</summary>
    [ObservableProperty]
    private string statusText = "就绪";

    /// <summary>
    /// 页面日志摘要（转发自当前活跃 Tab）
    ///</summary>
    [ObservableProperty]
    private string logSummary = "";

    /// <summary>
    /// 是否有未完成的操作
    ///</summary>
    [ObservableProperty]
    private bool hasPendingOperation;

    /// <summary>
    /// 创建同步工作台 ViewModel
    ///</summary>
    /// <param name="export">导出子 ViewModel</param>
    /// <param name="compare">快照比对子 ViewModel</param>
    /// <param name="directCompare">直连比对子 ViewModel</param>
    public SyncWorkflowViewModel(ExportViewModel export, CompareViewModel compare, DirectCompareViewModel directCompare)
    {
        Export = export;
        Compare = compare;
        DirectCompare = directCompare;

        Export.PropertyChanged += (_, e) =>
        {
            if (SelectedTabIndex == 0)
                ForwardChildStatus(Export, e.PropertyName);
            if (e.PropertyName == nameof(ExportViewModel.HasPendingOperation))
                RefreshPendingState();
        };

        Compare.PropertyChanged += (_, e) =>
        {
            if (SelectedTabIndex == 1)
                ForwardChildStatus(Compare, e.PropertyName);
            if (e.PropertyName == nameof(CompareViewModel.HasPendingOperation))
                RefreshPendingState();
        };

        DirectCompare.PropertyChanged += (_, e) =>
        {
            if (SelectedTabIndex == 2)
                ForwardChildStatus(DirectCompare, e.PropertyName);
            if (e.PropertyName == nameof(DirectCompareViewModel.HasPendingOperation))
                RefreshPendingState();
        };
    }

    /// <summary>
    /// Tab 切换时刷新状态显示
    ///</summary>
    partial void OnSelectedTabIndexChanged(int value)
    {
        IPageViewModel activePage = value switch
        {
            0 => Export,
            1 => Compare,
            2 => DirectCompare,
            _ => Export
        };
        StatusText = activePage.StatusText;
        LogSummary = activePage.LogSummary;
    }

    /// <summary>
    /// 切换到导出 Tab 并刷新连接
    ///</summary>
    public void ActivateExportTab()
    {
        SelectedTabIndex = 0;
        Export.RefreshConnections();
    }

    /// <summary>
    /// 切换到快照比对 Tab 并刷新连接
    ///</summary>
    public void ActivateCompareTab()
    {
        SelectedTabIndex = 1;
        Compare.RefreshCompareConnections();
    }

    /// <summary>
    /// 切换到直连比对 Tab 并刷新连接
    ///</summary>
    public void ActivateDirectCompareTab()
    {
        SelectedTabIndex = 2;
        DirectCompare.RefreshConnections();
    }

    /// <summary>
    /// 刷新待处理操作状态
    ///</summary>
    private void RefreshPendingState()
    {
        HasPendingOperation = Export.HasPendingOperation || Compare.HasPendingOperation || DirectCompare.HasPendingOperation;
    }

    /// <summary>
    /// 转发子 ViewModel 的状态属性到本 ViewModel
    ///</summary>
    private void ForwardChildStatus(IPageViewModel child, string? propertyName)
    {
        if (propertyName == nameof(IPageViewModel.StatusText))
            StatusText = child.StatusText;
        else if (propertyName == nameof(IPageViewModel.LogSummary))
            LogSummary = child.LogSummary;
    }
}
