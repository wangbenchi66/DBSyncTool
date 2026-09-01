namespace DBSync.Core.Models;

/// <summary>
/// 数据库列的通用类型分类，用于哈希规则的类型分支
///</summary>
public enum DbColumnType
{
    /// <summary>文本/字符串类型（CHAR、VARCHAR、NVARCHAR 等）</summary>
    Text,
    /// <summary>整数类型（INT、BIGINT、SMALLINT 等）</summary>
    Integer,
    /// <summary>精确小数类型（DECIMAL、NUMERIC）</summary>
    Decimal,
    /// <summary>浮点类型（FLOAT、REAL、DOUBLE）</summary>
    Float,
    /// <summary>布尔类型（BIT、BOOLEAN、TINYINT(1)）</summary>
    Boolean,
    /// <summary>日期时间类型（DATETIME、TIMESTAMP、DATE 等）</summary>
    DateTime,
    /// <summary>二进制类型（BINARY、VARBINARY、BLOB）</summary>
    Binary,
    /// <summary>JSON 类型</summary>
    Json,
    /// <summary>XML 类型</summary>
    Xml,
    /// <summary>其他或未知类型</summary>
    Other
}

/// <summary>
/// 数据库表的列定义
///</summary>
public sealed record ColumnModel
{
    /// <summary>
    /// 列名
    ///</summary>
    public required string Name { get; init; }

    /// <summary>
    /// 数据库原生类型名称（如 "nvarchar"、"int"）
    ///</summary>
    public required string DbTypeName { get; init; }

    /// <summary>
    /// 通用列类型枚举，用于哈希规则路由
    ///</summary>
    public required DbColumnType ColumnType { get; init; }

    /// <summary>
    /// 最大字符长度（仅文本类型有效）
    ///</summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// 数值精度（仅 DECIMAL/NUMERIC 有效）
    ///</summary>
    public int? Precision { get; init; }

    /// <summary>
    /// 数值小数位数（仅 DECIMAL/NUMERIC 有效）
    ///</summary>
    public int? Scale { get; init; }

    /// <summary>
    /// 列是否允许 NULL
    ///</summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// 列的默认值表达式（原始 SQL 字符串）
    ///</summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// 是否为 SQL Server IDENTITY 列
    ///</summary>
    public bool IsIdentity { get; init; }

    /// <summary>
    /// 是否为 MySQL/PostgreSQL AUTO_INCREMENT 或 SERIAL 列
    ///</summary>
    public bool IsAutoIncrement { get; init; }

    /// <summary>
    /// 列在表中的序号位置（从 1 开始）
    ///</summary>
    public int OrdinalPosition { get; init; }
}
