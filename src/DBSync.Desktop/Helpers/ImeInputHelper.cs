using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DBSync.Desktop.Helpers;

/// <summary>
/// IME 输入法去重辅助类。
/// 解决 Avalonia 在 Windows 上使用中文输入法时，
/// 按 Shift 切换输入模式导致 CompositionEnd 和 WM_CHAR 重复插入文本的问题。
/// 原理：通过 Tunnel 路由拦截 TextInput 事件，
/// 在短时间窗口内检测到相同文本时标记为已处理以阻止重复插入。
/// 参考：https://github.com/AvaloniaUI/Avalonia/issues/19507
///</summary>
public sealed class ImeInputHelper
{
    /// <summary>
    /// 去重时间窗口（毫秒），同一文本在此时间内再次出现视为重复
    ///</summary>
    private const int DeduplicateWindowMs = 50;

    /// <summary>
    /// 上一次接收到的输入文本内容
    ///</summary>
    private string? _lastText;

    /// <summary>
    /// 上一次接收到输入文本的时间戳
    ///</summary>
    private DateTime _lastTime;

    /// <summary>
    /// 为指定的 TextBox 注册 IME 去重拦截器
    ///</summary>
    /// <param name="textBox">需要修复 IME 重复输入的 TextBox 控件</param>
    public void Attach(TextBox textBox)
    {
        textBox.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Tunnel 路由的 TextInput 事件处理，检测并过滤重复输入
    ///</summary>
    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        var now = DateTime.UtcNow;

        if (e.Text is not null
            && e.Text == _lastText
            && (now - _lastTime).TotalMilliseconds < DeduplicateWindowMs)
        {
            e.Handled = true;
            return;
        }

        _lastText = e.Text;
        _lastTime = now;
    }
}
