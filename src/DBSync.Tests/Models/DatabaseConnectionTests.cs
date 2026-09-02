using DBSync.Core.Models;

namespace DBSync.Tests.Models;

/// <summary>
/// DatabaseConnection 的 BuildConnectionString 和 ParseConnectionString 单元测试
///</summary>
public class DatabaseConnectionTests
{
    // ── BuildConnectionString 测试 ──

    [Fact]
    public void BuildConnectionString_SqlServer_WithWindowsAuth_ReturnsIntegratedSecurity()
    {
        var conn = new DatabaseConnection
        {
            Name = "测试",
            DbType = DatabaseType.SqlServer,
            ConnectionString = "",
            Server = "localhost",
            Port = 1433,
            Database = "MyDb",
            UseWindowsAuth = true
        };

        var result = conn.BuildConnectionString();

        Assert.Contains("Server=localhost", result);
        Assert.Contains("Database=MyDb", result);
        Assert.Contains("Integrated Security=True", result);
        Assert.DoesNotContain("User Id", result);
    }

    [Fact]
    public void BuildConnectionString_SqlServer_WithSqlAuth_ReturnsUserPassword()
    {
        var conn = new DatabaseConnection
        {
            Name = "测试",
            DbType = DatabaseType.SqlServer,
            ConnectionString = "",
            Server = "192.168.1.1",
            Database = "MyDb",
            Username = "sa",
            Password = "pwd123"
        };

        var result = conn.BuildConnectionString();

        Assert.Contains("Server=192.168.1.1", result);
        Assert.Contains("Database=MyDb", result);
        Assert.Contains("User Id=sa", result);
        Assert.Contains("Password=pwd123", result);
        Assert.DoesNotContain("Integrated Security", result);
    }

    [Fact]
    public void BuildConnectionString_SqlServer_NonDefaultPort_IncludesPort()
    {
        var conn = new DatabaseConnection
        {
            Name = "测试",
            DbType = DatabaseType.SqlServer,
            ConnectionString = "",
            Server = "db-host",
            Port = 1434,
            Database = "MyDb",
            UseWindowsAuth = true
        };

        var result = conn.BuildConnectionString();

        Assert.Contains("Server=db-host,1434", result);
    }

    [Fact]
    public void BuildConnectionString_SqlServer_DefaultPort_OmitsPort()
    {
        var conn = new DatabaseConnection
        {
            Name = "测试",
            DbType = DatabaseType.SqlServer,
            ConnectionString = "",
            Server = "localhost",
            Port = 1433,
            Database = "MyDb",
            UseWindowsAuth = true
        };

        var result = conn.BuildConnectionString();

        Assert.Contains("Server=localhost;", result);
        Assert.DoesNotContain(",1433", result);
    }

    [Fact]
    public void BuildConnectionString_MySql_IncludesCharset()
    {
        var conn = new DatabaseConnection
        {
            Name = "测试",
            DbType = DatabaseType.MySql,
            ConnectionString = "",
            Server = "192.168.1.1",
            Port = 3306,
            Database = "app",
            Username = "root",
            Password = "pwd",
            Charset = "utf8mb4"
        };

        var result = conn.BuildConnectionString();

        Assert.Contains("Server=192.168.1.1", result);
        Assert.Contains("Database=app", result);
        Assert.Contains("Uid=root", result);
        Assert.Contains("Pwd=pwd", result);
        Assert.Contains("CharSet=utf8mb4", result);
    }

    [Fact]
    public void BuildConnectionString_PostgreSql_IncludesSearchPath()
    {
        var conn = new DatabaseConnection
        {
            Name = "测试",
            DbType = DatabaseType.PostgreSql,
            ConnectionString = "",
            Server = "pg-host",
            Port = 5432,
            Database = "mydb",
            Username = "user",
            Password = "pwd",
            Schema = "public"
        };

        var result = conn.BuildConnectionString();

        Assert.Contains("Host=pg-host", result);
        Assert.Contains("Database=mydb", result);
        Assert.Contains("Username=user", result);
        Assert.Contains("Password=pwd", result);
        Assert.Contains("Search Path=public", result);
    }

    [Fact]
    public void BuildConnectionString_Sqlite_UsesDataSource()
    {
        var conn = new DatabaseConnection
        {
            Name = "测试",
            DbType = DatabaseType.Sqlite,
            ConnectionString = "",
            Server = "/data/app.db"
        };

        var result = conn.BuildConnectionString();

        Assert.Equal("Data Source=/data/app.db;", result);
    }

    [Fact]
    public void BuildConnectionString_SqlServer_WithAdditionalParameters_AppendsToEnd()
    {
        var conn = new DatabaseConnection
        {
            Name = "测试",
            DbType = DatabaseType.SqlServer,
            ConnectionString = "",
            Server = "localhost",
            Database = "MyDb",
            UseWindowsAuth = true,
            AdditionalParameters = "Encrypt=True;TrustServerCertificate=True"
        };

        var result = conn.BuildConnectionString();

        Assert.Contains("Encrypt=True", result);
        Assert.Contains("TrustServerCertificate=True", result);
    }

    // ── ParseConnectionString 测试 ──

    [Fact]
    public void ParseConnectionString_SqlServer_WindowsAuth_SetsUseWindowsAuthTrue()
    {
        var result = DatabaseConnection.ParseConnectionString(
            DatabaseType.SqlServer,
            "Server=localhost;Database=MyDb;Integrated Security=True;",
            "测试");

        Assert.Equal("localhost", result.Server);
        Assert.Equal("MyDb", result.Database);
        Assert.True(result.UseWindowsAuth);
        Assert.Equal("", result.Username);
    }

    [Fact]
    public void ParseConnectionString_SqlServer_SqlAuth_SetsUsernamePassword()
    {
        var result = DatabaseConnection.ParseConnectionString(
            DatabaseType.SqlServer,
            "Server=db-host,1434;Database=MyDb;User Id=sa;Password=pwd123;",
            "测试");

        Assert.Equal("db-host", result.Server);
        Assert.Equal(1434, result.Port);
        Assert.Equal("MyDb", result.Database);
        Assert.Equal("sa", result.Username);
        Assert.Equal("pwd123", result.Password);
        Assert.False(result.UseWindowsAuth);
    }

    [Fact]
    public void ParseConnectionString_MySql_ExtractsAllFields()
    {
        var result = DatabaseConnection.ParseConnectionString(
            DatabaseType.MySql,
            "Server=mysql-host;Port=3307;Database=app;Uid=root;Pwd=secret;CharSet=utf8mb4;",
            "MySQL 测试");

        Assert.Equal("mysql-host", result.Server);
        Assert.Equal(3307, result.Port);
        Assert.Equal("app", result.Database);
        Assert.Equal("root", result.Username);
        Assert.Equal("secret", result.Password);
        Assert.Equal("utf8mb4", result.Charset);
    }

    [Fact]
    public void ParseConnectionString_PostgreSql_ExtractsSearchPath()
    {
        var result = DatabaseConnection.ParseConnectionString(
            DatabaseType.PostgreSql,
            "Host=pg-host;Port=5433;Database=mydb;Username=user;Password=pwd;Search Path=custom;",
            "PG 测试");

        Assert.Equal("pg-host", result.Server);
        Assert.Equal(5433, result.Port);
        Assert.Equal("mydb", result.Database);
        Assert.Equal("user", result.Username);
        Assert.Equal("pwd", result.Password);
        Assert.Equal("custom", result.Schema);
    }

    [Fact]
    public void ParseConnectionString_Sqlite_ExtractsDataSource()
    {
        var result = DatabaseConnection.ParseConnectionString(
            DatabaseType.Sqlite,
            "Data Source=/data/app.db;",
            "SQLite 测试");

        Assert.Equal("/data/app.db", result.Server);
    }

    [Fact]
    public void ParseConnectionString_MalformedString_ReturnsDefaultsWithOriginalConnectionString()
    {
        var malformed = "这不是一个有效的连接字符串!!!";
        var result = DatabaseConnection.ParseConnectionString(
            DatabaseType.SqlServer,
            malformed,
            "测试");

        Assert.Equal(malformed, result.ConnectionString);
        Assert.Equal("测试", result.Name);
        Assert.Equal(DatabaseType.SqlServer, result.DbType);
    }

    [Fact]
    public void ParseConnectionString_EmptyString_ReturnsDefaultValues()
    {
        var result = DatabaseConnection.ParseConnectionString(
            DatabaseType.SqlServer,
            "",
            "空测试");

        Assert.Equal("", result.ConnectionString);
        Assert.Equal("空测试", result.Name);
    }

    // ── WithDefaults 测试 ──

    [Fact]
    public void WithDefaults_SqlServer_ReturnsCorrectDefaults()
    {
        var result = DatabaseConnection.WithDefaults(DatabaseType.SqlServer);

        Assert.Equal(1433, result.Port);
        Assert.True(result.UseWindowsAuth);
        Assert.Equal("localhost", result.Server);
    }

    [Fact]
    public void WithDefaults_MySql_ReturnsCorrectDefaults()
    {
        var result = DatabaseConnection.WithDefaults(DatabaseType.MySql);

        Assert.Equal(3306, result.Port);
        Assert.Equal("utf8mb4", result.Charset);
        Assert.Equal("root", result.Username);
    }

    [Fact]
    public void WithDefaults_Sqlite_ReturnsNullPort()
    {
        var result = DatabaseConnection.WithDefaults(DatabaseType.Sqlite);

        Assert.Null(result.Port);
    }

    // ── 往返一致性测试 ──

    [Fact]
    public void BuildThenParse_SqlServer_RoundTrips()
    {
        var original = new DatabaseConnection
        {
            Name = "往返测试",
            DbType = DatabaseType.SqlServer,
            ConnectionString = "",
            Server = "db-host",
            Port = 1434,
            Database = "TestDb",
            Username = "sa",
            Password = "pwd"
        };
        var connStr = original.BuildConnectionString();

        var parsed = DatabaseConnection.ParseConnectionString(
            DatabaseType.SqlServer, connStr, original.Name);

        Assert.Equal(original.Server, parsed.Server);
        Assert.Equal(original.Port, parsed.Port);
        Assert.Equal(original.Database, parsed.Database);
        Assert.Equal(original.Username, parsed.Username);
        Assert.Equal(original.Password, parsed.Password);
    }

    [Fact]
    public void BuildThenParse_MySql_RoundTrips()
    {
        var original = new DatabaseConnection
        {
            Name = "往返测试",
            DbType = DatabaseType.MySql,
            ConnectionString = "",
            Server = "mysql-host",
            Port = 3307,
            Database = "app",
            Username = "root",
            Password = "secret",
            Charset = "utf8mb4"
        };
        var connStr = original.BuildConnectionString();

        var parsed = DatabaseConnection.ParseConnectionString(
            DatabaseType.MySql, connStr, original.Name);

        Assert.Equal(original.Server, parsed.Server);
        Assert.Equal(original.Port, parsed.Port);
        Assert.Equal(original.Database, parsed.Database);
        Assert.Equal(original.Username, parsed.Username);
        Assert.Equal(original.Password, parsed.Password);
        Assert.Equal(original.Charset, parsed.Charset);
    }
}
