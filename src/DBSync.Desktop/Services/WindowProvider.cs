using Avalonia.Controls;

namespace DBSync.Desktop.Services;

/// <summary>
/// 主窗口引用持有者，在应用启动时由 MainWindow 设置
///</summary>
public sealed class WindowProvider : IWindowProvider
{
    /// <summary>
    /// 主窗口引用
    ///</summary>
    private Window? _mainWindow;

    /// <summary>
    /// 设置主窗口引用（由 MainWindow 构造函数调用）
    ///</summary>
    /// <param name="window">主窗口实例</param>
    public void SetMainWindow(Window window) => _mainWindow = window;

    /// <summary>
    /// 获取主窗口引用
    ///</summary>
    /// <returns>主窗口实例，未设置时返回 null</returns>
    public Window? GetMainWindow() => _mainWindow;
}
