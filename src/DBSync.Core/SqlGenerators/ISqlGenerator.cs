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
    /// <param name="schemaDiff">结构差异</param>
    /// <param name="dataDiffs">各表的数据差异（表名 → DataDiff）</param>
    /// <param name="fullData">新增表的完整数据（表名 → 行数据列表），可为空</param>
    /// <returns>完整的 Upgrade.sql 脚本字符串</returns>
    string GenerateUpgradeScript(
        SchemaDiff schemaDiff,
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>? fullData = null);

    /// <summary>
    /// 生成单张表的 CREATE TABLE 语句
    /// </summary>
    /// <param name="table">表元数据</param>
    /// <returns>CREATE TABLE SQL 字符串</returns>
    string GenerateCreateTable(TableModel table);

    /// <summary>
    /// 生成单张表的 DROP TABLE 语句
    /// </summary>
    /// <param name="table">表元数据</param>
    /// <returns>DROP TABLE SQL 字符串</returns>
    string GenerateDropTable(TableModel table);

    /// <summary>
    /// 根据表结构差异生成 ALTER TABLE 语句组
    /// </summary>
    /// <param name="diff">单张表的结构差异</param>
    /// <returns>ALTER TABLE SQL 语句列表</returns>
    IReadOnlyList<string> GenerateAlterTable(TableDiff diff);

    /// <summary>
    /// 根据完整行数据生成 INSERT 语句组
    /// </summary>
    /// <param name="table">表元数据</param>
    /// <param name="rows">行数据列表（列名 → 字符串值，null 表示 NULL）</param>
    /// <returns>INSERT SQL 语句列表</returns>
    IReadOnlyList<string> GenerateInsertStatements(
        TableModel table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows);
}
