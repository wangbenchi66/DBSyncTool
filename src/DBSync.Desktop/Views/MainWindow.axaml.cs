using Avalonia.Media;
using Avalonia.Data.Converters;
using DBSync.Desktop.Services;
using DBSync.Desktop.ViewModels;
using SukiUI.Controls;

namespace DBSync.Desktop.Views;

/// <summary>
/// 主窗口
/// </summary>
public partial class MainWindow : SukiWindow
{
    /// <summary>
    /// 状态文本前景色转换器：错误时红色，正常时深色
    /// </summary>
    public static FuncValueConverter<bool, IBrush> ErrorForegroundConverter { get; } =
        new(isError => isError
            ? new SolidColorBrush(Color.Parse("#B91C1C"))
            : new SolidColorBrush(Color.Parse("#1F2632")));

    /// <summary>
    /// 设计器用无参构造函数
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// DI 构造函数
    /// </summary>
    /// <param name="viewModel">主窗口 ViewModel</param>
    /// <param name="windowProvider">窗口提供者</param>
    public MainWindow(MainWindowViewModel viewModel, WindowProvider windowProvider)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.AttachOwnerWindow(this);
        windowProvider.SetMainWindow(this);
    }
}
