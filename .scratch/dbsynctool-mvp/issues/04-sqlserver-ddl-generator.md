# 04: SQL Server DDL 生成器

**Status:** resolved

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

## 答案

已实现 `SqlServerSqlGenerator : ISqlGenerator` 的 DDL 部分：

- 支持 `CREATE TABLE`，包含列定义、主键、外键、IDENTITY、默认值和非主键索引。
- 支持 `DROP TABLE IF EXISTS`。
- 支持 `ALTER TABLE` 的新增列、删除列、修改列、主键约束变更和索引新增/删除/修改。
- `GenerateDdlScript` 返回完整有序 DDL 序列，DROP TABLE 按拓扑逆序排列。
- `GenerateUpgradeScript` 生成脚本头部注释、循环依赖提示、删除表警告，并按外键拓扑顺序输出新增表。
- 删除表默认只生成警告注释，不进入 Upgrade.sql，避免误删数据。
- `GenerateInsertStatements` 保留为空实现，INSERT 逻辑按票 08 处理。

验证命令：

```powershell
dotnet test 'src\DBSyncTool.slnx' --no-restore
```

结果：26 个测试通过，1 个 LocalDB 集成测试因当前环境不可用而跳过。
