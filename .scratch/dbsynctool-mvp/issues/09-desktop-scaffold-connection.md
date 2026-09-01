# 09: Avalonia 桌面脚手架 + 连接管理

**Status:** resolved

**Blocked by:** 01 - 解决方案脚手架 + 核心领域模型

**What to build:** 搭建 Avalonia UI 桌面应用骨架，实现数据库连接的增删改查和加密存储。完成后导出流程（票10）和比对流程（票11）均可开工。

- [x] 配置 Avalonia UI 项目，集成 CommunityToolkit.MVVM
- [x] 注册 Easy.Serilog.Core，输出日志到文件和控制台
- [x] 主窗口布局：顶部标题栏 + 中央两个大入口按钮（"导出快照 - 在生产机运行"、"加载快照并比对 - 在测试机运行"）+ 底部状态栏（当前操作状态 + 日志摘要）
- [x] 连接管理页面：列出已保存的连接（名称、数据库类型、服务器地址）、支持新增/编辑/删除/测试连接
- [x] 连接信息加密存储：Windows 使用 `ProtectedData`（DPAPI），其他平台切换为用户主密码 + AES-GCM（当前通过 `DBSYNC_MASTER_PASSWORD` 初始化）
- [x] 连接测试：点击"测试连接"按钮，通过 Easy.SqlSugar.Core 验证连接可用性，显示成功/失败提示
- [x] 行数阈值配置入口：在主窗口内提供数值输入框（默认 100,000），持久化到本地配置文件
- [x] 应用退出时确认未完成操作

## 答案

已新增 `DBSync.Desktop`：

- 使用 Avalonia + CommunityToolkit.MVVM 搭建桌面壳。
- 主窗口包含顶部标题、两个入口按钮、连接列表、状态栏和行数阈值配置。
- 已接入 `Easy.Serilog.Core`。
- 已实现连接本地保存与加密：Windows 走 DPAPI，其他平台走 AES-GCM。
- 已实现连接测试按钮，复用 `ISchemaReader.TestConnectionAsync`。
- 已实现退出确认对话框，未完成操作时会拦截关闭。

验证结果：

- `dotnet build 'src\DBSyncTool.slnx' --no-restore`：通过。
- `dotnet test 'src\DBSyncTool.slnx' --no-restore`：通过，41 通过、1 跳过。
