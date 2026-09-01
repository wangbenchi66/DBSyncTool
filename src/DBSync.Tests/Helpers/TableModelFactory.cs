using DBSync.Core.Models;

namespace DBSync.Tests.Helpers;

/// <summary>
/// 用于测试的 TableModel 构造工厂，提供最简化的默认值
///</summary>
internal static class TableModelFactory
{
    /// <summary>
    /// 创建只有主键列的最简单表模型
    /// </summary>
    /// <param name="name">表名</param>
    /// <param name="schema">Schema 名，默认 dbo</param>
    /// <returns>TableModel 实例</returns>
    internal static TableModel Simple(string name, string schema = "dbo") =>
        new()
        {
            Name = name,
            Schema = schema,
            Columns = [IdColumn()],
            PrimaryKeyColumns = ["Id"],
            ForeignKeys = [],
            Indexes = []
        };

    /// <summary>
    /// 创建带指定列集合的表模型
    /// </summary>
    /// <param name="name">表名</param>
    /// <param name="columns">列定义列表</param>
    /// <param name="pkColumns">主键列名列表</param>
    /// <param name="foreignKeys">外键列表，默认为空</param>
    /// <param name="schema">Schema 名，默认 dbo</param>
    /// <returns>TableModel 实例</returns>
    internal static TableModel WithColumns(
        string name,
        IReadOnlyList<ColumnModel> columns,
        IReadOnlyList<string>? pkColumns = null,
        IReadOnlyList<ForeignKeyModel>? foreignKeys = null,
        IReadOnlyList<IndexModel>? indexes = null,
        string schema = "dbo") =>
        new()
        {
            Name = name,
            Schema = schema,
            Columns = columns,
            PrimaryKeyColumns = pkColumns ?? ["Id"],
            ForeignKeys = foreignKeys ?? [],
            Indexes = indexes ?? []
        };

    /// <summary>
    /// 创建无主键的表模型（用于测试跳过数据比对的场景）
    /// </summary>
    /// <param name="name">表名</param>
    /// <returns>TableModel 实例（PrimaryKeyColumns 为空）</returns>
    internal static TableModel NoPrimaryKey(string name) =>
        new()
        {
            Name = name,
            Schema = "dbo",
            Columns = [Col("Col1", DbColumnType.Text)],
            PrimaryKeyColumns = [],
            ForeignKeys = [],
            Indexes = []
        };

    /// <summary>
    /// 创建标准 INT 主键列 Id
    ///</summary>
    /// <returns>ColumnModel 实例</returns>
    internal static ColumnModel IdColumn() =>
        Col("Id", DbColumnType.Integer, isIdentity: true);

    /// <summary>
    /// 创建列模型
    /// </summary>
    /// <param name="name">列名</param>
    /// <param name="type">列类型</param>
    /// <param name="isNullable">是否可空，默认 false</param>
    /// <param name="maxLength">最大长度</param>
    /// <param name="isIdentity">是否为 IDENTITY 列</param>
    /// <returns>ColumnModel 实例</returns>
    internal static ColumnModel Col(
        string name,
        DbColumnType type,
        bool isNullable = false,
        int? maxLength = null,
        bool isIdentity = false) =>
        new()
        {
            Name = name,
            DbTypeName = type.ToString().ToLowerInvariant(),
            ColumnType = type,
            IsNullable = isNullable,
            MaxLength = maxLength,
            IsIdentity = isIdentity,
            OrdinalPosition = 1
        };

    /// <summary>
    /// 创建外键模型
    /// </summary>
    /// <param name="columnName">本表的外键列名</param>
    /// <param name="referencedTable">被引用的目标表名</param>
    /// <param name="referencedColumn">目标表中的列名，默认 Id</param>
    /// <returns>ForeignKeyModel 实例</returns>
    internal static ForeignKeyModel Fk(
        string columnName,
        string referencedTable,
        string referencedColumn = "Id") =>
        new()
        {
            Name = $"FK_{columnName}_{referencedTable}",
            ColumnName = columnName,
            ReferencedTable = referencedTable,
            ReferencedColumn = referencedColumn
        };

    /// <summary>
    /// 创建索引模型。
    /// </summary>
    /// <param name="name">索引名</param>
    /// <param name="columnNames">索引列名列表</param>
    /// <param name="isUnique">是否唯一索引</param>
    /// <param name="isPrimaryKey">是否主键索引</param>
    /// <returns>IndexModel 实例</returns>
    internal static IndexModel Index(
        string name,
        IReadOnlyList<string> columnNames,
        bool isUnique = false,
        bool isPrimaryKey = false) =>
        new()
        {
            Name = name,
            ColumnNames = columnNames,
            IsUnique = isUnique,
            IsPrimaryKey = isPrimaryKey
        };
}
