namespace DBSync.Core.Models;

/// <summary>
/// 数据库对象类型枚举
///</summary>
public enum DatabaseObjectType
{
    /// <summary>视图</summary>
    View,
    /// <summary>存储过程</summary>
    StoredProcedure,
    /// <summary>函数</summary>
    Function,
    /// <summary>触发器</summary>
    Trigger
}

/// <summary>
/// 通用数据库对象模型（视图、存储过程、函数、触发器）
///</summary>
public sealed record DatabaseObjectModel
{
    /// <summary>
    /// 对象类型
    ///</summary>
    public DatabaseObjectType ObjectType { get; init; }

    /// <summary>
    /// Schema 名称
    ///</summary>
    public string Schema { get; init; } = "";

    /// <summary>
    /// 对象名称
    ///</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// 完整名称（Schema.Name）
    ///</summary>
    public string FullName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";

    /// <summary>
    /// SQL 定义文本
    ///</summary>
    public string SqlDefinition { get; init; } = "";

    /// <summary>
    /// 对象注释
    ///</summary>
    public string? Comment { get; init; }
}

/// <summary>
/// 通用数据库对象差异
///</summary>
public sealed record ObjectDiff
{
    /// <summary>
    /// 差异类型
    ///</summary>
    public ObjectDiffType DiffType { get; init; }

    /// <summary>
    /// 对象类型
    ///</summary>
    public DatabaseObjectType ObjectType { get; init; }

    /// <summary>
    /// 对象名称
    ///</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// 源端 SQL 定义
    ///</summary>
    public string? SourceSql { get; init; }

    /// <summary>
    /// 目标端 SQL 定义
    ///</summary>
    public string? TargetSql { get; init; }
}

/// <summary>
/// 对象差异类型
///</summary>
public enum ObjectDiffType
{
    /// <summary>新增（仅源端有）</summary>
    Added,
    /// <summary>删除（仅目标端有）</summary>
    Removed,
    /// <summary>修改（两端都有但定义不同）</summary>
    Modified
}
