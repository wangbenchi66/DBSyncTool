using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using DBSync.Desktop.Services;
using DBSync.Desktop.Views;
using System.Collections.ObjectModel;
using Serilog;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 连接管理页面的 ViewModel
///</summary>
public sealed partial class ConnectionListViewModel : ObservableObject, IPageViewModel
{
    /// <summary>
    /// 连接列表变更通知
    ///</summary>
    public event Action? ConnectionsChanged;

    /// <summary>
    /// 连接配置持久化存储
    ///</summary>
    private readonly IConnectionStore _connectionStore;

    /// <summary>
    /// 应用设置存储
    ///</summary>
    private readonly IAppSettingsStore _appSettingsStore;

    /// <summary>
    /// Schema 读取器，用于测试连接
    ///</summary>
    private readonly ISchemaReader _schemaReader;

    /// <summary>
    /// 窗口提供者，用于打开对话框
    ///</summary>
    private readonly IWindowProvider _windowProvider;

    /// <summary>
    /// 当前状态文本
    ///</summary>
    [ObservableProperty]
    private string statusText = "就绪";

    /// <summary>
    /// 日志摘要
    ///</summary>
    [ObservableProperty]
    private string logSummary = "";

    /// <summary>
    /// 当前选中的连接
    ///</summary>
    [ObservableProperty]
    private ConnectionItemViewModel? selectedConnection;

    /// <summary>
    /// 行数警告阈值文本
    ///</summary>
    [ObservableProperty]
    private string rowCountWarningThresholdText = "100000";

    /// <summary>
    /// 所有已保存的连接列表
    ///</summary>
    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = new();

    /// <summary>
    /// 创建连接管理 ViewModel
    ///</summary>
    /// <param name="connectionStore">连接持久化存储</param>
    /// <param name="appSettingsStore">应用设置存储</param>
    /// <param name="schemaReader">Schema 读取器</param>
    /// <param name="windowProvider">窗口提供者</param>
    public ConnectionListViewModel(
        IConnectionStore connectionStore,
        IAppSettingsStore appSettingsStore,
        ISchemaReader schemaReader,
        IWindowProvider windowProvider)
    {
        _connectionStore = connectionStore;
        _appSettingsStore = appSettingsStore;
        _schemaReader = schemaReader;
        _windowProvider = windowProvider;

        var settings = _appSettingsStore.Load();
        RowCountWarningThresholdText = settings.RowCountWarningThreshold.ToString();
        RefreshConnections();

        SelectedConnection = Connections.FirstOrDefault(c =>
            string.Equals(c.Name, settings.LastConnectionName, StringComparison.OrdinalIgnoreCase))
            ?? Connections.FirstOrDefault();
    }

    /// <summary>
    /// 新增连接（打开编辑对话框）
    ///</summary>
    [RelayCommand]
    private async Task AddConnectionAsync()
    {
        var window = _windowProvider.GetMainWindow();
        if (window is null)
            return;

        var editVm = new ConnectionEditViewModel(_schemaReader, _windowProvider);
        var dialog = new ConnectionEditWindow(editVm);
        await dialog.ShowDialog<bool>(window);

        if (editVm.Result is not null)
        {
            var item = ConnectionItemViewModel.FromDatabaseConnection(editVm.Result);
            Connections.Add(item);
            SelectedConnection = item;
            SaveConnections();
            StatusText = $"已添加连接：{item.Name}";
            ConnectionsChanged?.Invoke();
        }
    }

    /// <summary>
    /// 编辑选中的连接（打开编辑对话框并预填数据）
    ///</summary>
    [RelayCommand]
    private async Task EditConnectionAsync()
    {
        var target = SelectedConnection;
        if (target is null)
            return;

        var window = _windowProvider.GetMainWindow();
        if (window is null)
            return;

        var editVm = new ConnectionEditViewModel(_schemaReader, _windowProvider);
        editVm.LoadConnection(target.ToDatabaseConnection());
        var dialog = new ConnectionEditWindow(editVm);
        await dialog.ShowDialog<bool>(window);

        if (editVm.Result is not null)
        {
            var index = Connections.IndexOf(target);
            var updated = ConnectionItemViewModel.FromDatabaseConnection(editVm.Result);
            Connections[index] = updated;
            SelectedConnection = updated;
            SaveConnections();
            StatusText = $"已更新连接：{updated.Name}";
            ConnectionsChanged?.Invoke();
        }
    }

    /// <summary>
    /// 删除选中的连接（弹出确认对话框后执行）
    ///</summary>
    [RelayCommand]
    private async Task DeleteConnectionAsync()
    {
        var target = SelectedConnection;
        if (target is null)
            return;

        var window = _windowProvider.GetMainWindow();
        if (window is not null)
        {
            var dialog = new Avalonia.Controls.Window
            {
                Title = "确认删除",
                Width = 360,
                Height = 160,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                CanResize = false,
                Content = new Avalonia.Controls.StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 16,
                    Children =
                    {
                        new Avalonia.Controls.TextBlock { Text = $"确定要删除连接 \"{target.Name}\" 吗？", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        new Avalonia.Controls.StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                new Avalonia.Controls.Button { Content = "删除" },
                                new Avalonia.Controls.Button { Content = "取消" }
                            }
                        }
                    }
                }
            };
            var buttons = ((Avalonia.Controls.StackPanel)((Avalonia.Controls.StackPanel)dialog.Content).Children[1]);
            ((Avalonia.Controls.Button)buttons.Children[0]).Click += (_, _) => dialog.Close(true);
            ((Avalonia.Controls.Button)buttons.Children[1]).Click += (_, _) => dialog.Close(false);
            var confirmed = await dialog.ShowDialog<bool>(window);
            if (!confirmed)
                return;
        }

        Connections.Remove(target);
        SelectedConnection = Connections.FirstOrDefault();
        SaveConnections();
        StatusText = "已删除连接";
        ConnectionsChanged?.Invoke();
    }

    /// <summary>
    /// 测试选中连接的可用性
    ///</summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var target = SelectedConnection;
        if (target is null)
        {
            StatusText = "没有可测试的连接";
            return;
        }

        try
        {
            StatusText = "正在测试连接...";
            var connection = target.ToDatabaseConnection();
            var ok = await _schemaReader.TestConnectionAsync(connection);
            StatusText = ok ? "连接测试成功" : "连接测试失败";
            LogSummary = StatusText;
        }
        catch (Exception ex)
        {
            StatusText = "连接测试失败";
            LogSummary = ex.Message;
            Log.Error(ex, "连接测试失败");
        }
    }

    /// <summary>
    /// 保存行数阈值设置
    ///</summary>
    [RelayCommand]
    private void SaveSettings()
    {
        if (int.TryParse(RowCountWarningThresholdText, out var threshold) && threshold > 0)
        {
            var settings = _appSettingsStore.Load();
            settings = settings with { RowCountWarningThreshold = threshold };
            _appSettingsStore.Save(settings);
            StatusText = "设置已保存";
            ConnectionsChanged?.Invoke();
        }
        else
        {
            StatusText = "行数阈值必须是大于 0 的整数";
        }
    }

    /// <summary>
    /// 从持久化存储重新加载连接列表
    ///</summary>
    public void RefreshConnections()
    {
        var previousSelection = SelectedConnection?.Name;
        Connections.Clear();

        foreach (var conn in _connectionStore.Load())
            Connections.Add(ConnectionItemViewModel.FromDatabaseConnection(conn));

        SelectedConnection = Connections.FirstOrDefault(c =>
            string.Equals(c.Name, previousSelection, StringComparison.OrdinalIgnoreCase))
            ?? Connections.FirstOrDefault();
    }

    /// <summary>
    /// 将当前连接列表保存到持久化存储
    ///</summary>
    private void SaveConnections()
    {
        var connections = Connections.Select(c => c.ToDatabaseConnection()).ToList();
        _connectionStore.Save(connections);
    }
}
