using Avalonia.Controls;
using System.Diagnostics;

namespace DBSync.Desktop.Views;

/// <summary>
/// 导出快照页面视图
///</summary>
public partial class ExportView : UserControl
{
    /// <summary>
    /// 初始化导出视图
    ///</summary>
    public ExportView()
    {
        InitializeComponent();
        Loaded += (_, _) => LayoutRoot.ShowGridLines = Debugger.IsAttached;
    }
}
