using Avalonia.Controls;
using DBSync.Desktop.Helpers;

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
}
