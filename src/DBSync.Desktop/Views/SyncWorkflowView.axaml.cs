using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DBSync.Desktop.Helpers;
using DBSync.Desktop.ViewModels;

namespace DBSync.Desktop.Views;

/// <summary>
/// 同步工作台视图（组合导出快照 + 加载比对）
///</summary>
public partial class SyncWorkflowView : UserControl
{
    /// <summary>
    /// 导出过滤框的 IME 去重辅助实例
    ///</summary>
    private readonly ImeInputHelper _exportFilterImeHelper = new();

    /// <summary>
    /// 初始化同步工作台视图
    ///</summary>
    public SyncWorkflowView()
    {
        InitializeComponent();

        _exportFilterImeHelper.Attach(ExportFilterBox);
    }

    /// <summary>
    /// 点击分类按钮时强制刷新 SQL 预览（包括点击已选中的分类）
    ///</summary>
    private void DiffCategoryList_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox lb && lb.DataContext is CompareViewModel vm)
            vm.RefreshDiffSql();
    }
}
