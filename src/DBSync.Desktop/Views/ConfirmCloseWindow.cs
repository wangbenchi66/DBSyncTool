using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DBSync.Desktop.Views;

public sealed class ConfirmCloseWindow : Window
{
    private ConfirmCloseWindow()
    {
        Title = "确认退出";
        Width = 360;
        Height = 180;
        CanResize = false;

        var text = new TextBlock
        {
            Text = "当前有未完成的操作，确定要退出吗？",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var ok = new Button { Content = "退出", HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", HorizontalAlignment = HorizontalAlignment.Right };

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(text);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);

        ok.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
        Content = panel;
    }

    public static Task<bool> ShowAsync(Window owner)
    {
        return new ConfirmCloseWindow().ShowDialog<bool>(owner);
    }
}
