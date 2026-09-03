using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DBSync.Desktop.Services;
using DBSync.Desktop.ViewModels;

namespace DBSync.Desktop.Views;

/// <summary>
/// 主窗口
///</summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 状态文本前景色转换器：错误时玫红，正常时深灰
    ///</summary>
    public static FuncValueConverter<bool, IBrush> ErrorForegroundConverter { get; } =
        new(isError => isError
            ? new SolidColorBrush(Color.Parse("#B91C1C"))
            : new SolidColorBrush(Color.Parse("#1F2632")));

    /// <summary>
    /// 允许关闭标志（确认后设置为 true）
    ///</summary>
    private bool _allowClose;

    /// <summary>
    /// 设计器用无参构造函数
    ///</summary>
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    /// <summary>
    /// DI 构造函数
    ///</summary>
    /// <param name="viewModel">主窗口 ViewModel</param>
    /// <param name="windowProvider">窗口提供者</param>
    public MainWindow(MainWindowViewModel viewModel, WindowProvider windowProvider)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.AttachOwnerWindow(this);
        windowProvider.SetMainWindow(this);
        Closing += OnClosing;
    }

    /// <summary>
    /// 关窗拦截：有未完成操作时弹出确认对话框
    ///</summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
            return;

        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasPendingOperation)
            return;

        e.Cancel = true;
        var confirmed = await ConfirmCloseWindow.ShowAsync(this);
        if (!confirmed)
            return;

        _allowClose = true;
        Close();
    }
}
