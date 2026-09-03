using DBSync.Core.Models;

namespace DBSync.Core.SqlGenerators;

/// <summary>
/// SQL 语句生成器接口，各数据库方言分别实现
///</summary>
public interface ISqlGenerator
{
    /// <summary>
    /// 根据完整的 SchemaDiff 和 DataDiff 生成 Upgrade.sql 脚本内容
    /// </summary>
    /// <param name="dbType">目标数据库类型</param>
    /// <param name="schemaDiff">结构差异</param>
    /// <param name="dataDiffs">各表的数据差异（表名 → DataDiff）</param>
    /// <param name="fullData">新增表的完整数据（表名 → 行数据列表），可为空</param>
    /// <param name="useTransaction">是否在脚本外层包裹事务</param>
    /// <returns>完整的 Upgrade.sql 脚本字符串</returns>
    string GenerateUpgradeScript(
        DatabaseType dbType,
        SchemaDiff schemaDiff,
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>? fullData = null,
        bool useTransaction = true);

    /// <summary>
    /// 生成单张表的 CREATE TABLE 语句
    /// </summary>
    /// <param name="dbType">目标数据库类型</param>
    /// <param name="table">表元数据</param>
    /// <returns>CREATE TABLE SQL 字符串</returns>
    string GenerateCreateTable(DatabaseType dbType, TableModel table);

    /// <summary>
    /// 生成单张表的 DROP TABLE 语句
    /// </summary>
    /// <param name="dbType">目标数据库类型</param>
    /// <param name="table">表元数据</param>
    /// <returns>DROP TABLE SQL 字符串</returns>
    string GenerateDropTable(DatabaseType dbType, TableModel table);

    /// <summary>
    /// 根据表结构差异生成 ALTER TABLE 语句组
    /// </summary>
    /// <param name="dbType">目标数据库类型</param>
    /// <param name="diff">单张表的结构差异</param>
    /// <returns>ALTER TABLE SQL 语句列表</returns>
    IReadOnlyList<string> GenerateAlterTable(DatabaseType dbType, TableDiff diff);

    /// <summary>
    /// 根据变更行数据生成 UPDATE 语句组
    /// </summary>
    /// <param name="dbType">目标数据库类型</param>
    /// <param name="table">表元数据</param>
    /// <param name="rows">变更行数据列表（列名 → 字符串值）</param>
    /// <returns>UPDATE SQL 语句列表</returns>
    IReadOnlyList<string> GenerateUpdateStatements(
        DatabaseType dbType,
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows);

    /// <summary>
    /// 根据主键值生成 DELETE 语句组
    /// </summary>
    /// <param name="dbType">目标数据库类型</param>
    /// <param name="table">表元数据</param>
    /// <param name="primaryKeyValues">待删除行的主键值列表</param>
    /// <returns>DELETE SQL 语句列表</returns>
    IReadOnlyList<string> GenerateDeleteStatements(
        DatabaseType dbType,
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> primaryKeyValues);

    /// <summary>
    /// 根据完整行数据生成 INSERT 语句组
    /// </summary>
    /// <param name="dbType">目标数据库类型</param>
    /// <param name="table">表元数据</param>
    /// <param name="rows">行数据列表（列名 → 字符串值，null 表示 NULL）</param>
    /// <returns>INSERT SQL 语句列表</returns>
    IReadOnlyList<string> GenerateInsertStatements(
        DatabaseType dbType,
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows);
}
