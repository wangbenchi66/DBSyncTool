using Avalonia.Controls;
using DBSync.Desktop.Helpers;

namespace DBSync.Desktop.Views;

/// <summary>
/// 导出快照页面视图
/// </summary>
public partial class ExportView : UserControl
{
    /// <summary>
    /// 导出过滤框的 IME 去重辅助实例
    /// </summary>
    private readonly ImeInputHelper _exportFilterImeHelper = new();

    /// <summary>
    /// 初始化导出视图
    /// </summary>
    public ExportView()
    {
        InitializeComponent();
        _exportFilterImeHelper.Attach(ExportFilterBox);
    }
}
