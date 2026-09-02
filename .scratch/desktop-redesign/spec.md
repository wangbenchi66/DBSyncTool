# Desktop 层 UI 架构重构规格说明

Status: ready-for-agent
创建时间：2026-09-02
前置：dbsynctool-mvp（全部 14 项已完成）

---

## 问题陈述

DBSyncTool MVP 功能已开发完成，但 Desktop 层存在严重的架构债务，导致功能难以维护和扩展：

1. **God ViewModel**：`MainWindowViewModel` 约 1200 行，承载连接管理、导出、比对、报告、历史全部逻辑，职责严重违反单一职责原则
2. **连接管理不可用**：`ConnectionItemViewModel.ConnectionString` 硬编码为 `Server={ServerAddress};`，缺少数据库名、端口、认证等关键字段，导致连接测试必然失败
3. **编辑功能缺失**：`EditConnection()` 只在名称后追加 `*` 号，是占位实现
4. **服务接口耦合 ViewModel**：`IConnectionStore` 的 `Load()/Save()` 直接操作 `ConnectionItemViewModel` 而非领域模型，Desktop 层的 ViewModel 类型穿透到了存储接口
5. **无页面导航**：导出向导和比对向导挤在左侧长滚动面板，连接管理和历史挤在右侧 Tab 页，信息密度过高、操作流混杂
6. **ViewModel 嵌套**：`ConnectionItemViewModel` 和 `ExportTableItemViewModel` 定义在 `MainWindowViewModel` 内部，无法独立测试和复用
7. **连接模型不一致**：Core 层 `DatabaseConnection` 有完整的 `ConnectionString`，Desktop 层 `ConnectionItemViewModel` 却只有 `ServerAddress`，两者数据不对齐

---

## 解决方案

对 Desktop 层进行全面架构重构，引入侧边栏导航、拆分 ViewModel、重建连接管理模型，使每个功能域有独立的页面、ViewModel 和清晰的边界。Core 层保持不变。

**核心变更：**
- 左侧固定导航栏 + 右侧内容区的页面切换模型
- 4 个独立页面：连接管理、导出快照、加载比对、历史记录
- 连接配置采用结构化表单 + 原始连接字符串的混合模式
- `IConnectionStore` 解耦，基于领域模型而非 ViewModel

---

## 用户故事

### 导航与布局

1. 作为用户，我希望左侧有固定的导航栏列出所有功能入口（连接管理/导出快照/加载比对/历史记录），以便一眼找到目标功能
2. 作为用户，我希望点击导航项后右侧内容区切换到对应页面，以便每个功能有充足的展示空间
3. 作为用户，我希望导航栏标明当前选中的页面（高亮），以便随时知道自己在哪
4. 作为用户，我希望应用记住上次使用的页面，以便下次打开时直接进入

### 连接管理页面

5. 作为 DBA，我希望在连接管理页面看到已保存的所有连接列表（卡片或表格形式），以便快速浏览和管理
6. 作为 DBA，我希望列表中每条连接显示名称、数据库类型、服务器地址和数据库名，以便区分不同连接
7. 作为 DBA，我希望点击"新增"按钮后弹出连接编辑对话框，以便填写完整的连接信息
8. 作为 DBA，我希望连接编辑对话框根据所选数据库类型（SQL Server/MySQL/PostgreSQL/SQLite）动态显示对应字段，以便每种数据库只显示相关配置
9. 作为 DBA，我希望 SQL Server 连接表单包含：服务器地址、端口（默认 1433）、数据库名、认证方式（Windows 认证/SQL 认证）、用户名、密码，以便完整配置连接
10. 作为 DBA，我希望 MySQL 连接表单包含：服务器地址、端口（默认 3306）、数据库名、用户名、密码、字符集（默认 utf8mb4），以便完整配置连接
11. 作为 DBA，我希望 PostgreSQL 连接表单包含：服务器地址、端口（默认 5432）、数据库名、用户名、密码、Schema（默认 public），以便完整配置连接
12. 作为 DBA，我希望 SQLite 连接表单仅需要填写数据库文件路径（支持浏览选择），以便极简配置
13. 作为高级用户，我希望在连接编辑对话框中有"高级"选项卡，可直接查看和编辑完整的连接字符串，以便添加非标准参数（如 `Encrypt=True;TrustServerCertificate=True;`）
14. 作为 DBA，我希望在结构化表单和原始连接字符串之间双向同步——修改表单字段自动更新连接字符串，手动编辑连接字符串也能回填表单字段，以便两种方式无缝切换
15. 作为 DBA，我希望在对话框中点击"测试连接"按钮验证连接是否可用，并看到成功或失败的明确反馈，以便在保存前确认配置正确
16. 作为 DBA，我希望选中已有连接后点击"编辑"打开同样的对话框并预填现有配置，以便修改连接信息
17. 作为 DBA，我希望删除连接前有确认提示，以便避免误删
18. 作为 DBA，我希望连接密码在界面上以 `●●●●` 显示且不可复制，以便防止意外泄露

### 导出快照页面

19. 作为 DBA，我希望导出页面第一步是从下拉框选择已保存的连接（或点击"新增连接"跳转到连接管理），以便复用已配置的连接
20. 作为 DBA，我希望选择连接后点击"加载表"读取数据库表列表，显示表名、预估行数、数据大小，以便选择导出范围
21. 作为 DBA，我希望导出流程与 MVP 一致（选表 → 配置密码/路径 → 执行），以便不改变已有操作习惯

### 加载比对页面

22. 作为开发人员，我希望比对页面第一步是选择 .dbsync 文件并输入密码加载快照，以便开始比对流程
23. 作为开发人员，我希望第二步从下拉框选择已保存的连接作为比对目标（测试库），以便复用连接
24. 作为开发人员，我希望比对流程与 MVP 一致（加载快照 → 选连接 → 执行比对 → 预览差异 → 生成脚本），以便不改变已有操作习惯

### 历史记录页面

25. 作为用户，我希望历史记录页面显示最近的导出和比对操作记录，以便快速回溯和复用
26. 作为用户，我希望点击历史记录条目中的"使用"按钮，自动跳转到对应页面并预填连接和路径，以便快速重复操作

### 状态栏

27. 作为用户，我希望底部状态栏显示当前操作状态和最新日志摘要，以便随时了解工具运行情况
28. 作为用户，我希望状态栏的错误信息用醒目颜色（红色）显示，以便及时发现问题

---

## 实现决策

### 导航架构

- 采用侧边栏 + 内容区布局（ContentControl + DataTemplate 方式，不引入额外路由框架）
- 导航状态由 `MainWindowViewModel` 管理（`CurrentPage` 属性），但页面内逻辑由各自独立的 ViewModel 负责
- `MainWindowViewModel` 瘦身为纯导航容器（持有 4 个页面 ViewModel + 当前页面切换），不再包含任何业务逻辑

### ViewModel 拆分

按功能域拆分为独立 ViewModel，每个有对应的 UserControl 作为 View：

| ViewModel | 职责 |
|-----------|------|
| `MainWindowViewModel` | 导航切换、持有页面 ViewModel 引用、状态栏绑定 |
| `ConnectionListViewModel` | 连接列表展示、新增/编辑/删除触发 |
| `ConnectionEditViewModel` | 单个连接的编辑逻辑（结构化字段 ↔ 原始字符串双向同步、测试连接、保存） |
| `ExportViewModel` | 导出向导全流程（选连接 → 选表 → 配置 → 执行） |
| `CompareViewModel` | 比对向导全流程（加载快照 → 选连接 → 比对 → 预览 → 生成脚本） |
| `HistoryViewModel` | 历史记录展示和复用 |

辅助 ViewModel（从 MainWindowViewModel 中提取的嵌套类）：
- `ConnectionItemViewModel` → 独立文件，移到 `ViewModels/` 目录
- `ExportTableItemViewModel` → 独立文件，移到 `ViewModels/` 目录
- `CompareSchemaNodeViewModel` / `CompareDataSummaryViewModel` → 保留独立文件

### 连接领域模型重建

Core 层 `DatabaseConnection` record 扩展为结构化模型：

| 字段 | 类型 | 说明 |
|------|------|------|
| Name | string | 显示名称 |
| DbType | DatabaseType | 数据库类型枚举 |
| Server | string | 服务器地址（SQLite 时为文件路径） |
| Port | int? | 端口号（SQLite 无此项） |
| Database | string | 数据库名（SQLite 无此项） |
| Username | string | 用户名（Windows 认证时为空） |
| Password | string | 密码（内存中明文，持久化时加密） |
| UseWindowsAuth | bool | 仅 SQL Server：是否使用 Windows 认证 |
| Schema | string | 仅 PostgreSQL：schema 名称（默认 public） |
| Charset | string | 仅 MySQL：字符集（默认 utf8mb4） |
| AdditionalParameters | string | 额外连接字符串参数（键值对格式） |

提供 `BuildConnectionString()` 方法，根据 `DbType` 拼接对应方言的完整连接字符串。同时提供 `ParseConnectionString(DatabaseType, string)` 静态方法，将原始连接字符串解析回结构化字段。

### IConnectionStore 解耦

- `IConnectionStore.Load()` 返回 `IReadOnlyList<DatabaseConnection>`（Core 层领域模型）
- `IConnectionStore.Save()` 接受 `IReadOnlyList<DatabaseConnection>`
- ViewModel 层负责 `DatabaseConnection ↔ ConnectionItemViewModel` 的映射
- `LocalConnectionStore` 内部 DTO 也相应更新，存储结构化字段而非仅 ServerAddress

### 连接编辑对话框

- 使用 Avalonia Window（模态对话框）实现，不是内嵌面板
- `ConnectionEditViewModel` 同时持有结构化字段和原始连接字符串属性
- 结构化字段变更时通过 `BuildConnectionString()` 更新原始字符串
- 原始字符串手动编辑时通过 `ParseConnectionString()` 回填结构化字段（解析失败则保留原始字符串不清空表单字段）
- 数据库类型切换时重置表单字段为对应类型的默认值

### 生命周期

- `MainWindowViewModel`：Singleton（应用生命周期）
- 各页面 ViewModel（`ConnectionListViewModel` 等）：Singleton（随 MainWindowViewModel 创建）
- `ConnectionEditViewModel`：Transient（每次打开对话框时新建）
- 窗口级对话框（`ConnectionEditWindow`）：Transient

### 清理项

- 移除所有类上的 `IDependency` 标记接口（Autofac 残留）
- 移除 `Program.cs` 中被注释掉的 Autofac 代码
- 更新 DI 注册，反映新的 ViewModel 结构

---

## 测试决策

### 测试接缝

主测试接缝在 **ViewModel 层**——这是最高可用接缝：所有 UI 逻辑都经过 ViewModel，可以通过 Mock 服务接口进行纯 C# 单元测试，无需 Avalonia 渲染管线。

具体测试接缝：
- `ConnectionEditViewModel` 的连接字符串双向同步逻辑（结构化 ↔ 原始字符串）
- `ConnectionEditViewModel` 的表单验证逻辑（必填字段、端口范围等）
- `DatabaseConnection.BuildConnectionString()` 各方言的拼接结果
- `DatabaseConnection.ParseConnectionString()` 各方言的解析结果
- `ConnectionListViewModel` 的增删改列表操作

### 好的测试标准

- 只测外部行为：给定输入字段，断言输出的连接字符串；给定连接字符串，断言解析出的字段
- 不测 UI 渲染：不测 View 层的绑定是否正确、按钮是否可见等
- 参照现有测试风格（DBSync.Tests 中的 xUnit + Moq 模式）

### 测试模块

| 模块 | 测试类型 | 说明 |
|------|----------|------|
| `DatabaseConnection.BuildConnectionString()` | 单元测试 | 4 种数据库类型 × 正常/边界输入 |
| `DatabaseConnection.ParseConnectionString()` | 单元测试 | 4 种数据库类型 × 正常/畸形输入（含容错） |
| `ConnectionEditViewModel` | 单元测试 | 双向同步、表单验证、类型切换重置 |
| `ConnectionListViewModel` | 单元测试 | Mock IConnectionStore，验证增删改流程 |
| `LocalConnectionStore` | 集成测试 | 写入/读取/加密往返一致性 |

---

## 不在范围内

- **Core 层修改**：`ISchemaReader`、`ISqlGenerator`、`SchemaComparer`、`DataComparer` 等核心引擎不动（`DatabaseConnection` 模型扩展除外）
- **导出/比对业务逻辑变更**：只重构 UI 和 ViewModel 层的组织方式，不改变导出和比对的功能行为
- **多窗口/MDI 支持**：保持单窗口应用，侧边栏导航足够
- **主题切换/深色模式**：不在本次范围，保持现有 FluentTheme
- **国际化/多语言**：不在本次范围，保持中文
- **全新功能**：不新增功能点，只重构现有功能的 UI 架构

---

## 补充说明

- 重构过程中保持已有功能完整：导出快照、加载比对、生成脚本、差异预览、历史记录等功能不得丢失
- `ExportViewModel` 和 `CompareViewModel` 的内部逻辑从 `MainWindowViewModel` 中**平移**过来，不做行为变更，只做代码组织调整
- 连接管理是本次重构的**核心交付**，需要从不可用状态提升到生产可用状态
- 每个新增的方法/属性/字段/类必须按照项目规范添加完整 XML 注释
