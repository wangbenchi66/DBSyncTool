using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using DBSync.Desktop.Services;
using DBSync.Desktop.Storage;
using DBSync.Desktop.Models;
using System.Collections.ObjectModel;

namespace DBSync.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IConnectionStore _connectionStore;
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly ISchemaReader _schemaReader;

    [ObservableProperty]
    private string statusText = "就绪";

    [ObservableProperty]
    private string logSummary = "未记录操作";

    [ObservableProperty]
    private ConnectionItemViewModel? selectedConnection;

    [ObservableProperty]
    private string rowCountWarningThresholdText = "100000";

    [ObservableProperty]
    private bool hasPendingOperation;

    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = new();

    public MainWindowViewModel(
        IConnectionStore connectionStore,
        IAppSettingsStore appSettingsStore,
        ISchemaReader schemaReader)
    {
        _connectionStore = connectionStore;
        _appSettingsStore = appSettingsStore;
        _schemaReader = schemaReader;
        RowCountWarningThresholdText = _appSettingsStore.Load().RowCountWarningThreshold.ToString();
        LoadConnections();
    }

    [RelayCommand]
    private void OpenExport()
    {
        StatusText = "准备导出快照";
        LogSummary = "已进入导出入口";
        HasPendingOperation = true;
    }

    [RelayCommand]
    private void OpenCompare()
    {
        StatusText = "准备加载快照并比对";
        LogSummary = "已进入比对入口";
        HasPendingOperation = true;
    }

    [RelayCommand]
    private void AddConnection()
    {
        var item = new ConnectionItemViewModel("新连接", DatabaseType.SqlServer, "localhost");
        Connections.Add(item);
        SelectedConnection = item;
        HasPendingOperation = true;
        Save();
    }

    [RelayCommand]
    private void EditConnection()
    {
        var target = SelectedConnection ?? Connections.FirstOrDefault();
        if (target is null)
            return;

        target.Name = $"{target.Name}*";
        HasPendingOperation = true;

        Save();
    }

    [RelayCommand]
    private void DeleteConnection()
    {
        var target = SelectedConnection ?? Connections.FirstOrDefault();
        if (target is null)
            return;

        Connections.Remove(target);
        SelectedConnection = Connections.FirstOrDefault();
        HasPendingOperation = true;

        Save();
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (int.TryParse(RowCountWarningThresholdText, out var threshold) && threshold > 0)
        {
            _appSettingsStore.Save(new AppSettings { RowCountWarningThreshold = threshold });
            HasPendingOperation = false;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var target = SelectedConnection ?? Connections.FirstOrDefault();
        if (target is null)
        {
            StatusText = "没有可测试的连接";
            return;
        }

        var connection = new DatabaseConnection
        {
            Name = target.Name,
            DbType = target.DbType,
            ConnectionString = target.ConnectionString
        };

        var ok = await _schemaReader.TestConnectionAsync(connection);
        StatusText = ok ? "连接测试成功" : "连接测试失败";
        LogSummary = StatusText;
    }

    private void LoadConnections()
    {
        foreach (var item in _connectionStore.Load())
            Connections.Add(item);
    }

    private void Save()
    {
        _connectionStore.Save(Connections.ToList());
        HasPendingOperation = false;
    }
}

public sealed partial class ConnectionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private DatabaseType dbType;

    [ObservableProperty]
    private string serverAddress;

    public string ConnectionString => $"Server={ServerAddress};";

    public ConnectionItemViewModel(string name, DatabaseType dbType, string serverAddress)
    {
        this.name = name;
        this.dbType = dbType;
        this.serverAddress = serverAddress;
    }
}
