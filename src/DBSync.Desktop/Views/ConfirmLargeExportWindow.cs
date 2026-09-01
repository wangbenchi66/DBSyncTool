using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DBSync.Desktop.ViewModels;

namespace DBSync.Desktop.Views;

public sealed class ConfirmLargeExportWindow : Window
{
    public ConfirmLargeExportWindow(ExportTableItemViewModel table)
    {
        Title = "确认导出";
        Width = 420;
        Height = 220;
        CanResize = false;

        var info = new TextBlock
        {
            Text = $"表 {table.FullName} 的预估行数较大，是否继续导出结构+数据？",
            TextWrapping = TextWrapping.Wrap
        };

        var yes = new Button { Content = "继续导出" };
        var no = new Button { Content = "取消" };

        yes.Click += (_, _) => Close(true);
        no.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                info,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { no, yes }
                }
            }
        };
    }
}
