# dbsync

DBSyncTool v2 的 Go 实现（Windows 桌面工作台，MySQL 8.0.16 优先）。

> **只读边界**：本工具只做结构与数据比对、SQL 预览；**不执行任何 DDL/DML**，没有 Apply/执行按钮。数据库连接必须使用只读账号；生产代码禁止 `Exec/ExecContext`（`internal/readonly` 测试会拦截）。

## 当前状态

- M1：值模型、MySQL 结构读取、快照 v2（**结构+数据**，默认明文）
- M2：MySQL / SQLite / PostgreSQL / SQL Server 的结构与数据差异；SQL 预览仅 MySQL
- M3：Wails v3 桌面工作台（比较/差异/数据行/SQL 预览、导出源端快照，只读）
- M4（进行中）：本地快照库（列出/使用/导入/标签/备注/删除）、差异报告（MD/HTML/JSON/SARIF）、多目标比较、忽略规则、表级外键 + 四方言视图依赖、脚本目录基础版（CREATE TABLE/VIEW/TRIGGER）
- 单元测试：`go test ./...`
- 集成测试：需要设置 `DBSYNC_TEST_MYSQL_DSN`，并加 `-tags integration`
- CLI 暂不实现

## 本地测试

```powershell
$env:DBSYNC_TEST_MYSQL_DSN = "root:<password>@tcp(10.168.2.187:3306)/?parseTime=true&multiStatements=true&charset=utf8mb4&collation=utf8mb4_bin"
go test -tags integration ./internal/dialect/mysql
```

测试库必须专用、可清理，禁止指向生产。
