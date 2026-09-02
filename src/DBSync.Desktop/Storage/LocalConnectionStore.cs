using System.Text.Json;
using DBSync.Core.Models;
using DBSync.Desktop.Services;

namespace DBSync.Desktop.Storage;

/// <summary>
/// 本地文件系统连接配置存储，使用加密保护敏感信息
///</summary>
public sealed class LocalConnectionStore(IConnectionEncryption encryption) : IConnectionStore
{
    /// <summary>
    /// JSON 序列化选项
    ///</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// 存储目录路径
    ///</summary>
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DBSyncTool");

    /// <summary>
    /// 加密连接配置文件路径
    ///</summary>
    private static readonly string FilePath = Path.Combine(Folder, "connections.dat");

    /// <summary>
    /// 从加密文件加载所有已保存的连接配置
    ///</summary>
    /// <returns>连接配置列表</returns>
    public IReadOnlyList<DatabaseConnection> Load()
    {
        if (!File.Exists(FilePath))
            return [];

        var protectedBytes = File.ReadAllBytes(FilePath);
        var json = encryption.Unprotect(protectedBytes);
        var items = JsonSerializer.Deserialize<List<ConnectionDto>>(json, JsonOptions) ?? [];

        return items.Select(DtoToConnection).ToList();
    }

    /// <summary>
    /// 将连接配置列表加密后保存到文件
    ///</summary>
    /// <param name="connections">要保存的连接配置列表</param>
    public void Save(IReadOnlyList<DatabaseConnection> connections)
    {
        Directory.CreateDirectory(Folder);
        var items = connections.Select(ConnectionToDto).ToList();
        var json = JsonSerializer.SerializeToUtf8Bytes(items, JsonOptions);
        var protectedBytes = encryption.Protect(json);
        File.WriteAllBytes(FilePath, protectedBytes);
    }

    /// <summary>
    /// 将内部 DTO 转换为领域模型
    ///</summary>
    private static DatabaseConnection DtoToConnection(ConnectionDto dto)
    {
        var conn = new DatabaseConnection
        {
            Name = dto.Name ?? "",
            DbType = dto.DbType,
            ConnectionString = "",
            Server = dto.Server ?? dto.ServerAddress ?? "",
            Port = dto.Port,
            Database = dto.Database ?? "",
            Username = dto.Username ?? "",
            Password = dto.Password ?? "",
            UseWindowsAuth = dto.UseWindowsAuth,
            Schema = dto.Schema ?? "",
            Charset = dto.Charset ?? "",
            AdditionalParameters = dto.AdditionalParameters ?? ""
        };
        return conn with { ConnectionString = conn.BuildConnectionString() };
    }

    /// <summary>
    /// 将领域模型转换为内部 DTO
    ///</summary>
    private static ConnectionDto ConnectionToDto(DatabaseConnection conn)
    {
        return new ConnectionDto
        {
            Name = conn.Name,
            DbType = conn.DbType,
            Server = conn.Server,
            Port = conn.Port,
            Database = conn.Database,
            Username = conn.Username,
            Password = conn.Password,
            UseWindowsAuth = conn.UseWindowsAuth,
            Schema = conn.Schema,
            Charset = conn.Charset,
            AdditionalParameters = conn.AdditionalParameters
        };
    }

    /// <summary>
    /// 连接配置持久化 DTO（向后兼容旧格式的 ServerAddress 字段）
    ///</summary>
    private sealed record ConnectionDto
    {
        /// <summary>
        /// 连接显示名称
        ///</summary>
        public string? Name { get; init; }

        /// <summary>
        /// 数据库类型
        ///</summary>
        public DatabaseType DbType { get; init; }

        /// <summary>
        /// 服务器地址（旧字段，向后兼容）
        ///</summary>
        public string? ServerAddress { get; init; }

        /// <summary>
        /// 服务器地址（新字段）
        ///</summary>
        public string? Server { get; init; }

        /// <summary>
        /// 端口号
        ///</summary>
        public int? Port { get; init; }

        /// <summary>
        /// 数据库名称
        ///</summary>
        public string? Database { get; init; }

        /// <summary>
        /// 用户名
        ///</summary>
        public string? Username { get; init; }

        /// <summary>
        /// 密码
        ///</summary>
        public string? Password { get; init; }

        /// <summary>
        /// 是否使用 Windows 集成认证
        ///</summary>
        public bool UseWindowsAuth { get; init; }

        /// <summary>
        /// Schema 名称
        ///</summary>
        public string? Schema { get; init; }

        /// <summary>
        /// 字符集
        ///</summary>
        public string? Charset { get; init; }

        /// <summary>
        /// 额外连接字符串参数
        ///</summary>
        public string? AdditionalParameters { get; init; }
    }
}
