# 06: 行哈希指纹计算（SQL Server）

**Status:** ready-for-agent

**Blocked by:** 02 - SQL Server 结构读取器

**What to build:** 实现从 SQL Server 数据库流式读取行数据并计算哈希指纹，按统一规则处理各种列类型，产出可写入 .dbsync 的指纹集合。

- [ ] 实现 `SqlServerDataFingerprinter`，接受连接 + 表元数据 + 可选 WHERE 子句
- [ ] 流式游标查询（禁止 ToList 全量加载），逐行产出 `RowHash`（主键值 + 哈希字符串）
- [ ] WHERE 子句基础安全处理：去除末尾分号，拒绝包含语句分隔符（`;`）的输入
- [ ] 各列类型统一转换规则（在 SQL Server 端完成或 .NET 端完成）：
  - `NULL` → 固定字符串 `'NULL'`
  - `BINARY` / `VARBINARY` → `CONVERT(VARCHAR, col, 2)`（十六进制）
  - `DATETIME` / `DATETIME2` / `DATETIMEOFFSET` → 格式化为 `yyyy-MM-dd HH:mm:ss.fff`（UTC）
  - `FLOAT` / `REAL` → `STR(col, 25, 15)`（15 位有效数字）
  - `BIT` → `CONVERT(CHAR(1), col)`（'0' / '1'）
  - 其他类型 → `CAST(col AS NVARCHAR(MAX))`
- [ ] 哈希函数使用 `HASHBYTES('MD5', concatenated_string)`，分隔符为 `|`
- [ ] 无主键的表：返回空集合并设置 `NoPrimaryKey = true` 标记，不抛异常
- [ ] 集成测试：建含各列类型的测试表，验证相同数据两次计算结果一致
- [ ] 集成测试：NULL 值、边界值（最大长度字符串、极值数字、零日期）的哈希一致性
- [ ] 集成测试：带 WHERE 子句时只处理过滤后的行
