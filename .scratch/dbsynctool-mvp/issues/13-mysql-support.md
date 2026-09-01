# 13: MySQL 支持

**Status:** ready-for-agent

**Blocked by:** 04 - SQL Server DDL 生成器，06 - 行哈希指纹计算（SQL Server）

**What to build:** 补全 MySQL 的结构读取器、哈希指纹计算和 SQL 生成器，使工具对 MySQL 数据库具备与 SQL Server 相同的功能覆盖度。

- [ ] 实现 `MySqlSchemaReader : ISchemaReader`
  - 从 `INFORMATION_SCHEMA` 读取表、列（含 AUTO_INCREMENT 标记）、主键、外键、索引
  - 集成测试（需真实 MySQL 5.7+ 或 Docker）
- [ ] 实现 `MySqlDataFingerprinter`
  - 哈希算法：`MD5(CONCAT_WS('|', col1, col2, ...))`
  - 各列类型处理规则（与 SQL Server 一致，适配 MySQL 函数语法）：
    - BLOB/BINARY → `HEX(col)`
    - DATETIME/TIMESTAMP → `DATE_FORMAT(CONVERT_TZ(col, @@session.time_zone, '+00:00'), '%Y-%m-%d %H:%i:%s.%f')`
    - FLOAT/DOUBLE → `FORMAT(col, 15)`
    - JSON → `JSON_UNQUOTE(JSON_EXTRACT(col, '$'))` 规范化
    - BOOLEAN/TINYINT(1) → `IF(col, '1', '0')`
  - 集成测试覆盖各列类型哈希一致性
- [ ] 实现 `MySqlSqlGenerator : ISqlGenerator`
  - `CREATE TABLE`（MySQL 方言，含 AUTO_INCREMENT、ENGINE=InnoDB）
  - `DROP TABLE IF EXISTS`
  - `ALTER TABLE`（MySQL 语法）
  - INSERT 语句（MySQL 不需要 IDENTITY_INSERT，直接写入 AUTO_INCREMENT 值）
  - 完整升级脚本含事务控制（`START TRANSACTION / COMMIT / ROLLBACK`）
  - 快照单元测试
