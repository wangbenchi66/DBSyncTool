# dbsync

DBSyncTool v2 的 Go 实现（Windows 桌面工作台，MySQL 8.0.16 优先）。

## 当前状态

- M1：值模型、MySQL 结构读取、快照 v2（schema-only，默认明文）
- 单元测试：`go test ./...`
- 集成测试：需要设置 `DBSYNC_TEST_MYSQL_DSN`，并加 `-tags integration`
- CLI 暂不实现；GUI 在 M3 接入 Wails v3

## 本地测试

```powershell
$env:DBSYNC_TEST_MYSQL_DSN = "root:<password>@tcp(10.168.2.187:3306)/?parseTime=true&multiStatements=true&charset=utf8mb4&collation=utf8mb4_bin"
go test -tags integration ./internal/dialect/mysql
```

测试库必须专用、可清理，禁止指向生产。
