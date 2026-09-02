using System.Data.Common;

namespace DBSync.Core.Models;

/// <summary>
/// 数据库连接配置（内存中存储明文连接字符串，持久化时加密）
///</summary>
public sealed record DatabaseConnection
{
    /// <summary>
    /// 连接的显示名称（用户自定义，如"生产-SqlServer"）
    ///</summary>
    public required string Name { get; init; }

    /// <summary>
    /// 数据库类型
    ///</summary>
    public required DatabaseType DbType { get; init; }

    /// <summary>
    /// 连接字符串（内存中为明文，写入配置文件时加密）
    ///</summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// 服务器地址（SQLite 时为数据库文件路径）
    ///</summary>
    public string Server { get; init; } = "";

    /// <summary>
    /// 端口号（SQLite 无此项时为 null）
    ///</summary>
    public int? Port { get; init; }

    /// <summary>
    /// 数据库名称（SQLite 无此项）
    ///</summary>
    public string Database { get; init; } = "";

    /// <summary>
    /// 用户名（Windows 认证时为空）
    ///</summary>
    public string Username { get; init; } = "";

    /// <summary>
    /// 密码（内存中为明文，持久化时随连接整体加密）
    ///</summary>
    public string Password { get; init; } = "";

    /// <summary>
    /// 是否使用 Windows 集成认证（仅 SQL Server）
    ///</summary>
    public bool UseWindowsAuth { get; init; }

    /// <summary>
    /// Schema 名称（仅 PostgreSQL，默认 public）
    ///</summary>
    public string Schema { get; init; } = "";

    /// <summary>
    /// 字符集（仅 MySQL，默认 utf8mb4）
    ///</summary>
    public string Charset { get; init; } = "";

    /// <summary>
    /// 额外连接字符串参数（如 Encrypt=True;TrustServerCertificate=True;）
    ///</summary>
    public string AdditionalParameters { get; init; } = "";

    /// <summary>
    /// 根据结构化字段拼接对应数据库方言的完整连接字符串
    ///</summary>
    /// <returns>拼接后的连接字符串</returns>
    public string BuildConnectionString()
    {
        return DbType switch
        {
            DatabaseType.SqlServer => BuildSqlServerConnectionString(),
            DatabaseType.MySql => BuildMySqlConnectionString(),
            DatabaseType.PostgreSql => BuildPostgreSqlConnectionString(),
            DatabaseType.Sqlite => BuildSqliteConnectionString(),
            _ => ConnectionString
        };
    }

    /// <summary>
    /// 将原始连接字符串解析为结构化字段，返回新的 DatabaseConnection 实例。
    /// 解析失败时不抛异常，结构化字段保持默认值
    ///</summary>
    /// <param name="dbType">数据库类型</param>
    /// <param name="connectionString">原始连接字符串</param>
    /// <param name="name">连接显示名称</param>
    /// <returns>包含解析后结构化字段的新实例</returns>
    public static DatabaseConnection ParseConnectionString(
        DatabaseType dbType,
        string connectionString,
        string name = "")
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            return dbType switch
            {
                DatabaseType.SqlServer => ParseSqlServer(builder, name, connectionString),
                DatabaseType.MySql => ParseMySql(builder, name, connectionString),
                DatabaseType.PostgreSql => ParsePostgreSql(builder, name, connectionString),
                DatabaseType.Sqlite => ParseSqlite(builder, name, connectionString),
                _ => new DatabaseConnection
                {
                    Name = name,
                    DbType = dbType,
                    ConnectionString = connectionString
                }
            };
        }
        catch
        {
            return new DatabaseConnection
            {
                Name = name,
                DbType = dbType,
                ConnectionString = connectionString
            };
        }
    }

    /// <summary>
    /// 按数据库类型创建包含默认端口等值的初始实例
    ///</summary>
    /// <param name="dbType">数据库类型</param>
    /// <returns>包含默认值的新实例</returns>
    public static DatabaseConnection WithDefaults(DatabaseType dbType)
    {
        var conn = new DatabaseConnection
        {
            Name = "",
            DbType = dbType,
            ConnectionString = "",
            Server = dbType == DatabaseType.Sqlite ? "" : "localhost",
            Port = GetDefaultPort(dbType),
            Database = "",
            Username = dbType == DatabaseType.SqlServer ? "" : "root",
            UseWindowsAuth = dbType == DatabaseType.SqlServer,
            Schema = dbType == DatabaseType.PostgreSql ? "public" : "",
            Charset = dbType == DatabaseType.MySql ? "utf8mb4" : ""
        };
        return conn with { ConnectionString = conn.BuildConnectionString() };
    }

    /// <summary>
    /// 获取指定数据库类型的默认端口号
    ///</summary>
    /// <param name="dbType">数据库类型</param>
    /// <returns>默认端口号，SQLite 返回 null</returns>
    public static int? GetDefaultPort(DatabaseType dbType)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => 1433,
            DatabaseType.MySql => 3306,
            DatabaseType.PostgreSql => 5432,
            _ => null
        };
    }

    /// <summary>
    /// 拼接 SQL Server 连接字符串
    ///</summary>
    private string BuildSqlServerConnectionString()
    {
        var parts = new List<string>();

        var server = Server;
        if (Port.HasValue && Port.Value != 1433)
            server = $"{Server},{Port.Value}";
        parts.Add($"Server={server}");

        if (!string.IsNullOrEmpty(Database))
            parts.Add($"Database={Database}");

        if (UseWindowsAuth)
        {
            parts.Add("Integrated Security=True");
        }
        else
        {
            if (!string.IsNullOrEmpty(Username))
                parts.Add($"User Id={Username}");
            if (!string.IsNullOrEmpty(Password))
                parts.Add($"Password={Password}");
        }

        var result = string.Join(";", parts) + ";";
        if (!string.IsNullOrEmpty(AdditionalParameters))
            result += AdditionalParameters.TrimEnd(';') + ";";
        return result;
    }

    /// <summary>
    /// 拼接 MySQL 连接字符串
    ///</summary>
    private string BuildMySqlConnectionString()
    {
        var parts = new List<string>();

        parts.Add($"Server={Server}");
        if (Port.HasValue)
            parts.Add($"Port={Port.Value}");
        if (!string.IsNullOrEmpty(Database))
            parts.Add($"Database={Database}");
        if (!string.IsNullOrEmpty(Username))
            parts.Add($"Uid={Username}");
        if (!string.IsNullOrEmpty(Password))
            parts.Add($"Pwd={Password}");
        if (!string.IsNullOrEmpty(Charset))
            parts.Add($"CharSet={Charset}");

        var result = string.Join(";", parts) + ";";
        if (!string.IsNullOrEmpty(AdditionalParameters))
            result += AdditionalParameters.TrimEnd(';') + ";";
        return result;
    }

    /// <summary>
    /// 拼接 PostgreSQL 连接字符串
    ///</summary>
    private string BuildPostgreSqlConnectionString()
    {
        var parts = new List<string>();

        parts.Add($"Host={Server}");
        if (Port.HasValue)
            parts.Add($"Port={Port.Value}");
        if (!string.IsNullOrEmpty(Database))
            parts.Add($"Database={Database}");
        if (!string.IsNullOrEmpty(Username))
            parts.Add($"Username={Username}");
        if (!string.IsNullOrEmpty(Password))
            parts.Add($"Password={Password}");
        if (!string.IsNullOrEmpty(Schema))
            parts.Add($"Search Path={Schema}");

        var result = string.Join(";", parts) + ";";
        if (!string.IsNullOrEmpty(AdditionalParameters))
            result += AdditionalParameters.TrimEnd(';') + ";";
        return result;
    }

    /// <summary>
    /// 拼接 SQLite 连接字符串
    ///</summary>
    private string BuildSqliteConnectionString()
    {
        var result = $"Data Source={Server};";
        if (!string.IsNullOrEmpty(AdditionalParameters))
            result += AdditionalParameters.TrimEnd(';') + ";";
        return result;
    }

    /// <summary>
    /// 解析 SQL Server 连接字符串
    ///</summary>
    private static DatabaseConnection ParseSqlServer(
        DbConnectionStringBuilder builder, string name, string connectionString)
    {
        var server = GetValue(builder, "Server", "Data Source") ?? "";
        int? port = null;

        // SQL Server 的端口以逗号分隔在 Server 字段中（如 localhost,1433）
        if (server.Contains(','))
        {
            var parts = server.Split(',', 2);
            server = parts[0];
            if (int.TryParse(parts[1].Trim(), out var p))
                port = p;
        }

        var useWindowsAuth =
            GetBoolValue(builder, "Integrated Security") ||
            GetBoolValue(builder, "Trusted_Connection");

        var additional = CollectAdditionalParameters(builder,
            "Server", "Data Source", "Database", "Initial Catalog",
            "User Id", "User ID", "UID", "Password", "PWD",
            "Integrated Security", "Trusted_Connection");

        return new DatabaseConnection
        {
            Name = name,
            DbType = DatabaseType.SqlServer,
            ConnectionString = connectionString,
            Server = server,
            Port = port,
            Database = GetValue(builder, "Database", "Initial Catalog") ?? "",
            Username = useWindowsAuth ? "" : GetValue(builder, "User Id", "User ID", "UID") ?? "",
            Password = useWindowsAuth ? "" : GetValue(builder, "Password", "PWD") ?? "",
            UseWindowsAuth = useWindowsAuth,
            AdditionalParameters = additional
        };
    }

    /// <summary>
    /// 解析 MySQL 连接字符串
    ///</summary>
    private static DatabaseConnection ParseMySql(
        DbConnectionStringBuilder builder, string name, string connectionString)
    {
        int? port = null;
        var portStr = GetValue(builder, "Port");
        if (!string.IsNullOrEmpty(portStr) && int.TryParse(portStr, out var p))
            port = p;

        var additional = CollectAdditionalParameters(builder,
            "Server", "Host", "Data Source", "Port",
            "Database", "Uid", "User Id", "Pwd", "Password", "CharSet", "Charset");

        return new DatabaseConnection
        {
            Name = name,
            DbType = DatabaseType.MySql,
            ConnectionString = connectionString,
            Server = GetValue(builder, "Server", "Host", "Data Source") ?? "",
            Port = port,
            Database = GetValue(builder, "Database") ?? "",
            Username = GetValue(builder, "Uid", "User Id") ?? "",
            Password = GetValue(builder, "Pwd", "Password") ?? "",
            Charset = GetValue(builder, "CharSet", "Charset") ?? "",
            AdditionalParameters = additional
        };
    }

    /// <summary>
    /// 解析 PostgreSQL 连接字符串
    ///</summary>
    private static DatabaseConnection ParsePostgreSql(
        DbConnectionStringBuilder builder, string name, string connectionString)
    {
        int? port = null;
        var portStr = GetValue(builder, "Port");
        if (!string.IsNullOrEmpty(portStr) && int.TryParse(portStr, out var p))
            port = p;

        var additional = CollectAdditionalParameters(builder,
            "Host", "Server", "Port", "Database",
            "Username", "User Id", "Password", "Search Path");

        return new DatabaseConnection
        {
            Name = name,
            DbType = DatabaseType.PostgreSql,
            ConnectionString = connectionString,
            Server = GetValue(builder, "Host", "Server") ?? "",
            Port = port,
            Database = GetValue(builder, "Database") ?? "",
            Username = GetValue(builder, "Username", "User Id") ?? "",
            Password = GetValue(builder, "Password") ?? "",
            Schema = GetValue(builder, "Search Path") ?? "",
            AdditionalParameters = additional
        };
    }

    /// <summary>
    /// 解析 SQLite 连接字符串
    ///</summary>
    private static DatabaseConnection ParseSqlite(
        DbConnectionStringBuilder builder, string name, string connectionString)
    {
        var additional = CollectAdditionalParameters(builder,
            "Data Source", "Filename");

        return new DatabaseConnection
        {
            Name = name,
            DbType = DatabaseType.Sqlite,
            ConnectionString = connectionString,
            Server = GetValue(builder, "Data Source", "Filename") ?? "",
            AdditionalParameters = additional
        };
    }

    /// <summary>
    /// 从连接字符串构建器中按优先级尝试获取值
    ///</summary>
    private static string? GetValue(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && value is not null)
            {
                var str = value.ToString();
                if (!string.IsNullOrEmpty(str))
                    return str;
            }
        }
        return null;
    }

    /// <summary>
    /// 从连接字符串构建器中获取布尔值
    ///</summary>
    private static bool GetBoolValue(DbConnectionStringBuilder builder, params string[] keys)
    {
        var value = GetValue(builder, keys);
        if (string.IsNullOrEmpty(value))
            return false;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("sspi", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 收集不在已知键列表中的额外参数
    ///</summary>
    private static string CollectAdditionalParameters(
        DbConnectionStringBuilder builder,
        params string[] knownKeys)
    {
        var knownSet = new HashSet<string>(knownKeys, StringComparer.OrdinalIgnoreCase);
        var extras = new List<string>();

        foreach (string key in builder.Keys!)
        {
            if (!knownSet.Contains(key))
                extras.Add($"{key}={builder[key]}");
        }

        return extras.Count > 0 ? string.Join(";", extras) + ";" : "";
    }
}
