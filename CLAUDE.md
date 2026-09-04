# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

DBSyncTool 是一款跨平台桌面工具，用于在网络隔离环境之间通过"基线快照"实现数据库结构/数据差异比对与离线同步。

**核心工作流程（两阶段）：**
1. **导出基线**：在目标库（生产）导出 `.dbsync` 快照（结构元数据 + 行哈希指纹）
2. **比对生成**：在源库（测试）加载快照，比对差异，生成 `Upgrade.sql` + `Rollback.sql`

### 编码行为准则（Karpathy Guidelines）

每次编写、修改或审查代码时，**始终**遵守以下四条原则（来源：`.claude/skills/karpathy-guidelines.md`）：

1. **先思考再动手** — 明确说明假设；有歧义时先问，不默默选择；有更简单方案时主动提出。
2. **简洁优先** — 只写解决问题所需的最少代码；不做未被要求的功能、抽象或"灵活性"扩展；200 行能写成 50 行就重写。
3. **外科手术式修改** — 只改必须改的地方；不"顺手优化"无关代码；匹配现有风格；发现无关死代码时提及但不删除。
4. **目标驱动执行** — 将任务转化为可验证目标（如"写一个能复现此 bug 的测试，再让它通过"）；多步任务先列出验证点再执行。

## 技术选型

| 层级 | 技术 |
|------|------|
| 框架 | .NET 10 |
| UI | **Avalonia UI 12.x** + CommunityToolkit.MVVM 8.x（跨平台，当前主要在 Windows 上开发）|
| ORM/数据库访问 | **Easy.SqlSugar.Core**（自研，基于 SqlSugar，天然支持 SQL Server、MySQL、PostgreSQL、SQLite 等）|
| 缓存 | **Easy.Cache.Core**（自研）|
| 日志 | **Easy.Serilog.Core**（自研）|
| 测试数据生成 | **Easy.Bogus.Core**（自研）|
| 序列化 | System.Text.Json |
| 压缩/加密 | System.IO.Compression.ZipArchive + System.Security.Cryptography.Aes（AES-256）|
| 依赖注入 | Microsoft.Extensions.DependencyInjection + Hosting |
| 测试 | xUnit |

> **平台策略**：当前主要在 Windows 上开发，但 Avalonia 天然跨平台，`DBSync.Core` 层不得引入任何 Windows 专有 API。

## 解决方案结构

```
src/
├── DBSync.Core/               # 核心引擎类库（无 UI 依赖）
│   ├── Models/                # TableModel、ColumnModel、IndexModel、RowHash、SchemaDiff、DataDiff 等
│   ├── Schema/                # ISchemaReader 接口 + DatabaseSchemaReader 分发器 + 各数据库实现
│   ├── Comparers/             # SchemaComparer（结构对比）、DataComparer（数据对比）、FkTopologicalSorter
│   ├── Data/                  # IDataFingerprinter 接口 + 各数据库行哈希读取实现
│   ├── Snapshot/              # 快照导出器、加载器、AES 加解密
│   ├── SqlGenerators/         # ISqlGenerator 接口 + 各数据库方言 DDL+DML 实现
│   ├── Execution/             # IScriptExecutor 脚本执行引擎
│   ├── Extensions/            # AddDbSyncCore() 等扩展方法
│   └── DbDialectSupport.cs   # 列类型映射、标识符转义、CSV 处理
├── DBSync.Desktop/            # Avalonia UI 桌面应用（MVVM，跨平台）
│   ├── ViewModels/            # CommunityToolkit.Mvvm 的 ObservableObject 派生类
│   ├── Views/                 # AXAML 视图（含 DataTemplate 映射）
│   ├── Services/              # 对话框、加密、窗口提供者等 UI 服务
│   ├── Storage/               # JSON 设置持久化、加密连接存储、项目文件读写
│   └── Models/                # AppSettings、RecentHistoryItem
├── DBSync.CLI/                # 命令行工具（程序集名 dbsync）
│   └── Program.cs             # export / compare / script / execute 四个子命令
└── DBSync.Tests/              # 单元测试（xUnit + Easy.Bogus.Core）
```

### 核心接口分发模式

`ISchemaReader`、`ISqlGenerator`、`IDataFingerprinter` 均采用**分发器 + 方言实现**模式：`DatabaseSchemaReader`/`DatabaseSqlGenerator`/`DatabaseDataFingerprinter` 根据 `DatabaseType` 枚举路由到对应的 SqlServer/MySql/Postgres/Sqlite 实现类。新增数据库方言时需同时实现三个接口并在分发器中注册。

### Desktop MVVM 架构

- `MainWindowViewModel` 管理 5 个导航页：仪表盘、连接管理、同步工作台、历史记录、设置
- `SyncWorkflowViewModel` 是薄包装层，组合三个子 ViewModel：`ExportViewModel`（导出快照）、`CompareViewModel`（快照比对）、`DirectCompareViewModel`（直连比对）
- 所有页 ViewModel 实现 `IPageViewModel`（`StatusText` + `LogSummary`），状态向上转发到 `MainWindowViewModel`
- DI 注册在 `DBSync.Desktop/Extensions/ServiceCollectionExtensions.cs`，所有 ViewModel 注册为 Singleton
- 入口 `Program.cs` 使用 `Host.CreateDefaultBuilder` 构建宿主，通过 `App.Services` 静态属性暴露 `IServiceProvider`

### 设计体系

`App.axaml` 定义了 Brand/Ink 色阶和公共样式（panel、toolbar、button variants: primary/success/secondary/ghost/danger、badge-* 等），所有视图通过 DynamicResource 引用。

## 常用命令

```bash
# 构建整个解决方案
dotnet build

# 运行所有测试
dotnet test

# 运行单个测试类/方法
dotnet test --filter "ClassName=SchemaComparerTests"
dotnet test --filter "FullyQualifiedName~MethodName"

# 运行桌面应用
dotnet run --project src/DBSync.Desktop

# 运行 CLI 工具
dotnet run --project src/DBSync.CLI -- export --connection "Server=..." --output snapshot.dbsync
dotnet run --project src/DBSync.CLI -- compare --snapshot snapshot.dbsync --connection "Server=..."

# 发布（自包含单文件）
dotnet publish src/DBSync.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### CLI 退出码

| 退出码 | 含义 |
|--------|------|
| 0 | 成功 / 无差异 |
| 1 | 存在差异 |
| 2 | 错误 |

## 架构关键约束

### 流式处理（必须遵守）
导出基线时**禁止**调用 SqlSugar 的 `ToList()` 全量加载。必须使用游标查询（`AsSugarList` + 分页）或 `ExecuteReaderAsync` 逐行读取，实时写入 `ZipArchive` + `GZipStream`，确保千万级数据表导出内存稳定。

### 快照文件格式（.dbsync）
实质是 AES-256 加密的 ZIP 包，内部结构：
- `manifest.json`：版本、数据库类型、导出时间、表列表
- `schema/*.json`：每张表的结构定义
- `data_fingerprint/*.fp`：主键 + 哈希指纹（GZip 压缩的 JSON Lines）
- `data_full/*.csv.gz`：仅新增表的完整数据（可选）

### 哈希指纹计算
NULL 值统一表示为字符串 `'NULL'` 以避免歧义。各数据库哈希方式：
- **SQL Server**：`HASHBYTES('MD5', CONCAT(col1, col2, ...))`
- **MySQL/PostgreSQL**：`MD5(CONCAT_WS('|', col1, col2, ...))`
- **SQLite**：在 .NET 端计算（内置函数不足）

### 数据库抽象
所有数据库操作通过 `ISchemaReader` 和 `ISqlGenerator` 接口隔离方言差异。Easy.SqlSugar.Core 作为统一访问层在 Core 层直接使用，禁止的是在接口实现之外散落 Provider 特定的 SQL 方言逻辑。

### 数据同步仅生成 INSERT（新增行）
数据比对结果**只生成 `INSERT` 语句**，处理"基线有、源库无"的新增行。**不生成** `UPDATE` 和 `DELETE`，不生成回滚脚本。这是有意限制，避免误删生产数据。

### 外键拓扑排序（必须遵守）
生成 `CREATE TABLE` / `DROP TABLE` 语句时，必须先构建外键依赖图，按拓扑顺序排列：
- `CREATE TABLE`：被依赖表先建（父表优先）
- `DROP TABLE`：依赖他人的表先删（子表优先）

检测到循环依赖时，提示用户手动处理并跳过该组表。

### 特殊列类型的哈希处理
生成行哈希指纹时，各类型统一转换为字符串后再参与哈希，规则如下：
- `NULL`：统一表示为字符串 `'NULL'`
- `BINARY` / `VARBINARY` / `BLOB`：转为十六进制字符串（`HEX()`）
- `DATETIME` / `TIMESTAMP`：统一格式化为 `yyyy-MM-dd HH:mm:ss.fff`（UTC）
- `FLOAT` / `DOUBLE`：格式化为固定精度字符串（15 位有效数字），避免浮点差异
- `JSON` / `XML`：规范化（去除无意义空白）后再哈希
- `BOOLEAN`：统一为 `'0'` / `'1'`

### 行数阈值
新增表导出完整数据时的警告阈值由**用户在界面中配置**（默认 10 万行），超出阈值弹出确认对话框。

### 密码提示
`.dbsync` 文件的 `manifest.json` 中支持存储一条**明文密码提示**（`passwordHint` 字段），帮助两端协作时回忆密码，但提示本身不参与加解密。用户导出时可选填，导入时工具显示该提示。

## Agent skills

### Issue tracker

Issue 以本地 Markdown 文件形式存放在 `.scratch/` 目录下。详见 `docs/agents/issue-tracker.md`。

### Domain docs

单上下文布局：根目录 `CONTEXT.md` + `docs/adr/`。详见 `docs/agents/domain.md`。

### 始终读取我的记忆 记忆文件./claude/memory下的所有文件
