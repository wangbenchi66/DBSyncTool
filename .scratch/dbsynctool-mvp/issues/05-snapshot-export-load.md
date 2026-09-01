# 05: 快照导出 / 加载 + AES-256 加密

**Status:** resolved

**Blocked by:** 02 - SQL Server 结构读取器

**What to build:** 实现 `.dbsync` 文件的写入和读取，含 AES-256 加密/解密和流式处理。完成后桌面导出流程（票10）和比对流程（票11）均可开工。

- [x] 实现 `SnapshotExporter : ISnapshotExporter`
  - 接收数据库连接 + 选定表列表 + 导出选项（含 passwordHint）
  - 创建 ZIP 流，实时写入 `manifest.json`（含 passwordHint 明文字段）
  - 流式写入每张表的 `schema/<tableName>.json`（结构定义）
  - 流式写入 `data_fingerprint/<tableName>.fp`（GZip 压缩的 JSON Lines 格式行哈希）
  - 对选择了完整数据的新增表，流式写入 `data_full/<tableName>.csv.gz`
  - ZIP 流最终以 AES-256 加密写入文件（密钥从用户密码 PBKDF2 派生，IV 随机生成并写入文件头）
  - 支持取消令牌（CancellationToken）
- [x] 实现 `SnapshotLoader : ISnapshotLoader`
  - 读取文件头获取 IV，用密码派生密钥解密
  - 解析 `manifest.json`，返回 passwordHint（若存在）
  - 按当前接口加载各表的结构 JSON、指纹文件和完整数据文件
- [x] 往返一致性集成测试：导出 → 加载，验证 manifest、表结构、指纹数据完全一致
- [x] 测试：错误密码时抛出明确异常（非通用 IO 错误）
- [x] 大文件流式处理：导出和文件解析使用流式读写；按当前 `Snapshot` 模型，调用 `LoadAsync` 后结果集合会驻留内存

## 答案

已实现 `.dbsync` 快照导出和加载：

- 新增 `SnapshotExporter`，导出文件头、加密 ZIP、`manifest.json`、`schema/*.json`、`data_fingerprint/*.fp`，并在选择完整数据时流式写入 `data_full/*.csv.gz`。
- 新增 `SnapshotLoader`，支持读取明文密码提示、AES-256-CBC 解密、加载 manifest、表结构、行指纹和完整数据。
- 新增 `SnapshotFileFormat`，文件头包含 magic、salt、IV、passwordHint 长度和明文提示；密钥使用 PBKDF2-SHA256 派生。
- 快照导出时间使用本地时区 `DateTimeOffset.Now`，适配中国时间环境。
- 已将 `ISnapshotExporter`、`ISnapshotLoader` 注册到核心 DI。
- 按当前 `ISnapshotLoader` 契约，加载阶段返回完整 `Snapshot` 对象；未额外扩展懒加载 API，因此返回结果本身会占用对应数据量的内存。

验证结果：

- `dotnet build 'src\DBSyncTool.slnx' --no-restore`：通过，保留既有 `SQLitePCLRaw.lib.e_sqlite3 2.1.10` 高危漏洞警告。
- `dotnet test 'src\DBSyncTool.slnx' --no-restore`：通过，33 通过、1 跳过。
- 跳过项为 LocalDB 集成测试；本机 `MSSQLLocalDB` 自动实例不可用，Docker 引擎未运行，因此真实 SQL Server 集成验证未执行。
