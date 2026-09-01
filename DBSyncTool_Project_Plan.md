# 数据库结构 / 数据差异同步工具（DBSyncTool）项目开发文档

文档版本：v1.0
最后更新：2026‑09‑01
作者：项目团队

## 1. 项目概述

### 1.1 项目名称

DBSyncTool（暂定名）—— 基于 .NET 的数据库结构 / 数据差异比对与离线同步工具

### 1.2 项目目标

开发一款**开源、跨平台（或 Windows）的桌面工具**，用于在**网络隔离、无法直连**的生产与测试环境之间，通过**导出 / 导入 “基线快照”**的方式，实现：

- 表结构的差异比对（列、类型、约束、索引等）
- 表数据的差异比对（基于主键哈希指纹）
- 生成可交付给运维执行的 SQL 升级脚本（含回滚脚本）
- 支持按表粒度精细控制同步内容（仅结构 / 结构 + 数据）

### 1.3 背景与场景

- 生产环境与测试环境物理或逻辑隔离，不允许直接数据库连接。
- 开发人员在测试环境完成迭代，需要将变更部署到生产环境。
- DBA 要求所有变更必须经 SQL 脚本评审，禁止直连操作。
- 现有工具（如 Redgate、Navicat）无法满足离线、轻量、开源、自定义表粒度的需求。

## 2. 核心工作流程

工具设计为**两阶段工作模式**，生成独立的中间文件（基线快照），绕过网络直连限制。

表格

| 阶段 | 运行环境 | 角色 | 操作 | 产出物 |
| --- | --- | --- | --- | --- |
| **阶段一：导出基线** | 生产环境（或目标库所在机器） | 运维 / DBA | 选择要同步的表，导出 “基线快照”（仅元数据 + 可选数据指纹） | `.dbsync` 加密压缩包 |
| **阶段二：比对生成** | 测试环境（或源库所在机器） | 开发人员 | 加载基线快照，连接源库，按表粒度对比差异，生成升级脚本 | `Upgrade.sql` + `Rollback.sql` + 差异报告 |

## 3. 功能需求

### 3.1 数据库连接管理

- 支持常见关系型数据库：**SQL Server, MySQL, PostgreSQL, SQLite**（优先支持 SQL Server 和 MySQL）。
- 连接信息加密存储（本地配置文件，用户级）。
- 支持测试连接。

### 3.2 导出基线（快照模式）

#### 3.2.1 表选择界面

- 以树形或表格列出目标库中所有表，显示预估行数、数据大小。
- 用户通过复选框选择需要同步的表。
- 对每张表可独立设置同步选项：
  - **同步结构**（默认勾选）
  - **同步数据**（仅当表为 “新增表” 时可选；对已存在的表，数据对比采用哈希指纹而非全量）
- 提供 “全选”、“反选”、“按筛选条件过滤” 等功能。

#### 3.2.2 数据导出策略（核心优化）

- **对于已存在的表**（用于数据差异对比）：
  - 仅导出每行数据的**主键 + 哈希指纹**（如 `MD5(CONCAT(col1, col2, ...))`）。
  - 不导出完整行数据，确保基线文件大小仅为实际数据量的 1%~3%。
- **对于新增的表**（在目标库不存在）：
  - 用户可勾选 “导出结构” 或 “结构 + 数据”。
  - 若勾选 “结构 + 数据”，则导出完整数据行（用于 `INSERT` 脚本）。超出**用户自定义行数阈值**（默认 10 万行）时弹出确认对话框。
- **支持数据过滤条件**：允许用户为每张表添加 `WHERE` 子句（如 `create_time > '2026‑01‑01'`），进一步缩减数据范围。

#### 3.2.3 导出文件格式

单文件 `.dbsync`（实际为 ZIP 压缩包），内部结构：

```
manifest.json         # 元数据：版本、数据库类型、导出时间、表列表
schema/
  table1.json         # 表结构定义（列、类型、约束、索引）
  table2.json
data_fingerprint/
  table1.fp           # 主键+哈希指纹（CSV或JSON Lines格式，GZip压缩）
  table2.fp
data_full/            # 仅当新增表选择导出完整数据时存在
  new_table.csv.gz
```

- 文件使用 AES‑256 对称加密（密码由用户输入，不存储在文件中）。
- `manifest.json` 中包含可选的 `passwordHint` 明文字段，供两端协作时回忆密码；该字段不参与加解密，导出时用户可选填，导入时工具显示。

#### 3.2.4 导出性能

- 使用 `DbDataReader` 流式读取，**禁止**将全量数据载入内存。
- 实时写入 ZIP/GZip 流，减少磁盘 IO。
- 支持取消操作。

### 3.3 加载基线并进行差异比对（对比模式）

#### 3.3.1 加载快照

- 用户选择 `.dbsync` 文件并输入解密密码。
- 解析 `manifest.json` 和各个表的结构定义。

#### 3.3.2 结构比对（Schema Compare）

将基线表结构与源库当前表结构进行深度对比。
检测差异类型：

- **新增表**：基线中有，源库中无 → 生成 `CREATE TABLE` 语句（若勾选数据则含 `INSERT`）。
- **删除表**：基线中无，源库中有 → 生成 `DROP TABLE` 语句（需警告，建议默认忽略）。
- **表结构变更**：列的新增 / 删除 / 类型修改、默认值、约束、索引变更 → 生成 `ALTER TABLE` 语句。
- 差异结果以树形列表展示，用户可预览并选择性包含 / 排除某些变更。

**外键拓扑排序**：生成 DDL 语句时，须先构建外键依赖图并做拓扑排序：
- `CREATE TABLE`：父表（被引用表）先建
- `DROP TABLE`：子表（引用他人的表）先删
- 检测到循环依赖时，提示用户手动处理，跳过该组表

#### 3.3.3 数据比对（Data Compare）

- **仅对 “已存在” 且 “同步数据” 被勾选**的表进行比对。
- 比对逻辑：

1. 从基线快照中加载该表的 “主键 + 哈希指纹” 集合（`data_fingerprint/table.fp`）。
2. 连接源库，实时计算当前表的 “主键 + 哈希指纹”（使用相同的哈希算法）。
3. 进行集合比较，**只处理新增行**：
   - **新增行**（基线无，源库有）→ 生成 `INSERT` 语句。
   - 删除行和更新行**不处理**，仅在差异报告中标注供人工审阅。

- 不生成回滚脚本，不生成 `UPDATE` / `DELETE` 语句。

#### 3.3.4 差异预览与编辑

- 在生成最终 SQL 脚本前，提供**可视化差异预览**，按表分组展示差异条目。
- 用户可手动勾选 / 取消勾选特定差异项，或修改生成的 SQL 内容（高级模式）。

### 3.4 SQL 脚本生成

- 生成最终升级脚本 `Upgrade.sql`，包含所有选中的 DDL 和 INSERT 语句（不生成 UPDATE / DELETE / 回滚脚本）。
- 脚本中包含事务控制（`BEGIN TRAN` / `COMMIT` / `ROLLBACK`）和错误处理。
- 脚本头部生成注释信息（导出时间、表数量、影响行数估计）。

### 3.5 附加功能

- **差异报告导出**：生成 HTML 或 Markdown 格式的差异报告，供团队审阅（含删除行、更新行的人工审阅提示）。
- **历史记录**：保存最近使用的连接、快照文件路径，方便快速操作。

## 4. 非功能需求

### 4.1 性能要求

- 导出基线时，对于千万级数据表（仅哈希指纹），导出时间 ≤ 5 分钟，文件大小 ≤ 200 MB。
- 比对过程应在合理时间内完成（视数据量而定，可通过进度条反馈）。
- 内存占用稳定，避免 OOM（内存溢出）。

### 4.2 安全性

- 数据库连接字符串加密存储（使用 Windows DPAPI 或用户主密码）。
- 快照文件必须支持 AES‑256 加密，密码不存储。
- 传输过程中（如通过邮件 / 网盘）建议用户自行额外加密。

### 4.3 兼容性

- 支持 Windows 7+ / Linux (Ubuntu) /macOS（若采用 Avalonia UI）。
- 数据库版本：SQL Server 2012+, MySQL 5.7+, PostgreSQL 10+, SQLite 3。

### 4.4 易用性

- 界面简洁，步骤引导清晰（向导式）。
- 提供详细的日志输出（可设置日志级别）。
- 错误信息友好，提供解决建议。

### 4.5 可扩展性

- 数据库访问层抽象为接口，便于后续支持 Oracle、国产数据库（达梦、人大金仓等）。
- 对比引擎模块化，可独立复用。

## 5. 技术选型

表格

| 层级 | 技术 | 理由 |
| --- | --- | --- |
| 框架 | **.NET 8 LTS** | 跨平台支持、性能优异、长期支持 |
| UI 框架 | **Avalonia UI** + CommunityToolkit.MVVM | 跨平台（Win/Linux/macOS），界面效果现代 |
| 数据库访问 | **Easy.SqlSugar.Core**（自研，基于 SqlSugar） | 天然支持 SQL Server、MySQL、PostgreSQL、SQLite 等，无需单独引入官方驱动 |
| 缓存 | **Easy.Cache.Core**（自研） | 统一缓存管理 |
| 日志 | **Easy.Serilog.Core**（自研） | 基于 Serilog，灵活输出到控制台 / 文件 |
| 测试数据生成 | **Easy.Bogus.Core**（自研） | 集成测试中生成虚拟数据 |
| 序列化 | **System.Text.Json** | 内置，高性能 JSON 处理 |
| 压缩 / 加密 | **System.IO.Compression.ZipArchive** + **System.Security.Cryptography.Aes** | 原生支持，无需第三方依赖 |
| 依赖注入 | **Microsoft.Extensions.DependencyInjection** | 便于模块解耦和测试 |
| 测试框架 | **xUnit** + **Moq** | 单元测试和模拟 |

## 6. 模块划分与架构设计

```
DBSyncTool/
├── DBSync.Core/                # 核心引擎（类库，无 UI 依赖）
│   ├── Models/                 # 元数据模型（Table, Column, Index, Constraint, RowHash）
│   ├── Schema/                 # ISchemaReader 接口 + 各数据库实现
│   │   ├── SqlServerSchemaReader.cs
│   │   ├── MySqlSchemaReader.cs
│   │   ├── PostgresSchemaReader.cs
│   │   └── SqliteSchemaReader.cs
│   ├── Comparers/              # 结构对比器、数据对比器
│   ├── Snapshot/               # 快照导出器、加载器、AES 加解密
│   ├── SqlGenerators/          # ISqlGenerator 接口 + 各数据库方言 DDL+DML 实现
│   │   ├── SqlServerSqlGenerator.cs
│   │   ├── MySqlSqlGenerator.cs
│   │   ├── PostgresSqlGenerator.cs
│   │   └── SqliteSqlGenerator.cs
│   └── Extensions/             # 扩展方法
├── DBSync.Desktop/             # Avalonia UI 桌面应用（MVVM，跨平台）
│   ├── ViewModels/             # MVVM 视图模型（CommunityToolkit.MVVM）
│   ├── Views/                  # 窗口/用户控件（Avalonia XAML）
│   ├── Services/               # UI 服务（对话框、文件选择器、消息提示）
│   └── Resources/              # 样式、图标
└── DBSync.Tests/               # 单元测试和集成测试
```

## 7. 数据库适配细节

### 7.1 结构元数据读取

需要从系统视图中获取：

- **表列表**：`INFORMATION_SCHEMA.TABLES`
- **列信息**：`INFORMATION_SCHEMA.COLUMNS`（包含数据类型、长度、精度、是否可空、默认值）
- **主键**：`INFORMATION_SCHEMA.TABLE_CONSTRAINTS` + `INFORMATION_SCHEMA.KEY_COLUMN_USAGE`
- **外键**、**索引**（不同数据库差异较大，需各自实现）

### 7.2 哈希指纹计算

为每行数据计算哈希值，算法需跨数据库一致：

- **SQL Server**：`HASHBYTES('MD5', CONCAT(col1, col2, ...))`
- **MySQL / PostgreSQL**：`MD5(CONCAT_WS('|', col1, col2, ...))`
- **SQLite**：在 .NET 端计算（内置函数不足）

各列类型在参与哈希前统一转换为字符串，规则如下：

| 类型 | 处理方式 |
| --- | --- |
| `NULL` | 固定字符串 `'NULL'` |
| `BINARY` / `VARBINARY` / `BLOB` | 十六进制字符串（`HEX()`）|
| `DATETIME` / `TIMESTAMP` | 格式化为 `yyyy-MM-dd HH:mm:ss.fff`（UTC）|
| `FLOAT` / `DOUBLE` | 固定精度字符串（15 位有效数字）|
| `JSON` / `XML` | 规范化（去除无意义空白）后转字符串 |
| `BOOLEAN` | 统一为 `'0'` / `'1'` |

### 7.3 SQL 生成方言

- DDL 语句（`CREATE TABLE`, `ALTER TABLE`）语法因数据库而异，需实现各自的生成器。
- DML 语句（`MERGE` / `INSERT ... ON DUPLICATE KEY UPDATE`）同样需适配。

## 8. 用户界面原型建议

### 8.1 主窗口布局

- **顶部工具栏**：新建连接、打开快照、设置。
- **步骤导航**：Step1 选择源 / 目标 → Step2 选择表 → Step3 对比预览 → Step4 生成脚本。
- **中间内容区**：根据当前步骤动态切换。
- **底部状态栏**：当前操作状态、进度条、日志摘要。

### 8.2 表选择界面设计

表格列：

- 复选框（选择）
- 表名
- 行数（预估）
- 数据大小（MB）
- 同步结构（复选框，默认勾选）
- 同步数据（复选框，仅当表在基线中存在时不可用；若为新增表则可用，并下拉选择 “仅结构”/“结构 + 数据”）
- 过滤条件（文本输入框，可选）

### 8.3 差异预览

分两部分：

- **结构差异**：树形展示变更类型（新增表、修改列、删除列等），可勾选。
- **数据差异**：展示新增 / 修改 / 删除行数，可点击查看详细行（若数据量不大）。

## 9. 开发路线图

### Phase 1：核心引擎 MVP（预计 4 周）

- 定义元数据模型和接口
- 实现 SQL Server 的 Schema 读取器（`SqlServerSchemaReader`）
- 实现快照导出（流式 + GZip 压缩）和加载
- 实现结构对比器（生成 ALTER 脚本）

### Phase 2：数据对比功能（预计 3 周）

- 实现行哈希计算（SQL Server，使用 Easy.SqlSugar.Core 游标查询）
- 实现数据指纹流式导出
- 实现数据对比器（集合比较）
- 生成 MERGE / UPDATE / DELETE 脚本
- 支持回滚脚本生成

### Phase 3：多数据库支持（预计 3 周）

- 在 `DBSync.Core` 内补全 MySQL、PostgreSQL、SQLite 的 SchemaReader 和 SqlGenerator
- 统一各数据库的哈希和 DDL 方言差异

### Phase 4：桌面 UI（预计 4 周）

- 实现连接管理界面
- 实现表选择和数据导出配置界面
- 实现差异预览和 SQL 脚本生成界面
- 集成进度反馈和日志显示

### Phase 5：增强与打磨（预计 2 周）

- 差异报告导出（HTML）
- 安装包制作（Inno Setup / .deb / .dmg）
- 文档和示例

## 10. 开源计划

- **开源许可证**：MIT（便于社区贡献和使用）。
- **代码仓库**：GitHub（或 Gitee 同步），设立 `README.md`、`CONTRIBUTING.md`、`CHANGELOG.md`。
- **沟通渠道**：GitHub Issues + Discussions，QQ 群 / 微信群（可选）。
- **发布**：GitHub Releases 提供各平台安装包，NuGet 发布核心库。

## 11. 风险与挑战

表格

| 风险 | 缓解措施 |
| --- | --- |
| 不同数据库的元数据查询差异大 | 为每个数据库单独实现 `ISchemaReader`，单元测试覆盖 |
| 哈希算法在不同数据库结果不一致 | 统一在 .NET 端计算哈希（适用于中小数据量），或使用标准化函数 |
| 大数据量导出内存溢出 | 强制流式处理，设置内存阈值警告 |
| 生成的 SQL 在目标库执行失败 | 支持事务和错误回滚，提供预检查模式 |
| 用户误操作（如删除数据） | 默认禁用 `DELETE` 生成，需用户手动开启并确认 |

## 12. 附录：关键类设计示例（C#）

```
// 核心模型
public class TableModel
{
    public string Name { get; set; }
    public List<ColumnModel> Columns { get; set; }
    public List<IndexModel> Indexes { get; set; }
    public ConstraintModel PrimaryKey { get; set; }
    // ...
}

// 快照服务接口
public interface ISnapshotService
{
    Task ExportAsync(DatabaseConnection source, IEnumerable<string> tables, SnapshotOptions options, Stream output);
    Task<Snapshot> LoadAsync(Stream input, string password);
}

// 对比引擎
public class SchemaComparer
{
    public CompareResult Compare(TableModel source, TableModel target);
}

// SQL生成器
public interface ISqlGenerator
{
    string GenerateCreateTable(TableModel table);
    string GenerateAlterTable(TableModel source, TableModel target);
    string GenerateMerge(TableModel table, IEnumerable<RowDiff> diffs);
}
```

