using Avalonia.Controls;
using Avalonia.Data.Converters;
using DBSync.Core.Models;
using DBSync.Desktop.ViewModels;
using System.Globalization;

namespace DBSync.Desktop.Views;

/// <summary>
/// 连接编辑对话框窗口
///</summary>
public partial class ConnectionEditWindow : Window
{
    /// <summary>
    /// 数据库类型列表（供 ComboBox 绑定）
    ///</summary>
    public static DatabaseType[] DbTypes { get; } =
    [
        DatabaseType.SqlServer,
        DatabaseType.MySql,
        DatabaseType.PostgreSql,
        DatabaseType.Sqlite
    ];

    /// <summary>
    /// 数据库环境列表（供 ComboBox 绑定）
    ///</summary>
    public static ConnectionEnvironment[] EnvironmentOptions { get; } =
    [
        ConnectionEnvironment.Unspecified,
        ConnectionEnvironment.Development,
        ConnectionEnvironment.Testing,
        ConnectionEnvironment.Staging,
        ConnectionEnvironment.Production
    ];

    /// <summary>
    /// 是否为 SQLite 类型的转换器
    ///</summary>
    public static FuncValueConverter<DatabaseType, bool> IsSqliteConverter { get; } =
        new(dbType => dbType == DatabaseType.Sqlite);

    /// <summary>
    /// 是否非 SQLite 类型的转换器
    ///</summary>
    public static FuncValueConverter<DatabaseType, bool> NotSqliteConverter { get; } =
        new(dbType => dbType != DatabaseType.Sqlite);

    /// <summary>
    /// 是否为 SQL Server 类型的转换器
    ///</summary>
    public static FuncValueConverter<DatabaseType, bool> IsSqlServerConverter { get; } =
        new(dbType => dbType == DatabaseType.SqlServer);

    /// <summary>
    /// 是否为 MySQL 类型的转换器
    ///</summary>
    public static FuncValueConverter<DatabaseType, bool> IsMySqlConverter { get; } =
        new(dbType => dbType == DatabaseType.MySql);

    /// <summary>
    /// 是否为 PostgreSQL 类型的转换器
    ///</summary>
    public static FuncValueConverter<DatabaseType, bool> IsPostgreSqlConverter { get; } =
        new(dbType => dbType == DatabaseType.PostgreSql);

    /// <summary>
    /// 环境显示文本转换器
    ///</summary>
    public static FuncValueConverter<ConnectionEnvironment, string> EnvironmentDisplayConverter { get; } =
        new(environment => environment switch
        {
            ConnectionEnvironment.Development => "开发",
            ConnectionEnvironment.Testing => "测试",
            ConnectionEnvironment.Staging => "预发",
            ConnectionEnvironment.Production => "生产",
            _ => "未设置"
        });

    /// <summary>
    /// 设计器用无参构造函数
    ///</summary>
    public ConnectionEditWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 创建连接编辑对话框
    ///</summary>
    /// <param name="viewModel">连接编辑 ViewModel</param>
    public ConnectionEditWindow(ConnectionEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseDialog = () => Close(viewModel.Result is not null);
    }
}
