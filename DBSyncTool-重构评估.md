# DBSyncTool 重构评估：语言选型与功能补强

> 评估对象：`wangbenchi66/DBSyncTool`
> 核查基线：`c5dbb2d`（2026-09-16）；工作区另有三处未提交改动（直连比对相关）
> 评估时间：2026-11-27
> 结论有效期：架构类结论长期有效；语言选型取决于第 2.3 节的三个决策条件

**证据等级约定**（全文统一使用）：

| 等级 | 含义 |
| :--- | :--- |
| **读码确认** | 直接命中代码路径与调用点，可复现，不依赖运行时 |
| **静态推断** | 由代码路径推出，但最终结果取决于数据库行为，未实测 |
| **行业判断** | 经验性意见，需结合自身场景权衡 |

> 本版相对初稿修正了 20 余处事实与推断（行数口径、循环外键后果、跨方言比较根因、v2.5 归属等），逐条对照见附录 B。凡与初稿冲突处，以本版为准。

---

## 0. 结论摘要

1. **"不好用"不是 .NET 的锅，是五个结构性决策的叠加**：值没有类型、行哈希交给数据库算、差异模型不完整、脚本由界面拼装、测试不覆盖"生成的 SQL 能否执行"。换语言能改善分发与观感，但这五点不解决，新版本会把同样的 bug 装进更漂亮的壳里。
2. **语言建议**：默认 **Go**（核心与 CLI 用 Go，GUI 用 Wails）；但只要命中第 2.3 节任一"留在 C# 的条件"（Windows 集成认证刚需、Always Encrypted/TDS 8/MARS/Azure AD 完整链、必须原生桌面观感），就**留在 C# 重写 Core、保留 Avalonia**。
3. **原版的问题规模**：10 条 P0（会产出非法脚本、静默丢数据或漏表），另有 11 条 P1 能力缺口。10 条 P0 的缺陷机制都是读码确认的；其中 #7、#8 的**最终后果**（目标库是否接受隐式转换、跨 locale 是否真的出错）仍需实测。最刺眼的不是单点 bug，而是**同一类契约缺失反复出现**——调用方只传主键、生成器默认缺列填 `NULL`、比较器不看类型名、环内表被排序器丢弃。
4. **文档与代码的落差本身就是风险**：`docs/roadmap.md` 把 v2.2/v2.4/v2.5 标成"✅ 已完成"，但过滤规则面板、执行确认对话框、视图/存储过程比对在代码里分别是**未接线**、**不存在**、**返回空列表的存根**；README 宣称的"回滚脚本"从未生成。使用者会据此建立错误预期。
5. **推荐的实施顺序**：先 CLI、后 GUI；先正确性、后体验。核心正确性没解决时做 UI，等于把 bug 包进更好看的壳里——这正是原版走过的路。

---

## 1. 体检基线（实测）

统计口径：物理行（按换行计），排除 `obj/`、`bin/`；`HEAD` 为 `c5dbb2d`，工作区含未提交改动。命令见附录 A。

| 指标 | 实测（HEAD） | 初稿值 | 说明 |
| :--- | :--- | :--- | :--- |
| 代码量 | Core **7,191** / Desktop **10,520**（8,038 `.cs` + 2,482 `.axaml`）/ CLI **399** / Tests **2,661**，合计 **20,771** | 6,279 / 9,377 / 347 / 2,249 | 初稿是**非空行**口径且未说明；Desktop 的 9,377 已含 `.axaml` |
| 最大单文件 | [`CompareViewModel.cs`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs) **1,889** 行；[`DirectCompareViewModel.cs`](src/DBSync.Desktop/ViewModels/DirectCompareViewModel.cs) **1,224** 行（HEAD）/ **1,311** 行（工作区） | 1,644 / 1,057 | 初稿是 HEAD 的非空行；直连文件已被未提交改动加长 |
| 测试 | **122** 个属性（120 `[Fact]` + 2 `[Theory]`），其中 **1 个 `[Fact(Skip)]`** 是需要 LocalDB 的真库测试；可执行的真库集成测试 **0**；SQL golden 文件 **0** | 121 / 0 / 0 | 初稿漏掉 Skip 那个；"零真实数据库测试"应表述为"零个会执行的" |
| 最近提交 | 2026-09-16；近 10 次里 **7 次**是发布/打包/CI（若把 `0793bd8` 记为功能则 6 次）；"删掉失效的行指纹往返测试"是**第 13 次**提交（`ef795f6`） | 2026-09-16 / 6 次 / 在近 10 次里 | 日期正确，另外两项口径有误 |
| 待办标记 | 源码/已跟踪文本中 `TODO/FIXME/HACK` **0 处**，但存在"v2.5 存根实现，后续填充"这类注释 | 0 | 数字对，结论要补：没有显式标记 ≠ 没有已知缺口 |

**这说明什么**：模型层（`record` 不可变、接口分层、中文注释）工程质量不差；但测试集中在**纯函数**与**字符串包含断言**上，唯一的高风险契约——"生成的 SQL 能不能在真库上跑通"——完全没有覆盖。更麻烦的是，仓库文档已经提前宣称了多个"已完成"的功能，而代码里是存根或未接线，这会让使用者对工具边界产生系统性误判。

---

## 2. 语言选型

### 2.1 先认清这个工具真正的难点

| 难点 | 工作量占比（估算） | 语言相关性 |
| :--- | :--- | :--- |
| 方言保真：元数据怎么读、DDL 怎么发、类型怎么映射 | 约 45% | **低**（都是拼 SQL + 协议库） |
| 差异引擎：结构对比、数据归并、依赖排序 | 约 20% | 中（流式/并发写起来是否顺手） |
| 快照格式与加解密 | 约 10% | 低 |
| CLI、报告、CI 集成 | 约 10% | 低 |
| GUI | 约 15% | **高** |

初稿说"语言权重不到 20%"，方向正确但过于绝对；按上面的拆解，**语言真正影响的是 20%–30% 的工作量**（并发模型、GUI、分发），剩下 70% 以上是"SQL 与元数据的体力活"，换什么语言都要重做。

所以选语言应该按"分发 + 驱动 + UI 迭代速度"来选，而不是按"哪个语言更先进"。

### 2.2 候选对比

权重：驱动成熟度 30%、分发部署 20%、GUI 能力 20%、大表流式处理 15%、单人+AI 迭代效率 15%（1–5 分）。

| 方案 | 驱动 | 分发 | GUI | 流式 | 效率 | 加权 | 一句话 |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **C#/.NET 10** | 5 | 3 | 4 | 4 | 5 | **4.25** | 驱动最强（`Microsoft.Data.SqlClient` 是参考实现），Avalonia 已验证 |
| **Go** | 4 | 5 | 3 | 5 | 4 | **4.15** | 单文件 ~15 MB、毫秒启动、交叉编译一行命令 |
| TS + Electron | 4 | 1 | 5 | 3 | 5 | 3.60 | UI 最快，但 150 MB+、启动慢，DBA 工具最不该选 |
| Rust + Tauri | 3 | 5 | 3 | 5 | 2 | 3.55 | 最小最快，但 SQL Server 驱动 `tiberius` 偏弱、开发慢 |
| Python + Qt | 3 | 1 | 3 | 2 | 5 | 2.75 | 适合自用，不适合分发 |

**矩阵不是判决书，先看敏感度。** 记权重为 `w`，则 Go 相对 C# 的加权差为：

```
Go − C# = −w驱动 + 2·w分发 − wGUI + w流式 − w效率
基准权重下 = −0.30 + 0.40 − 0.20 + 0.15 − 0.15 = −0.10（C# 领先 0.10）
```

反超条件为 `2·w分发 + w流式 > w驱动 + wGUI + w效率`。代入基准值即 `0.55 > 0.65`，不成立。换算成单变量阈值：

- 「分发」权重从 20% 提到 **25% 以上** → Go 反超；
- 或「GUI」权重从 20% 降到 **10% 以下** → Go 反超；
- 或「驱动」权重从 30% 降到 **20% 以下** → Go 反超。

也就是说：**C# 与 Go 的 0.10 分差完全落在权重的主观误差内**。真正决定结论的是下面三个二值条件，而不是这张表。

### 2.3 决策规则（明确结论，不打太极）

```
if 用户环境大量使用 Windows 集成认证 / Kerberos        → C#（保留 Avalonia，重写 Core）
else if 需要 Always Encrypted / TDS 8 / MARS / Azure AD 完整链 → C#（同上）
else if 必须原生桌面观感，不接受 WebView2               → C#（同上）
else                                                  → Go + Wails，重写
```

**默认选 Go。** 理由：

1. **价值的大头在无界面部分**：差异引擎 + 脚本生成 + CI 集成。GUI 只是差异查看器，用 WebView 做树形 diff 和行级并排对比，体验只会更好。
2. **分发是硬指标**：Go 编出 ~15 MB 单文件、一行交叉编译、启动 10 ms 级；.NET 自包含单文件 60–80 MB、启动 300 ms+。仓库已经为此做过妥协——[`docs/memory-and-size-optimization.md`](docs/memory-and-size-optimization.md) 记录的就是 Trim/压缩/去依赖/关 Server GC 这一串动作。
3. **这个细分领域就是 Go 的天下**：`sqldef`、`Atlas`、`skeema`、`gh-ost` 都是 Go，元数据读取、DDL 生成、流式归并有大量可对照实现。
4. **并发模型天然适配**"两端同时流式读 + 归并"：goroutine + channel 几十行就能写对。

**第三个选项（容易被忽略）**：保留已跑通的 Avalonia 外壳，只把 Core 换成 Go 进程（CLI + JSON-RPC）。代价是多一个二进制与跨进程协议、分发体积仍受 .NET UI 影响；收益是**不用重写 UI、可以按模块渐进替换**。如果第 10 节第 3 问的答案是"先别推倒重来"，这是比"原地重构 Core"更干净的中间态。

### 2.4 明确不推荐

- **Electron**：150 MB 安装包 + 冷启动 2 秒，与"内网 U 盘拷过去就能用"的产品调性冲突。
- **Python**：PyInstaller 打出来的包杀软误报、体积 100 MB+、跨平台坑多。
- **纯 Rust**：除非打算长期深耕并愿意付 2–3 倍开发时间，否则性价比低。

### 2.5 反方观点与风险（选 Go 必须提前认账）

| 风险 | 具体表现 | 缓解 |
| :--- | :--- | :--- |
| **Wails 依赖 WebView2** | Windows 10 内网机器可能没装 WebView2 Runtime；离线环境需预置 Evergreen 安装器或固定版本 | 安装包内置引导；启动时检测并给出明确提示 |
| **SQL Server 认证能力收窄** | `go-mssqldb` 的 Windows 集成认证靠 `winsspi`，Always Encrypted、MARS、TDS 8、Azure AD 链覆盖显著弱于官方驱动 | 若这三条是刚需，直接选 C#，不要赌 |
| **重写期的"方言坑"要重踩** | 这两万行里沉淀的元数据查询、类型映射、怪癖处理不会自动迁移 | 先建 golden fixtures 与"坏例回归集"，把原版行为固化成测试 |
| **单人 + AI 的工期风险** | 第 9 节的 7–12 周是乐观值；方言细节与 GUI 打磨通常超支 50%+ | 每个里程碑设"可放弃点"；UI 永远最后做 |
| **双轨维护** | 迁移期老版本继续修 bug，新版本没有用户，精力被摊薄 | 明确冻结策略：老版本只修 P0，新功能只进新版 |
| **生态与招聘** | Windows 企业内网里 C# 更常见，Go 的桌面/Windows 经验相对少 | 产品自用或小团队时影响有限 |

---

## 3. 原版问题清单（全部带代码证据）

严重度：**P0** = 会产出错误/非法脚本，或静默丢数据/漏表；**P1** = 能力缺失或语义错；**P2** = 性能与体积；**P3** = 可维护性；**P4** = 易用性。

> 编号沿用初稿，便于与旧版对照；因此同一节内编号不连续。新增条目为 39–41。

### 3.1 P0：正确性（10 条）

| # | 现象 | 根因 | 证据 | 等级 |
| :-- | :--- | :--- | :--- | :--- |
| 1 | **勾选"生成 UPDATE"后产出非法 SQL**：`UPDATE T SET  WHERE id = N'1';` | 变更行只带主键值（`RowHash` 结构上只有主键 + 整行哈希），生成器又把主键列从 SET 过滤掉，SET 为空 | [`CompareViewModel.cs:777`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs#L777) + [`SqlServerSqlGenerator.cs:470-475`](src/DBSync.Core/SqlGenerators/SqlServerSqlGenerator.cs#L470-L475) | 读码确认 |
| 2 | **二进制列写入快照后变成字符串 `System.Byte[]`** | `Convert.ToString(byte[])` 返回类型名；导入端无类型还原，最终生成 `N'System.Byte[]'` | [`SnapshotExporter.cs:180`](src/DBSync.Core/Snapshot/SnapshotExporter.cs#L180) | 读码确认 |
| 3 | **含换行的文本字段静默破坏快照数据** | 导出端按 CSV 加引号并写出换行；导入端逐行 `ReadLineAsync`，无跨行状态机 | [`SnapshotExporter.cs:183`](src/DBSync.Core/Snapshot/SnapshotExporter.cs#L183) vs [`SnapshotLoader.cs:206`](src/DBSync.Core/Snapshot/SnapshotLoader.cs#L206) | 读码确认 |
| 4 | **真值 `\N` 被静默当成 NULL** | 用裸 `\N` 表示 NULL，反斜杠从不转义，解析端精确匹配 `\N` | [`SnapshotLoader.cs:213`](src/DBSync.Core/Snapshot/SnapshotLoader.cs#L213) | 读码确认 |
| 5 | **主键仅大小写不同的两行 → 比对直接失败** | `ToDictionary(..., OrdinalIgnoreCase)` 撞键抛异常，被上层捕获成"比对失败"/退出码 2 | [`DataComparer.cs:25-26`](src/DBSync.Core/Comparers/DataComparer.cs#L25-L26) | 读码确认 |
| 6 | **跨 schema 同名表（`dbo.X` / `sales.X`）→ 比对失败** | 拓扑排序用 `t.Name` 而非 `FullName` 建字典，重复键抛异常 | [`FkTopologicalSorter.cs:19-21`](src/DBSync.Core/Comparers/FkTopologicalSorter.cs#L19-L21)，经 [`SchemaComparer.cs:45`](src/DBSync.Core/Comparers/SchemaComparer.cs#L45) 触发 | 读码确认 |
| 7 | **所有 DML 字面量一律当字符串**：日期/数值/布尔/二进制靠隐式转换 | `FormatSqlLiteral` 没有任何类型分支 | [`SqlServerSqlGenerator.cs:448-453`](src/DBSync.Core/SqlGenerators/SqlServerSqlGenerator.cs#L448-L453) | 读码确认 |
| 8 | **数值/日期经 `Convert.ToString` 落到区域设置**，同一份数据在不同机器上导出成不同文本 | 未使用 `InvariantCulture` | [`SnapshotExporter.cs:180`](src/DBSync.Core/Snapshot/SnapshotExporter.cs#L180) | 读码确认 |
| 11 | **循环外键（含自引用外键）的表被静默丢弃**：不建表、不插数据、也不报错 | 拓扑排序只把入度为 0 的节点入队，环内节点永远不入队、不在 `Sorted` 里；生成器只对 `Sorted` 建表 | [`FkTopologicalSorter.cs:35-49`](src/DBSync.Core/Comparers/FkTopologicalSorter.cs#L35-L49) + [`SqlServerSqlGenerator.cs:359-371`](src/DBSync.Core/SqlGenerators/SqlServerSqlGenerator.cs#L359-L371) | 读码确认 |
| 39 | **直连比对生成的 INSERT 把非主键列写成 `NULL`** | 调用方只传主键值，生成器对缺失列取 `null` | [`DirectCompareViewModel.cs:1012`](src/DBSync.Desktop/ViewModels/DirectCompareViewModel.cs#L1012) + [`SqlServerSqlGenerator.cs:441`](src/DBSync.Core/SqlGenerators/SqlServerSqlGenerator.cs#L441) | 读码确认 |

**逐条补充说明（影响面与触发条件）**

- **#1 范围比初稿大**：加载比对有 3 处相同的 UPDATE 组装（`775-783`、`908-913`、`1556-1563`），直连比对还有 1 处（`1032-1041`）。CLI 的 `script` 命令**根本不生成 UPDATE/DELETE**，所以不是"CLI 是对的"，而是"CLI 没这功能"。生成器四个方言都没有"SET 为空"的兜底。单元测试抓不到，是因为测试喂的是带 `Name`/`Age` 的完整行——**测试与真实调用契约不一致**，这是 #1 这类 bug 的共同温床。
- **#2 有前提**：只有该表被勾成"结构+数据"（`SyncData = true`，默认 `false`）时才写 `data_full`。行指纹通道（JSON）不受影响，所以现象是"差异判得对、语句写坏了"。四个方言同样中招，因为导出器与方言无关。
- **#3 后果要改写**：不是"后续列右移"，而是——当前行在换行字段处**被截断**，其后所有列取 `null`；余下片段被当成**一条伪行**；不抛异常，是静默损坏。若截断的恰好是主键列，后续 `ResolveRowsToInsert` 匹配失败还会回退成"只带主键的行"，非主键列全变 `NULL`，形成复合放大。
- **#4 无转义**：`EscapeCsv` 只处理 `"`、`,`、`\r`、`\n`，从不处理反斜杠；解析端也没有 `\\` 还原。字面量 `\N` 与 NULL 同形；带引号包裹也照样在去引号后命中。
- **#5 不是"崩溃"**：异常在 [`CompareViewModel.cs:623`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs#L623)、[`DirectCompareViewModel.cs:706`](src/DBSync.Desktop/ViewModels/DirectCompareViewModel.cs#L706)、[`Program.cs:58`](src/DBSync.CLI/Program.cs#L58) 被捕获。触发面也不止"大小写敏感库"：PostgreSQL 文本主键、SQLite BINARY、MySQL `_bin` 排序规则都是**默认即命中**。
- **#6 抛出点在比对阶段**，不是生成脚本时。直连比对默认 `IgnoreSchemaPrefix = true`，且 `ToKeyedDictionary` 对重复键保留首个，**默认规避**；取消"忽略库名前缀"后才触发。另有一层隐患：外键的 `ReferencedTable` 是裸表名，即便不抛，跨 schema 的外键也会挂到同名不同 schema 的表上，排序结果本身不可信。
- **#7 需限定**：`N'…'` 只有 SQL Server 方言有；MySQL/PG/SQLite 是普通 `'…'`，但"无类型分支"是共性。只影响 DML；DDL 的默认值原样拼进列定义。二进制**必然**丢内容（上游已是 `System.Byte[]`），其余类型"能否隐式转过去"取决于方言与目标库设置，未实测。
- **#8 措辞要改**："同一快照在不同 locale 机器上**解析**结果不同"不成立——加载端不做任何数值/日期解析，只存字符串。真实风险是：①文件内容随**导出**机器的区域设置变化；②生成的 `N'…'` 由**目标库**按自身日期/语言设置隐式转换。初稿还漏了同类问题：SQL Server / MySQL / PostgreSQL 的指纹器取**主键值**时同样没走 `InvariantCulture`（[`SqlServerDataFingerprinter.cs:48`](src/DBSync.Core/Data/SqlServerDataFingerprinter.cs#L48)、MySQL/PG 的 `:37`），只有 SQLite 用了 Invariant。主键串既用于两端配对又用于 WHERE，跨 locale 会凭空产生成对的 delete + insert。
- **#11 初稿说反了**：环内表不是"仍按内联 FK 建表 → 执行必失败"，而是**从排序结果里消失**，既不建表也不生成 INSERT，执行不报错、只是表没了。自引用外键（`parent_id` 这种最常见形态）同样中招。注释列表来自**全库**表求环，与本次新增表无关，所以提示还常常是误导。这条比初稿描述的更严重，因此上调为 P0。
- **#39 直连 INSERT**：与 #1 同源。`RowsToInsert` 同样只取 `PrimaryKeyValues`，生成器按整表列名拼 SQL、缺失列取 `null`，于是非主键列全被写成 `NULL`。这是 HEAD 就存在的问题，不是未提交改动引入的。

### 3.2 P1：能力缺口与语义错误（11 条）

| # | 现象 | 真相 | 证据 | 等级 |
| :-- | :--- | :--- | :--- | :--- |
| 9 | [`docs/roadmap.md:335`](docs/roadmap.md#L335) 把 **v2.5 视图/存储过程/函数/触发器比对**标为"✅ 已完成" | **从未实现**：四个方言的 `ReadAllObjectsAsync` 全是 `return []` 的**存根**，无生产调用；`ObjectDiff` 从未被构造；`SchemaComparer` 从不给 `ObjectDiffs` 赋值；生成器也没有对象 DDL 方法 | [`SqlServerSchemaReader.cs:595-601`](src/DBSync.Core/Schema/SqlServerSchemaReader.cs#L595-L601)（另三个方言同形）+ [`SchemaComparer.cs:47-53`](src/DBSync.Core/Comparers/SchemaComparer.cs#L47-L53) | 读码确认 |
| 10 | README 宣称"**外键**等逐项比对" | **外键差异完全没比**：数据已读进 `TableModel.ForeignKeys`，但 `TableDiff` 没有 FK 字段，`GenerateAlterTable` 不生成 FK 增删。给已有表加外键**永远不会**出现在脚本里 | [`SchemaDiff.cs:74-110`](src/DBSync.Core/Models/SchemaDiff.cs#L74-L110) + [`SqlServerSqlGenerator.cs:186-229`](src/DBSync.Core/SqlGenerators/SqlServerSqlGenerator.cs#L186-L229) | 读码确认 |
| 12 | 改主键直接 `DROP CONSTRAINT` 再 `ADD` | 不先删依赖它的外键 → 执行失败；且约束名是**拼出来的** `PK_{表名}`，生产库实际名（如 `PK__Users__3214EC07…`）不同时会先报"约束不存在" | [`SqlServerSqlGenerator.cs:212-217`](src/DBSync.Core/SqlGenerators/SqlServerSqlGenerator.cs#L212-L217)、[`:394-397`](src/DBSync.Core/SqlGenerators/SqlServerSqlGenerator.cs#L394-L397) | 读码确认 |
| 13 | 未比对：**检查约束、排序规则、自增种子/步长、计算列、序列** | 成立。但要更正三处：①唯一约束**以索引身份**参与了比较与生成；②列顺序字段 `OrdinalPosition` **存在**，只是没参与比较；③`MS_Description` 已读。另有两处更糟：`IDENTITY(1,1)` 是写死的，计算列会被当普通列读进来 | [`ColumnModel.cs`](src/DBSync.Core/Models/ColumnModel.cs)、[`SqlServerSqlGenerator.cs:271`](src/DBSync.Core/SqlGenerators/SqlServerSqlGenerator.cs#L271) | 读码确认 |
| 14 | `IgnoreColumnOrder` 是死开关 | 比初稿更彻底：该属性从未被比较器读取；`ApplyFilter` / `SaveProject` / `LoadProject` 三个命令**没有任何视图绑定**；直连比对把过滤器传成 `null`。列顺序根本不参与比较 | [`FilterOptions.cs:28`](src/DBSync.Core/Models/FilterOptions.cs#L28) + [`SchemaComparer.cs:150-158`](src/DBSync.Core/Comparers/SchemaComparer.cs#L150-L158) + [`DirectCompareViewModel.cs:647`](src/DBSync.Desktop/ViewModels/DirectCompareViewModel.cs#L647) | 读码确认 |
| 15 | 跨方言比对不可用 | **根因与初稿相反**：`DbTypeName` **根本不参与比较**，比较的是通用类型枚举 + 长度 + 精度 + 可空 + identity + 默认值文本 + 注释。结果是**漏报**同族类型变化（`varchar`↔`nvarchar`、`char`↔`varchar`、`datetime`↔`datetime2` 都判相等）；跨方言误报主要来自 `IsIdentity` 与默认值文本。另外快照模式**按 DbType 过滤连接**，根本不支持跨方言 | [`SchemaComparer.cs:150-158`](src/DBSync.Core/Comparers/SchemaComparer.cs#L150-L158) + [`CompareViewModel.cs:826`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs#L826) | 读码确认 |
| 16 | 默认值 `((0))` vs `(0)` 误报差异 | 成立。比较是 `OrdinalIgnoreCase` 的原样文本，默认值未做规范化（去冗余括号、去 CAST、统一函数名大小写） | [`SchemaComparer.cs:157`](src/DBSync.Core/Comparers/SchemaComparer.cs#L157) | 读码确认 |
| 17 | 重命名无识别 | 拆开看：**列重命名** → 旧列 DROP + 新列 ADD，且"结构变更"节点**默认勾选**，默认脚本就会丢列数据；**表重命名** → 建新表、旧表默认**不删**（节点默认不勾选，CLI 也从不 DROP），所以初稿"表也 DROP + ADD"不成立 | [`CompareViewModel.cs:1240`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs#L1240)、[`:1248`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs#L1248) | 读码确认 |
| 18 | MySQL 上"事务包裹 + 失败自动回滚"是假的 | 机制成立：MySQL DDL 隐式提交。但"UI 仍宣传自动回滚"不成立——**Desktop 根本没有执行入口**，只有 CLI 的 `execute` | [`MySqlSqlGenerator.cs:30-34`](src/DBSync.Core/SqlGenerators/MySqlSqlGenerator.cs#L30-L34) + [`Program.cs:267-315`](src/DBSync.CLI/Program.cs#L267-L315) | 读码确认 |
| 40 | README 宣称"含回滚脚本"、roadmap 宣称"失败自动回滚" | **回滚脚本从未生成**（全仓无反向生成逻辑）；执行失败时 `RolledBack` 只是"脚本文本含 `BEGIN`"的**假标志**，从不发 `ROLLBACK`。MySQL 的 `START TRANSACTION` 甚至不含 `BEGIN`，连这个假标志都不会置位 | [`README.md:68`](README.md#L68)、[`roadmap.md:311`](docs/roadmap.md#L311)、[`ScriptExecutor.cs:147`](src/DBSync.Core/Execution/ScriptExecutor.cs#L147) | 读码确认 |
| 41 | 加载比对里**结构方向与数据方向相反** | 结构用 `Compare(快照, 当前库)`，数据用 `Compare(当前库行, 快照行)`：结构 DDL 把当前库当 source，数据 DML 又把当前库当 baseline。直连比对是自洽的。叠加 UI 把当前库标成"目标库"、`OnlyTargetSchemaNodes` 里装的其实是"源库独有"表 | [`CompareViewModel.cs:565`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs#L565) vs [`:609`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs#L609)、[`:1230-1235`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs#L1230-L1235) | 读码确认 |

### 3.3 P2：性能与体积

| # | 现象 | 根因 | 证据 |
| :-- | :--- | :--- | :--- |
| 19 | **快照体积 ≈ 全量数据**（gzip 后通常小于库文件，但同阶） | 凡是勾了"同步数据"的表一律全量落盘 `data_full/*.csv.gz`；导出发生在比对**之前**，此时不存在"新增行"这个概念 | [`SnapshotExporter.cs:73-77`](src/DBSync.Core/Snapshot/SnapshotExporter.cs#L73-L77) |
| 20 | **每张表被完整扫两遍** | 一遍算指纹、一遍取全量，且是**两个独立连接**、两次带排序的全表扫描 | [`SnapshotExporter.cs:75-76`](src/DBSync.Core/Snapshot/SnapshotExporter.cs#L75-L76) |
| 21 | **比对阶段全内存** | 两端各 `ToDictionary`；活库侧先把整个结果集攒进 `List`；快照加载也是 `ReadToEnd` + 全量 `List`。百万行级别确有 OOM 风险（每端数百 MB 量级） | [`DataComparer.cs:25-26`](src/DBSync.Core/Comparers/DataComparer.cs#L25-L26)、[`SnapshotLoader.cs:193-220`](src/DBSync.Core/Snapshot/SnapshotLoader.cs#L193-L220) |
| 22 | **没有表级校验和**，无法跳过"完全没变"的表 | 无此设计；快照模式只全扫活库一侧，直连模式两端全扫。唯一的跳过条件是无主键或只导了结构 | 无此字段（`SnapshotManifest` 见 [`Snapshot.cs:21-47`](src/DBSync.Core/Models/Snapshot.cs#L21-L47)） |
| 23 | **行哈希有分隔符歧义**：值里含 `\|` 会跨列混淆；字面量 `'NULL'` 与真 NULL 同哈希 | `CONCAT_WS('\|', …)` + `COALESCE(x,'NULL')`，四个方言同病；覆盖的是**全列**而非仅主键。更严重的是主键基准串也用 `\|` 拼接，会让**两行直接配错对** | [`SqlServerDataFingerprinter.cs:99`](src/DBSync.Core/Data/SqlServerDataFingerprinter.cs#L99)、[`:130`](src/DBSync.Core/Data/SqlServerDataFingerprinter.cs#L130)、[`RowHash.cs:21-22`](src/DBSync.Core/Models/RowHash.cs#L21-L22) |
| 24 | **跨方言同一行可能哈希不同** | 不是"MD5 弱"，而是**编码/格式不统一**：SQL Server 对 NVARCHAR 走 UTF-16LE、`HASHBYTES` 日期 3 位小数、二进制大写 hex；MySQL/PG 走 utf8、日期位数与 hex 大小写各不相同。仅影响直连混选方言，同方言跨库一致 | 四个 `*DataFingerprinter` 的格式化分支 |

### 3.4 P3：架构与可维护性

| # | 问题 | 证据 |
| :-- | :--- | :--- |
| 25 | **同一套比对 UI 写了两遍**：直连 VM 非空行中 **58.6%** 与加载 VM 逐字相同；两套 XAML 非空行重合 **85%**；**16 个同名 `ObservableProperty`**、12 组同名/同义方法。另外 [`SyncWorkflowView.axaml`](src/DBSync.Desktop/Views/SyncWorkflowView.axaml)（569 行）内嵌了两份旧 UI，但导航已移除该页面——**死代码** | 见 [`CompareViewModel.cs`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs) 与 [`DirectCompareViewModel.cs`](src/DBSync.Desktop/ViewModels/DirectCompareViewModel.cs) |
| 26 | **脚本生成逻辑写在 ViewModel 里**（`BuildUpgradeScriptFromNodes`），CLI 拿不到；README 宣称"两种模式结果完全一致"**不成立** | GUI 按节点逐表拼（[`CompareViewModel.cs:687`](src/DBSync.Desktop/ViewModels/CompareViewModel.cs#L687)），CLI 走 `ISqlGenerator.GenerateUpgradeScript`。差异至少有 6 处：CLI 不生成 DROP/UPDATE/DELETE、排序不同（拓扑 vs 界面顺序）、头注释不同、选项不同（无排除自增列）、方言来源不同（加载用目标库类型、直连用源库类型）、直连 INSERT 只传主键 |
| 27 | 用 `node.Title.Split('（')[0]` **从显示文本反解表名** | 共 **7 处**；表名本身含全角括号就会被截断，且查找失败是**静默跳过**（少生成语句，不报错） |
| 28 | `ISqlGenerator` 方法多、门面再套一层 | 接口实际是 **7 个**方法（初稿"12 个"不成立）；四个方言各自另有一组成对重载；门面 [`DatabaseSqlGenerator`](src/DBSync.Core/SqlGenerators/DatabaseSqlGenerator.cs) 还多暴露了一个接口没有的 `GenerateDdlScript`；改接口要动 **6 个**文件（接口 + 门面 + 4 方言） |
| 29 | SqlSugar 读元数据，Core 里又混用 4 个原生驱动 | 精确说：SqlSugar **只用于 SQL Server 元数据 + 脚本执行**；4 个驱动是 `sqlSugarCore` 的**传递依赖**，未在 csproj 声明，还顺带带入 Oracle/DM/Kingbase 适配包。`0793bd8` 确实做了"去 Easy 元包"的瘦身 |
| 30 | 密码走命令行参数 | 精确说：`export --password` 是**快照 AES 加密密码**；`compare/script` 用 `--snapshot-password`；**数据库密码在 `--connection` 连接串里**。三者都会进进程列表/shell history，且无环境变量、无 stdin 交互替代 |

### 3.5 P4：为什么用户觉得"不好用"

| # | 体验问题 | 证据 | 该怎么改 |
| :-- | :--- | :--- | :--- |
| 31 | **看不到行级差异**，只显示"3 行变更"；连列级结构差异也没渲染（`Children` 节点建了，但两个 XAML 都没有 `TreeView`） | 模型层只有"主键 + 整行哈希"，没有任何列值 | 行级并排、按列高亮；结构差异树真正绑定 |
| 32 | **无法只更新变化的列** | 只有整行哈希，没有列级哈希；且当前 UPDATE 因 #1 根本不可用 | 列级哈希 → 只 SET 变化列 + 并发守卫 |
| 33 | **命名与文案把自己绕晕**：`baseline` = 目标库、`source` = 源库，而 UI 把当前库标成"目标库"、分类标签与内容错位 | 见 #41 | 统一 source → target，UI/报告/枚举注释一并对齐 |
| 34 | **每次比对都全量重读行数据**，无缓存、无增量 | 无表级校验和；但"改一个勾选就重算"不成立——勾选只重拼 SQL，不碰数据库 | 差异结果缓存 + 节点勾选只重拼脚本（方向已对，需彻底） |
| 35 | **加载比对必须先有快照**，即使两库可直连 | 直连模式已是 v2.3 的独立功能，不是补丁；问题是两套页面各写一遍 | 统一为"数据源适配器 + 单一 Diff 视图" |
| 36 | 无"仅同步选中列/选中行"的粒度 | 选择粒度只到"表 + 结构/增/删/改"四个开关；唯一的例外是 DROP 表默认不勾选 | 列级、行级选择 |
| 37 | 报告只有 Markdown/HTML，且只有加载比对页有 | CLI 已有 JSON/text + 0/1/2 退出码（**正式契约**，不是"雏形"） | 补 SARIF/JUnit 与 `--fail-on` 策略 |
| 38 | 没有"已应用基线"概念 | 历史只记文件路径；项目文件不含"已应用到哪个库"；GUI 连执行入口都没有 | 记录已执行脚本/版本，下次只算增量 |

---

## 4. 根因追溯：症状不是 41 个，是 5 类

把 3 节的条目按"根因"归并，才能看清为什么修一个 bug 会冒出三个新的：

| 根因 | 直接导致的症状 | 新架构中的对应组件 |
| :--- | :--- | :--- |
| **A. 值没有类型**（全链路 `string`） | 1、2、3、4、7、8、39 | 类型化 `Value` + NDJSON 快照 + 类型化字面量渲染 |
| **B. 哈希在数据库侧算** | 5、21、23、24、34 | 客户端哈希（BLAKE3/xxh3）+ 流式归并 |
| **C. 差异模型不完整** | 9、10、11、12、13、15、16、17、41 | 完整结构 IR + 类型等价层 + 依赖图 + 分阶段计划 |
| **D. 脚本由界面拼装、两份 UI** | 1、25、26、27、28、33、39 | `plan` 层 + 单一 Diff 视图 + `Source` 适配器 |
| **E. 测试不覆盖契约** | 全部 P0（尤其 1、3、11、39） | 集成矩阵 + golden + 调用点契约测试 |

**这张表也是重构的验收清单**：五类根因各自的修复组件必须到位，否则就是"换个语言继续修 41 个点"。

---

## 5. 新版本建议架构

### 5.1 分层

```
dbsync/
├── cmd/dbsync/            # CLI（cobra 或手写）→ 唯一入口，GUI 也调它下面的包
├── internal/model/        # 类型化 IR：Schema/Table/Column/Index/Constraint/Object/Value
├── internal/dialect/      # 每方言：introspect / normalize / emit / typemap
│   ├── sqlserver/  mysql/  postgres/  sqlite/
├── internal/diff/         # schema diff、data diff（流式归并）、依赖图、改名识别
├── internal/snapshot/     # v2 格式：zip + NDJSON + （可选）age 加密
├── internal/plan/         # 变更计划：分阶段、阻断项、回滚脚本
├── internal/emit/         # SQL 渲染：类型化字面量、幂等 DDL
├── internal/report/       # json / md / html / sarif
├── internal/gui/          # Wails 绑定
├── web/                   # 前端（Svelte 或 React）
└── testdata/              # 每方言的 golden fixtures
```

**核心原则：CLI 是唯一实现，GUI 是它的可视化外壳。** 只有把"按节点勾选组装脚本"下沉到 `plan` 层，"两种模式结果完全一致"才是真的，而不是靠人肉对齐——原版反例见 #26。

### 5.2 类型化值模型（解决根因 A）

不要再用 `string` 承载列值：

```go
type Kind uint8
const ( KNull Kind = iota; KBool; KInt; KDecimal; KFloat; KText; KBytes; KTime; KJSON )

type Value struct {
    K Kind
    I int64; D decimal.Decimal; F float64; S string; B []byte
    T time.Time  // 统一存 UTC + 原偏移量
}
```

配套三件事：

1. **规范化编码**（用于哈希）：`kind || len || payload`，长度前缀，永远不靠分隔符——直接消灭 #23。
2. **类型化字面量渲染**：按 `K` 分别产出 `0x…`、`CAST('…' AS datetime2(7))`、`0/1`、不带引号的数值，且一律 InvariantCulture——消灭 #2、#7、#8。
3. **列级哈希**：行级哈希用于判断"变了没有"，列级哈希用于回答"哪几列变了"——这是 #32 的前提。

### 5.3 比对算法：流式归并 + 表级短路（解决根因 B）

```
1) 表级预检（一次查询/端）：
   用两端各自可算的确定性摘要（行数 + 按列类型的增量哈希聚合）
   → 两端不等才进入行级比对；相等直接跳过（多数表在这里结束）

2) 行级比对：两端各开一个按主键排序的游标，客户端归并
   if a.pk < b.pk  → deleted
   if a.pk > b.pk  → inserted
   else            → 比较 per-column hash，记录变化列
   → 内存 O(1)，只保留差异行

3) 无主键表：整行内容哈希 + 多重集比较（顺序无关）
```

**关键点**：行指纹在**客户端**算（BLAKE3 或 xxh3-128），不在数据库里算。跨方言语义完全由你掌控，不用再担心 `MD5()` 在 MySQL 与 PG 上行为不同（#24）。

> 表级预检要用"两端都能算出的同一种摘要"。`SUM(row_hash)` 这类方案会受类型与溢出影响，建议先定义清楚再实现；这里不假装它是免费的。

### 5.4 快照格式 v2（解决 A 类 + #19）

```
baseline.dbsync            # 就是一个 ZIP，unzip -l 能看
├── manifest.json          # 格式版本、工具版本、引擎+版本、生成时间、表清单、每表校验和
├── schema/<table>.json    # 类型化结构
├── data/<table>.ndjson    # 一行一个 JSON 对象，值按 5.2 编码
└── CHECKSUMS.txt
```

- **NDJSON 不是 CSV**：一行一个 JSON 对象，换行与 `\N` 在 JSON 里天然被转义——直接消灭 #3、#4。
- 结构透明、可被 `jq`/`unzip` 检查，出问题能自己看，而不是"文件坏了，重导一遍"。
- 加密改用 **AEAD**（age 或 AES-256-GCM）。现状是 AES-256-CBC + PKCS7、**无 MAC**、PBKDF2 10 万次（[`SnapshotFileFormat.cs:111-117`](src/DBSync.Core/Snapshot/SnapshotFileFormat.cs#L111-L117)）：错误密码通常因填充失败被发现，但篡改无法可靠检测，且 10 万次迭代低于 2026 年的建议值。
- 支持流式写读、逐行校验、断点续传；**只导差异所需的最小数据**（先算校验和，只对两端不同的表导全量行）。

### 5.5 结构 IR 与类型映射（解决根因 C）

- 对象覆盖：schema / table / column / index / **constraint(pk, unique, check, fk)** / view / matview / procedure / function / trigger / sequence / type / extension。
- **依赖图先行**：所有变更先排成 DAG，输出分阶段计划：`drop FKs → create tables → alter columns → add indexes → add FKs → data → views/procs`。**环与自引用 FK 拆两阶段**（先建表、后加约束），不能像现在这样把环内表直接丢掉（#11）。
- 约束名从元数据读取，不要拼 `PK_{表名}`（#12）。
- **类型映射三层判定**（跨方言比对唯一可行的做法）：

| 判定 | 含义 | 默认行为 |
| :--- | :--- | :--- |
| 等价 | `int` ↔ `integer` ↔ `int4` | 不报差异 |
| 可接受 | `varchar` ↔ `nvarchar`（编码差异）、`datetime` ↔ `timestamp` | 报"提示"，可一键忽略并记住 |
| 必须处理 | `text` ↔ `int`、精度收窄 | 报"阻断"，脚本里标注 |

- 比较前先**规范化**：默认值去冗余括号/CAST、标识符按方言规则折叠大小写、SQL 文本去注释与空白、`COLLATE` 显式化（#16）。
- 补上 identity 的种子/步长、计算列、检查约束、排序规则、序列（#13）。

### 5.6 脚本生成（解决 #1、#11、#12、#18、#40）

生成的不是"一串语句"，而是**一个带前置检查的变更计划**：

```sql
-- phase 0: preflight（只读，不通过就整体失败）
--   ✗ users.email 将设为 NOT NULL，但表内有 1,204 行 NULL
--   ✗ 将删除 products.legacy_id，被视图 v_product 引用
--   ⚠ 将重建 orders 主键，涉及 2 个外键，需先删除
-- phase 1: drop dependent FKs / drop dependent indexes
-- phase 2: DDL（幂等：IF NOT EXISTS / CREATE OR REPLACE）
-- phase 3: data DML（类型化、批量、可分批提交）
-- phase 4: add FKs / indexes
-- phase 5: 回滚脚本（能生成才生成，不能生成就明说）
```

- **幂等**：重复执行不报错（`IF EXISTS` / `IF NOT EXISTS`）。
- **阻断项**：执行前静态分析 + 可选的真实查询验证，把"跑一半失败"变成"跑之前就告诉你"。
- **UPDATE 只改变化的列**，并带并发守卫（`WHERE col = 旧值` 或 `rowversion`），避免覆盖生产上的并发修改。
- **诚实标注方言限制**：MySQL 输出 `-- 注意：MySQL DDL 自动提交，本段不可回滚`，不要用一个假 `RolledBack` 标志掩盖（#40）。
- **回滚脚本要么真生成，要么从文档里删掉**（当前 README 与实现不符）。

### 5.7 一个 Diff 视图（解决 #25、#26、#41）

```
interface Source {            // 快照文件、直连库、上次结果，三种实现
    Schema() Schema
    Rows(table) Iterator<Value[]>
}
```

界面只有一个 Diff 视图，数据源可换；"加载快照"和"直连"不再是两套代码，而是两次构造。方向统一为 **source → target**，脚本语义固定为"让 target 变成 source"，UI 文案、枚举注释、报告措辞一并对齐（#41）。

---

## 6. 功能优先级

### P0 — 不做就不能交付（正确性）

按依赖顺序排列（后一条依赖前一条）：

1. 类型化值模型 + 客户端哈希（根因 A/B 的地基）
2. 流式归并比对 + 表级短路（含无主键表的多重集比较）
3. 结构 IR 补全：外键、检查约束、唯一约束、默认值、identity 种子、计算列、序列
4. 依赖图 + 分阶段计划 + 阻断项 + **环/自引用两阶段**
5. 类型化 SQL 字面量 + InvariantCulture（修掉 `System.Byte[]`、`N'…'`、区域设置）
6. 列级哈希 + UPDATE 只改变化列（修掉空 SET 与直连 INSERT 写 NULL）
7. 快照 v2（可解包、NDJSON、AEAD、每表校验和）
8. 真实数据库集成测试矩阵 + SQL golden + **调用点契约测试**
9. 脚本组装下沉到 Core（单一实现，GUI/CLI 同源）
10. 事务/回滚语义如实标注，假标志下线

> 验收判据不是"代码写完"，而是：**Docker 里建表 → 比对 → 生成 → 执行 → 再比对 = 无差异**，四方言各跑一遍；且第 3 节每个 P0 都有一个先红后绿的回归用例。

### P1 — 决定"好不好用"

11. 行级并排 diff，按列高亮
12. 单一 Diff 视图 + Source 适配器（干掉两份 ViewModel）
13. 勾选到任意粒度（表/对象/列/行），实时重拼脚本
14. 项目文件（连接 + 过滤 + 策略）真正接线并保存复用
15. 执行前 dry-run、执行中逐句进度、失败定位
16. CI 模式：JSON + SARIF/JUnit + `--fail-on breaking` + 退出码
17. 改名识别（启发式 + 人工覆盖文件）
18. 类型映射的三层容忍度
19. 加载比对与直连比对的方向/文案统一

### P2 — 差异化与规模化

20. 三向比对（生产 / 预发 / 上次基线）——发布评审场景的刚需
21. "已应用基线"：记录执行过的脚本，下次只算增量
22. 定时漂移检测（生产被手工改动的告警）
23. 导出脱敏（快照带出内网时的合规需求）
24. 生成 Liquibase / Flyway / EF Core migration，而不只是裸 SQL
25. 分库分表、跨 schema、多库一次性比对

### 最小可交付版本（MVP）

**P0 的 1–8 + P1 的 11、12、16**：一个 CLI，能对四种方言做"结构 + 数据"比对、生成可执行且幂等的分阶段脚本、在 Docker 矩阵里自证正确、在 CI 里输出 JSON 与退出码。GUI 不上，先把"可信"立住。

---

## 7. 从旧项目能保留什么

| 保留 | 原因 |
| :--- | :--- |
| `record` 不可变模型风格 | 差异计算天然适合不可变数据 |
| 按方言拆 `Reader` / `Generator` 的目录结构 | 分层正确，换语言照搬 |
| **快照模式这个产品创意** | 内网隔离、零端口暴露、U 盘传递——Redgate/Atlas 都不覆盖，是护城河，务必保留 |
| `FilterOptions` 的形态 | 设计合理，但要把 UI 接线（#14） |
| CLI 的退出码约定（0/1/2） | 已是正式契约，CI 友好 |
| 中文注释与文档习惯 | 文档质量高于同类项目（但"完成度"声明要与代码一致） |

**算法要带走、实现要重写**：`FkTopologicalSorter` 的 Tarjan 求环思路是对的，但键必须换成 `FullName`，且环内表不能从排序结果里消失——现有实现会静默漏表（#11）。

**不复用**：CSV 数据通道、数据库侧哈希、`TableDiff` 模型、`CompareViewModel` 的脚本拼接、CBC 加密头、`Split('（')` 反解表名。

---

## 8. 测试策略（这是原版最大的空白）

| 层级 | 做法 |
| :--- | :--- |
| 单元 | 值编码 round-trip 用**属性测试**：随机生成含 emoji/控制字符/换行/`\N`/极值/负零/时区的值 |
| Golden | 每种方言一组 `(源 schema, 目标 schema) → 期望 SQL` fixture，改生成器必须过全量 |
| 契约 | 对每个生成器调用点断言"传入的数据形状满足生成器要求"（#1、#39 就是调用点与生成器契约不一致，纯单元测试永远抓不到） |
| 集成 | Docker 起 `mssql / mysql:8 / postgres:16 / sqlite`，真库建表、跑生成的脚本、再比对一次应当"无差异"（**幂等性自检**） |
| 模糊 | 快照解析器做 fuzz（多行、引号、`\N`、截断文件），断言"要么正确解析，要么明确报错" |
| 区域 | 在 `tr-TR` / `de-DE` / `en-US` 三种 locale 下跑同一份快照 round-trip（#8） |
| 回归 | 把第 3 节每个 P0 都变成一个失败用例，先红后绿 |

那个"跑完脚本再比对应当无差异"的自检特别值钱——它一次性覆盖 DDL 正确性、类型保真、数据往返。原版连一个可执行的真库测试都没有（唯一的那个被 `Skip` 了），这正是这些 P0 能活到发布的原因。

---

## 9. 建议的实施路线

| 里程碑 | 内容 | 产出判据 |
| :--- | :--- | :--- |
| M0（3–5 天） | 选型落锤、golden fixtures 骨架、**把第 3 节 P0 变成失败用例集** | 坏例集能跑，全部红 |
| M1（1–2 周） | 骨架 + 值模型 + SQL Server 方言 + 快照 v2 读写 | 快照 round-trip 无损（含二进制、换行、`\N`） |
| M2（2–3 周） | 流式比对 + 结构 IR + 约束差异 + 分阶段计划 | Docker 里跑通"建表→比对→生成→执行→再比对=无差异" |
| M3（2–3 周） | CLI 完整 + JSON/SARIF + golden/集成矩阵 | CI 可用，退出码/策略生效，四方言矩阵绿 |
| M4（2–4 周） | Wails GUI + 行级 diff + 项目文件 | 一个 Diff 视图服务两种数据源 |

**关键路径**：值模型 → 客户端哈希 → 流式归并 → 结构 IR → plan → emit → 集成矩阵。任何一环返工，后面全等。

**工期口径**：上表合计 **7–12 周**是**乐观值**（单人 + AI、需求不变、方言坑不超预期）；按同类项目经验，含返工的现实区间是 **12–18 周**。M0 不是可选项——它决定你是在"重写"还是在"重写并重踩一遍原版的坑"。

先做 M1+M2 的 CLI，UI 放最后。理由：核心正确性没解决时做 UI，等于把 bug 包进更好看的壳里——这正是原版的路径。

---

## 10. 需要你确认的三件事

1. **主力数据库是哪个？** 如果 SQL Server 占 80% 且用户环境用 Windows 集成认证，改推 C#（保留 Avalonia，重写 Core）。
2. **GUI 必须原生观感吗？** 如果可接受 WebView2（Wails），Go 没有悬念；若内网机器可能缺 WebView2 Runtime，要把预置方案算进成本。
3. **是重写还是原地重构？** 原地重构省 1–2 个月，但会带着 `CompareViewModel` 那 1,889 行、两份 UI、CSV 数据通道和"假回滚"走很久；重写能一次性把上面 P0 全清掉。第三条路是"保留 Avalonia 外壳、Core 换 Go 进程"，适合先验证核心再换 UI。

**默认建议**：**Go + CLI 先行 + Wails GUI，重写，保留"快照模式"产品创意。** 若第 1 问答案是"SQL Server 且集成认证刚需"，则改为 **C# 重写 Core、保留 Avalonia**。

---

## 附录 A：核查方法与边界

- **基线**：`HEAD = c5dbb2d`（2026-09-16），工作区另有三处未提交改动（`DirectCompareViewModel.cs`、`DirectCompareView.axaml`、`DirectCompareViewModelTests.cs`），涉及行数为 1,311 的那份文件已按工作区计。
- **行数**：`[IO.File]::ReadAllLines()` 计数（物理行，等于换行数），`git ls-files` 枚举，排除 `obj/bin`。
- **测试**：`git grep -E '\[(Fact|Theory)' HEAD -- src/DBSync.Tests`；`[Fact(Skip=…)]` 单独统计。
- **链接**：全部改为仓库相对路径（`src/...`、`docs/...`、`README.md`）。行号锚点对应上述基线，后续提交会漂移，引用时以符号名（方法名/类名）为准。
- **边界**：未执行 `dotnet build/test`、未连接任何数据库、未运行 GUI。表格中的"读码确认"只表示代码路径确定，不表示运行时已观测；涉及"数据库会怎么处理"的后果（#7、#8 等）按**静态推断**对待。

---

## 附录 B：本版相对初稿的修订要点

| 初稿说法 | 本版结论 | 依据 |
| :--- | :--- | :--- |
| 代码量 6,279 / 9,377 / 347 / 2,249 | 7,191 / 10,520 / 399 / 2,661（物理行，含 `.axaml`） | 初稿用的是未注明的"非空行"口径 |
| 121 个测试 | 122（其中 1 个 `Skip`） | 漏算了跳过的真库测试 |
| 近 10 次里 1 次删掉指纹往返测试 | 该提交是第 13 次（`ef795f6`） | `git log` 计数 |
| README 与 roadmap 都宣称 v2.5 完成 | 只有 `docs/roadmap.md:335`；README 从未这样写 | 全仓检索 |
| 四个方言"都实现了" `ReadAllObjectsAsync` | 四个都是返回 `[]` 的存根，从未实现 | `git log -S` + 源码 |
| 循环外键"仍内联建表 → 执行必失败" | 环内表被排序器**静默丢弃**，不建表、不插数据 | `FkTopologicalSorter` + 单测 |
| `ISqlGenerator` 有 12 个方法 | 7 个；改接口动 6 个文件 | `ISqlGenerator.cs` |
| 类型比较是字符串相等，varchar ≠ nvarchar | `DbTypeName` 根本不比，同族类型**漏报** | `SchemaComparer.ColumnsEqual` |
| 跨 schema 撞键导致崩溃 | 是"比对失败"（异常被捕获），且直连默认规避 | 调用点 catch |
| 主键哈希用 `CONCAT_WS` | 是覆盖**全列**的行哈希；主键串另有分隔符歧义 | 四个指纹器 |
| MySQL"UI 仍宣传自动回滚" | Desktop 无执行入口；假回滚标志在 `ScriptExecutor` | `ScriptExecutor.cs:147` |
| 表重命名 = DROP + ADD 丢数据 | 建新表、旧表默认不删；列重命名才默认 DROP | 节点默认勾选逻辑 |
| "改一个勾选就要重算" | 勾选只重拼 SQL，不自动重比对 | 命令绑定 |
| GUI/CLI 结果"完全一致"（引述 README） | 不一致，至少 6 处差异 | 两条生成路径 |
| `--password` 是库密码 | 是快照加密密码；库密码在 `--connection` | `Program.cs` |
| 语言权重"不到 20%" | 约 20%–30%（按工作量拆解） | 见 2.1 |

**本版新增**：#39（直连 INSERT 写 NULL）、#40（回滚承诺不成立）、#41（加载比对方向相反），以及第 4 节根因矩阵、2.2 权重敏感度、2.5 风险清单、6 节依赖顺序与 MVP 定义、8 节契约测试与 fuzz/locale 测试、附录 A 核查边界。
