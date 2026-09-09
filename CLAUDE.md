# CLAUDE.md

本文件用于指导在本仓库中工作的自动化代理。

## 项目概览

`DBSyncTool` 是一个基于 `.NET 10` 的数据库同步工具，核心目标是：

1. 从目标库导出基线快照
2. 在源库中加载快照并生成结构或数据差异
3. 输出可执行的升级脚本

当前仓库包含四块主要内容：

- `src/DBSync.Core`：核心领域与算法
- `src/DBSync.Desktop`：Avalonia 桌面端（SukiUI 主题）
- `src/DBSync.CLI`：命令行入口
- `src/DBSync.Tests`：单元测试

## 技术栈

- .NET 10
- Avalonia UI 12
- CommunityToolkit.MVVM
- Easy.SqlSugar.Core
- Easy.Serilog.Core
- Easy.Bogus.Core（仅测试项目）
- xUnit

## 目录说明

- `src/DBSync.Core`
  - `Models`：表、列、索引、外键、快照、差异等模型
  - `Schema`：各数据库的结构读取器
  - `Comparers`：结构对比、数据对比、外键拓扑排序
  - `Data`：行哈希与数据指纹
  - `Snapshot`：快照导出、加载、格式定义、加解密
  - `SqlGenerators`：各数据库方言的 SQL 生成器
  - `Execution`：脚本执行器
  - `Versioning`：应用版本号解析与比较（桌面端更新检查与 CLI 版本显示共用）
  - `Extensions`：核心依赖注入扩展

- `src/DBSync.Desktop`
  - `Views`：主窗口、仪表盘、连接管理、导出快照、加载对比、直连对比、历史记录、设置等界面
  - `ViewModels`：页面视图模型（含 CompareViewModels.cs 共享差异节点/表选择模型）
  - `Services`：窗口、加密、导出、更新检查（UpdateChecker）等 UI 服务
  - `Models`：AppSettings、GitHubRelease 等桌面端配置与数据模型
  - `Storage`：本地配置与连接持久化
  - `Helpers`：IME 输入辅助等工具类

- `src/DBSync.CLI`
  - 支持 `export`、`compare`、`script`、`execute`

- 根目录
  - `Directory.Build.props`：统一版本号源（桌面端 / CLI / 程序集共用）
  - `.github/workflows/release.yml`：tag 触发自动打包与 GitHub Release 发布
  - `docs/release.md`：发布与自动更新说明

## 当前界面状态

- 主界面采用侧边导航（SukiWindow），导航项为独立页面（非 TabControl）
- 侧边栏菜单：仪表盘、连接管理、导出快照、加载对比、直连对比、历史记录、设置
- 连接编辑窗：可调整大小，数据库名称支持 AutoCompleteBox 搜索选择
- 导出快照/加载对比/直连对比：选择连接后自动加载服务器数据库列表（ISchemaReader.ListDatabasesAsync）
- 表选择区域统一使用 DataGrid（非 ListBox），避免虚拟化渐入问题
- 加载对比和直连对比布局一致：表选择三栏 + 分类按钮（全部/不同/源库/目标/数据）+ SQL 类型开关 + 差异列表 + SQL 预览
- 脚本生成按 UI 节点勾选状态组装（所见即所得），支持启用事务和排除自增列
- 直连对比支持结构/结构+数据模式切换（默认仅结构），源库勾选联动目标库同名表
- 导出快照加载表后，预估行数 < 10000 的表自动勾选
- 页面切换时各页面通过 RefreshConnections 刷新连接列表，必须保存并恢复之前选中的连接和数据库
- 桌面端启动时自动检查 GitHub Releases 新版本（UpdateChecker），设置页可配置更新源与代理
- 版本号由 Directory.Build.props 统一管理，发布时由 CI 用 tag 覆盖
- 全局关闭 ListBox 虚拟化（App.axaml），全局禁用 Transitions 动画

## 关键约束

1. 核心层不要引入 Windows 专有 API
2. 导出快照必须流式处理，避免整表读入内存
3. 快照文件中的密码提示只用于提示，不参与加解密
4. 结构对比要遵守基线表在前、目标表在后的方向
5. 不要删除用户未要求清理的方法和注释
6. 保持改动尽量小，优先复用现有模式
7. 表选择区域使用 DataGrid（不要用 ListBox，避免虚拟化渐入问题）
8. SQL 预览和脚本导出保持所见即所得（事务包裹、排除自增列等选项实时反映）
9. 非 Windows 平台需设置 DBSYNC_MASTER_PASSWORD 环境变量（用于连接信息加密）
10. 页面切换时 RefreshConnections 必须保存/恢复选中项，不能让用户选好的连接和数据库丢失

## 常用命令

```bash
dotnet build src/DBSyncTool.slnx
dotnet test src/DBSyncTool.slnx
dotnet run --project src/DBSync.Desktop
dotnet run --project src/DBSync.CLI -- export --connection "..." --output snapshot.dbsync
dotnet run --project src/DBSync.CLI -- compare --snapshot snapshot.dbsync --connection "..."
```

## 代码风格

- 对话、说明、注释统一使用简体中文
- 代码注释也必须是中文
- 生成的提交信息必须是中文
- 保留现有功能，不要顺手清理无关代码

