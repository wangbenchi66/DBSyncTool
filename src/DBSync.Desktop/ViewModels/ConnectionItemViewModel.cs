using CommunityToolkit.Mvvm.ComponentModel;
using DBSync.Core.Models;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 数据库连接项的视图模型，用于列表展示和选择
///</summary>
public sealed partial class ConnectionItemViewModel : ObservableObject
{
    /// <summary>
    /// 连接的显示名称
    ///</summary>
    [ObservableProperty]
    private string name = "";

    /// <summary>
    /// 数据库类型
    ///</summary>
    [ObservableProperty]
    private DatabaseType dbType;

    /// <summary>
    /// 服务器地址（SQLite 时为文件路径）
    ///</summary>
    [ObservableProperty]
    private string server = "";

    /// <summary>
    /// 端口号
    ///</summary>
    [ObservableProperty]
    private int? port;

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
    private bool useWindowsAuth;

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
    /// 根据结构化字段拼接的完整连接字符串
    ///</summary>
    public string ConnectionString => ToDatabaseConnection().ConnectionString;

    /// <summary>
    /// 连接摘要信息（用于列表展示）
    ///</summary>
    public string DisplayInfo
    {
        get
        {
            if (DbType == DatabaseType.Sqlite)
                return Server;
            var info = Server;
            if (!string.IsNullOrEmpty(Database))
                info += $" / {Database}";
            return info;
        }
    }

    /// <summary>
    /// 将视图模型转换为 Core 层领域模型
    ///</summary>
    /// <returns>DatabaseConnection 领域模型实例</returns>
    public DatabaseConnection ToDatabaseConnection()
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
            AdditionalParameters = AdditionalParameters
        };
        return conn with { ConnectionString = conn.BuildConnectionString() };
    }

    /// <summary>
    /// 从 Core 层领域模型创建视图模型实例
    ///</summary>
    /// <param name="connection">领域模型实例</param>
    /// <returns>视图模型实例</returns>
    public static ConnectionItemViewModel FromDatabaseConnection(DatabaseConnection connection)
    {
        return new ConnectionItemViewModel
        {
            Name = connection.Name,
            DbType = connection.DbType,
            Server = connection.Server,
            Port = connection.Port,
            Database = connection.Database,
            Username = connection.Username,
            Password = connection.Password,
            UseWindowsAuth = connection.UseWindowsAuth,
            Schema = connection.Schema,
            Charset = connection.Charset,
            AdditionalParameters = connection.AdditionalParameters
        };
    }
}
