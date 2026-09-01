# 05: 快照导出 / 加载 + AES-256 加密

**Status:** ready-for-agent

**Blocked by:** 02 - SQL Server 结构读取器

**What to build:** 实现 `.dbsync` 文件的写入和读取，含 AES-256 加密/解密和流式处理。完成后桌面导出流程（票10）和比对流程（票11）均可开工。

- [ ] 实现 `SnapshotExporter : ISnapshotExporter`
  - 接收数据库连接 + 选定表列表 + 导出选项（含 passwordHint）
  - 创建 ZIP 流，实时写入 `manifest.json`（含 passwordHint 明文字段）
  - 流式写入每张表的 `schema/<tableName>.json`（结构定义）
  - 流式写入 `data_fingerprint/<tableName>.fp`（GZip 压缩的 JSON Lines 格式行哈希）
  - 对选择了完整数据的新增表，流式写入 `data_full/<tableName>.csv.gz`
  - ZIP 流最终以 AES-256 加密写入文件（密钥从用户密码 PBKDF2 派生，IV 随机生成并写入文件头）
  - 支持取消令牌（CancellationToken）
- [ ] 实现 `SnapshotLoader : ISnapshotLoader`
  - 读取文件头获取 IV，用密码派生密钥解密
  - 解析 `manifest.json`，返回 passwordHint（若存在）
  - 按需懒加载各表的结构 JSON 和指纹文件
- [ ] 往返一致性集成测试：导出 → 加载，验证 manifest、表结构、指纹数据完全一致
- [ ] 测试：错误密码时抛出明确异常（非通用 IO 错误）
- [ ] 测试：大文件流式处理，内存峰值不随数据量线性增长（监控内存不超过固定阈值）
