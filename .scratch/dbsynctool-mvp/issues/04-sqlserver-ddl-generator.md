# 04: SQL Server DDL 生成器

**Status:** ready-for-agent

**Blocked by:** 02 - SQL Server 结构读取器，03 - Schema 比较器 + 外键拓扑排序

**What to build:** 实现 `SqlServerSqlGenerator` 的 DDL 部分，将 `SchemaDiff` 转换为按正确顺序排列的 SQL DDL 语句。完成后 INSERT 脚本（票08）和多数据库扩展（票13/14）可以开工。

- [ ] 实现 `SqlServerSqlGenerator : ISqlGenerator` 的 DDL 生成方法
- [ ] `GenerateCreateTable(TableModel)` → 完整 `CREATE TABLE` 语句，含列定义、主键约束、外键约束、IDENTITY 列声明
- [ ] `GenerateDropTable(TableModel)` → `DROP TABLE IF EXISTS` 语句
- [ ] `GenerateAlterTable(TableDiff)` → 一组 `ALTER TABLE` 语句（ADD COLUMN、DROP COLUMN、ALTER COLUMN、ADD/DROP CONSTRAINT、ADD/DROP INDEX）
- [ ] 整合拓扑排序：`GenerateDdlScript(SchemaDiff)` 返回完整的有序 DDL 语句序列
- [ ] `DROP TABLE` 按拓扑逆序排列；循环依赖组的表附带注释说明需手动处理
- [ ] 脚本头部生成注释块（生成时间、工具版本、影响表数量）
- [ ] 单元测试：给定已知 `SchemaDiff`，验证输出 SQL 语义正确（快照测试）
- [ ] 测试覆盖：含 IDENTITY 列的新增表、含外键的新增表、列类型变更的 ALTER、含循环依赖的场景
