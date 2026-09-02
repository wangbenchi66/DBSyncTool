# 14: PostgreSQL + SQLite 支持

**Status:** done

**Blocked by:** 04 - SQL Server DDL 生成器，06 - 行哈希指纹计算（SQL Server）

**What to build:** 补全 PostgreSQL 和 SQLite 的结构读取器、哈希指纹计算和 SQL 生成器。SQLite 的哈希在 .NET 端计算（数据库内置函数不足）。

### PostgreSQL

- [x] 实现 `PostgresSchemaReader : ISchemaReader`
  - 从 `INFORMATION_SCHEMA` 和 `pg_catalog` 读取表、列（含 SERIAL/IDENTITY 标记）、主键、外键、索引
  - 集成测试（需真实 PostgreSQL 10+ 或 Docker）
- [x] 实现 `PostgresDataFingerprinter`
  - 哈希算法：`MD5(CONCAT_WS('|', col1::text, col2::text, ...))`
  - 各列类型处理：BYTEA → `encode(col, 'hex')`；TIMESTAMP → `to_char(col AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS.MS')`；JSONB → 规范化后转字符串
  - 集成测试覆盖各列类型
- [x] 实现 `PostgresSqlGenerator : ISqlGenerator`
  - PostgreSQL DDL 方言（`CREATE TABLE`、`ALTER TABLE`，SERIAL/IDENTITY 列声明）
  - INSERT 语句含 `OVERRIDING SYSTEM VALUE` 以覆盖 IDENTITY 列值
  - 事务控制：`BEGIN / COMMIT / ROLLBACK`
  - 快照单元测试

### SQLite

- [x] 实现 `SqliteSchemaReader : ISchemaReader`
  - 从 `sqlite_master` + `PRAGMA table_info` 读取表结构
  - SQLite 无外键约束信息（通过 `PRAGMA foreign_key_list` 读取）
  - 集成测试（SQLite 文件直连，无需 Docker）
- [x] 实现 `SqliteDataFingerprinter`
  - SQLite 无内置 MD5，在 .NET 端读取行数据后使用 `System.Security.Cryptography.MD5` 计算哈希
  - 各列类型统一序列化为字符串后在 .NET 端拼接哈希
  - 集成测试覆盖各列类型
- [x] 实现 `SqliteSqlGenerator : ISqlGenerator`
  - SQLite DDL 方言（`CREATE TABLE`，SQLite 无 ALTER TABLE MODIFY COLUMN，需重建表）
  - INSERT 语句
  - 事务控制：`BEGIN / COMMIT / ROLLBACK`
  - 快照单元测试
