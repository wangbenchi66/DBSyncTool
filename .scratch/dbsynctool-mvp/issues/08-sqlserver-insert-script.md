# 08: SQL Server INSERT 脚本 + 事务包装

**Status:** ready-for-agent

**Blocked by:** 04 - SQL Server DDL 生成器，07 - 数据比较器（仅 INSERT 逻辑）

**What to build:** 扩展 `SqlServerSqlGenerator`，将 `DataDiff` 中的新增行转换为 INSERT 语句，并将完整升级脚本包裹在事务中输出为可执行的 Upgrade.sql。

- [ ] 扩展 `SqlServerSqlGenerator`，实现 `GenerateInsertStatements(TableModel table, IEnumerable<RowValues> rows) → string`
- [ ] 对含 IDENTITY 列的表，INSERT 语句前后自动包裹 `SET IDENTITY_INSERT [table] ON/OFF`
- [ ] 实现 `GenerateUpgradeScript(SchemaDiff schemaDiff, IReadOnlyDictionary<string, DataDiff> dataDiffs) → string`，产出完整 Upgrade.sql：
  - 脚本头部注释块：生成时间、工具版本、涉及表数量、预计影响行数
  - DDL 语句（按拓扑顺序）
  - INSERT 语句（按外键依赖顺序，先插入父表数据）
  - 整体包裹在 `BEGIN TRANSACTION / COMMIT / ROLLBACK` 中，含 `SET XACT_ABORT ON`
- [ ] 单元测试：给定已知差异，验证输出脚本包含正确的 INSERT 语句和事务控制
- [ ] 测试覆盖：含 IDENTITY 列的表、多表按外键顺序插入、空数据差异（只有 DDL）
