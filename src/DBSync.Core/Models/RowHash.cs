namespace DBSync.Core.Models;

/// <summary>
/// 数据库表中一行数据的主键值 + 哈希指纹，用于数据差异比对
///</summary>
public sealed record RowHash
{
    /// <summary>
    /// 主键列名到列值的映射，支持复合主键（值为 null 表示该列为 NULL）
    ///</summary>
    public required IReadOnlyDictionary<string, string?> PrimaryKeyValues { get; init; }

    /// <summary>
    /// 该行所有列值按统一规则序列化拼接后的 MD5 哈希值（十六进制字符串）
    ///</summary>
    public required string Hash { get; init; }

    /// <summary>
    /// 主键的规范字符串表示，用于集合比较时的唯一键（列名按字母排序）
    ///</summary>
    public string PrimaryKeyString =>
        string.Join("|", PrimaryKeyValues.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}"));
}
