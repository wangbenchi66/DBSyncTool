# DBSyncTool MVP 规格说明

Status: ready-for-agent
创建时间：2026-09-01

---

## 问题陈述

开发团队在网络隔离的生产与测试环境之间进行数据库变更部署时，面临以下困难：

- 生产环境与测试环境物理或逻辑隔离，无法直接数据库连接
- DBA 要求所有变更必须以 SQL 脚本形式提交评审，禁止直连操作
- 现有商业工具（Redgate、Navicat）不支持离线、轻量、开源的工作流
- 手动比对表结构差异效率低、容易遗漏

---

## 解决方案

提供一款桌面工具，通过"基线快照"文件（.dbsync）绕过网络限制，实现两阶段离线同步：

1. **阶段一（生产端）**：DBA 在生产机上运行工具，选择需要同步的表，导出包含结构元数据和行哈希指纹的加密快照文件
2. **阶段二（测试端）**：开发人员在测试机上加载快照，与本地测试库比对差异，生成可交付给 DBA 执行的 Upgrade.sql 脚本

---

## 用户故事

### 连接管理

1. 作为 DBA，我希望保存数据库连接信息，以便每次操作无需重复输入
2. 作为 DBA，我希望连接信息在本地加密存储，以便敏感信息不会泄露
3. 作为 DBA，我希望在保存连接前测试连接是否可用，以便及早发现配置错误
4. 作为 Windows 用户，我希望连接字符串通过 DPAPI 加密，以便无需记额外密码
5. 作为 Linux/macOS 用户，我希望通过用户主密码加密连接字符串，以便跨平台使用

### 导出基线（阶段一）

6. 作为 DBA，我希望打开工具后直接看到"导出快照"和"加载快照并比对"两个入口，以便快速进入对应工作流
7. 作为 DBA，我希望以表格形式浏览目标库（生产库）中的所有表，并看到预估行数和数据大小，以便有依据地选择同步范围
8. 作为 DBA，我希望通过复选框逐表选择需要同步的内容，以便按需精细控制
9. 作为 DBA，我希望对每张表独立配置"仅同步结构"或"结构+数据"，以便灵活处理不同类型的表
10. 作为 DBA，我希望为每张表添加可选的 WHERE 过滤条件，以便缩小数据范围（如只导出近三个月数据）
11. 作为 DBA，我希望当选择导出数据的表行数超过自定义阈值时收到确认提示，以便避免意外导出超大数据集
12. 作为 DBA，我希望在界面设置中配置行数警告阈值（默认 10 万行），以便适配不同项目规模
13. 作为 DBA，我希望导出时看到实时进度条，并能随时取消，以便在操作耗时时保持控制感
14. 作为 DBA，我希望导出文件使用 AES-256 加密，以便通过邮件或网盘传输时数据安全
15. 作为 DBA，我希望在导出时填写一条可选的密码提示（passwordHint），以便收件人能回忆密码，同时提示本身不影响加密安全性
16. 作为 DBA，我希望对已存在的表只导出行哈希指纹而非完整数据，以便快照文件保持小体积（实际数据量的 1%~3%）
17. 作为 DBA，我希望对新增表（目标库不存在的表）可选导出完整数据，以便生成 INSERT 脚本

### 加载快照与差异比对（阶段二）

18. 作为开发人员，我希望选择 .dbsync 文件并输入密码后加载快照，以便开始比对流程
19. 作为开发人员，我希望在加载时看到密码提示（若 DBA 填写了），以便更容易记起正确密码
20. 作为开发人员，我希望工具自动对比快照中的表结构与当前测试库的表结构，以便识别所有 DDL 差异
21. 作为开发人员，我希望差异结果以树形列表按表分组展示，并可勾选/取消单条差异，以便精细控制生成内容
22. 作为开发人员，我希望工具识别新增表（快照有、测试库无），以便生成 CREATE TABLE 语句
23. 作为开发人员，我希望工具识别删除表（快照无、测试库有），并默认忽略该差异（只展示警告），以便避免误删
24. 作为开发人员，我希望工具识别列新增/删除/类型修改/默认值/约束/索引变更，以便生成精确的 ALTER TABLE 语句
25. 作为开发人员，我希望生成的 CREATE TABLE / DROP TABLE 语句按外键依赖拓扑顺序排列，以便脚本可直接执行而不产生约束冲突
26. 作为开发人员，我希望当检测到循环外键依赖时收到提示，以便手动处理该特殊情况
27. 作为开发人员，我希望对启用了"同步数据"的已存在表进行行哈希比对，以便识别测试库中的新增行
28. 作为开发人员，我希望对无主键的表跳过数据比对并在界面标注原因，以便不因无法生成指纹而中断流程
29. 作为开发人员，我希望删除行和更新行只出现在差异报告中（不生成 SQL），以便人工审阅后由 DBA 决策
30. 作为开发人员，我希望在生成 SQL 前预览所有差异，以便确认内容后再生成

### SQL 脚本生成

31. 作为开发人员，我希望生成一份 Upgrade.sql，包含所有选中的 DDL 语句和 INSERT 语句，以便交给 DBA 评审执行
32. 作为开发人员，我希望 Upgrade.sql 包含事务控制（BEGIN TRAN / COMMIT / ROLLBACK ON ERROR），以便执行失败时自动回滚
33. 作为开发人员，我希望脚本头部包含元信息注释（导出时间、表数量、影响行数估计），以便 DBA 快速了解变更规模
34. 作为开发人员，我希望对新增表的 INSERT 脚本自动包裹 SET IDENTITY_INSERT ON/OFF（SQL Server），以便保留原始主键值，维持外键引用完整性
35. 作为开发人员，我希望导出 HTML 或 Markdown 格式的差异报告，以便团队成员在无工具的环境下也能审阅差异
36. 作为开发人员，我希望历史记录保存最近使用的连接和快照路径，以便下次快速操作

---

## 实现决策

### 核心架构

- **DBSync.Core**（纯类库，无 UI 依赖）包含所有业务逻辑，包括：
  - `Models/`：`TableModel`、`ColumnModel`、`IndexModel`、`RowHash` 等不可变数据模型
  - `Schema/`：`ISchemaReader` 接口 + 各数据库实现（SqlServer、MySQL、PostgreSQL、SQLite）
  - `Comparers/`：`SchemaComparer`（纯函数）、`DataComparer`（纯函数）
  - `Snapshot/`：`ISnapshotExporter`、`ISnapshotLoader`、AES-256 加解密
  - `SqlGenerators/`：`ISqlGenerator` 接口 + 各数据库方言实现

- **DBSync.Desktop**（Avalonia UI + CommunityToolkit.MVVM）仅包含 ViewModel、View 和 UI 服务，不直接操作数据库

### 技术选型

- ORM 层使用 **Easy.SqlSugar.Core**（基于 SqlSugar），天然支持多数据库方言，无需为每个数据库单独引入官方驱动
- 日志：**Easy.Serilog.Core**；缓存：**Easy.Cache.Core**；测试数据：**Easy.Bogus.Core**
- 快照文件：`System.IO.Compression.ZipArchive` + `System.Security.Cryptography.Aes`

### 快照文件格式（.dbsync）

```
manifest.json          # { version, dbType, exportedAt, tables[], passwordHint? }
schema/
  <tableName>.json     # 表结构定义
data_fingerprint/
  <tableName>.fp       # GZip 压缩的 JSON Lines：{ pk: ..., hash: "..." }
data_full/
  <tableName>.csv.gz   # 仅新增表选择"结构+数据"时存在
```

密码提示（`passwordHint`）以**明文**存储在 `manifest.json` 中，不参与加密。

### 数据同步范围限制

- 数据同步**仅生成 INSERT 语句**（针对源库有、基线无的新增行）
- 不生成 UPDATE、DELETE，不生成回滚脚本
- 删除行和更新行仅出现在差异报告中，供人工决策

### 流式处理

- 导出时禁止全量加载内存，使用 SqlSugar 游标查询/分页逐行读取，实时写入 ZipArchive + GZipStream
- 比对时同样流式处理基线指纹文件

### 哈希指纹生成规则

各列类型在参与哈希前统一转换为字符串：

| 类型 | 处理规则 |
|------|----------|
| NULL | 固定字符串 `'NULL'` |
| BINARY/VARBINARY/BLOB | HEX() 十六进制字符串 |
| DATETIME/TIMESTAMP | `yyyy-MM-dd HH:mm:ss.fff`（UTC） |
| FLOAT/DOUBLE | 15 位有效数字固定精度字符串 |
| JSON/XML | 规范化（去除无意义空白）后转字符串 |
| BOOLEAN | `'0'` / `'1'` |

### 外键拓扑排序

生成 DDL 时先构建外键依赖图，拓扑排序后输出：
- CREATE TABLE：父表优先
- DROP TABLE：子表优先
- 检测到循环依赖时：跳过该组表并告知用户

### 无主键表

无主键的表只做结构比对，跳过数据比对，界面标注"⚠ 无主键，已跳过数据比对"。

### 连接字符串加密

- Windows：使用 DPAPI（`ProtectedData`）
- Linux / macOS：用户输入主密码，AES-GCM 加密存储
- 平台检测在应用启动时执行，自动切换

### WHERE 子句安全

用户输入的 WHERE 子句拼接前做基础清理：去除末尾分号、拒绝包含语句分隔符（`;`）的输入。不做完整 SQL 解析，信任 DBA 用户。

### IDENTITY 列处理（SQL Server）

新增表完整数据 INSERT 脚本自动包裹：
```sql
SET IDENTITY_INSERT [TableName] ON
-- INSERT 语句
SET IDENTITY_INSERT [TableName] OFF
```
MySQL AUTO_INCREMENT 列直接写入值（MySQL 在显式 INSERT 时不需要额外指令）。

---

## 测试决策

### 好的测试标准

- 只测外部可见行为，不测实现细节（不测私有方法、不测具体 SQL 字符串的内部拼接逻辑）
- 使用 `Easy.Bogus.Core` 生成测试用的 `TableModel` / `ColumnModel` 数据
- 集成测试使用真实数据库连接（Docker 容器或本地实例），不 Mock 数据库

### 测试模块

| 模块 | 测试类型 | 说明 |
|------|----------|------|
| `SchemaComparer` | 单元测试 | 纯函数，输入两组 TableModel，验证 SchemaDiff 正确性 |
| `DataComparer` | 单元测试 | 纯函数，输入两组主键+哈希集合，验证只输出新增行 |
| `ISnapshotExporter + ISnapshotLoader` | 集成测试 | 导出后读回，验证往返一致性；含加密/解密 |
| `ISchemaReader`（各 Provider）| 集成测试 | 需真实数据库，验证元数据读取正确性 |
| `ISqlGenerator`（各方言）| 单元测试 | 给定 SchemaDiff，验证生成 SQL 的语义正确性（快照测试）|
| 哈希指纹计算 | 单元测试 | 验证 NULL / BLOB / DATETIME 等特殊类型的统一处理 |
| 外键拓扑排序 | 单元测试 | 给定依赖图，验证排序结果；含循环依赖检测 |

---

## 不在范围内

- **回滚脚本**：不生成，数据层无法在只有哈希指纹的情况下重建原始值
- **UPDATE / DELETE 语句生成**：只报告，不生成，防止误删生产数据
- **命令行模式（CLI）**：当前版本只做桌面 GUI
- **视图、存储过程、触发器、函数**：只处理表结构和数据
- **Oracle、达梦、人大金仓等扩展数据库**：当前只支持 SQL Server、MySQL、PostgreSQL、SQLite
- **实时/增量同步**：只支持快照模式，不支持持续监听变更

---

## 补充说明

- 工具以 MIT 许可证开源，目标发布到 GitHub + Gitee 双镜像
- 优先实现 SQL Server + MySQL 支持，PostgreSQL 和 SQLite 在后续迭代中补全
- 界面语言暂定中文，国际化资源预留但不在本次 MVP 范围内
