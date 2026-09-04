using Avalonia.Controls;
using DBSync.Desktop.ViewModels;

namespace DBSync.Desktop.Views;

/// <summary>
/// 导出输出设置弹窗
///</summary>
public partial class ExportOptionsWindow : Window
{
    /// <summary>
    /// 设计器和 XAML 编译器使用的无参构造函数
    ///</summary>
    public ExportOptionsWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 创建导出输出设置弹窗
    ///</summary>
    /// <param name="viewModel">导出视图模型</param>
    public ExportOptionsWindow(ExportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }
}
