using Avalonia.Controls;

namespace DBSync.Desktop.Services;

/// <summary>
/// 提供主窗口引用的服务接口，供 ViewModel 访问文件选择器和模态对话框
///</summary>
public interface IWindowProvider
{
    /// <summary>
    /// 获取主窗口引用
    ///</summary>
    /// <returns>主窗口实例，未设置时返回 null</returns>
    Window? GetMainWindow();
}
