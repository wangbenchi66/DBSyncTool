using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SukiUI.Controls;

namespace DBSync.Desktop.Views;

/// <summary>
/// 更新弹窗的用户选择动作
///</summary>
public enum UpdateAction
{
    /// <summary>
    /// 稍后处理（0 保证用户点右上角 X 关窗时返回默认值等价于“稍后”，避免误触下载）
    ///</summary>
    Later = 0,

    /// <summary>
    /// 前往下载（用系统浏览器打开 Release 页）
    ///</summary>
    Download = 1
}

/// <summary>
/// “发现新版本”提示窗：纯代码构造的两按钮模态窗（仿 ConfirmCloseWindow 风格）。
///</summary>
public sealed class UpdateAvailableWindow : SukiWindow
{
    /// <summary>
    /// 创建更新提示窗
    ///</summary>
    /// <param name="newVersion">新版本号（不含 v 前缀）</param>
    /// <param name="currentVersion">当前版本号（不含 v 前缀）</param>
    private UpdateAvailableWindow(string newVersion, string currentVersion)
    {
        Title = "发现新版本";
        Width = 420;
        Height = 200;
        CanResize = false;

        var text = new TextBlock
        {
            Text = $"新版本 v{newVersion} 已发布（当前 v{currentVersion}），是否前往 GitHub Releases 下载？",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var later = new Button { Content = "稍后", HorizontalAlignment = HorizontalAlignment.Right };
        later.Classes.Add("secondary");
        var download = new Button { Content = "前往下载", HorizontalAlignment = HorizontalAlignment.Right };
        download.Classes.Add("primary");

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(text);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(later);
        buttons.Children.Add(download);
        panel.Children.Add(buttons);

        later.Click += (_, _) => Close(UpdateAction.Later);
        download.Click += (_, _) => Close(UpdateAction.Download);
        Content = panel;
    }

    /// <summary>
    /// 以模态方式显示更新提示窗
    ///</summary>
    /// <param name="owner">父窗口</param>
    /// <param name="newVersion">新版本号</param>
    /// <param name="currentVersion">当前版本号</param>
    /// <returns>用户选择的动作</returns>
    public static Task<UpdateAction> ShowAsync(Window owner, string newVersion, string currentVersion)
    {
        return new UpdateAvailableWindow(newVersion, currentVersion).ShowDialog<UpdateAction>(owner);
    }
}
