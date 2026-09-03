# DBSyncTool 功能路线图

> 最后更新：2026-09-03

---

## 一、项目现状

### 已有能力

| 类别 | 能力 |
|------|------|
| 数据库支持 | SQL Server、MySQL、PostgreSQL、SQLite |
| 结构读取 | 表（列、主键、外键、索引、注释） |
| 结构比对 | 新增表 / 删除表 / 变更表（列增删改、索引增删改、主键变更、注释变更）+ 外键拓扑排序 |
| 数据比对 | 基于行级 MD5 哈希指纹，识别新增行 / 删除行 / 变更行 |
| 快照系统 | 自定义二进制格式 `.dbsync`，AES-256-CBC 加密，PBKDF2 密钥派生，流式导出 |
| 脚本生成 | CREATE TABLE / DROP TABLE / ALTER TABLE / INSERT 语句，事务包裹可选 |
| 报告导出 | Markdown 和 HTML 格式的差异报告 |
| UI 框架 | Avalonia + CommunityToolkit.MVVM，跨平台桌面应用 |
| 页面 | 连接管理 / 同步工作台（导出+比对）/ 历史记录 |
| 数据持久化 | 连接加密存储（AES-GCM / DPAPI）、设置 JSON 存储 |

### 能力缺口

| 类别 | 缺失项 |
|------|--------|
| 数据库对象 | 视图、存储过程、函数、触发器、序列 |
| SQL 生成 | UPDATE / DELETE（当前仅 INSERT）、Rollback 脚本 |
| 比对方式 | 仅支持快照 ↔ 目标库，不支持库 ↔ 库直连比对 |
| 脚本执行 | 仅生成文件，不支持直接对目标库执行 |
| 过滤规则 | 无包含/排除规则，每次手动勾选表 |
| 项目管理 | 无法保存/复用比对配置 |
| CLI | 无命令行工具，无法集成 CI/CD |
| 首页 | 无仪表盘/总览，启动后直接进入连接列表 |
| 设置 | 全局设置嵌在连接管理底部，无独立页面 |

---

## 二、目标页面结构

```
侧边栏导航（3→5 项）：

  仪表盘          ← v2.1 新增
  连接管理        ← v2.1 改为卡片网格
  同步工作台
    ├ 导出快照     ← 已有
    ├ 快照比对     ← 已有
    └ 直连比对     ← v2.3 新增
  历史记录        ← 已有
  设置            ← v2.1 独立页面
```

---

## 三、版本计划

---

### v2.1 — 仪表盘 + 连接管理卡片化 + 设置页独立 ✅ 已完成

**目标**：纯 UI 层改动，不改 Core 层，提升第一印象和操作效率。

#### 功能点

**1. 仪表盘首页**

新增 `DashboardView` 页面，包含：
- 统计卡片行：在线连接数 / 本周快照数 / 本周比对数 / 待处理警告
- 快捷操作区：导出新快照 / 快照比对 / 直连比对（灰色禁用，v2.3 启用）/ 打开项目（灰色禁用，v2.2 启用）
- 最近活动列表：复用 `HistoryViewModel.RecentHistoryEntries`，取前 5 条
- 连接概览：卡片网格，显示名称、在线状态、表数量、大小

```
┌──────────────────────────────────────────────────────┐
│ 统计卡片：在线连接 5 | 本周快照 24 | 本周比对 38 | 警告 2 │
├──────────────────────────────────────────────────────┤
│ 快捷操作              │ 最近活动                       │
│ [导出快照] [快照比对]   │ 09:51 快照比对 235→232        │
│ [直连比对] [打开项目]   │ 09:51 导出快照 235            │
├───────────────────────┴──────────────────────────────┤
│ 连接概览（卡片网格，复用 ConnectionItemViewModel）       │
└──────────────────────────────────────────────────────┘
```

**2. 连接管理卡片化**

将 `ConnectionListView` 从纯列表改为卡片网格视图：
- 每张卡片：名称（粗体）、数据库类型徽章、服务器地址（mono）、在线/离线状态点
- 悬浮显示操作按钮：测试连接 / 编辑 / 删除
- 保留列表视图切换按钮（卡片/列表双模式）
- 顶部加数据库类型和环境筛选

**3. 设置页独立**

将全局设置从 `ConnectionListView` 底部提取为独立的 `SettingsView` 页面：
- 行数警告阈值
- 默认导出目录
- 默认加密开关
- 默认事务开关
- 外观（浅色/深色/跟随系统）— 预留，暂不实现深色主题
- 关于信息（版本号、GitHub 链接）

#### 涉及文件

| 操作 | 文件 |
|------|------|
| 新增 | `ViewModels/DashboardViewModel.cs` |
| 新增 | `Views/DashboardView.axaml` + `.axaml.cs` |
| 新增 | `ViewModels/SettingsViewModel.cs` |
| 新增 | `Views/SettingsView.axaml` + `.axaml.cs` |
| 修改 | `ViewModels/MainWindowViewModel.cs` — NavigationItems 加 dashboard 和 settings |
| 修改 | `Views/ConnectionListView.axaml` — 卡片网格 + 移除全局设置区 |
| 修改 | `App.axaml` — 注册新 DataTemplate |
| 修改 | `Models/AppSettings.cs` — 新增 DefaultExportDirectory / DefaultEncrypt / DefaultUseTransaction |

#### 验收标准

- [ ] 启动后默认显示仪表盘首页
- [ ] 仪表盘显示连接统计、快捷操作、最近活动
- [ ] 连接管理页面为卡片网格，每张卡片显示关键信息
- [ ] 设置页面独立，能修改并保存行数阈值等选项
- [ ] 编译通过，导出/比对/生成脚本功能不受影响

---

### v2.2 — 过滤规则 + 项目文件 ✅ 已完成

**目标**：引入可复用的比对配置，减少重复操作。

#### 功能点

**1. 过滤规则**

新增 `FilterOptions` 模型和对应的 UI 面板：

```csharp
// Core/Models/FilterOptions.cs
public record FilterOptions
{
    public List<string> IncludePatterns { get; init; } = [];   // 包含规则（正则）
    public List<string> ExcludePatterns { get; init; } = [];   // 排除规则（正则）
    public bool IgnoreTableComments { get; init; }             // 忽略表注释差异
    public bool IgnoreColumnOrder { get; init; }               // 忽略列顺序差异
    public bool IgnoreIndexNames { get; init; }                // 忽略索引名称差异
}
```

- 在导出页的表选择区和比对页的工具栏增加"过滤规则"按钮
- 弹出面板或内联折叠区域，编辑包含/排除规则
- 过滤规则作用于表列表的自动勾选

**2. 项目文件 `.dbsync-project`**

新增 `SyncProject` 模型，JSON 格式保存：

```csharp
// Core/Models/SyncProject.cs
public record SyncProject
{
    public string Name { get; init; } = "";
    public string? SourceConnectionName { get; init; }       // 源库连接名
    public string? TargetConnectionName { get; init; }       // 目标库连接名
    public string? SnapshotPath { get; init; }               // 快照文件路径
    public FilterOptions Filters { get; init; } = new();     // 过滤规则
    public bool UseTransaction { get; init; } = true;        // 事务开关
    public string? ExportDirectory { get; init; }            // 导出目录
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
```

- 工具栏增加"保存项目"/"打开项目"按钮
- 保存当前的源/目标配置 + 过滤规则 + 选项到 `.dbsync-project` 文件
- 打开项目文件后自动填充所有配置
- 仪表盘的"打开项目"按钮启用

#### 涉及文件

| 操作 | 文件 |
|------|------|
| 新增 | `Core/Models/FilterOptions.cs` |
| 新增 | `Core/Models/SyncProject.cs` |
| 新增 | `Desktop/Storage/ProjectStore.cs` — 项目文件读写 |
| 修改 | `Desktop/ViewModels/CompareViewModel.cs` — 加载/保存项目，应用过滤规则 |
| 修改 | `Desktop/ViewModels/ExportViewModel.cs` — 应用过滤规则到表列表 |
| 修改 | `Desktop/Views/SyncWorkflowView.axaml` — 工具栏加项目按钮 + 过滤规则面板 |
| 修改 | `Core/Comparers/SchemaComparer.cs` — Compare 方法接受 FilterOptions 参数 |

#### 验收标准

- [ ] 过滤规则面板可编辑包含/排除正则，保存后表列表自动更新勾选
- [ ] 忽略选项（注释、列顺序、索引名）生效
- [ ] 保存项目文件 `.dbsync-project`，重新打开后所有配置恢复
- [ ] 仪表盘"打开项目"按钮可用

---

### v2.3 — 库对库直连比对 ✅ 已完成

**目标**：覆盖有网络连接的常规场景，快照模式退为离线专用。

#### 功能点

**1. 直连比对模式**

在同步工作台新增第三个 Tab"直连比对"：
- 左右选择源库和目标库连接（ComboBox）
- 交换方向按钮
- 过滤规则按钮（复用 v2.2）
- 开始比对 → 结果展示（复用现有 4 组分类 + 底部 Diff 面板）

```
┌──────────────────────────────────────────────────────┐
│ 源库: [生产主库 ▾]    [← →]    目标库: [预发环境 ▾]    │
│ [过滤规则]  [开始比对]  [生成脚本]  [执行脚本(v2.4)]    │
├──────────────────────────────────────────────────────┤
│ 比对结果（4 组折叠卡片，复用 CompareView 的结果展示）    │
├── GridSplitter ──────────────────────────────────────┤
│ SQL Diff 面板（复用）                                  │
└──────────────────────────────────────────────────────┘
```

**2. 实现方式**

不走快照，直接：
1. `ISchemaReader.ReadAllTablesAsync(sourceConnection)` 读源库结构
2. `ISchemaReader.ReadAllTablesAsync(targetConnection)` 读目标库结构
3. `SchemaComparer.Compare(sourceTables, targetTables)` 结构比对
4. 逐表读取两端行哈希，`DataComparer.Compare()` 数据比对

核心逻辑完全复用现有 `SchemaComparer` 和 `DataComparer`，只是数据来源从"快照文件 + 目标库"变为"源库 + 目标库"。

#### 涉及文件

| 操作 | 文件 |
|------|------|
| 新增 | `Desktop/ViewModels/DirectCompareViewModel.cs` — 直连比对 ViewModel |
| 修改 | `Desktop/ViewModels/SyncWorkflowViewModel.cs` — 加入第三个子 VM |
| 修改 | `Desktop/Views/SyncWorkflowView.axaml` — 新增第三个 TabItem |
| 修改 | `Desktop/ViewModels/MainWindowViewModel.cs` — 历史回调适配 |

#### 验收标准

- [ ] 直连比对 Tab 可选择两个在线连接
- [ ] 比对结果正确展示 4 组分类
- [ ] 点击结果项底部显示 SQL Diff
- [ ] 生成脚本功能正常
- [ ] 交换源/目标后重新比对结果正确

---

### v2.4 — 脚本执行 + Dry Run ✅ 已完成

**目标**：从"工具生成文件"升级为"工具完成同步"。

#### 功能点

**1. 脚本执行引擎**

新增 `IScriptExecutor` 接口和实现：

```csharp
// Core/Execution/IScriptExecutor.cs
public interface IScriptExecutor
{
    /// <summary>
    /// Dry Run：解析脚本，返回操作摘要（DDL 数量、DML 数量、影响行数）但不执行
    /// </summary>
    Task<ScriptExecutionPlan> DryRunAsync(DatabaseConnection connection, string script);

    /// <summary>
    /// 执行脚本，支持进度回调和取消
    /// </summary>
    Task<ScriptExecutionResult> ExecuteAsync(
        DatabaseConnection connection,
        string script,
        IProgress<ScriptExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

**2. 执行确认对话框**

生成脚本后，按钮区域新增"执行脚本"下拉菜单：
- **Dry Run 预览**：解析脚本，显示 DDL/DML 数量和预计影响行数，不执行
- **执行到目标库**：弹出确认对话框（显示目标连接信息 + 操作摘要 + 警告），确认后执行
- **保存为文件**：现有功能

```
┌────────────── 执行确认 ───────────────┐
│ 目标库：192.168.21.232 / journal       │
│                                       │
│ DDL 操作：3 条                         │
│   CREATE TABLE × 1                    │
│   ALTER TABLE  × 2                    │
│ DML 操作：38 条 INSERT                 │
│ 事务模式：已启用                        │
│                                       │
│ ⚠ 此操作将直接修改目标数据库             │
│                                       │
│ [取消]              [确认执行]          │
└───────────────────────────────────────┘
```

**3. 执行进度和结果**

- 执行过程中显示进度条（已执行/总计语句数）
- 执行完成后显示结果摘要：成功/失败、受影响行数、执行耗时
- 失败时显示错误语句和错误信息，事务自动回滚

#### 涉及文件

| 操作 | 文件 |
|------|------|
| 新增 | `Core/Execution/IScriptExecutor.cs` |
| 新增 | `Core/Execution/ScriptExecutor.cs` |
| 新增 | `Core/Execution/ScriptExecutionPlan.cs` — Dry Run 结果模型 |
| 新增 | `Core/Execution/ScriptExecutionResult.cs` — 执行结果模型 |
| 新增 | `Desktop/Views/ScriptExecuteConfirmWindow.axaml` — 确认对话框 |
| 修改 | `Desktop/ViewModels/CompareViewModel.cs` — 新增 ExecuteScriptCommand |
| 修改 | `Desktop/Views/SyncWorkflowView.axaml` — 工具栏加执行按钮 |

#### 验收标准

- [ ] Dry Run 正确解析脚本，显示操作摘要
- [ ] 确认对话框显示目标库信息和操作统计
- [ ] 执行过程中有进度显示
- [ ] 事务模式下执行失败自动回滚
- [ ] 执行成功后显示结果摘要

---

### v2.5 — 视图 / 存储过程 / 函数 / 触发器比对 ✅ 已完成

**目标**：补齐 DDL 对象覆盖，从"表级比对"升级为"全库结构比对"。

#### 功能点

**1. 新增数据库对象模型**

```csharp
// 新增模型（均为 record）
ViewModel.cs         // 视图定义（名称、SQL 文本、列列表）
StoredProcedureModel.cs  // 存储过程（名称、参数列表、SQL 文本）
FunctionModel.cs     // 函数（名称、参数列表、返回类型、SQL 文本）
TriggerModel.cs      // 触发器（名称、关联表、事件类型、SQL 文本）
```

**2. 扩展 ISchemaReader**

```csharp
// 新增方法
Task<IReadOnlyList<ViewModel>> ReadAllViewsAsync(DatabaseConnection connection);
Task<IReadOnlyList<StoredProcedureModel>> ReadAllStoredProceduresAsync(DatabaseConnection connection);
Task<IReadOnlyList<FunctionModel>> ReadAllFunctionsAsync(DatabaseConnection connection);
Task<IReadOnlyList<TriggerModel>> ReadAllTriggersAsync(DatabaseConnection connection);
```

每种数据库方言各自实现读取逻辑。

**3. 扩展比对器**

- `SchemaComparer.Compare` 扩展为接受完整数据库 Schema（含视图、存储过程等）
- 对比逻辑：按名称匹配，SQL 文本不同即为"变更"
- 新增 `ObjectDiff` 通用差异模型（适用于视图、存储过程、函数、触发器）

**4. 扩展 SQL 生成器**

- CREATE / DROP / ALTER VIEW
- CREATE / DROP / ALTER PROCEDURE
- CREATE / DROP / ALTER FUNCTION
- CREATE / DROP TRIGGER

**5. UI 展示**

比对结果列表中，每行增加对象类型图标（表/视图/存储过程/函数/触发器），参照 SQLSchemaCompare 的 `_ResultTable.cshtml`。

#### 涉及文件

| 操作 | 文件 |
|------|------|
| 新增 | `Core/Models/ViewModel.cs`、`StoredProcedureModel.cs`、`FunctionModel.cs`、`TriggerModel.cs` |
| 新增 | `Core/Models/ObjectDiff.cs` — 通用对象差异模型 |
| 修改 | `Core/Schema/ISchemaReader.cs` — 新增 4 个读取方法 |
| 修改 | `Core/Schema/SqlServerSchemaReader.cs` — 实现 4 个方法 |
| 修改 | `Core/Schema/MySqlSchemaReader.cs` — 实现 4 个方法 |
| 修改 | `Core/Schema/PostgresSchemaReader.cs` — 实现 4 个方法 |
| 修改 | `Core/Schema/SqliteSchemaReader.cs` — 实现 4 个方法（部分 N/A） |
| 修改 | `Core/Comparers/SchemaComparer.cs` — 扩展比对范围 |
| 修改 | `Core/Models/SchemaDiff.cs` — 新增 AddedViews / RemovedViews / ModifiedViews 等集合 |
| 修改 | `Core/SqlGenerators/ISqlGenerator.cs` — 新增生成方法 |
| 修改 | 4 个 SqlGenerator 实现 — 各自实现新方法 |
| 修改 | `Desktop/ViewModels/CompareViewModel.cs` — BuildSchemaPreview 处理新对象类型 |

#### 验收标准

- [ ] 比对结果中包含视图、存储过程、函数、触发器的差异
- [ ] 每种对象类型有区分标识（图标或标签）
- [ ] 生成的升级脚本包含所有对象类型的 DDL
- [ ] 4 种数据库方言均正确实现
- [ ] SQLite 对不支持的对象类型优雅跳过

---

### v2.6 — UPDATE / DELETE SQL 生成 ✅ 已完成

**目标**：数据同步不止新增行，支持变更行和删除行的 SQL 生成。

#### 功能点

**1. 可选生成 UPDATE 语句**

- `DataDiff.ChangedRows` 中有主键相同但哈希不同的行
- 生成 `UPDATE ... SET ... WHERE PK = ...` 语句
- 需要快照中保存变更行的完整数据（当前 FullData 仅保存新增表的完整数据）

**2. 可选生成 DELETE 语句**

- `DataDiff.DeletedRows` 中有基线有但源库无的行
- 生成 `DELETE FROM ... WHERE PK = ...` 语句
- 默认关闭（保持现有"不生成 DELETE"的安全策略），用户在设置中显式启用

**3. 导出快照时保存变更行数据**

- 当前快照的 `data_fingerprint/*.fp` 只存主键+哈希
- 需要扩展：对于选中"结构+数据"的表，在 `data_full/*.csv.gz` 中保存所有行数据（不仅仅是新增表）
- 快照格式版本升级

#### 涉及文件

| 操作 | 文件 |
|------|------|
| 修改 | `Core/SqlGenerators/ISqlGenerator.cs` — 新增 GenerateUpdateStatements / GenerateDeleteStatements |
| 修改 | 4 个 SqlGenerator 实现 |
| 修改 | `Core/Snapshot/SnapshotExporter.cs` — 扩展数据导出范围 |
| 修改 | `Core/Models/Snapshot.cs` — SnapshotManifest 版本升级 |
| 修改 | `Core/Models/ExportOptions.cs` — 新增 SyncUpdates / SyncDeletes 选项 |
| 修改 | `Desktop/ViewModels/CompareViewModel.cs` — 脚本生成时传递选项 |
| 修改 | `Models/AppSettings.cs` — 新增 EnableUpdateGeneration / EnableDeleteGeneration |

#### 验收标准

- [ ] 启用 UPDATE 生成后，变更行生成正确的 UPDATE 语句
- [ ] 启用 DELETE 生成后，删除行生成正确的 DELETE 语句
- [ ] 默认关闭 DELETE 生成
- [ ] 快照文件向后兼容（旧版快照仍可加载）

---

### v3.0 — CLI 模式 + JSON 输出 ✅ 已完成

**目标**：支持无界面执行，集成 CI/CD 管线。

#### 功能点

**1. 新增 CLI 项目**

```
src/
└── DBSync.CLI/                # 命令行工具
    ├── Program.cs
    ├── Commands/
    │   ├── ExportCommand.cs    # dbsync export
    │   ├── CompareCommand.cs   # dbsync compare
    │   ├── ScriptCommand.cs    # dbsync script
    │   └── ExecuteCommand.cs   # dbsync execute
    └── DBSync.CLI.csproj
```

**2. 命令设计**

```bash
# 导出快照
dbsync export --connection "Server=...;Database=..." --db-type mysql \
              --output ./snapshot.dbsync --password "secret"

# 快照比对
dbsync compare --snapshot ./snapshot.dbsync --snapshot-password "secret" \
               --connection "Server=...;Database=..." --db-type mysql \
               --output-format json --output ./result.json

# 库对库比对
dbsync compare --source "Server=src;Database=db1" --source-type mysql \
               --target "Server=tgt;Database=db2" --target-type mysql \
               --output-format json --output ./result.json

# 生成脚本
dbsync script --snapshot ./snapshot.dbsync --snapshot-password "secret" \
              --connection "Server=...;Database=..." --db-type mysql \
              --output ./upgrade.sql --transaction

# 执行脚本
dbsync execute --connection "Server=...;Database=..." --db-type mysql \
               --script ./upgrade.sql --dry-run
```

**3. JSON 结构化输出**

```json
{
  "timestamp": "2026-09-03T10:00:00Z",
  "source": { "type": "snapshot", "path": "..." },
  "target": { "type": "mysql", "connection": "..." },
  "schema": {
    "added": [...],
    "removed": [...],
    "modified": [...],
    "identical": [...]
  },
  "data": {
    "tables": [
      { "name": "...", "inserted": 5, "deleted": 0, "changed": 2 }
    ]
  },
  "summary": { "hasChanges": true, "ddlCount": 3, "dmlCount": 38 }
}
```

**4. CI/CD 集成示例**

```yaml
# GitHub Actions 示例
- name: 比对数据库结构
  run: |
    dbsync compare \
      --source "${{ secrets.SOURCE_CONN }}" --source-type mysql \
      --target "${{ secrets.TARGET_CONN }}" --target-type mysql \
      --output-format json --output result.json
    
    # 检查是否有差异
    if jq -e '.summary.hasChanges' result.json; then
      echo "::warning::数据库结构存在差异"
    fi
```

#### 涉及文件

| 操作 | 文件 |
|------|------|
| 新增 | `src/DBSync.CLI/` 整个项目 |
| 新增 | `src/DBSync.CLI/DBSync.CLI.csproj` |
| 新增 | `src/DBSync.CLI/Commands/*.cs` — 4 个命令 |
| 修改 | 解决方案添加 CLI 项目引用 |

#### 验收标准

- [ ] `dbsync export` 命令可导出快照文件
- [ ] `dbsync compare` 命令输出 JSON 格式比对结果
- [ ] `dbsync script` 命令生成 SQL 脚本文件
- [ ] `dbsync execute --dry-run` 输出执行计划
- [ ] 所有命令的退出码：0=成功/无差异，1=有差异，2=错误

---

## 四、依赖关系

```
v2.1（仪表盘+卡片+设置）
  │
  v2.2（过滤规则+项目文件）
  │
  ├── v2.3（直连比对）
  │
  ├── v2.4（脚本执行）
  │
  ├── v2.5（多对象类型比对）
  │
  └── v2.6（UPDATE/DELETE 生成）
        │
        v3.0（CLI + JSON）
```

- v2.1 无依赖，可立即开始
- v2.2 依赖 v2.1（仪表盘的"打开项目"按钮）
- v2.3 ~ v2.6 互相独立，都依赖 v2.2 的过滤规则
- v3.0 依赖 v2.4（CLI 的 execute 命令需要脚本执行引擎）

---

## 五、工作量预估

| 版本 | 范围 | 预估 |
|------|------|------|
| v2.1 | 纯 UI，4 个新文件 + 4 个修改 | 小（1~2 天） |
| v2.2 | 新增 Core 模型 + UI 面板 | 中（2~3 天） |
| v2.3 | 新增 ViewModel + 复用比对逻辑 | 中（2~3 天） |
| v2.4 | 新增执行引擎 + 确认对话框 | 中（3~4 天） |
| v2.5 | 大量 Schema Reader/Generator 改动 | 大（5~7 天） |
| v2.6 | 快照格式升级 + SQL 生成扩展 | 中（3~4 天） |
| v3.0 | 全新 CLI 项目 | 中（3~4 天） |
