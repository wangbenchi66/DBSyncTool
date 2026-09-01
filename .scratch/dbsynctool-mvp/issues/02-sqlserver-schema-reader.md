# 02: SQL Server 结构读取器

**Status:** ready-for-agent

**Blocked by:** 01 - 解决方案脚手架 + 核心领域模型

**What to build:** 实现 `SqlServerSchemaReader`，从 SQL Server 数据库读取完整的表结构元数据并映射为领域模型。完成后快照导出（票05）和哈希指纹（票06）都可以开工。

- [ ] 实现 `SqlServerSchemaReader : ISchemaReader`，通过 Easy.SqlSugar.Core 连接 SQL Server
- [ ] 查询 `INFORMATION_SCHEMA.TABLES` 获取表列表
- [ ] 查询 `INFORMATION_SCHEMA.COLUMNS` 获取列信息（名称、数据类型、长度/精度、是否可空、默认值、是否为 IDENTITY 列）
- [ ] 查询 `INFORMATION_SCHEMA.TABLE_CONSTRAINTS` + `KEY_COLUMN_USAGE` 获取主键列
- [ ] 查询系统表（`sys.foreign_keys`、`sys.foreign_key_columns`）获取外键关系（用于拓扑排序）
- [ ] 查询 `sys.indexes` / `sys.index_columns` 获取非主键索引（索引名、列、唯一性、是否聚集）
- [ ] 结果映射为 `TableModel` 列表
- [ ] 集成测试：连接真实 SQL Server（本地或 Docker），创建含各种列类型和约束的测试表，验证读取结果与预期一致
- [ ] 集成测试覆盖：有外键关系的表、有复合主键的表、无主键的表
