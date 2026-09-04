using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using DBSync.Desktop.Services;
using Serilog;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 连接编辑对话框的 ViewModel，支持结构化字段与原始连接字符串的双向同步
///</summary>
public sealed partial class ConnectionEditViewModel : ObservableObject
{
    /// <summary>
    /// Schema 读取器，用于测试连接
    ///</summary>
    private readonly ISchemaReader _schemaReader;

    /// <summary>
    /// 窗口提供者，用于获取文件选择器
    ///</summary>
    private readonly IWindowProvider _windowProvider;

    /// <summary>
    /// 是否正在从结构化字段同步到原始字符串（防止循环）
    ///</summary>
    private bool _isSyncingFromFields;

    /// <summary>
    /// 是否正在从原始字符串同步到结构化字段（防止循环）
    ///</summary>
    private bool _isSyncingFromRaw;

    /// <summary>
    /// 连接显示名称
    ///</summary>
    [ObservableProperty]
    private string name = "";

    /// <summary>
    /// 数据库类型
    ///</summary>
    [ObservableProperty]
    private DatabaseType dbType = DatabaseType.SqlServer;

    /// <summary>
    /// 服务器地址（SQLite 时为文件路径）
    ///</summary>
    [ObservableProperty]
    private string server = "localhost";

    /// <summary>
    /// 端口号
    ///</summary>
    [ObservableProperty]
    private int? port = 1433;

    /// <summary>
    /// 数据库名称
    ///</summary>
    [ObservableProperty]
    private string database = "";

    /// <summary>
    /// 用户名
    ///</summary>
    [ObservableProperty]
    private string username = "";

    /// <summary>
    /// 密码
    ///</summary>
    [ObservableProperty]
    private string password = "";

    /// <summary>
    /// 是否使用 Windows 集成认证（仅 SQL Server）
    ///</summary>
    [ObservableProperty]
    private bool useWindowsAuth = true;

    /// <summary>
    /// Schema 名称（仅 PostgreSQL）
    ///</summary>
    [ObservableProperty]
    private string schema = "";

    /// <summary>
    /// 字符集（仅 MySQL）
    ///</summary>
    [ObservableProperty]
    private string charset = "";

    /// <summary>
    /// 额外连接字符串参数
    ///</summary>
    [ObservableProperty]
    private string additionalParameters = "";

    /// <summary>
    /// 连接所属环境
    ///</summary>
    [ObservableProperty]
    private ConnectionEnvironment environment = ConnectionEnvironment.Unspecified;

    /// <summary>
    /// 原始连接字符串（高级编辑模式）
    ///</summary>
    [ObservableProperty]
    private string rawConnectionString = "";

    /// <summary>
    /// 测试连接结果文本
    ///</summary>
    [ObservableProperty]
    private string testConnectionStatusText = "";

    /// <summary>
    /// 是否为新增连接模式
    ///</summary>
    [ObservableProperty]
    private bool isNewConnection = true;

    /// <summary>
    /// 编辑结果（保存时设置，对话框关闭后由调用方读取）
    ///</summary>
    public DatabaseConnection? Result { get; private set; }

    /// <summary>
    /// 对话框关闭回调（由 View 设置）
    ///</summary>
    public Action? CloseDialog { get; set; }

    /// <summary>
    /// 创建连接编辑 ViewModel（新增模式）
    ///</summary>
    /// <param name="schemaReader">Schema 读取器</param>
    /// <param name="windowProvider">窗口提供者</param>
    public ConnectionEditViewModel(
        ISchemaReader schemaReader,
        IWindowProvider windowProvider)
    {
        _schemaReader = schemaReader;
        _windowProvider = windowProvider;
        RawConnectionString = BuildConnection()?.ConnectionString ?? string.Empty;
    }

    /// <summary>
    /// 加载已有连接数据进行编辑
    ///</summary>
    /// <param name="connection">已有的连接配置</param>
    public void LoadConnection(DatabaseConnection connection)
    {
        IsNewConnection = false;
        _isSyncingFromRaw = true;
        try
        {
            Name = connection.Name;
            DbType = connection.DbType;
            Server = connection.Server;
            Port = connection.Port;
            Database = connection.Database;
            Username = connection.Username;
            Password = connection.Password;
            UseWindowsAuth = connection.UseWindowsAuth;
            Schema = connection.Schema;
            Charset = connection.Charset;
            AdditionalParameters = connection.AdditionalParameters;
            Environment = connection.Environment;
        }
        finally
        {
            _isSyncingFromRaw = false;
        }
        RawConnectionString = BuildConnection()?.ConnectionString ?? string.Empty;
    }

    /// <summary>
    /// 数据库类型变更时重置相关字段
    ///</summary>
    partial void OnDbTypeChanged(DatabaseType value)
    {
        if (_isSyncingFromRaw)
            return;

        _isSyncingFromFields = true;
        try
        {
            Port = DatabaseConnection.GetDefaultPort(value);

            switch (value)
            {
                case DatabaseType.SqlServer:
                    UseWindowsAuth = true;
                    Schema = "";
                    Charset = "";
                    if (string.IsNullOrEmpty(Server)) Server = "localhost";
                    break;
                case DatabaseType.MySql:
                    UseWindowsAuth = false;
                    Schema = "";
                    Charset = "utf8mb4";
                    if (string.IsNullOrEmpty(Username)) Username = "root";
                    if (string.IsNullOrEmpty(Server)) Server = "localhost";
                    break;
                case DatabaseType.PostgreSql:
                    UseWindowsAuth = false;
                    Charset = "";
                    Schema = "public";
                    if (string.IsNullOrEmpty(Username)) Username = "postgres";
                    if (string.IsNullOrEmpty(Server)) Server = "localhost";
                    break;
                case DatabaseType.Sqlite:
                    UseWindowsAuth = false;
                    Username = "";
                    Password = "";
                    Database = "";
                    Schema = "";
                    Charset = "";
                    Server = "";
                    break;
            }
        }
        finally
        {
            _isSyncingFromFields = false;
        }

        SyncFieldsToRawString();
    }

    /// <summary>
    /// 原始连接字符串变更时回填结构化字段
    ///</summary>
    partial void OnRawConnectionStringChanged(string value)
    {
        if (_isSyncingFromFields)
            return;

        _isSyncingFromRaw = true;
        try
        {
            var parsed = DatabaseConnection.ParseConnectionString(DbType, value, Name);
            Server = parsed.Server;
            Port = parsed.Port;
            Database = parsed.Database;
            Username = parsed.Username;
            Password = parsed.Password;
            UseWindowsAuth = parsed.UseWindowsAuth;
            Schema = parsed.Schema;
            Charset = parsed.Charset;
            AdditionalParameters = parsed.AdditionalParameters;
        }
        finally
        {
            _isSyncingFromRaw = false;
        }
    }

    /// <summary>
    /// 测试当前连接配置是否可用
    ///</summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        SyncFieldsToRawString();
        var connection = BuildConnection();
        if (connection is null)
        {
            TestConnectionStatusText = "连接信息不完整";
            return;
        }

        try
        {
            TestConnectionStatusText = "正在测试...";
            var ok = await _schemaReader.TestConnectionAsync(connection);
            TestConnectionStatusText = ok ? "✓ 连接成功" : "✗ 连接失败";
        }
        catch (Exception ex)
        {
            TestConnectionStatusText = $"✗ {ex.Message}";
            Log.Error(ex, "编辑连接页测试连接失败");
        }
    }

    /// <summary>
    /// 保存连接配置并关闭对话框
    ///</summary>
    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            TestConnectionStatusText = "请输入连接名称";
            return;
        }

        if (DbType != DatabaseType.Sqlite && string.IsNullOrWhiteSpace(Server))
        {
            TestConnectionStatusText = "请输入服务器地址";
            return;
        }

        SyncFieldsToRawString();
        Result = BuildConnection();
        CloseDialog?.Invoke();
    }

    /// <summary>
    /// 取消编辑并关闭对话框
    ///</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseDialog?.Invoke();
    }

    /// <summary>
    /// 浏览选择 SQLite 数据库文件
    ///</summary>
    [RelayCommand]
    private async Task BrowseSqlitePathAsync()
    {
        var window = _windowProvider.GetMainWindow();
        if (window is null)
            return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "选择 SQLite 数据库文件",
            AllowMultiple = false
        });

        var file = files.FirstOrDefault();
        var localPath = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
            Server = localPath;
    }

    /// <summary>
    /// 根据当前字段构建 DatabaseConnection 实例
    ///</summary>
    /// <returns>连接配置实例</returns>
    private DatabaseConnection? BuildConnection()
    {
        var conn = new DatabaseConnection
        {
            Name = Name,
            DbType = DbType,
            ConnectionString = "",
            Server = Server,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password,
            UseWindowsAuth = UseWindowsAuth,
            Schema = Schema,
            Charset = Charset,
            AdditionalParameters = AdditionalParameters,
            Environment = Environment
        };
        return conn with { ConnectionString = conn.BuildConnectionString() };
    }

    /// <summary>
    /// 从结构化字段同步生成原始连接字符串
    ///</summary>
    private void SyncFieldsToRawString()
    {
        if (_isSyncingFromRaw)
            return;

        _isSyncingFromFields = true;
        try
        {
            var conn = BuildConnection();
            if (conn is not null)
                RawConnectionString = conn.ConnectionString;
        }
        finally
        {
            _isSyncingFromFields = false;
        }
    }
}
