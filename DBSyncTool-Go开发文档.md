# DBSyncTool v2 Go 开发文档

> 状态：实施草案 v1（可直接拆任务开工）
> 输入：[`DBSyncTool-重构评估.md`](DBSyncTool-重构评估.md)（第 2 版）
> 基线：仓库 `c5dbb2d`；目标 Go 1.26
> 日期：2026-11-27
> 阅读顺序：第 1–4 节（决策与边界）→ 第 22 节（排期）→ 按模块深入

> **只读边界（最高优先级，覆盖全文）**：本工具**不执行任何 DDL/DML**，不提供 `apply`/`execute`/`resume`/`dry-run`/事务/回滚/自检执行。连接必须使用**只读账号**；SQL 只用于**预览/导出**，不能在本工具内执行。§14 的执行器已删除；§13.5 的 plan 只是只读预览。

> **范围调整（2026-11-27）**：v2.0 **只交付 Windows 桌面工作台**（Wails v3），**CLI/CI 不实现**；**MySQL 8.0.16 优先**，随后 SQL Server/PostgreSQL/SQLite；GUI 必须包含**基础数据差异页**；**列顺序不比较，排序规则参与比较**；快照**默认不加密**；仅中文、固定浅色主题、无自动更新。

**一页纸决策**：

| 决策 | 结论 |
| :--- | :--- |
| 语言 | Go 1.26（已确认 `go1.26.0 windows/amd64`）；模块路径 `gitee.com/wangbenchi66/dbsync` |
| 入口 | Windows 桌面工作台唯一交付；核心 `service` 单一实现；CLI 暂缓（v2.x） |
| 执行 | **永不执行 DDL/DML**；只读比对 + SQL 预览 |
| 数据库顺序 | **MySQL 8.0.16 → SQLite → PostgreSQL → SQL Server（均已接）** |
| 平台 | Windows 10/11 x64（WebView2） |
| 方向 | `source`（期望）→ `target`（现状），计划 = 让 target 变成 source |
| 数据模型 | 类型化 `Value` + 规范化编码 + 客户端哈希 |
| 快照 | ZIP + NDJSON + 每表摘要；**默认不加密**，按需 age |
| 列顺序 | **不参与比较**，不提供开关 |
| 排序规则 | **参与比较**（同方言），差异生成 ALTER COLLATE；跨方言只展示 |
| 语言/主题/更新 | 仅中文；固定浅色；不做主题切换与自动更新 |
| 正确性优先 | 每个 P0 都有"先红后绿"的回归用例；集成矩阵自证幂等 |

---

## 1. 文档定位与既有决策

本文不重复论证"为什么选 Go"（见评估文档 §2）。它解决三个问题：

1. **把评估结论变成可实现的接口与数据结构**，避免实现时重新发明；
2. **把评估里含糊的地方定死**（方向、排序、摘要、事务），这些是返工高发区；
3. **给出可验收的里程碑与任务拆解**，让"7–11 周"变成可核对的清单。

已定、本文不再讨论的决策：

- **D0** **只读**：工具不执行任何 DDL/DML，不提供执行器；所有 SQL 只是预览。数据库连接必须使用只读账号；代码中禁止出现 `Exec/ExecContext`（由 `internal/readonly` 测试静态拦截）。
- **D1** 语言 Go 1.26（已确认可用）；GUI 用 Wails v3（要求 Go 1.26+ 与 WebView2）。
- **D2** **桌面工作台优先**；CLI 暂缓（v2.x）。核心 `internal/service` 是唯一实现，GUI 只是外壳；未来 CLI 复用同一 service，不允许第二份逻辑。
- **D3** 保留"快照模式"产品创意（内网隔离、U 盘传递）；**快照默认不加密**，敏感场景手动开 age。
- **D4** 不复用原版的 CSV 通道、数据库侧哈希、`TableDiff` 模型、ViewModel 拼脚本。
- **D5** 破坏性变更默认不执行；GUI 的破坏性开关等价于 CLI 的 `--allow-destructive`。
- **D6** **列顺序永不参与比较**：不实现"忽略列顺序/严格列顺序"开关（原版 #14 的假开关不能复现）。
- **D7** v2.0 GUI 必须包含基础数据差异页：没有 CLI 兜底时，"能执行数据 DML 却看不见行"不可接受。
- **D8** **MySQL 8.0.16 优先**：M1 做 MySQL 方言，M2 再做另外三种；测试矩阵以 MySQL 8.0.16 为主。
- **D9** **排序规则参与比较**（同方言）：列 collation 不同 → 生成 ALTER COLLATE，不阻断，但索引需重建；跨方言只展示不判定。
- **D10** **只做 Windows**：不做 macOS/Linux GUI；Wails 的 Linux 构建要求不进入 v2.0。
- **D11** **仅中文、固定浅色、无主题切换、无自动更新**。安装包以**便携 ZIP** 最快，Inno Setup 后置。

仍需在 M0 关闭的开放问题见 §23.2；三个方向性问题（主力库、原生观感、重写 vs 增量）的默认答案沿用评估文档 §10，本文不再重复。

> **关于代码片段**：本文的 Go 片段是接口草案，用于锁定设计与评审，不是可编译实现。本版在没有 Go 工具链的环境下编写，片段**未经编译**；M0 的第一步就是把它们落成可编译的骨架并跑通 `go vet`。

---

## 2. 目标、范围与验收

### 2.1 功能目标

| 编号 | 目标 | 验收方式 |
| :--- | :--- | :--- |
| G1 | **MySQL / SQLite / PostgreSQL / SQL Server** 的结构读取与差异 | 四方言矩阵绿 |
| G2 | 结构 + 数据比对，输出差异与**只读 SQL 预览**（不执行） | "比较后无差异"与预览 SQL 可核对 |
| G3 | 快照 v2：流式读写、可检查、可加密、带摘要 | round-trip 无损（二进制/换行/`\N`/时区） |
| G4 | **桌面工作台**可完成"比较 → 差异 → SQL 预览" | 不跳页，不执行任何 SQL |
| G5 | 行级差异与列级变更（GUI 数据页） | v2.0 基础网格可看、可翻页、列高亮；搜索/导出在 v2.1 |
| G6 | 破坏性差异有标注、有风险提示 | 只标注，不执行 |

### 2.2 非功能目标

| 编号 | 指标 | 目标值（验收线，不是宣传语） |
| :--- | :--- | :--- |
| N1 | Windows 便携包 | 解压即用；不含 WebView2 Runtime，启动时检测/引导；目标 ≤ 80 MB |
| N2 | 启动 | 热启动 ≤ 1 s、冷启动 ≤ 2 s（Windows 10/11，关闭 Defender 首次扫描影响） |
| N3 | 流式内存 | 单表 100 万行比对，**进程常驻 ≤ 384 MB**（其中数据预算 256 MiB：差异集 128 + 多重集 128，其余为运行时开销） |
| N4 | 吞吐 | 局域网内单表 ≥ 20k 行/秒（**窄行 200B、主键索引可用、无服务端排序**；宽行或强制 COLLATE 排序时按实测报告，不承诺） |
| N5 | 可复现 | 同一输入两次运行，**明文快照与 plan.json** 字节级一致（时间戳字段除外）；加密快照因随机 file key/salt/nonce 不可复现，只要求解密后内容一致 |

> N3/N4 是**灰度目标**：M2 结束前必须用 `internal/bench` 的固定数据集测出来并写进 README；达不到就调架构，不许改口径。N1/N2 在 M3（GUI 骨架搭起来后）实测，不要拖到发布前才发现超限。CLI 的 25 MB / 50 ms 指标随 CLI 一起推迟到 v2.x。

### 2.3 不做什么（v2.0）

- **不执行任何 DDL/DML**：没有 apply/execute/resume/dry-run/事务/回滚；SQL 只做预览；
- **不交付 CLI/CI**：`cmd/dbsync` 与 `internal/cli` 留到 v2.x；v2.0 只发 Windows 桌面工作台；
- **不做自动更新、主题切换、多语言**：固定浅色、仅中文；
- 不做数据迁移以外的 ETL、不做定时任务调度（P2 的漂移检测另立）；
- 不读取也不生成权限/角色/表空间/分区方案（IR 没有这些字段；v2.1 再评估"只读报告"）；
- 不做 Oracle / 达梦 / 人大金仓（方言接口留好，M5 以后再评估）；
- **不同步序列当前值**（PostgreSQL 独立 sequence 的 `last_value`）：v2.0 只建序列对象，不 `setval`；需要精确衔接自增值时由 DBA 手工处理，风险登记在 §23.1；
- **v2.0 只保证同方言的数据同步**；跨方言只做结构报告（只读），数据同步留到 v2.1——类型映射不是单射（见 §7.3），强行做会把"读回类型"变成猜谜；
- 不做云端托管与多租户。

### 2.4 v2.0 完成定义（DoD）

- [ ] 桌面工作台完成"建快照 → 选端点 → 比较 → 看差异 → 看 SQL 预览"全流程；
- [ ] 评估文档 §3.1 的 10 条 P0 全部有回归用例，且全绿；
- [ ] 集成矩阵"建表 → 比对"在 MySQL + SQLite + PostgreSQL + SQL Server 全绿；**测试夹具之外不出现任何写语句**；
- [ ] 同一份快照在 `tr-TR` / `de-DE` / `en-US` 下 round-trip 一致；
- [ ] JSON 报告 schema 冻结为 v1，并有兼容性测试（GUI 导出）；
- [ ] 破坏性差异被明确标注；**工具内没有任何执行入口**；
- [ ] 文档：README 的"已完成"声明与代码一致（评估文档 §0 结论 4 的问题不复现）；
- [ ] GUI 的比较/预览都调用同一份 `service`，不出现第二套差异逻辑。

---

## 3. 术语与方向约定

| 术语 | 定义 |
| :--- | :--- |
| **source** | 期望状态。可以是活库，也可以是快照 |
| **target** | 现状。差异中要变成 source（**工具不执行**） |
| **endpoint** | 任意一端：`mssql://`、`mysql://`、`postgres://`、`sqlite://`、`snapshot://` |
| **plan** | 从 target 到 source 的只读差异 SQL 预览（含风险标注） |
| **phase** | 预览语句的展示分组 |
| **preflight** | 只读风险检查，产出阻断项/告警 |
| **canonical value** | 与方言无关、可编码、可哈希、可排序的值 |
| **digest** | 表的行多重集摘要，用于"可证明相等时跳过" |
| **object** | 表、列、索引、约束、视图、存储过程、函数、触发器、序列 |

**方向铁律（全文所有示例必须遵守）**：

```
source = 期望（DEV / 新版 / 快照）
target = 现状（TEST / 生产）
plan   = 让 target 变成 source
```

因此：`source` 有、`target` 无的行 → INSERT；`target` 有、`source` 无的行 → DELETE；两边都有但内容不同 → UPDATE（只改变化列）。结构同理：`source` 有 → CREATE；`target` 有 → DROP（默认不勾选、需显式开启）；两边都有 → ALTER。

> 原版把"基线（目标库）"放在第一个参数、又对数据翻转了一次，导致结构 DDL 与数据 DML 方向相反（评估文档 #41）。本设计用**唯一方向**消除这类错误：任何函数签名里，第一个参数是 source、第二个是 target。

---

## 4. 总体架构与依赖规则

### 4.1 分层

```
┌──────────────────────────────────────────────────────────┐
│ cmd/dbsync  +  internal/cli  +  internal/gui              │  参数、渲染、绑定
├──────────────────────────────────────────────────────────┤
│ internal/service                                          │  编排：端点→比对→SQL 预览
├──────────────────────────────────────────────────────────┤
│ internal/plan   →  internal/emit                          │  只读预览、SQL 渲染
├──────────────────────────────────────────────────────────┤
│ internal/diff   →  internal/snapshot  →  internal/report  │  纯计算、格式、报告
├──────────────────────────────────────────────────────────┤
│ internal/endpoint  →  internal/dialect/*                  │  数据源抽象、方言
├──────────────────────────────────────────────────────────┤
│ internal/model  →  internal/value                         │  IR、值
└──────────────────────────────────────────────────────────┘
```

### 4.2 依赖规则（CI 用 `go-arch-lint` 或 `depguard` 强制）

| 包 | 允许依赖 | 禁止依赖 |
| :--- | :--- | :--- |
| `internal/value` | 标准库 | `model`、其它内部包 |
| `internal/model` | `value` | `dialect`、`diff` |
| `internal/stream` | `model`、`value` | `dialect`、`diff` |
| `internal/driver` | 标准库、`model` | `diff`、`plan` |
| `internal/dialect` | `model`、`value`、`stream`、`emit` 接口 | `diff`、`plan`、`cli` |
| `internal/diff` | `model`、`value`、`stream` | `dialect` 的具体实现、`plan`、`endpoint` |
| `internal/snapshot` | `model`、`value` | `dialect`、`diff` |
| `internal/endpoint` | `model`、`value`、`stream`、`driver`、`dialect` 接口 | `diff`、`plan` |
| `internal/plan` | `model`、`diff`、`stream` | `dialect` 具体实现、`endpoint` |
| `internal/emit` | `model`、`plan`、`value` | `dialect`、`cli`、`gui` |
| `internal/report` | `model`、`plan` | `dialect`、`diff` |
| `internal/progress` | 标准库 | 其它内部包 |
| `internal/config` | 标准库、`model` | `dialect`、`diff` |
| `internal/logging` | 标准库 | 其它内部包 |
| `internal/service` | `endpoint`、`diff`、`snapshot`、`plan`、`emit`、`report`、`progress` | `cli`、`gui`、`database/sql`（写） |
| `internal/cli` | `service`、`report`、`config`、`logging`、`model`、`progress`（**v2.x，v2.0 不实现**） | `dialect`、`diff`、`plan` |
| `internal/gui` | `service`、`progress` | 具体方言实现、`diff` |
| `internal/bench` | 任意（仅测试/基准） | — |

**硬约束**：`internal/diff` 与 `internal/plan` **不做 IO**——不打开连接、不读文件、不写日志、不依赖 `database/sql`。它们只接受注入的 `stream.RowStream` 与纯数据结构；`plan` 只**生成**预览规格（SQL + 参数），**不执行**。IO 全部发生在 `dialect`（读）、`endpoint`（连接）、`snapshot`（读写）；生产代码不得出现 `Exec/ExecContext`。这条规则保证核心逻辑可以用表驱动测试覆盖，也保证 GUI/CLI 复用同一份实现。

`emit` 需要标识符转义时，自己定义 `Quoter` 接口（`Quote(parts ...string) string`），由 `dialect` 实现并在调用时注入；**`emit` 不 import `dialect`**，否则会形成 `dialect → emit → dialect` 环。

### 4.3 三条运行时数据流

```
① 导出快照
   dialect.Introspect → Schema IR → snapshot.Write(schema, rows) → .dbsync

② 比对 / 生成计划
   endpoint.Open → Introspect + ordered rows → diff.Compare → plan.Build → emit.SQL

③ 预览/导出
   plan.Build → plan.sql / plan.json（只读，不执行）
```

**入口**：v2.0 由桌面工作台调用 `internal/service`；CLI 是 v2.x 的另一个外壳，复用同一 service。`emit.SQL` 是只读预览，不暴露执行入口。

---

## 5. 代码结构与包职责

```
dbsync/
├── go.mod                          # module gitee.com/wangbenchi66/dbsync
├── cmd/dbsync/main.go              # v2.x：CLI 入口，v2.0 不构建
├── internal/
│   ├── cli/                        # v2.x：cobra 命令、flag、退出码（v2.0 不实现）
│   │   ├── root.go  diff.go  plan.go  snapshot.go  schema.go  ddl.go
│   │   └── exit.go                 # 退出码常量与映射
│   ├── service/                    # 编排：端点→比对→SQL 预览，GUI（及未来 CLI）共用
│   ├── endpoint/                   # 数据源抽象：活库 / 快照 / RowStream 实现
│   ├── stream/                     # RowStream/Comparator/DeltaSink（纯接口）
│   ├── driver/                     # endpoint → (driverName, dsn) 适配与会话初始化
│   ├── config/                     # .dbsync.yaml、环境变量、优先级合并
│   ├── value/                      # Value、Kind、编码、哈希、比较、十进制
│   │   ├── value.go  encode.go  hash.go  compare.go  decimal.go
│   ├── model/                      # IR：Schema/Table/Column/Index/Constraint/Object
│   │   ├── schema.go  table.go  column.go  index.go  constraint.go
│   │   ├── object.go  diff.go  plan.go  type.go
│   ├── dialect/
│   │   ├── dialect.go              # 接口、能力矩阵、注册表
│   │   ├── sqlserver/  mysql/  postgres/  sqlite/
│   │   └── typemap.go              # 三方映射的公共规则
│   ├── diff/                       # 纯函数：schema.go data.go merge.go rename.go
│   ├── snapshot/                   # 格式、读写、加密、校验
│   │   ├── format.go  writer.go  reader.go  manifest.go  crypt.go  inspect.go
│   ├── plan/                       # 只读差异 SQL 预览、依赖图、风险标注
│   ├── report/                     # text、json、md、sarif
│   ├── progress/                   # 事件总线（CLI 渲染 / GUI 推送共用）
│   ├── bench/                      # 固定数据集与基准（N3/N4）
│   ├── logging/                    # slog 初始化、脱敏
│   └── gui/                        # Wails 绑定（M3 主交付）
├── main.go  app.go                 # Wails 入口与绑定层
├── frontend/                       # 前端（M3）
├── testdata/                       # golden fixtures
│   ├── schema/<dialect>/<case>/    # 源/目标 IR 的 JSON + 期望 SQL
│   └── snapshot/<case>/            # 快照样本与期望解析结果
├── tests/integration/              # //go:build integration
├── deploy/docker-compose.yml       # mssql / mysql / postgres
└── docs/                           # 本文与 ADR
```

包注释（`doc.go`）必须写清"这个包不做什么"，例如 `diff/doc.go`：不打开连接、不生成 SQL。

---

## 6. 值模型与规范化编码

### 6.1 设计约束

来自评估文档的五条硬约束：

1. 列值**不允许**用 `string` 承载（#2、#3、#7、#8）；
2. 哈希在**客户端**算，不在数据库里算（#23、#24）；
3. 编码用**长度前缀**，永远不靠分隔符（#23）；
4. 渲染 SQL 字面量必须**按类型分支**，且与 locale 无关（#7、#8）；
5. 行哈希用于"变没变"，列哈希用于"哪列变了"（#32）。

### 6.2 类型与值

```go
package value

// Kind 是规范化值类型。新增 Kind 必须同时更新 Encode、Compare、Hash 与快照文档。
type Kind uint8

const (
	KindNull Kind = iota
	KindBool
	KindInt
	KindUint
	KindDecimal
	KindFloat
	KindText
	KindBytes
	KindTimestampTZ // 绝对时刻（UTC）
	KindTimestamp   // 无时区的墙上时间
	KindDate
	KindTime
	KindJSON
)

// Value 是类型化列值。同一语义必须只有一种表示；禁止用 == 比较。
type Value struct {
	Kind  Kind
	Bool  bool
	Int   int64
	Uint  uint64
	Dec   Decimal
	Float float64
	Text  string
	Bytes []byte
	TS    Timestamp // KindTimestampTZ / KindTimestamp
	Day   int32     // KindDate：1970-01-01 起的天数，可为负
	Nanos int64     // KindTime：时长纳秒，可为负、可超过 24 小时（MySQL TIME）
}

// Timestamp 是秒+纳秒，避免 UnixNano 在 1678–2262 年之外溢出。
// KindTimestampTZ 时 Sec 是 UTC 时刻；KindTimestamp 时 Sec 由"墙上时间按 UTC 解释"得到。
type Timestamp struct {
	Sec  int64
	Nano int32 // 0..999999999
}

// Decimal 是不可变十进制：(-1)^Neg × 0.Coef × 10^Exp。
// Coef 是无前导零的 ASCII 数字串，零为 "0"；不使用 *big.Int，避免共享指针被原地修改。
type Decimal struct {
	Neg  bool
	Coef string
	Exp  int32
}
```

**值语义**：`Value` 含 `[]byte`，**不可比较**；`Decimal` 可比较但不要依赖 `==`（要按数值）。统一用 `Equal`/`Compare`；从驱动拿到 `[]byte` 时必须 `bytes.Clone`，否则下一行可能复用同一缓冲。

语义规则（写死在测试里）：

| 类型 | 规则 |
| :--- | :--- |
| NULL | 只允许 `KindNull`；**空字符串不是 NULL** |
| 浮点 | NaN 归一化为单一 quiet NaN；`-0` 归一化为 `+0`（与 SQL 相等语义一致，见 ADR-002） |
| 十进制 | 数值相等即相等（`1.10` 与 `1.1` 同值）；保留精度只影响 DDL，不影响数据比对 |
| 时间 | `KindTimestampTZ` 存 UTC 的 `(Sec,Nano)`（偏移量仅用于展示）；`KindTimestamp` 存墙上时间，按"看作 UTC"编码为 `(Sec,Nano)`，**读取时不能用 `.UTC()` 改时刻**，要用年月日时分秒重构 |
| 日期 | `KindDate` 只存天数，不存 `time.Time`——避免 DST 把午夜归一化成 01:00 |
| 时间量 | `KindTime` 是**时长**，不是"当日时刻"，因此 MySQL 的 `-838:59:59` / `838:59:59` 可表示 |
| 二进制 | 原样字节，不做 Base64 即不进入 SQL 文本；渲染时才转 `0x…`；构造时 clone |
| JSON | 按驱动返回的文本比较（`jsonb` 会被服务端重排，跨方言只做提示，见 §7.4） |
| 未知类型 | 默认报 `E_UNSUPPORTED`；`--allow-unknown-types` 时降级为 `KindText` 并告警（绝不静默） |

### 6.3 规范化编码（哈希与快照共用）

```
Encode(v) = tag || payload
tag = 1 字节 Kind
```

| Kind | payload |
| :--- | :--- |
| Null | 空 |
| Bool | 1 字节 0/1 |
| Int | zigzag varint |
| Uint | uvarint |
| Decimal | 符号 1 字节、uvarint 系数长度、系数 ASCII 字节、zigzag varint 指数 |
| Float | 8 字节大端 IEEE754（先归一化 NaN/-0） |
| Text | uvarint 字节长度 + UTF-8 字节 |
| Bytes | uvarint 字节长度 + 原始字节 |
| TimestampTZ | zigzag varint 秒 + uvarint 纳秒（UTC） |
| Timestamp | zigzag varint 秒 + uvarint 纳秒（墙上时间按 UTC 解释） |
| Date | zigzag varint int32 天数 |
| Time | zigzag varint int64 纳秒时长 |
| JSON | uvarint 长度 + 原始 UTF-8 字节 |

**为什么不是 JSON 直接哈希**：JSON 的键序、数字格式、转义方式都会影响字节，任何一处不稳定都会造成"同一行哈希不同"。编码是二进制、定长前缀、与语言无关的。

### 6.4 哈希

| 用途 | 算法 | 说明 |
| :--- | :--- | :--- |
| 列哈希 | xxh3-128("col\0" ‖ Encode(value)) | **不含列名与类型**：名字大小写、列序、类型调整都不该造成"数据变了"；类型差异由结构层处理 |
| 行哈希 | xxh3-128("row\0" ‖ 列哈希**按调用方给定的规范列序**拼接) | 规范列序由 diff 层提供（数据比对用 source 的共有列顺序），两端一致即可，不依赖物理列序 |
| 表摘要 | 128 位模加：Σ 行哈希 | 可交换、与行序无关，因此无主键表也能算 |

```go
// ColumnHash 是单列值的摘要（不含列名/类型）。
type ColumnHash struct {
	Lo, Hi uint64
}

// RowHash 是整行摘要。
type RowHash struct {
	Lo, Hi uint64
}

// Digest 是流式累加器：Add 做 128 位模加，不保存整表行哈希。
type Digest struct {
	Lo, Hi uint64
}

func (d *Digest) Add(r RowHash)

func (d Digest) Sum() (lo, hi uint64)

func ColumnHashOf(v Value) ColumnHash // 输入 = "col\0" ‖ Encode(v)

// RowHashOf 按传入顺序拼接，调用方负责保证两端列序一致。
func RowHashOf(cols []ColumnHash) RowHash
```

**列序与列集必须由 diff 层显式给定**：

- **数据比对**：规范列序 = source 的**共有列**顺序；`RowStream` 按这个顺序产出 `[]ColumnHash`（target 端按同一顺序映射自己的列）。这样物理列序不同、列名大小写不同都不会造成假变更；名字匹配由结构层完成，哈希不掺和。
- **快照摘要**：覆盖该表的**可比较列**（排除计算列、rowversion 等数据库自动维护列），按列名排序后计算，写入 manifest。

**为什么列哈希不含类型**：类型差异是结构问题，`varchar(100)`→`varchar(50)` 不代表数据变了。若把类型签名混进行哈希，一次长度调整会让每行都判"变更"，生成整表 UPDATE。编码里已经带 `Kind` 标签，`int 1` 与文本 `"1"` 不会碰撞。

两种范围在**列集或列序不同**时算出的哈希不同，因此 manifest 的 `digest` **不能**拿来当行哈希用于数据比对；反之亦然。这是有意设计：避免"摘要相同但列集不同"被误判为数据相同。

**域分隔**：列哈希输入以 `"col\x00"` 开头，行哈希以 `"row\x00"` 开头；表摘要是模加和，**不再额外加前缀**（对加法无意义）。若需要"这个和属于哪种用途"，把 `Sum()` 的结果再哈希一次并在格式里注明。

**流式**：`Digest.Add` 逐行累加，内存 O(1)；不要先收集 `[]RowHash` 再求和（100 万行就是 16 MB，且随行数线性增长）。

**非对抗性**：xxh3-128 不是密码学哈希。快照若来自**不可信来源**（不是你自己导出的），不要用它做完整性判断——用 `CHECKSUMS.txt` 的 SHA-256 或 age 的认证；表摘要只用于"我方两端"的快速短路。

**摘要的诚实边界**：表摘要相等 ⇒ 行多重集相等的概率是 1−2⁻¹²⁸；但**只有两端都用同一套规范化编码、且列集相同**时才成立。因此摘要只在"两端都由本工具计算"时启用（快照↔快照，或未来方言实现可信摘要）；数据库侧 `CHECKSUM`/`MD5` 一律不用（原版 #23/#24 的根因）。

### 6.5 相等与排序

- **相等**：按 Kind 与语义规则比较，不按字符串。
- **排序**：定义"规范化全序"，供归并使用。文本按 UTF-8 字节序（等价于 Unicode 码点序）；数值按数值；时间按时刻/墙上时间；二进制按字节序；NULL 不允许出现在主键中。
- **复合主键**：按列序做字典序比较。
- **比较器必须是严格全序**：属性测试覆盖 `antisymmetry / transitivity / totality`（见 §20.1）。

**全序的边界规则**（必须写进比较器测试）：

| 情形 | 规则 |
| :--- | :--- |
| 同列不同 Kind（SQLite 亲和性允许） | 按 §6.2 的 Kind 顺序：Null < Bool < Int < Uint < Decimal < Float < Text < Bytes < Time 类 < JSON；跨 Kind 比较只用于排序，不用于判等 |
| NaN | 归一化为单一 quiet NaN，排在所有数之后、+Inf 之前 |
| -0 / +0 | 归一化为 +0 |
| 主键为 NULL | 报 `E_NULL_PK`（SQLite 的 `INTEGER PRIMARY KEY` 除外，其 NULL 即 rowid；其他主键列仍禁止） |

> PostgreSQL 的 `NULLS LAST` 默认、SQL Server 的 NULL 排序位置不同，因此**主键列一律非空**，从源头绕开；这也与"主键必须非空"的建表约束一致。

---

## 7. 类型系统与方言映射

### 7.1 规范化类型

```go
type TypeID uint8

const (
	TypeBool TypeID = iota
	TypeInt
	TypeUint
	TypeFloat
	TypeDecimal
	TypeText
	TypeBinary
	TypeDate
	TypeTime
	TypeTimestamp
	TypeTimestampTZ
	TypeUUID
	TypeJSON
	TypeXML
)

// TextEncoding 记录文本的字符集语义，用于"可接受"级别的比较。
type TextEncoding uint8

const (
	EncUTF8 TextEncoding = iota
	EncUTF16
	EncASCII
	EncLatin1
)

type Type struct {
	ID        TypeID
	Bits      int    // Int/Uint/Float 位宽
	Length    int    // Text/Binary 长度；-1 表示 max
	Precision int    // Decimal
	Scale     int    // Decimal
	Encoding  TextEncoding // UTF8/UTF16/ASCII/Latin1；只对 Text 有意义
	Collation string       // 有效排序规则；同方言参与比较，跨方言仅展示
	Raw       string // 方言原生类型名，仅用于展示与排错
}

type Column struct {
	Name       string
	Type       Type
	Nullable   bool
	Default    string // 已按方言规范化
	Identity   *Identity
	Computed   *Computed
	Comment    string
	Ordinal    int
}
```

### 7.2 映射原则

- 读取时：方言原生类型 → `ReadType` → `Type`（**保留 Raw**，便于报告与排错）；
- 比较时：`Type` → 三层判定（§12.2）；
- 生成时：`Type` → `EmitType` → 方言类型；若源类型无法表达，报 `E_UNSUPPORTED` 并给出最接近的类型建议，**不静默降级**。

### 7.3 常见映射（完整表见附录 C）

| 规范类型 | SQL Server | MySQL | PostgreSQL | SQLite |
| :--- | :--- | :--- | :--- | :--- |
| Bool | `bit` | `tinyint(1)` | `boolean` | `BOOLEAN` |
| Int64 | `bigint` | `bigint` | `bigint` | `INTEGER` |
| Uint64 | `decimal(20,0)` | `bigint unsigned` | `numeric(20,0)` | `UINT64_TEXT` |
| Decimal(p,s) | `decimal(p,s)` | `decimal(p,s)` | `numeric(p,s)` | `DECIMAL_TEXT` |
| Float64 | `float` | `double` | `double precision` | `REAL` |
| Text(n) | `nvarchar(n)` | `varchar(n)` | `varchar(n)` | `TEXT` |
| Bytes | `varbinary(n)` | `varbinary(n)` | `bytea` | `BLOB` |
| Date | `date` | `date` | `date` | `DATE_TEXT` |
| Time | `time(7)` | `time(6)` | `time` | `TIME_TEXT` |
| TimestampTZ | `datetimeoffset(7)` | `timestamp(6)`（会话 `time_zone='+00:00'`，1970–2038） | `timestamptz` | `TIMESTAMPTZ_TEXT` |
| Timestamp | `datetime2(7)` | `datetime(6)` | `timestamp` | `DATETIME_TEXT` |
| JSON | `nvarchar(max)`+`ISJSON` | `json` | `jsonb` | `JSON_TEXT` |
| UUID | `uniqueidentifier` | `char(36)` | `uuid` | `UUID_TEXT` |

> MySQL 的 `timestamp` 会做会话时区转换，因此**不要**用它承载"无时区时间"——无时区用 `datetime`，绝对时刻用 `timestamp` 且把会话时区固定为 UTC。SQL Server 的 `timestamp`/`rowversion` 是二进制版本号，映射为 `TypeBinary`，不是时间。
>
> **SQLite 刻意使用带 `_TEXT` 的逻辑类型名**（`DATETIME_TEXT`/`DECIMAL_TEXT`/`UINT64_TEXT`…）：SQLite 的声明类型决定**亲和性**。若写成 `NUMERIC`/`DECIMAL`/`UINT64`，亲和性会把看起来像数字的文本转成 INTEGER/REAL，大整数和高精度小数会被静默丢精度；名字里带 `TEXT` 才能保证按文本原样存储。读回时按声明名映射回逻辑类型，二次 diff 才收敛。`BOOLEAN` 用 0/1 存、`REAL`/`INTEGER` 用原生存储，不受影响。
>
> **MySQL `TIMESTAMP` 的范围是 1970–2038**：源数据超出范围时，`TimestampTZ` 映射到 MySQL 必须在预检阻断，不能静默截断。
>
> **这张表在 v2.0 范围内可逆**：同方言内，SQLite 的声明类型名由本工具写入、读取时按名映射；MySQL 的 `timestamp` 与 `datetime` 分别对应 TZ 与 naive。真正不可逆的是**跨方言**（例如 MySQL 的 `char(36)` 承载 UUID 与普通文本无法区分），所以 v2.0 不做跨方言数据同步。要做双向同步，必须先在 §7.1 引入"逻辑类型 + 映射族 + 读回判定"，见 ADR-008（待定）。

- **Uint64 的特殊处理**：SQLite 的 `INTEGER` 是有符号 64 位，`uint64 > 2^63-1` 会被存成负数或溢出，因此 SQLite 侧用 `text` 存十进制串；SQL Server 用 `decimal(20,0)`。读取时按列类型解析回 `KindUint`，不要依赖数据库的隐式转换。
- **MySQL `TIME`**：范围 `-838:59:59`–`838:59:59`，是时长而非时刻，映射到 `KindTime`（`Nanos int64`），不要按"当日纳秒"处理。
- **目标 MySQL 是 8.0.16**：该版本**没有 `utf8mb4_0900_bin`**（8.0.17 才加入），排序用 `utf8mb4_bin`（PAD SPACE）；如果未来连到 8.0.17+，启动时查 `information_schema.COLLATIONS`，有 `utf8mb4_0900_bin` 就优先用（NO PAD），没有就回退 `utf8mb4_bin`。CHECK 约束从 8.0.16 起强制执行。
- **SQL Server `tds`/`_BIN` 细节**：`_BIN` 不是"UTF-16 码元序"，而是"首个 WCHAR + 余下字节"；**必须用 `_BIN2`**。补充平面字符（emoji、CJK 扩展 B）要用测试向量验证 BIN2 与 Go UTF-8 字节序一致。

### 7.4 已知不保真项（必须在报告里标注）

| 项 | 行为 |
| :--- | :--- |
| JSON 键序 | `jsonb` 读取时已重排；跨方言比较只做"提示"，不阻断 |
| 文本排序规则 | **同方言参与列比较**：不同则生成 ALTER COLLATE（不阻断，但索引要重建）；跨方言只展示名称，不判定 |
| 浮点 NaN/-0 | 归一化后再比较，可能与某些库的位级差异不一致（不视为数据差异） |
| 无时区时间 | 跨方言按墙上时间比较，不做时区推断 |
| 计算列 / rowversion | **不参与数据比对与 DML**：值由数据库生成，两端必然不同；只在结构差异里报告定义 |
| 数据库自动维护列 | 同上；若用户坚持同步，必须显式 `--include-generated` 并接受"每次都有差异" |

---

## 8. 结构 IR

```go
type Schema struct {
	Engine   EngineInfo
	Tables   []*Table
	Objects  []*Object // 视图/过程/函数/触发器/序列
	Warnings []string
}

type Table struct {
	Schema      string
	Name        string
	Columns     []*Column
	Indexes     []*Index
	PrimaryKey  *Constraint
	Uniques     []*Constraint
	Checks      []*Constraint
	ForeignKeys []*Constraint
	Comment     string
	RowCount    int64 // 可选：仅 --with-count 时统计，不参与差异
}

type Constraint struct {
	Name       string
	Kind       ConstraintKind // PK / Unique / Check / FK
	Columns    []string
	RefSchema  string
	RefTable   string
	RefColumns []string
	OnDelete   string
	OnUpdate   string
	Expression string // CHECK 表达式；FK/唯一约束为空
	Deferrable bool   // 仅 PostgreSQL 有意义
}

// ColumnChange 描述一列的变更；Old/New 都参与 SET 与并发守卫。
type ColumnChange struct {
	Name string
	Old  value.Value
	New  value.Value
}

type Diagnostic struct {
	Code     string // E_PREFLIGHT_NOT_NULL ...
	Severity Severity // Info / Warning / Blocking
	Object   ObjectRef
	Message  string
	Hint     string
}

type Statement struct {
	SQL         string
	Object      ObjectRef
	Destructive bool
	Reversible  bool
}

type Index struct {
	Name      string
	Columns   []string
	Include   []string
	Unique    bool
	Clustered bool
	Primary   bool
	Predicate string // 部分索引条件
	Method    string // btree/gist/…；方言不支持时为空
}

type Identity struct {
	Seed      int64
	Increment int64
	Always    bool // PostgreSQL GENERATED ALWAYS
}

type Computed struct {
	Expression string
	Stored     bool
}

type Object struct {
	Kind       ObjectKind // View / Procedure / Function / Trigger / Sequence
	Schema     string
	Name       string
	Definition string // 空表示读取失败（权限/加密），标 unknown
}

type EngineInfo struct {
	Kind    string // sqlserver/mysql/postgres/sqlite
	Version string
}

type EndpointRef struct {
	Kind        string // snapshot/mssql/mysql/postgres/sqlite
	Label       string // 用户可见的逻辑名
	RedactedDSN string // 已脱敏
	Identity    string // host:port/database 的 SHA-256（不含凭据），用于标识端点
}

type ObjectRef struct {
	Kind     string // table/column/index/constraint/view/…
	FullName string
	Column   string
}

type NodeID string
```

**不变式（单元测试 + 调试断言）**：

1. `Table.FullName()` 唯一；所有字典键用 `FullName`，绝不用裸表名（原版 #6）。
2. 每个 `Column.Type.ID` 已被 `Encode` 支持；否则构造即返回错误。
3. 主键列非空、列名在表中存在。
4. 外键的 `RefTable` 使用 `FullName` 语义（读取时补全 schema）。
5. `Schema` 内表按 `FullName`、对象按 `(Kind, FullName)` 排序，保证输出确定性（N5）。

---

## 9. 方言抽象与四库差异

### 9.1 接口

```go
package dialect

type Dialect interface {
	Name() string
	Quote(parts ...string) string
	Capabilities() Capabilities
	Introspect(ctx context.Context, q Queryer, opts Options) (*model.Schema, error)
	ReadType(native string) (model.Type, error) // 方言类型 → 规范类型
	EmitType(t model.Type) (string, error)      // 规范类型 → 方言类型
	NormalizeDefault(expr string) string
	OrderByPK(t *model.Table) (string, error) // 二进制排序规则，见 §10.4
	SessionInit() []string                    // 连接级设置（SET XACT_ABORT / PRAGMA / search_path）
	Emitter() emit.Emitter
}

// 读取与执行分开：内省只读，执行需要 DDL/DML。
type Queryer interface {
	QueryContext(ctx context.Context, query string, args ...any) (*sql.Rows, error)
}

type Execer interface {
	ExecContext(ctx context.Context, query string, args ...any) (sql.Result, error)
}

type Options struct {
	Schemas        []string // 空表示全部用户 schema
	Tables         []string // 通配（`*`/`?`），空表示全部
	IncludeObjects bool     // 是否读取视图/过程/函数/触发器
	IncludeData    bool     // 快照导出时是否读数据
	WithCount      bool     // 是否统计行数（默认关，大表很贵）
}
```

`Capabilities` 是**能力矩阵**，不是特性开关：

```go
type Capabilities struct {
	TransactionalDDL bool // MySQL: false
	CheckConstraint  bool // MySQL 8.0.16+ 支持；SQLite 支持但不可命名
	Comments         bool // SQLite 无原生列注释
	Sequences        bool // SQL Server/MySQL 用 identity，PG 用 sequence
	PartialIndex     bool
	ComputedColumn   bool
	CreateOrReplace  bool   // 视图/过程是否支持 CREATE OR REPLACE
	IdentityInsert   string // 写入自增列的方式：SET_IDENTITY_INSERT / OVERRIDING / NATIVE
	RebuildTable     bool   // 改列是否需要重建表（SQLite: true）
	BinaryCollation  string // 方言的"码点序"排序规则名
}
```

### 9.2 方言要点（实现前必读）

| 方言 | 必读差异 |
| :--- | :--- |
| SQL Server | `sys.*` 目录视图 + `OBJECT_DEFINITION` 读对象；`datetime2` 精度；`datetimeoffset`；`timestamp` 是 rowversion；计算列 `is_computed`；`IDENTITY(seed,inc)` 必须读；`COLLATE Latin1_General_100_BIN2` 是码点序（BIN 不是）；**collation 从 `sys.columns.collation_name` 读**；**连接加 `ApplicationIntent=ReadOnly` 并使用只读账号**（SQL Server 没有可靠的会话级只读开关） |
| MySQL | `information_schema`；DDL 隐式提交；**8.0.16 用 `utf8mb4_bin`（PAD SPACE），8.0.17+ 可探测 `utf8mb4_0900_bin`（NO PAD）**；`ON UPDATE CURRENT_TIMESTAMP`；`AUTO_INCREMENT`；唯一约束以索引形式存在（读取时要还原为约束语义）；**collation 从 `COLUMNS.COLLATION_NAME` + `CHARACTER_SET_NAME` 读有效值** |
| PostgreSQL | `pg_catalog`（`pg_attribute`/`pg_constraint`/`pg_index`）；`search_path` 决定 schema；`COLLATE "C"` 是字节序；`jsonb` 重排；分区表与继承需要显式排除；`SERIAL` 读成 identity；**collation 从 `pg_attribute.attcollation` 关联 `pg_collation` 读** |
| SQLite | `pragma table_info/index_list/foreign_key_list`；类型是"亲和性"，按声明类型映射；**无真正 ALTER，改列要按官方 12 步重建表**（M2 实现）；`COLLATE BINARY`；无用户/权限概念。**连接级坑**：`PRAGMA foreign_keys=ON`/`busy_timeout` 是每连接设置，必须写进 DSN（`_pragma=...`）；`:memory:` 多连接会得到多个独立库，需 `file::memory:?cache=shared` + `MaxOpenConns(1)`；写并发用 WAL（`_pragma=journal_mode(WAL)`）+ busy_timeout；**SQLite 是单写者，`--parallel` 默认降为 1** |

### 9.3 连接、DSN 与凭据

**DSN 不能直接透传**：本文的 endpoint 是自定义 scheme，四种驱动接受的格式各不相同，必须由 `internal/driver` 显式转换：

| endpoint 写法 | 驱动名 | 转换后的 DSN |
| :--- | :--- | :--- |
| `mssql://user:pass@host:1433/db?encrypt=true` | `sqlserver` | `sqlserver://user:pass@host:1433?database=db&encrypt=true`（**本文 endpoint 的 path 是数据库名**；驱动自己的 URL path 是实例名，所以适配器必须改写成 `?database=`，不能原样透传） |
| `mysql://user:pass@host:3306/db` | `mysql` | `user:pass@tcp(host:3306)/db?parseTime=true&loc=UTC&charset=utf8mb4` |
| `postgres://user:pass@host:5432/db?sslmode=require` | `pgx` | 原样传给 `sql.Open("pgx", dsn)`（pgx 不注册 `postgres`） |
| `sqlite:///path/to/file.db` | `sqlite` | `file:/path/to/file.db?_pragma=busy_timeout(5000)&_pragma=foreign_keys(1)` |

每种转换必须有 `sql.Open + Ping + SELECT 1` 的集成测试；MySQL 不加 `parseTime=true&loc=UTC` 会拿到 `[]byte` 日期，直接把类型体系打乱。

**每连接初始化**：`SET XACT_ABORT ON`（SQL Server）、`PRAGMA foreign_keys=ON`（SQLite）、`SET search_path`（PG）、`SET SESSION information_schema_stats_expiry=0` 与 `SET time_zone='+00:00'`（MySQL）都是**连接级**设置，`db.Exec` 只影响池里一条连接。由 `Dialect.SessionInit()` 返回语句列表，打开 `*sql.Conn` 时逐条执行。

**内省与执行都走 `*sql.Conn`**（不是 `*sql.DB`）：取连接 → 跑 `SessionInit` → 查询/执行 → 归还。否则池里不同连接的 `search_path`/`foreign_keys`/`XACT_ABORT` 可能不一致，元数据与写入行为会"看运气"。

- 密码优先来自环境变量/文件，其次才允许出现在 DSN。
- 所有日志与报告对 DSN 做**脱敏**（用户名保留、密码替换为 `***`），由 `logging.RedactDSN` 统一处理。
- 连接池：每 endpoint 独立 `*sql.DB`；`SetMaxOpenConns(--max-conns)`（默认 8）、`SetMaxIdleConns` 与 max 相同，`SetConnMaxLifetime` 30 分钟。并发约束见 §18.1：**每 endpoint `--max-conns ≥ --parallel + 2`**（预留内省与进度）。

---

## 10. 读取、转换与排序

### 10.1 驱动类型 → Value

| 驱动返回 | 处理 |
| :--- | :--- |
| `nil` | `KindNull` |
| `bool` | `KindBool` |
| `int64` / `uint64` | 按列类型 `KindInt`/`KindUint`（禁止 `fmt.Sprintf`） |
| `float32` | 提升为 `float64`（MySQL `FLOAT` 会返回 `float32`） |
| `float64` | `KindFloat`（归一化 NaN/-0） |
| `[]byte` | **必须 `bytes.Clone`**；再按列类型解码：Text→UTF-8、Decimal→`ParseDecimal`、Binary→原样、未开 `parseTime` 的日期→按方言布局解析 |
| `string` | 文本/十进制/JSON/UUID 按列类型解析（pgx 的 `NUMERIC` 走这里） |
| `time.Time` | 仅用于 `TimestampTZ`；`Timestamp` 用年月日时分秒**重构**，不能 `.UTC()`；`Date` 取 `Year/Month/Day` 折天数 |
| 其它 | 报 `E_UNSUPPORTED`，附列名与 Go 类型 |

> **按列类型派发，不要按 Go 类型猜**：同一个 `[]byte` 可能是 `varbinary`、`decimal` 或未解析的 `datetime`；同一个 `string` 可能是文本或 `numeric`。先看 `model.Column.Type`，再校验驱动给的 Go 类型是否在允许集合里，不匹配就报 `E_TYPE_MISMATCH`。

> 若驱动对文本列返回 `[]byte` 且内容不是 UTF-8（例如 latin1 库），不要猜测转码：先要求连接参数显式指定字符集；仍失败则报 `E_ENCODING` 并给出列名。跨库文本必须统一到 UTF-8。

**关键**：解析一律用显式规则（`strconv.ParseInt`、`value.ParseDecimal`、`time.Parse` 指定布局），**不使用 `fmt.Sprint`/`Convert.ToString` 这类受 locale 影响的路径**（原版 #8）。

### 10.2 标识符与 SQL 拼装

- 标识符：`Quote(parts...)`，SQL Server `[x]`、MySQL 反引号、PG/SQLite 双引号；内部 `"` 按方言双写。
- 值：只通过字面量渲染器生成；**任何值都不允许直接字符串拼接**。
- 查询：表名/列名只能来自 IR，不接受用户随意输入（`--tables` 正则只用于筛选，不作为 SQL 片段）。

### 10.3 流式读取

```go
// ScanRows 逐行读取并把驱动值转换为 Value；不缓存整表。
func ScanRows(ctx context.Context, rows *sql.Rows, cols []*model.Column, fn func([]value.Value) error) error
```

- `rows.Close()` 必须 `defer`；每 N 行检查 `ctx.Err()`；
- 循环结束后必须检查 `rows.Err()`——驱动错误不会自动冒泡，漏了就是"静默少读几行"；
- 提前返回（如 `E_ORDER`）时先取消 `ctx` 再 `Close()`，否则 `Close` 可能把剩余结果读完才释放连接；
- `ScanRows` 统一负责"关闭 + 返回原始错误"，调用方不再各自 defer；
- `[]byte` 一律 `bytes.Clone`（驱动可能复用读缓冲）；
- 读取超时通过 `context.WithTimeout`（默认 30 分钟，可配 `--timeout`）；
- 进度事件每 5000 行或 500 ms 发一次，避免刷屏。

### 10.4 排序与归并的正确性（本设计最容易做错的地方）

归并连接要求**两端流按同一个全序输出**。数据库的 `ORDER BY` 用的是**排序规则**，不是字节序：`'a'` 与 `'B'` 在不同 collation 下顺序不同。直接信任 `ORDER BY` 会让归并把两行错配，产生"假删除 + 假插入"。

**做法（三层）**：

1. **强制二进制排序**：`OrderByPK` 为每个文本主键列追加码点序 collation：
   - SQL Server：`COLLATE Latin1_General_100_BIN2`（BIN2 是码点序；`_BIN` 是 UTF-16 码元序，不能用）；
   - MySQL：**8.0.16 用 `COLLATE utf8mb4_bin`（PAD SPACE）**；8.0.17+ 若探测到 `utf8mb4_0900_bin`（NO PAD）则优先。两者都是码点序，但 PAD SPACE 会让尾部空格的比较出现相等，严格递增断言必须保留；
   - PostgreSQL：`COLLATE "C"`；
   - SQLite：`COLLATE BINARY`。
   **前提**：PostgreSQL/MySQL 的库编码必须是 UTF-8（连接与库两层都检查，非 UTF-8 直接报 `E_ENCODING`）；SQL Server 用 `nvarchar` + BIN2，不受默认排序规则影响。

   **代价**：在 `ORDER BY` 上临时 `COLLATE` 通常会让主键索引失效，数据库只能排序（SQL Server tempdb / MySQL filesort / PG sort / SQLite 临时 B-tree）。100 万行排序可能比"流式读 + 归并"本身更贵。因此 `--sort=db` 前先检查主键索引的 collation 是否已是二进制序；不是则报告"预计排序 N 行"，并建议 `--sort=external` 或建表达式索引。

   **字符集限制**：SQL Server 的 `varchar` 主键按**代码页字节**排序，只有 Latin1 这类"字节序=码点序"的代码页才与 Go 的 UTF-8 字节序一致；CP936 等其他代码页可能不一致。文本主键优先用 `nvarchar`；非 Unicode 主键在预检里提示"可能退化为外排"，不要默认相信 BIN2。
2. **单调性断言**：读取每一行时与上一行比较，必须严格递增（按 §6.5 的比较器）。一旦违反，立刻中止并返回 `E_ORDER`，附首个违例行，**绝不继续合并、绝不写出半份差异**。
3. **回退**：`--sort=external` 时改为客户端外排（分块排序 + 归并临时文件），保证任意 collation 下正确；代价是磁盘 IO。默认 `--sort=db`：数据库排序 + 单调断言，**一旦断言失败就整次中止**并提示"改用 `--sort=external` 重跑"——不在流中途自动切换（差异集可能已写出，无法干净回退）。

```go
// Merge 按规范化顺序归并两端；要求输入满足严格递增，否则返回 E_ORDER。
func Merge(ctx context.Context, t *model.Table, src, tgt RowStream,
	cmp Comparator, sink DeltaSink) error
```

三个配套类型定义在 `internal/stream`（纯接口，不依赖 `database/sql`；`endpoint` 提供活库/快照实现）：

```go
// PK 是规范化主键元组。
type PK []value.Value

// RowStream 逐行给出主键、整行值与列哈希；hashes 由实现按 diff 给定的规范列序算好。
// 返回 io.EOF 表示结束；EOF 时 key/row/hashes 必须为零值。
type RowStream interface {
	Next(ctx context.Context) (key PK, row []value.Value, hashes []value.ColumnHash, err error)
	Close(ctx context.Context) error
}

// Comparator 按 §6.5 的全序比较主键；不可比较时返回 error，而不是 panic 或 0。
type Comparator interface {
	Compare(a, b PK) (int, error)
}

// DeltaSink 接收差异；实现可以是内存收集器，也可以是落盘写入器。
type DeltaSink interface {
	BeginTable(ctx context.Context, t *model.Table) error
	Insert(ctx context.Context, key PK, row []value.Value) error
	Update(ctx context.Context, key PK, changed []model.ColumnChange) error
	Delete(ctx context.Context, key PK) error
	Flush(ctx context.Context) error
	Close(ctx context.Context) error
}
```

**为什么 `Merge` 要拿整表与列哈希**：只给 `key` 和 `row`，它无法知道"哪一列变了"，也就无法生成 `UPDATE 只改变化列`（评估 #1 的正面修复）。列哈希在 `RowStream.Next` 里算（按 diff 给定的规范列序），`Merge` 只做逐位比较；位置 `i` 对应哪个列由 `model.Table` 的规范列序决定。`DeltaSink.Update` 拿到 `ColumnChange{Old,New}`，可直接渲染 SET 与守卫。

**EOF 约定**：`Next` 返回 `io.EOF` 时不得返回上一行的残留值；实现必须显式清零。`Close` 接受 `ctx`，取消时能尽快释放连接。

**为什么不在 Go 里实现 collation**：这是无底洞（每种语言一套 ICU 规则），而且与"数据是否相同"无关。我们只需要一个**自洽的全序**，然后让数据库按它输出；做不到就客户端排。这个结论写进 ADR-006。

### 10.5 无主键表

- 无法排序 → 不参与归并；
- 用**行哈希多重集**比较：两端各维护 `map[hash]count`，内存超限（默认 128 MB，与 `--memory-budget` 的一半一致）时落盘为有序 run，最后归并计数；
- 差异输出为"新增 N 行 / 删除 N 行（按内容）"，不生成逐行 UPDATE/DELETE（没有稳定定位符）；
- `--no-pk-strategy=skip|replace`（默认 `skip`）：`skip` 只报告不生成 DML；`replace` 生成"清空 + 全量插入"，**必须**配合 `--allow-destructive`，且预检提示会丢失自增/默认值之外的列状态；
- 在计划里标注：无主键表只能整表重建（即 `replace`）。

---

## 11. 快照格式 v2

### 11.1 容器

```
baseline.dbsync             # 明文：ZIP（魔数 PK）
baseline.dbsync             # 加密：age(ZIP 字节流)（魔数 age-encryption.org/v1）
```

扩展名不因加密改变，读取时按**文件头魔数**判断；`.age` 后缀作为兼容别名接受。这样 `snapshot://./baseline.dbsync` 在加密与否时都成立，避免"忘记改后缀"这类错误。

- 明文用 ZIP：`unzip -l`/`dbsync snapshot inspect` 都能看，满足"出问题能自己查"。
- **默认明文**：`--encrypt none`（默认）直接写 ZIP；`--encrypt age` 才加密。敏感数据带出内网时必须手动开启，UI 要显著提示"当前快照未加密"。
- 加密用 [age](https://filippo.io/age)：口令模式 `age.NewScryptRecipient`（只能有唯一 recipient）；也支持 X25519 公钥模式。
- **加密不改容器**：age v1.3+ 提供 `DecryptReaderAt`，可直接把 `*os.File`（`io.ReaderAt`）解成明文 `io.ReaderAt` 交给 `zip.NewReader`，**不需要落明文临时文件**；只有输入是 stdin/管道（不可 seek）时才落临时文件。
- **关闭顺序**：写入时 `zip.Writer` 包在 age `WriteCloser` 上，必须先 `zip.Close()` 再 `age.Close()`——否则中央目录没写完、最后一块密文没 flush，包直接损坏。
- **口令参数**：解密用 `age.NewScryptIdentity`，并 `SetMaxWorkFactor(20)`；scrypt 加密默认 2^18（约 1 秒），解密默认允许到 2^22（约 15 秒），不设上限会被恶意快照拖死。
- **体积**：NDJSON + `zip.Deflate`；不再有"指纹文件 + 全量文件"两份。

### 11.2 目录

```
manifest.json
schema/<escaped-schema>.<escaped-table>.json
data/<escaped-schema>.<escaped-table>.ndjson
objects/<kind>/<escaped-name>.json
CHECKSUMS.txt
```

`<escaped-*>` 用 URL-safe 转义（保留 `A-Za-z0-9_-`，其余 `%XX`），避免表名里的 `/`、`:`、`..` 破坏 ZIP 路径；读取时反转义并校验原名。

`CHECKSUMS.txt` 记录每个条目**解压后内容**的 SHA-256（不是压缩字节），`snapshot verify` 逐项校验；manifest 里的 per-table `digest` 是**行内容**摘要，两者职责不同：前者证明文件没坏，后者用于短路比较。

### 11.3 manifest.json

```json
{
  "formatVersion": { "major": 2, "minor": 0 },
  "toolVersion": "2.0.0",
  "createdAt": "2026-11-27T10:00:00Z",
  "dataScope": "full",
  "engine": { "kind": "sqlserver", "version": "16.0.1000.6" },
  "options": { "includeData": true, "tables": ["dbo.Users"] },
  "tables": [
    {
      "schema": "dbo",
      "name": "Users",
      "columns": 12,
      "rows": 12034,
      "digest": "xxh3-128:9f2c...",
      "hasData": true
    }
  ]
}
```

`dataScope` 取 `full`（含数据）或 `schema-only`；`hasData` 是**表级**事实（某表可能因为无主键/被排除而没有数据）。`hasData=false` 时 `digest` 与 `rows` 允许为 `null`，读取端不得用它们做"空表"推断。

**兼容策略**：`major` 不匹配直接拒绝（`E_SNAPSHOT_VERSION`）；`minor` 更高时忽略未知字段并告警。单整数无法表达 major/minor，所以这里用对象。

### 11.4 NDJSON

一行一个 JSON 对象，值用类型化数组（附录 B）：

```json
{"v":[["i","1"],["s","张三"],["ts","2026-11-27T10:00:00.1234567Z"],["n"]]}
```

- 换行、引号、反斜杠、`\N` 全部由 JSON 转义处理，**不存在 CSV 的多行/`\N` 问题**（原版 #3、#4）。
- 二进制用 `["x","<base64>"]`；JSON 值用 `["j","<原始文本>"]`。
- 每行独立可校验；读取端按行流式解析，损坏立即报 `E_SNAPSHOT_CORRUPT` 并给出行号。
- **行宽断言**：每行值个数必须等于 `schema/<table>.json` 的列数，不等报 `E_SNAPSHOT_ARITY`（附行号与期望值），避免 schema 与数据错位后静默错列。

### 11.5 写入与读取

- 写入：先写临时文件 `<name>.dbsync.tmp`，完成后 `os.Rename` 原子替换；中途失败清理临时文件。**ZIP 条目的修改时间固定为 1980-01-01**，否则明文快照无法做到 N5 的字节级可复现。
- **写出顺序**：有主键的表必须按 §10.4 的二进制主键序写出（`OrderByPK`；方言做不到时客户端排序），否则读取端归并会错配；无主键表不要求顺序（摘要可交换），按读取顺序写出即可。
- **写出时校验**：每写一行就与上一行比较，违反严格递增立即中止、删除临时文件并报 `E_ORDER`。不允许"写出坏包、读取端才发现"。
- 读取：流式逐行；`hasData=false` 时跳过 `data/` 条目，并把该表标为"不可数据比对"。有主键的表在读取时同样做**严格递增断言**，不满足返回 `E_SNAPSHOT_ORDER`。
- **摘要**：写完每表后用 `Digest` 逐行累加（只覆盖可比较列）写入 manifest；`snapshot verify` 重新计算并比对。
- 限制：单条目解压后 ≤ 8 GiB、总条目 ≤ 10000、路径必须是相对路径且不含 `..`（防 Zip Slip）。**不要只信 ZIP 头里的 `UncompressedSize64`**（可伪造）：解压时用 `io.LimitReader` 逐块计数，超限立即中止；Go 1.20+ 可用 `zip.Reader` 的 `ErrInsecurePath` 辅助拦截绝对路径。
- **临时明文**：仅在不可 seek 的输入（stdin/管道）上落盘。`--temp-dir` 应指向受控目录；Unix 用 `0600`，**Windows 的 `0600` 不产生 ACL**，不要把它当安全边界（§19.3）。崩溃残留按 `.dbsync-dec-*` 前缀在下次启动时清理。

---

## 12. 比对引擎

### 12.1 结构比对

1. 表按 `FullName` 配对。`--ignore-schema-prefix` 时按 `Name` 配对；若同端出现同名不同 schema 的表，**必须报 `E_AMBIGUOUS_TABLE`** 并列出候选，不能用"保留首个"糊过去（原版 #6 的教训）。需要跨 schema 合并时用显式 `--schema-map from=to`。
2. 列按名称配对：默认**精确**（区分大小写）；`--case-insensitive` 时先精确、再忽略大小写匹配。同端"仅大小写不同"的多列必须各自保留并可区分，不能合并。**不要按数据库排序规则自动切换**——排序规则只影响排序，不决定"两个名字是不是同一列"。
3. 索引、约束、对象同理；全部输出按 `FullName` 稳定排序。

### 12.2 类型等价三层

比较方向：**把 target 改成 source 之后是否安全**。

| 判定 | 条件 | 预览变更 | 风险标注 | 预览结果 |
| :--- | :--- | :--- | :--- | :---: |
| 等价 | `ID`、位宽、长度、精度、标度、编码、`Collation` 全同 | 否 | 否 | 0 |
| 可接受 | 同 ID 但文本编码不同（`varchar`↔`nvarchar`）、**source 比 target 宽**（标度/长度放宽） | 是（带提示） | 否 | 0 |
| 修改 | 同 ID/参数，但 `Collation` 不同 | 是（ALTER COLLATE，索引重建） | 否 | 0 |
| 阻断 | 跨 ID 且不在转换族（`text`→`int`）、**source 的长度/精度/标度小于 target**（收窄）、target 可空而 source 非空且存在 NULL | **否** | 是 | 4 |

> **Collation 比较**：只对文本列、只在同方言内比较；MySQL 比较 `CHARACTER_SET_NAME + COLLATION_NAME` 的有效值，SQL Server 读 `sys.columns.collation_name`，PostgreSQL 读 `attcollation`，SQLite 没有列级 collation（跳过并记 info）。跨方言的 collation 名称不可比，只展示不判定。
>
> Collation 变更**不阻断**，但会重建该列上的索引；唯一索引列还要按新 collation 查重（见 §13.3）。

> 收窄方向容易写反：`source` 是"要变成的样子"。`source varchar(50)` 覆盖 `target varchar(100)` 是收窄，可能截断；反过来是放宽，安全。
>
> **阻断语义**：阻断项**不会**写进可导出的 SQL 预览；预览以失败状态返回诊断，绝不生成"带注释的半份脚本"。工具不执行任何语句。
>
> **`--ignore` 语义**：`--ignore=type-encoding` 表示这类差异"既不生成变更、也不报诊断"，必须在报告里记一条 info，避免"忽略"变成"静默"。可忽略项只有"可接受"级；阻断级不可忽略；**排序规则不提供忽略开关**（既然选择比较，就不做假开关）。

转换族白名单（可配）：`Int↔Decimal`、`Date↔Timestamp`、`Int↔Uint`（无负数时）。白名单之外一律阻断，避免"看起来能转"。

### 12.3 默认值规范化

按方言先规范化再比较：去外层冗余括号、去 `CAST(... AS ...)` 包装（同类型时）、统一函数名大小写、去首尾空白、`((0))` 与 `(0)` 视为相同（原版 #16）。规范化函数必须有 golden 用例（至少 30 组）。

### 12.4 数据比对

**前置条件**：两端该表都 `hasData=true`（§11.5）。否则不比对数据，并给出 `E_NO_DATA_SIDE` 诊断——**绝不能把"缺失数据"解释成"源端没有行"**（否则会把 target 全表删空）。

**主键不一致的表不走归并**：只要两端主键的列集合或顺序不同（含一侧完全没有主键），行级配对就失去意义，也无法在旧主键上安全地 UPDATE/INSERT。

- **要求数据同步时默认阻断**（`E_PK_CHANGED`）：让用户显式二选一——`--data=none` 只改结构，或 `--allow-destructive` 做整表替换；
- `--data=none`：只生成主键/列的 DDL，不动数据；主键变更作为普通 DDL 在阶段 3 执行（先删旧约束、再建新约束），不需要整表清空；
- `--allow-destructive`：按**整表替换**处理——阶段 2 清空该表、阶段 3 重建主键/删除旧列、阶段 4 全量插入；
- 禁止"先按旧主键更新、再改主键"的混搭路径，那种顺序在旧主键约束下必然冲突。

```
common := 列名交集(source.columns, target.columns)
for each table with PK and hasData on both sides:
    src := orderedRows(source, table, binaryCollation)
    tgt := orderedRows(target, table, binaryCollation)
    merge(src, tgt):
        src < tgt  → Insert(整行，含 identity 列)
        src > tgt  → Delete(tgt.pk)
        src == tgt →
            changed := 共有列中列哈希不同者（按规范列序逐位）
            changed += source 独有列中"值 != 列默认值"者
            if len(changed) > 0 → Update(pk, changed)
            else                → no-op（绝不生成空 SET）
```

规则：

- **行哈希只覆盖共有列，且按 source 的共有列顺序逐位比较**（§6.4）；因此物理列序、列名大小写都不会制造假变更。
- **源端独有列**（本次新增）由阶段 3 先加列（可空或有默认），数据阶段回填非默认值；值等于默认值时不需要 UPDATE。
- **目标端独有列**（将被删除）不参与比较，值随阶段 3 `DROP COLUMN` 消失。
- **`changed` 为空时跳过**；生成器层再加一次断言（`SET` 为空直接报错，而不是输出非法 SQL）。这是评估 #1 的双保险。
- **不参与数据比对的列**：计算列（computed/generated）、SQL Server 的 `rowversion`/`timestamp` 版本列、以及数据库自动维护的列。它们由数据库生成，两端必然不同；参与比对会造成"永远有差异、永远在更新"。这些列也不出现在 INSERT/UPDATE 的列清单里。
- **Insert 写入整行，包含自增/identity 列**——否则目标库重新分配主键，插入行的 PK 与源库不同，外键全部错位，下一轮 diff 会变成"删+插"，"执行后再比对 = 无差异"永远不成立。方言处理见 §13.4。计算列不写。
- `--exclude-identity` 只用于"故意重建数据"的场景；开启时强制告警，并在报告里标注"主键可能被重排"。
- Update 只 SET 变化列；`--optimistic` 追加 `AND col = 旧值` 守卫（NULL 用 `IS NULL`，不能写 `= NULL`）。**必须检查 `RowsAffected`**：为 0 说明该行在 diff 之后被改过，按 `--on-conflict=abort|skip`（默认 `abort`）处理，不能静默当作成功。**新增列不参与守卫**（target 没有旧值），只对原有列生效。
- Delete 只带主键。
- 差异集按 `(table, pk)` 排序输出，保证确定性。

#### 12.4.1 列集不一致时的配对规则

结构差异与数据差异发生在同一次运行里，因此**数据比对必须能处理"两端列集不同"**：

| 情况 | 规则 |
| :--- | :--- |
| 共有列 | 正常比较列哈希 |
| 源端独有列（新增） | 先看值是否等于该列默认值：等于则不产生变更；不等于则加入 `changed`（回填）。默认值不是字面量（如 `GETDATE()`）时**一律回填**，宁多写不猜。**值为 NULL 且列可空时视为默认，不回填**，避免整表空更新 |
| 目标端独有列（删除） | 忽略；不参与行哈希，不产生 UPDATE；**阶段 3 删列**（必须在 INSERT 之前，否则旧的 NOT NULL 列会挡住插入） |
| 共有列为空 | 该表跳过数据比对，报 `E_NO_COMMON_COLUMNS`（只做结构变更） |
| 类型不可比（§12.2 阻断级） | 数据比对阶段直接阻断，不允许"先转再看看" |

> 这条规则解决的是"结构还没改、数据已经比"的时序矛盾：diff 前后两端列集必然可能不同。实现上不要要求"先改结构再比数据"，那会把一次比对拆成两次运行。

### 12.5 表级短路（诚实版）

| 场景 | 是否短路 | 原因 |
| :--- | :--- | :--- |
| 快照 ↔ 快照 | `dataScope=full`、两端 `hasData` 都为真、**列集相同（schema 摘要一致）**、`rows` 与 `digest` 同时相等 | 两端摘要都由本工具按同一编码计算；行数相等排除极端碰撞；列集不同时摘要本就不可比，自然不短路 |
| 活库 ↔ 活库 | **不短路** | 数据库侧摘要无法证明"相等⇒逐行相等"，误判会静默丢变更 |
| 快照 ↔ 活库 | 默认不短路 | 活库摘要不可信；只有方言能返回与本工具**逐字节同构**的摘要时才允许，目前无此计划 |

**硬性守卫**：任何一侧 `hasData=false`（`--schema-only` 快照）时，禁止数据比对与短路；只做结构比对。若用户显式要求数据比对，报 `E_NO_DATA_SIDE` 并以退出码 4 阻断。

> 评估文档把"表级校验和短路"列为 P0。这里做**降级实现**：先保证正确，摘要能力只在可证明的范围内启用。这是有意偏离，记录为 ADR-007。

### 12.6 差异规模保护

- 单表差异行默认上限 **50 万**（`--max-changes`），超出报 `E_TOO_MANY_CHANGES`，建议分批/加 WHERE；需要更多时显式提高并配合 `--spill`；
- 差异集每行只存：主键值、变化列名、新值、旧值哈希（`--optimistic` 时额外存旧值用于守卫）；不存整行；
- 差异集预算默认 128 MB（`--memory-budget` 的一半）；超限时提示分批，`--spill` 落盘（M2 后期实现）。`--optimistic` 会让每行体积约翻倍，默认关闭，开启时重新估算预算。

### 12.7 视图/过程/函数/触发器比对

- 按 `FullName` + 类型配对，比较规范化后的定义文本；
- 规范化只做**空白与注释**处理（`--ignore-whitespace`），不改写语义；
- 定义为空（权限不足/加密对象）时标为 `unknown`，不判定为"新增/删除"；
- 对象差异进入阶段 6；被视图引用的表/列变更在预检里告警（阶段 1 可自动 drop/recreate 视图，需 `--recreate-views`）。

### 12.8 改名识别与落地

改名识别是**建议**，不是自动行为。没有确认时一律按 DROP + ADD 处理（并标破坏性），绝不猜测后直接改名。

**输入**：`--renames renames.yaml`（或项目文件 `renames:`），显式声明：

```yaml
renames:
  - kind: table
    from: dbo.Users
    to: dbo.AppUsers
  - kind: column
    table: dbo.AppUsers
    from: old_name
    to: new_name
  - kind: column
    table: dbo.Orders
    from: legacy_id
    to: customer_id
    confirm: true   # 缺省 false 时只提示，不生成改名
```

**启发式**（只用于提示，置信度写进诊断）：

| 对象 | 规则 |
| :--- | :--- |
| 表 | 主键列集合相同；列名 Jaccard ≥ 0.6；名称编辑距离 ≤ 3（或互为前后缀） |
| 列 | 类型三层判定为"等价"；可空性相同；同表内唯一边界；名称编辑距离 ≤ 2 或同义词典命中的常见对（`id`/`no`、`name`/`title` 不做自动建议） |

**生成规则**：

| 方言 | 表改名 | 列改名 |
| :--- | :--- | :--- |
| SQL Server | `EXEC sp_rename 'dbo.Old', 'New'` | `EXEC sp_rename 'dbo.T.Old', 'New', 'COLUMN'` |
| PostgreSQL | `ALTER TABLE dbo.old RENAME TO new` | `ALTER TABLE t RENAME COLUMN old TO new` |
| MySQL | `RENAME TABLE old TO new` | `ALTER TABLE t RENAME COLUMN old TO new`（8.0+） |
| SQLite | `ALTER TABLE old RENAME TO new` | `ALTER TABLE t RENAME COLUMN old TO new`（3.25+） |

**连锁处理**：改名放进预览阶段 3（schema-additive），先于数据；索引/约束/外键引用旧列名的，在阶段 5 按新名重建；视图引用旧名的，按 §13.1.1 处理。工具不执行改名，只预览。

**验收**：`TestRenameKeepsData`——改名前后行数、行哈希不变，且第二次 diff 无差异。

---

## 13. 变更计划与 SQL 预览（只读）

> 本节的“阶段”只用于**差异 SQL 预览的排序**，不代表工具会执行。工具没有 apply/execute；`plan.sql`/`plan.json` 只能导出查看，不能在应用内运行。

### 13.1 阶段

| 阶段 | 内容 | 说明 |
| :--- | :--- | :--- |
| 0 preflight | 只读风险检查 | 只影响预览标注；不执行、不阻断工具启动 |
| 1 drop dependents | 预览"删除参与数据变更或阻塞结构变更的外键与唯一索引" | 精确选取：只动受影响的表，不搞全库删除 |
| 2 data-cleanup | 预览 DELETE（target 独有行） | 仅用于排序展示 |
| 3 schema-additive | 预览 CREATE TABLE、ADD COLUMN、放宽类型/可空性、collation 变更、DROP target 独有表/列 | 目标独有列的删除是破坏性差异，只标注 |
| 4 data-load | 预览 INSERT / UPDATE | 仅展示差异 SQL |
| 5 schema-finalize | 预览 UNIQUE/CHECK/索引/FK、收窄类型、自增计数器重设 | 仅展示 |
| 6 objects | 预览视图/过程/函数/触发器/序列 | 仅展示 |
| 7 verify | **不执行**；仅提示用户可以自行复核 | 工具不做执行后自检 |

这样排的原因：
- **删除先于加约束**：否则 target 里的历史重复行/孤儿行会让新唯一键或外键直接失败；
- **目标独有列在装载前删除**：INSERT 只写 source 的列，target 独有的 NOT NULL 列（无默认值）会让插入直接失败；删列是破坏性操作，需 `--allow-destructive`；
- **收窄放在数据之后**：数据先按 source 值装载，再收紧类型；主键变更不走"先更新再改键"的路径，而是整表替换（阶段 2 清空、阶段 3 重建主键）；
- **索引放最后**：避免边写边维护索引；大表可用 `--index-early` 提前建（代价是唯一冲突更早暴露）。

#### 13.1.1 外键、唯一索引与视图的处理（否则数据阶段必然失败）

| 对象 | 策略 |
| :--- | :--- |
| 外键 | 凡是**本计划要改数据或改结构**的表，其相关外键在阶段 1 统一删除，阶段 5 重建。只靠"仅删受阻的"不够：阶段 2 删行与阶段 4 插行同时存在时，父/子顺序不存在单一解（评估文档未覆盖这一点） |
| 唯一索引/唯一约束 | 对**有数据变更**的表，阶段 1 删除、阶段 5 重建。否则"两行交换唯一值"（A→B、B→A）必然中途冲突；`--index-early` 不改变这条规则 |
| 普通索引 | 默认保留（写入变慢但省一次重建）；`--rebuild-indexes` 时随唯一索引一起重建 |
| collation 变更列上的索引 | 阶段 1 删除、阶段 5 重建；MySQL 可能隐式重建，但计划里仍显式处理，保证各阶段一致 |
| 视图 | 默认**不删**。若某变更被依赖视图阻塞，预检报 `E_VIEW_BLOCK` 并给出视图清单；只有显式 `--recreate-views` 才在阶段 1 摘下、阶段 6 按 **source 的定义**重建。**source 中已不存在的视图不要重建**——它本来就该被删除 |
| 触发器 | 默认保留；`--disable-triggers` 时按方言禁用/启用，避免数据装载期间重复写入审计表 |

**关键**：阶段 1 必须记录 `droppedForDependency{kind, fullName, targetDefinition, existsInSource}`。阶段 6 的重建规则：

1. `existsInSource=true` → 用 **source 的定义**重建（不是 target 的旧定义，否则可能引用已删的列）；
2. `existsInSource=false` → 不重建，按"目标独有对象"删除并报告；
3. 多个视图互相依赖时按视图依赖拓扑序重建，环内视图报 `E_VIEW_CYCLE` 由人工处理。

否则"因为列变更被摘下、但自身没差异"的视图会永久消失，或者被重建出一个编译不过的旧定义。

> **预览提示**：这些语句只用于展示 target 变成 source 需要的操作，工具不会执行。真正执行时请自行在维护窗口、用只读账号之外的人工流程操作；本工具不承担执行风险。

**环与自引用外键**：依赖图做 SCC 分解；同一 SCC 内的外键**统一挪到阶段 5**（先建表、后加约束），绝不会像原版那样把环内表从排序结果里丢掉（#11）。同一 SCC 内按 `FullName` 排序建表，保证输出稳定（N5）。

**整表替换路径**（主键变更或一侧无主键）：阶段 2 清空、阶段 3 重建主键/列、阶段 4 全量插入。它绕开"旧主键定位"与"新主键唯一"的矛盾，代价是全表重写；必须 `--allow-destructive`，且报告里标明"该表不可增量"。清空用 `DELETE`，不用 `TRUNCATE`——MySQL 的 `TRUNCATE` 隐式提交，会破坏阶段事务。

### 13.2 依赖图

```go
type Graph struct{ /* 邻接表 + 入度 */ }

// SCC 返回强连通分量；环内表走"两阶段"路径。
func (g *Graph) SCC() [][]NodeID
```

- 节点：表、约束、索引、对象；
- 边：外键依赖、视图→表、视图→视图、触发器→表、过程/函数→表、索引→列；
- **依赖边来源**：优先读目录（SQL Server `sys.sql_expression_dependencies`、PostgreSQL `pg_depend`、SQLite `sqlite_master` 的 SQL 文本）；MySQL 没有等价目录，只能对 `VIEWS.VIEW_DEFINITION` 做受限解析。解析失败时**降级为"未知依赖"并告警**，不要假设无依赖，更不要静默跳过。
- 拓扑序用于阶段 1/3/6 内部排序；SCC 内的边不参与拓扑，转阶段 5。
- **阶段 3 内部顺序**：先按**逆拓扑**删表/删列（子先父后），再按**正拓扑**建表/加列（父先子后）；同一阶段里同时有删除与创建时，不能用一个方向套到底。
- **数据阶段的表序**：阶段 2（删）按拓扑逆序（子先父后），阶段 4（插/改）按拓扑正序（父先子后）；同一 SCC 内串行。外键已在阶段 1 删除，这个顺序是为了减少死锁与临时不一致，而不是满足约束。

### 13.3 预检项（全部先红后绿）

| 检查 | 阻断条件 | 修复建议 |
| :--- | :--- | :--- |
| 破坏性操作 | 存在 DROP TABLE/COLUMN、收窄类型、整表替换，且未给 `--allow-destructive` | 显式开启；报告列出全部破坏项 |
| 依赖视图阻塞 | 变更被视图引用，且未给 `--recreate-views` | 开启重建，或先手工改视图 |
| NOT NULL 收紧 | **最终保留的数据**存在 NULL：同步表看 source，未同步表看 target | 先回填或改为可空 |
| 类型收窄 | **最终保留的数据**有超长/超精度/不可转换值（同步表看 source，未同步表看 target） | 清洗或放宽 |
| 删列 | 列被视图/过程/索引/约束引用 | 先删依赖或 `--recreate-views` |
| 改主键 | 主键列集合/顺序在两端不同且要求数据同步 | `--data=none`（只改结构）或 `--allow-destructive`（整表替换）；引用旧主键的外键在阶段 1 删除 |
| 不安全主键类型 | 主键含浮点/JSON/XML/无二进制序的文本 | 阻断或要求 `--sort=external` + 人工确认 |
| 唯一约束/索引 | **最终数据**有重复（同步表看 source，未同步表看 target） | 去重或改用普通索引 |
| CHECK 约束 | **最终数据**不满足表达式（同步表看 source，未同步表看 target） | 清洗数据，或先以 `NOT VALID`/`WITH NOCHECK` 方式添加 |
| 外键约束 | **最终数据**有孤儿行（同步表看 source，未同步表看 target） | 先补父行或清理子行 |
| 排序规则变更 | 该列被唯一索引/主键引用，且新 collation 下出现重复 | 按新 collation 去重，或阶段 1 删索引、阶段 5 重建 |
| 删除表 | 有外键/视图引用 | 阻断，需 `--allow-destructive` |
| 改名候选 | 启发式命中但未确认 | 提示，需 `--renames` 确认 |
| MySQL 时间范围 | `TimestampTZ` 值早于 1970 或晚于 2038 | 改用 `datetime` 并明确语义，或阻断 |

**精确 vs 采样**：上表的检查必须用走索引的 `EXISTS` 查询（`SELECT 1 FROM t WHERE … LIMIT 1` / `TOP 1`），**不允许采样**——采样假阴性会让阶段 3/5 直接失败。只有"预计影响行数"这类展示信息允许采样，且采样结果不得参与阻断判定。

**检查对象**：预检判断的是"**最终状态**"是否满足约束，不是"当前 target"。同步数据的表最终数据 = source，所以查 source；`--data=none`、无主键跳过、主键不一致阻断的表最终数据 = target 现值，所以查 target。把两者搞反会造成两种错误：用 target 查会把"source 已经修好 NULL、只是还没同步"误判为阻断；用 source 查会漏掉不参与同步的表。

预检 SQL 全部只读；大表用索引列（主键/唯一键）构造查询，避免全表扫描。

### 13.4 生成规则

- **幂等**：`IF NOT EXISTS` / `IF EXISTS` / `CREATE OR REPLACE`（按方言能力）；`--no-idempotent` 可关，但默认开。
- **类型化字面量**：数值不带引号；文本 `N'…'`（SQL Server）/`'…'`；二进制 `0x…`；时间显式 `CAST('…' AS datetime2(7))`；布尔按方言 `1/0` 或 `TRUE/FALSE`；NULL 显式 `NULL`（原版 #7）。
- **建表只带列与主键**：新表的 `CREATE TABLE` 只输出列定义与主键；`UNIQUE`/`CHECK`/索引/外键统一在阶段 5 创建，避免与"阶段 1 摘下、阶段 5 重建"的清单重复。
- **标识符**：一律 `Quote`；大小写按方言规则折叠后再输出。
- **语句分组**：每条语句一个 `Statement{SQL, Object, Destructive, Reversible}`，只用于预览展示与导出。
- **批大小**：INSERT 多值 `--batch=500`（按行宽与**参数个数**自适应：SQLite 的参数上限远低于行数×列数，按 ≤30000 个参数切分；单语句 ≤ 1 MB）；MySQL `max_allowed_packet` 预检。
- **事务**：按能力矩阵输出 `BEGIN/COMMIT`；**MySQL 的 DDL 阶段不要输出 `BEGIN/COMMIT`**（DDL 隐式提交，包了只会误导），改为在脚本头部写显式警告："DDL 隐式提交，本段不可回滚"（原版 #18/#40）。DML 阶段仍可用事务。
- **自增列**：数据装载必须写入原值，否则主键重排、外键错位。按方言生成：
  - SQL Server：`SET IDENTITY_INSERT <t> ON;` … `OFF;`（同一会话、每条语句成对出现）；
  - PostgreSQL：`INSERT INTO t (…) OVERRIDING SYSTEM VALUE VALUES (…)`；
  - MySQL：显式值即可，`AUTO_INCREMENT` 计数器会自动上调；
  - SQLite：显式 rowid/主键即可，`AUTOINCREMENT` 的 `sqlite_sequence` 会自动上调。
  `--exclude-identity` 仅用于重建数据，开启时告警。
- **自增计数器重设**（阶段 5 必做）：SQL Server 的 `IDENTITY_INSERT` **不会**更新当前种子，必须 `DBCC CHECKIDENT ('t', RESEED, <max>)`；PostgreSQL 的 identity/sequence 要 `setval`；MySQL/SQLite 会随显式插入自动上调。漏掉这一步，同步后第一次自动插入就会撞主键。
- **排序规则**：文本列 DDL 必须显式输出 collation——MySQL 用 `MODIFY COLUMN <完整定义> CHARACTER SET <cs> COLLATE <coll>`，SQL Server/PG 用 `ALTER COLUMN ... COLLATE <coll>`；SQLite 没有列级 collation，遇到 `Collation` 差异报 `E_COLLATION_UNSUPPORTED` 并跳过。collation 变更列上的索引在阶段 1 删除、阶段 5 重建。
- **回滚脚本**：只为可逆操作生成（CREATE→DROP、ADD COLUMN→DROP COLUMN、INSERT→按主键 DELETE）；不可逆操作在脚本里用注释标明，**不再伪造 `RolledBack` 标志**。

### 13.5 Plan 的机器格式

```go
type Plan struct {
	FormatVersion     int          `json:"formatVersion"`
	Source            EndpointRef  `json:"source"`
	Target            EndpointRef  `json:"target"`
	TargetFingerprint string       `json:"targetFingerprint"` // 规范化 IR 的 SHA-256（§14.2）
	Phases            []Phase      `json:"phases"`
	Diagnostics       []Diagnostic `json:"diagnostics"`
}

type Phase struct {
	Kind  PhaseKind `json:"kind"`
	Steps []Step    `json:"steps"`
}

type Step struct {
	ID         string      `json:"id"` // 内容哈希：sha256(phase|kind|object|statements)，保证可复现
	Kind       StepKind    `json:"kind"`
	Object     ObjectRef   `json:"object"`
	Statements []Statement `json:"statements"`
	DependsOn  []string    `json:"dependsOn,omitempty"`
}

// PlanState 是执行状态文件（plan.json.state），与 plan.json 分开存放。
type PlanState struct {
	PlanHash string       `json:"planHash"` // plan.json 内容的 SHA-256，防止状态文件被挪用到别的计划
	Records  []StepRecord `json:"records"`
}

type StepRecord struct {
	ID        string    `json:"id"`
	Status    string    `json:"status"` // started / completed / failed
	StartedAt time.Time `json:"startedAt"`
	EndedAt   time.Time `json:"endedAt,omitempty"`
	Rows      int64     `json:"rows,omitempty"`
}
```

- `Destructive`/`Reversible` **只存在于 `Statement`**；`Step` 需要时由语句汇总派生，不重复存（避免两处不一致）。
- `Step.ID` 用**内容哈希**而不是 UUID/随机数：同一输入两次生成的预览必须字节一致（N5）。
- `Plan.FormatVersion` 与 JSON tag 在 M0 冻结；改名即破坏导出兼容。

`plan` 同时输出 `plan.sql`（人看）与 `plan.json`（导出/审阅），二者由同一份 `Plan` 渲染，**不允许各写一套**（原版 #26 的教训）。`plan.json` 不含任何密码，只有 endpoint 的逻辑名与脱敏 DSN；但它**包含行数据**（INSERT/UPDATE 的值），必须按敏感文件对待：默认权限 `0600`，`--plan-json=-` 可写 stdout 交给管道的加密工具。**导出不等于执行**。

### 13.6 SQLite 重建表

SQLite 不支持大部分 `ALTER COLUMN`，以下变更走"重建表"：

- 改列类型、可空性、默认值、排序规则；
- 删除列（3.35+ 支持 `DROP COLUMN`，但被索引/约束/视图引用时仍需重建）；
- 修改/新增 CHECK、主键、外键。

**Plan 表示**：独立 `StepKind = RebuildTable`，一个 Step 内含完整语句序列（**只预览，不提交**）：

```
PRAGMA foreign_keys=OFF;              -- 必须在事务外
BEGIN;
CREATE TABLE "__dbsync_new_<table>" (新结构);
INSERT INTO "__dbsync_new_<table>" (列…) SELECT 列… FROM "<table>";
DROP TABLE "<table>";
ALTER TABLE "__dbsync_new_<table>" RENAME TO "<table>";
-- 重建索引、触发器（从 IR 生成）
PRAGMA foreign_key_check;             -- 有孤儿行则整体失败
COMMIT;
PRAGMA foreign_keys=ON;               -- defer 保证异常时也恢复
```

- 临时表名加固定前缀 + 随机后缀，预检冲突；
- **必须逐条 `ExecContext`**：`modernc.org/sqlite` 的多语句 `Exec` 只 prepare 第一条，其余会被静默丢弃（不报错）。12 步拆成 10 次以上的独立调用；
- **重建清单去重**：重建表时旧表的索引/触发器会随 `DROP TABLE` 消失，必须在该 Step 内重建；计划生成时要把这些对象从阶段 5 的"新建清单"里扣除，否则会重复创建。
- `PRAGMA foreign_keys` 在事务内是 no-op：关闭要在 `BEGIN` 之前，恢复要在 `COMMIT` 之后，并用 `defer` 保证异常时也恢复；`foreign_key_check` 在提交前执行；
- 视图引用在 `RENAME` 时由 SQLite 自动更新（3.25+），但仍按 §13.1.1 先摘后建，避免依赖差异；
- **验收**：`TestSQLiteRebuild`——重建前后行数、行哈希、索引、触发器、外键全部保真，且 `PRAGMA foreign_key_check` 为空。

---

## 14. 执行器（已删除，禁止实现）

本工具是**只读比对工具**，不提供 `apply`、`execute`、`resume`、`dry-run`、事务、回滚、自检执行。

- 代码中不得出现 `Exec/ExecContext`（`internal/readonly` 测试静态拦截）；
- SQL 只用于预览/导出，不能在本工具内执行；
- 数据库连接必须使用只读账号；
- 任何"顺手加一个执行按钮"的改动都违反产品边界。

## 15. CLI 设计（v2.x，v2.0 暂不实现）

> 本节保留为后续规格。v2.0 不构建 `cmd/dbsync`，所有能力通过桌面工作台调用 service；endpoint 语法、命令、退出码在 v2.x 复用本文，不另设计。

### 15.1 endpoint 语法

```
mssql://user:pass@host:1433/db?encrypt=true
mysql://user:pass@host:3306/db
postgres://user:pass@host:5432/db?sslmode=require
sqlite:///path/to/file.db
snapshot://./baseline.dbsync?pass_env=DBSYNC_SNAPSHOT_PASS
```

也支持 `--source-env DBSYNC_SOURCE_DSN` / `--target-env DBSYNC_TARGET_DSN`，避免 DSN 进 shell history。快照口令也可写在 query 里（`pass_env=NAME`，NAME 是环境变量名），与 `--pass-env` 等价。

**数据选择**：`--data=auto|all|none`（默认 auto：所有启用的表都比对；无主键表走多重集，`--no-pk-strategy=skip` 时只报告不生成 DML）；`--schema-only` 等价于 `--data=none`。

### 15.2 命令

| 命令 | 作用 | 关键参数 |
| :--- | :--- | :--- |
| `dbsync snapshot create` | 导出快照（默认含数据、**默认不加密**） | `--source` `--output` `--tables` `--schema-only` `--with-count` `--encrypt none\|age`（默认 `none`） `--pass-env` `--age-recipient` |
| `dbsync snapshot inspect` | 查看 manifest/表清单 | `--json` `--pass-env` |
| `dbsync snapshot verify` | 校验摘要与完整性 | `--pass-env` |
| `dbsync diff` | 比对并输出差异 | `--source` `--target` `--tables` `--include` `--exclude` `--schema-only` `--data` `--format` `--output` `--fail-on` `--rows` |
| `dbsync plan` | 生成**只读 SQL 预览**（不执行） | `--source` `--target` `--tables` `--include` `--exclude` `--schema-only` `--out` `--plan-json` `--allow-destructive` `--renames` |
| `dbsync schema` | 打印 IR（调试基线） | `--source` `--format json` |
| `dbsync ddl export` | 导出整库 DDL（源或目标） | `--source`/`--target` `--side source\|target` `--out` `--format sql` |
| `dbsync version` | 版本 | `--json` |

**全局参数**（所有命令可用）：

| 参数 | 默认 | 说明 |
| :--- | :--- | :--- |
| `--config` | `.dbsync.yaml` | 项目文件 |
| `--verbose` / `--quiet` | info | 日志级别 |
| `--log-format` | text | `text`/`json` |
| `--timeout` | 30m | 单次操作超时 |
| `--parallel` | `min(GOMAXPROCS,4)` | 表级并发 |
| `--max-conns` | 8 | 单 endpoint 连接上限（须 ≥ `2×parallel+2`） |
| `--memory-budget` | 256MiB | 进程内存预算（差异集/多重集各占一半） |
| `--sort` | db | `db`（带单调断言）/`external`，见 §10.4 |
| `--max-changes` | 500000 | 单表差异行上限 |
| `--spill` | false | 超限时落盘 |
| `--temp-dir` | 系统临时目录 | 加密快照解密与落盘位置 |
| `--no-color` | false | 关闭彩色输出 |

**命令级参数**（不属于全局）：`--optimistic`（diff/plan）、`--batch`（plan/emit，默认 500）、`--schema-map`（diff/plan）、`--rows`（diff）、`--case-insensitive` / `--ignore-schema-prefix` / `--ignore` / `--ignore-whitespace` / `--include-generated`（diff/plan 比较选项）、`--allow-unknown-types`、`--no-idempotent`（plan 预览）、`--with-count` / `--snapshot-window`（snapshot create）、`--source-env` / `--target-env`（任何需要 endpoint 的命令）。

### 15.3 退出码

| 码 | 含义 | 场景 |
| :---: | :--- | :--- |
| 0 | 成功且无差异；`plan` 预览生成成功 | 默认 |
| 1 | 成功但**有差异** | `diff` 默认；或 `--fail-on` 命中；`--fail-on=none` 时返回 0 |
| 2 | 用法/配置错误 | 参数冲突、配置文件语法错 |
| 3 | 运行时错误 | 连接、IO、快照损坏、未支持类型 |
| 4 | 风险阻断 | `plan` 预览的阻断项 |

> 与旧 CLI 的 0/1/2 兼容：0/1/2 语义不变，3/4/5 是新增，不会让既有脚本误判。`plan` 的"生成了计划"是成功，因此默认 0——CI 里判断差异请用 `diff`。
>
> `--fail-on` 取 `none|any|blocking`（默认 `any`）：`none` 永远返回 0；`any` 有任何表/行差异即返回 1；`blocking` 只在存在阻断级诊断时返回 1。`--fail-on` 只影响退出码，不影响报告内容。

### 15.4 配置优先级

`--flag` > 环境变量（`DBSYNC_*`）> 项目文件 `.dbsync.yaml` > 用户配置 `~/.config/dbsync/config.yaml` > 默认值。

配置里引用环境变量用 `env:NAME` 前缀（只在读取配置时解析，不写回）；DSN 也可以用 `--source-env NAME` 直接指定变量名。

```yaml
# .dbsync.yaml
source: env:DBSYNC_SOURCE_DSN
target: env:DBSYNC_TARGET_DSN
include: ["dbo.*"]
exclude: ["dbo.Audit*", "*.tmp_*"]
data: auto
tables:
  dbo.Users: { data: true, updates: changed-columns }
renames:
  - { table: dbo.Users, from: old_name, to: new_name }
policies:
  allowDestructive: false
  idempotent: true
```

### 15.5 示例

```bash
# 导出生产基线（默认不加密；敏感环境加 --encrypt age）
DBSYNC_SOURCE_DSN="mysql://user@host:3306/app" \
dbsync snapshot create --source-env DBSYNC_SOURCE_DSN \
  --output baseline.dbsync --tables "app.*"

# 比对：快照（期望）→ 同方言测试库（现状）
dbsync diff --source snapshot://./baseline.dbsync --target "mssql://sa@test/app?encrypt=true" \
  --format json --fail-on any

# 生成只读 SQL 预览（人看 + 导出审阅；不执行）
dbsync plan --source snapshot://./baseline.dbsync --target "mssql://sa@test/app?encrypt=true" \
  --out plan.sql --plan-json plan.json
```

---

## 16. 报告与 CI 集成（v2.x；v2.0 只做 GUI 导出）

> v2.0 的 GUI 导出 `plan.json` / `plan.sql` 与比较结果 JSON；Markdown/HTML/SARIF 报告中心在 v2.1（B7），CLI 在 v2.x。本节其余内容保留为 CLI 阶段规格。

### 16.1 报告格式

| 格式 | 用途 | 稳定性 |
| :--- | :--- | :--- |
| `text` | 终端阅读 | 不承诺 |
| `json` | CI/工具链 | **schemaVersion=1 冻结** |
| `md` | PR 评论 | 不承诺 |
| `sarif` | GitHub Code Scanning | 按 SARIF 2.1.0 |

默认只输出**汇总 + 结构变更**；加 `--rows` 才输出行级明细（可能很大，CLI 默认截断到 1 万行并标 `"truncated": true`）。

**字段方向**：`current` = target 现状，`desired` = source 期望；不要用 `from`/`to`（读者会各猜一次方向）。`tablesAdded` 指"target 缺少、需要新建"的表，即 source 独有。

JSON 结构（节选）：

```json
{
  "schemaVersion": 1,
  "generatedAt": "2026-11-27T10:00:00Z",
  "source": { "kind": "snapshot", "label": "baseline.dbsync" },
  "target": { "kind": "mysql", "label": "test/app" },
  "summary": { "tablesAdded": 1, "tablesRemoved": 0, "tablesChanged": 2,
                "rowsInserted": 12, "rowsUpdated": 3, "rowsDeleted": 0,
                "diagnostics": { "blocking": 1, "warning": 4 } },
  "tables": [
    { "name": "dbo.Users", "status": "changed",
      "columns": [ { "name": "email", "change": "modified",
                     "current": "varchar(100)", "desired": "varchar(255)" } ],
      "data": { "inserted": 12, "updated": 3, "deleted": 0 } }
  ],
  "diagnostics": [
    { "code": "E_PREFLIGHT_NOT_NULL", "severity": "blocking",
      "object": "dbo.Users.email", "message": "存在 1204 行为 NULL" }
  ]
}
```

### 16.2 CI 示例

```yaml
- name: Schema drift check
  continue-on-error: true          # 有差异时也要继续上传 SARIF
  env:
    DBSYNC_TARGET_DSN: ${{ secrets.DBSYNC_TARGET_DSN }}
  run: |
    dbsync diff --source snapshot://./baseline.dbsync \
                --target-env DBSYNC_TARGET_DSN \
                --format sarif --output drift.sarif --fail-on blocking
- uses: github/codeql-action/upload-sarif@v3
  if: always()
  with: { sarif_file: drift.sarif }
```

### 16.3 SARIF 映射

- `ruleId` = 诊断码（如 `E_PREFLIGHT_NOT_NULL`）；
- `level` = blocking→error、warning→warning、info→note；
- `locations[].logicalLocations` = 表/列（SARIF 逻辑位置，不伪造文件路径）。

---

## 17. GUI（Wails v3，M3 主交付）

> 布局、功能清单与线框图见 [`DBSyncTool-功能与界面设计.md`](DBSyncTool-功能与界面设计.md)。本节只约束"GUI 不许有第二份实现"。

- **原则**：GUI 不实现比对逻辑，只调用 `internal/service`（GUI 与未来 CLI 共用的编排层）。
- 绑定：`Diff(req) (DiffView, error)`、`Plan(req) (PlanView, error)`、`Cancel(runID string) error`。**只读**：不提供 Apply；`Plan` 只返回 SQL 预览。**不要用 `chan` 参数**——Wails 绑定生成器不支持 channel，进度用 `app.Event.Emit("dbsync:progress", event)` 推送。
- 取消：Go 侧维护 `map[runID]context.CancelFunc`，前端调 `Cancel(runID)`；不要靠关闭 channel。
- 大数据：差异列表分页（每页 500 行）+ 虚拟滚动；行级 diff 只取选中表。**v2.0 必须有基础数据差异页（只读、分页、列高亮）**，搜索/导出/按列视图放 v2.1。
- WebView2：Windows 10/11 通常自带；安装包内置引导（`wails doctor` 可诊断），缺失时给下载指引。
- **平台**：v2.0 只发 Windows 10/11 x64；不做 macOS/Linux GUI（未来版本再评估）。
- 不为 GUI 单独写 SQL/差异代码；出现第二份实现即视为退回原版 #25/#26。

---

## 18. 并发、内存与性能

### 18.1 并发模型

- 表级并行：`--parallel`（默认 `min(GOMAXPROCS, 4)`）；每张表在**每个 endpoint** 各占一个连接，因此单 endpoint 的连接上限应满足 **`--max-conns ≥ --parallel + 2`**（+2 留给内省与元数据查询），不足时自动降并发并告警。
- 端点连接上限：`--max-conns`（默认 8），超出排队；避免把生产库打满。
- 顺序：结果按表名排序收集（worker 带序号，主协程按序 flush），保证确定性。
- 取消：`signal.NotifyContext` 捕获 Ctrl-C，先取消查询再关连接。

### 18.2 内存

| 环节 | 预算 | 超限行为 |
| :--- | :--- | :--- |
| 单表行缓冲 | 不缓冲整表（流式） | — |
| 差异集 | 128 MB 默认 | 报错建议分批，或 `--spill` |
| 无主键多重集 | 128 MB | 落盘多路归并 |
| 快照读取 | 单行 ≤ 64 MB | 报 `E_ROW_TOO_LARGE` |
| Go 运行时 + 驱动 + 连接缓冲 | 约 128 MB | 设 `GOMEMLIMIT` 辅助，不承诺硬上限 |
| **合计目标** | **≤ 384 MB** | 与 §2.2 N3 一致；超限先降并发，再 `--spill` |

### 18.3 基准

`internal/bench` 固定数据集：10 张表 × 10 万行（含文本/二进制/时间/十进制）。基准矩阵必须覆盖 **行宽 200B / 1KB / 10KB × 主键索引可用/不可用 × 已支持方言（MySQL/SQLite/PostgreSQL/SQL Server）**，并记录：`ORDER BY` 是否触发排序、临时空间、RSS、行/秒。CI 只跑 size 与启动，性能基准本地跑并归档；**M2 结束前必须出第一组数字**，不能拖到 M3。

> `modernc.org/sqlite` 是纯 Go、可交叉编译，但比 CGO 驱动慢（社区基准常见 1.5–2× per-query CPU、二进制约 +3 MB）。选它是为了分发便利，SQLite 方言的吞吐必须单独测，不能沿用其他方言的结论。

---

## 19. 可观测性、错误与安全

### 19.1 日志

- `log/slog`，JSON handler（`--log-format json`）或文本；
- 级别 `--verbose`（debug）、默认 info、`--quiet` 只输出错误；
- 每条日志带 `run_id`、`table`、`phase`；
- **脱敏**：DSN、密码、快照口令、行数据默认不记；`--log-rows` 需显式开启（本地调试）。

### 19.2 错误模型

```go
type Error struct {
	Code  string // E_CONN / E_INTROSPECT / E_ORDER / E_UNSUPPORTED / ...
	Op    string
	Table string
	Err   error
}

func (e *Error) Error() string
func (e *Error) Unwrap() error
```

- `errors.Is/As` 可判定；v2.x CLI 按 `Code` 映射退出码（v2.0 由 GUI 显示错误码）；
- 用户消息给"下一步做什么"，技术细节进日志；
- 禁止 `panic` 出现在库代码（`cmd` 顶层 recover 仅用于兜底并打印堆栈）。

### 19.3 安全清单

- [ ] 口令优先从 env/stdin/文件读；允许写进 DSN 但会告警，且日志/报告一律脱敏；
- [ ] 快照加密用 AEAD（age），口令模式走 scrypt；
- [ ] 临时文件：Unix `0600`；Windows 用私有目录 + 显式 ACL（`0600` 在 Windows 不生效）；优先走 `DecryptReaderAt` 避免落盘；
- [ ] 口令参数有上限（scrypt `SetMaxWorkFactor`），防止恶意快照做慢速 DoS；
- [ ] ZIP 条目校验路径（防穿越）、大小（防炸弹）；
- [ ] 标识符按方言转义；值只经字面量渲染；
- [ ] 预检只读；执行前校验计划指纹；
- [ ] 破坏性操作需显式开关；
- [ ] 非对抗摘要：不可信快照的完整性用 SHA-256 / age 认证，不拿 xxh3 当安全边界；
- [ ] ZIP 解压流式限流（`io.LimitReader`），不只信头部声明的大小；
- [ ] 默认不打印行数据。

---

## 20. 测试策略

### 20.1 分层

| 层 | 范围 | 工具 | 门禁 |
| :--- | :--- | :--- | :--- |
| 单元 | value/model/diff/plan/emit | `go test` | 覆盖率 ≥ 80%（核心包） |
| Golden | MySQL SQL 预览 | `testdata/` + `-update` | 全绿 |
| 契约 | 生成器调用点形状 | 表驱动 | 全绿（对应 #1/#39） |
| 集成 | 真库/文件全链路 | `//go:build integration` + Docker | MySQL + SQLite + PostgreSQL 矩阵 |
| 属性 | 编码/比较全序 | `pgregory.net/rapid` | 全绿 |
| 模糊 | 快照解析/编码 | `go test -fuzz` | 30 分钟无崩溃 |
| 区域 | locale 无关 | `tr-TR/de-DE/en-US` | 快照 round-trip 一致 |
| 幂等 | 执行后再比对 | 集成测试 | 无差异 |

### 20.2 集成矩阵

**先跑 MySQL 矩阵**（M1 主方言，目标 8.0.16），再跑 **SQLite / PostgreSQL / SQL Server**。SQLite 用临时文件；MySQL 用 `mysql:8.0.16` 或直连测试库；PostgreSQL 用 `postgres:16`；SQL Server 用 `mcr.microsoft.com/mssql/server:2022-latest`。每个方言在自己的 source↔target 配对里跑（同方言）：

> 集成测试只连**专用、可清理**的测试库/临时 schema，测试前后自行建删；**禁止指向生产**。连接信息走本地环境变量或未跟踪配置，不写入仓库。

1. 建源/目标 schema（含外键环、自引用、跨 schema 同名表、二进制、换行、`\N`、时区、collation 差异）；
2. `diff` → `plan`（只读预览）→ 再 `diff`，断言目标未被修改、差异仍在；
3. 断言：没有写语句进入生产路径、预览可解释。

### 20.3 关键用例（对应 P0）

| 用例 | 断言 |
| :--- | :--- |
| `TestUpdateUsesChangedColumns` | 生成 `SET a=…,b=…`，不含主键，**不为空** |
| `TestEmptyUpdateNeverEmitted` | 变化列为空时不生成任何 UPDATE（兜底） |
| `TestInsertCarriesAllColumns` | INSERT 列数 = 源表列数（含 identity，除 `--exclude-identity`） |
| `TestIdentityPreserved` | 预览的 INSERT 显式带 identity 列；不执行 |
| `TestBinaryRoundTrip` | `[]byte` → 快照 → 值为 `KindBytes`，SQL 为 `0x…` |
| `TestMultilineAndBackslashN` | 含 `\n`、`\N` 的值 round-trip 一致 |
| `TestCaseOnlyPKDifference` | 大小写不同的两行都能保留，不撞键 |
| `TestCrossSchemaSameName` | 两表分别处理，不抛重复键 |
| `TestColumnOrderIndependent` | 两端物理列序不同，不产生假变更 |
| `TestCollationCompared` | MySQL 源 `utf8mb4_0900_ai_ci`、目标 `utf8mb4_bin` → 预览含 ALTER COLLATE；目标不被修改 |
| `TestCollationUniquePreflight` | 唯一索引列改 collation 后出现重复 → 阻断，不生成计划 |
| `TestAddedColumnBackfill` | 新增列的非默认值被回填；等于默认值不产生 UPDATE |
| `TestCycleTwoPhase` | 环内表仍被建，FK 在阶段 5 |
| `TestExistingFkHandled` | 既有外键在阶段 1 删除、阶段 5 重建，数据阶段不因父/子顺序失败 |
| `TestViewDroppedRecreated` | 被摘下的无差异视图在阶段 6 被重建 |
| `TestSchemaOnlySourceRejected` | schema-only 快照作 source 比数据 → `E_NO_DATA_SIDE`，退出码 4，**不删任何行** |
| `TestTypeLiteralGolden` | #7 的类型化字面量 golden（数值/文本/二进制/时间/布尔/NULL） |
| `TestLocaleIndependence` | 三种 locale 下编码字节一致 |
| `TestOrderViolationFailsLoud` | 乱序输入返回 `E_ORDER`，不产出差异 |
| `TestRenameKeepsData` | 改名前后行哈希不变，二次 diff 为空 |
| `TestPkChangeRequiresReplace` | 主键变化默认跳过数据并报 `E_PK_CHANGED`；`--allow-destructive` 时走整表替换 |
| `TestSQLiteRebuild` | 重建前后数据/索引/触发器/外键保真，`foreign_key_check` 为空 |

### 20.4 运行方式

```bash
go test ./...                          # 单元
go test -tags=integration ./tests/...  # 集成（需 docker）
go test ./internal/emit -run Golden -update   # 更新 golden
go test -fuzz=FuzzSnapshot -fuzztime=30m ./internal/snapshot
```

---

## 21. 编码规范

- **Go 1.26**；`go.mod` 写具体版本 `go 1.26.0`（`go 1.26.x` 不是合法指令）。注意 Go 1.26 的 `go mod init` 默认写 `go 1.25.0`，必须 `go mod edit -go=1.26.0` 显式改。
- `gofmt` + `goimports`；`golangci-lint`（errcheck、govet、staticcheck、revive、depguard、gosec）；CI 阻断。
- 错误：一律 `%w` 包装；不忽略 `err`；不用 `panic` 做控制流。
- `context`：所有 IO 函数第一个参数是 `ctx`；禁止存进 struct。
- 接口：消费方定义（`dialect.Queryer` 在 `dialect` 包内，而不是 `sql` 包）。
- 注释：**中文**（遵循仓库约定），导出符号必须有 doc comment；`TODO(owner):` 格式。
- 测试：`TestXxx_场景_期望`；表驱动优先；`t.Parallel()` 用于纯函数。
- 提交：中文，`✨/🐛/♻️/📝/✅` 前缀，正文写"为什么"。
- 覆盖率：核心包 ≥ 80%；GUI 以冒烟/端到端测试为主；`cmd`（v2.x）暂不设门禁；新增代码不得降低总覆盖率。

---

## 22. 里程碑与任务拆解

### M0 决策与坏例集（3–5 天）

| ID | 任务 | 产出 |
| :--- | :--- | :--- |
| M0-1 | 关闭 §23.2 的开放问题，冻结 ADR-001–006；007/008/009/010 立草案 | `docs/adr/*.md` |
| M0-2 | 建 `testdata` 骨架与 **13 条 P0 回归用例**（覆盖 10 条 P0，全红） | `go test` 全红 |
| M0-3 | 定 JSON schema v1 与 manifest schema | `docs/schema/*.json` |

**出口**：坏例集能跑、全红；schema 冻结。

### M1 值模型 + MySQL + 快照（1–2 周）

| ID | 任务 | 验收 |
| :--- | :--- | :--- |
| M1-1 | `value` 包：Kind/Value/Decimal/Encode/Compare/Hash | 属性测试绿 |
| M1-2 | **MySQL 方言**：Introspect + ReadType/EmitType + Emit 基础（含 collation 读取） | 能读 IR、生成 CREATE |
| M1-3 | 快照 v2 读写（**默认明文**，可选 age；结构+数据） | round-trip 无损 |

**出口**：service 层能创建/校验/读取快照（MySQL），不依赖 CLI。

### M2 补齐三方言 + 比对/计划/执行（3–4 周）

| ID | 任务 | 验收 |
| :--- | :--- | :--- |
| M2-1 | `diff`：结构 + 数据 + 归并 + 排序断言 | 单表比对正确 |
| M2-2 | `plan`：依赖图、SCC、预检、阶段、collation 变更 | MySQL 全链路 |
| M2-3 | `plan` 只读预览：SQL 生成、风险标注、只读断言 | 目标数据/结构在预览前后完全一致 |
| M2-4 | MySQL / SQLite / PostgreSQL / SQL Server 方言 | 四方言结构+数据差异 |
| M2-5 | 基准与 N3/N4 实测 | 数字写进 README，不达标就调架构 |

**出口**：MySQL/SQLite/PostgreSQL/SQL Server 的"建表→比对"通过 service 测试；目标库在预览前后不变。

### M3 桌面工作台（3–5 周，v2.0 主交付）

> 功能清单、布局与线框图见 [`DBSyncTool-功能与界面设计.md`](DBSyncTool-功能与界面设计.md)。

| ID | 任务 | 验收 |
| :--- | :--- | :--- |
| M3-1 | Wails v3 外壳、三阶段工作台、端点卡片、对象树/检查器 | 不跳页完成"比较→差异→SQL 预览" |
| M3-2 | 项目 + 凭据库 + 连接抽屉 | 逻辑名连接、重启恢复、密码不落日志 |
| M3-3 | 结构差异 + **基础数据差异网格**（只读、分页、列高亮） | 数据差异可见；无执行入口 |
| M3-4 | 比较选项 + 规则预览 | 选项与比较器一致；排序规则默认比较；列顺序不出现 |
| M3-5 | SQL 预览页 + 整库 DDL 导出；**不提供执行页/按钮** | 与 service 同一份 Plan；只读 |
| M3-6 | **便携 ZIP**（最快交付）+ WebView2 检测；Inno Setup 后置 | 解压即用；缺 WebView2 有引导 |

**出口**：v2.0 DoD 全部勾选。

### M4 桌面增强（v2.1，2–3 周）

| ID | 任务 | 验收 |
| :--- | :--- | :--- |
| M4-1 | 数据差异增强（搜索、导出、按列视图） | 10 万行不卡 |
| M4-2 | 快照库（标签/来源/保留） | 已完成：本地库列出/使用/删除 |
| M4-3 | 脚本目录端点（`dir://`） | 只读比对 Git 工作区 |
| M4-4 | 多目标比较 | 一套源对 N 个目标 |
| M4-5 | 依赖影响视图 + 接受/忽略规则 + 报告中心 | 报告中心已完成（MD/HTML/JSON）；依赖/接受待做 |

**工期**：v2.0（M0–M3）约 7–11 周（乐观）/ 12–18 周（含返工）；M4 另计 2–3 周。CLI 不在 v2.0 范围内，留到 v2.x。评估文档的"7–12 周"是含 CLI 的旧口径，本版 GUI-first 按 7–11 周。

---

## 23. 风险、开放问题与 ADR

### 23.1 风险登记

| 风险 | 影响 | 缓解 |
| :--- | :--- | :--- |
| 排序规则不一致导致错配 | 假删除/假插入 | §10.4 三层策略 + `E_ORDER` 显式失败 |
| 列集/列序不同导致假变更 | 全表 UPDATE 或空 SET | §6.4 行哈希按列名排序、只覆盖共有列；§12.4.1 列集规则 |
| identity 主键被重排 | 外键错位、二次 diff 不收敛 | §13.4 显式写入 + 方言开关；`TestIdentityPreserved` |
| schema-only 快照当 source | 误删目标全表 | §12.5 硬性守卫 + `E_NO_DATA_SIDE` |
| 依赖视图被摘下未重建 | 执行后视图消失 | §13.1.1 记录并**无条件重建** |
| DSN 直接透传驱动 | 四种端点全部连不上 | §9.3 适配层 + 每种驱动 `Ping` 集成测试 |
| age 旧版本/关闭顺序错误 | 加密包损坏或临时明文泄露 | §11.1 `DecryptReaderAt` + `zip.Close` 先于 `age.Close` |
| SQLite 多语句 Exec / PRAGMA 每连接 | 重建表只执行一半、外键未生效 | §13.6 逐条执行 + DSN `_pragma` |
| 十进制/时间精度丢失 | 数据被"悄悄改小" | 值模型保真 + 集成用例覆盖极值 |
| MySQL DDL 不可回滚 | 半途失败留下半套结构 | 阶段化 + 预检 + 明确告警 |
| 大表差异集撑爆内存 | OOM | `--max-changes` + `--spill` |
| SQLite 改列需重建表 | 实现复杂、易丢数据 | M2 明确"重建表"路径与回滚脚本 |
| Wails 依赖 WebView2 | 老内网机不可用；**v2.0 没有 CLI 兜底**，装不上就完全不可用 | 安装包内置检测与引导；提供离线 WebView2 安装包说明；列为 v2.0 发布阻塞风险 |
| MySQL 8.0.16 无 0900_bin | 只能用 PAD SPACE 的 `utf8mb4_bin`，尾部空格排序会相等 | 严格递增断言保留；主键唯一性本身不允许尾空格共存；必要时 `--sort=external` |
| collation 变更重建索引 | 大表锁表/耗时，唯一键可能冲突 | 阶段 1 删索引、阶段 5 重建；唯一列先按新 collation 查重 |
| 快照默认明文 | 数据带出内网时泄露 | 敏感场景手动开 age；UI 明确提示"当前未加密" |
| 单人维护 | 工期与质量 | M0 坏例集 + 自动化矩阵，降低"人肉记忆" |
| 序列当前值不同步 | 同步后自增值可能冲突或跳号 | 文档明确边界；identity 表用 §13.4 的 reseed，独立 sequence 手工处理 |

### 23.2 开放问题（M0 关闭）

> 已确认：**Go 1.26.0** 可用；**MySQL 目标版本 8.0.16**（无 `utf8mb4_0900_bin`，用 `utf8mb4_bin`；CHECK 从 8.0.16 起强制执行）。

1. SQL Server 最低版本？（决定 `datetime2`、`BIN2`、`STRING_AGG` 可用性；MySQL 优先后可放到 M2 前定）
2. SQL Server BIN2 与 Go 的 UTF-8 字节序在**补充平面字符**（emoji、CJK 扩展 B）上是否完全一致？不一致则 SQL Server 文本主键必须走外排（需要实测向量，不能只信文档）。
3. 是否需要"仅生成差异、不读全表"的采样模式？（与正确性冲突，需产品取舍）
4. WebView2 离线策略：预装 / 内置 Bootstrapper / 内置固定运行时（无 CLI 兜底，这是发布阻塞项）。

### 23.3 ADR 索引

| ADR | 决策 | 状态 |
| :--- | :--- | :--- |
| ADR-001 | Go 1.26.0 + Wails v3；桌面工作台先行，CLI 暂缓 | 已定 |
| ADR-002 | 值归一化规则（NaN/-0/十进制/时区） | 已定，M1 复核 |
| ADR-003 | 快照 = ZIP + NDJSON；**默认不加密**，按需 age | 已定 |
| ADR-004 | 方向统一 source→target | 已定 |
| ADR-005 | 退出码 0/1/2/3/4/5（v2.x CLI） | 已定，暂不实现 |
| ADR-006 | 排序：数据库二进制排序 + 单调断言 + 外排回退 | 已定 |
| ADR-007 | 表级摘要只在可证明范围启用 | 待 M2 复核 |
| ADR-008 | v2.0 不做跨方言数据同步，只做结构报告 | 已定 |
| ADR-009 | v2.0 只交付桌面工作台，CLI 推到 v2.x；GUI 必须含基础数据页 | 已定 |
| ADR-010 | 列顺序不参与比较，不提供相关开关 | 已定 |
| ADR-011 | MySQL 8.0.16 优先，M1 做 MySQL；其余三方言 M2 | 已定 |
| ADR-012 | 只做 Windows 10/11 x64，不做 macOS/Linux GUI | 已定 |
| ADR-013 | 排序规则参与比较；差异生成 ALTER COLLATE，索引重建 | 已定 |
| ADR-014 | 快照默认不加密；敏感场景手动开 age | 已定 |
| ADR-015 | 仅中文、固定浅色、无主题切换、无自动更新 | 已定 |
| ADR-016 | 发布以便携 ZIP 优先，Inno Setup 后置 | 已定 |
| ADR-017 | MySQL 8.0.16 用 `utf8mb4_bin`；8.0.17+ 探测到 `utf8mb4_0900_bin` 才用 | 已定 |

---

## 附录 A：快照 manifest schema（节选）

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["formatVersion", "toolVersion", "createdAt", "dataScope", "engine", "tables"],
  "properties": {
    "dataScope": { "enum": ["full", "schema-only"] },
    "formatVersion": {
      "type": "object",
      "required": ["major", "minor"],
      "properties": {
        "major": { "const": 2 },
        "minor": { "type": "integer", "minimum": 0 }
      }
    },
    "toolVersion": { "type": "string" },
    "createdAt": { "type": "string", "format": "date-time" },
    "engine": {
      "type": "object",
      "required": ["kind"],
      "properties": { "kind": { "enum": ["sqlserver", "mysql", "postgres", "sqlite"] }, "version": { "type": "string" } }
    },
    "tables": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["schema", "name", "columns", "hasData"],
        "properties": {
          "schema": { "type": "string" }, "name": { "type": "string" },
          "columns": { "type": "integer", "minimum": 0 },
          "rows": { "type": ["integer", "null"], "minimum": 0 },
          "digest": { "type": ["string", "null"], "pattern": "^xxh3-128:[0-9a-f]{32}$" },
          "hasData": { "type": "boolean" }
        }
      }
    }
  }
}
```

---

## 附录 B：规范化编码与 NDJSON 值语法

### B.1 二进制编码

见 §6.3；完整实现必须以 `internal/value/encode_test.go` 的向量为准（每种 Kind 至少 3 组边界值：最小、最大、空/零）。

### B.2 NDJSON 值数组

| 语法 | 含义 |
| :--- | :--- |
| `["n"]` | NULL |
| `["b",true]` | 布尔 |
| `["i","-123"]` | 有符号整数（十进制字符串，避免 JS 精度问题） |
| `["u","18446744073709551615"]` | 无符号整数 |
| `["d","12.3400"]` | 十进制（保留原始标度用于展示，比较用数值） |
| `["f","1.5"]` | 浮点（`strconv.FormatFloat(..., 'g', -1, 64)`） |
| `["s","文本"]` | UTF-8 文本 |
| `["x","base64"]` | 二进制 |
| `["ts","2026-11-27T10:00:00.1234567Z"]` | 绝对时刻（UTC，RFC3339Nano） |
| `["t","2026-11-27T10:00:00.1234567"]` | 无时区时间 |
| `["dt","2026-11-27"]` | 日期 |
| `["tm","10:00:00.1234567"]` | 时间 |
| `["j","{...}"]` | JSON 原始文本 |

> `["d",…]` 的文本只是**展示形式**；规范化编码按数值归一（§6.2），所以 `12.3400` 与 `12.34` 的哈希相同。

---

## 附录 C：类型映射全表

| 规范 | SQL Server | MySQL | PostgreSQL | SQLite | 备注 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Bool | `bit` | `tinyint(1)` | `boolean` | `BOOLEAN` | SQLite 用 0/1 存 |
| Int(8/16/32/64) | `tinyint/smallint/int/bigint` | `tinyint/smallint/int/bigint` | `smallint/integer/bigint` | `INTEGER` | SQLite 无宽度 |
| Uint(8/16/32/64) | `tinyint/smallint/int/decimal(20,0)` | `tinyint unsigned/.../bigint unsigned` | `numeric(20,0)` | `UINT64_TEXT` | SQL Server 无无符号；SQLite `INTEGER` 有符号 64 位，存不下 uint64 |
| Decimal(p,s) | `decimal(p,s)` | `decimal(p,s)` | `numeric(p,s)` | `DECIMAL_TEXT` | p≤38（SQL Server）、p≤65（MySQL）；SQLite 文本保精度 |
| Float(32/64) | `real/float` | `float/double` | `real/double precision` | `REAL` | |
| Text(n/-1) | `nvarchar(n)/nvarchar(max)` | `varchar(n)/longtext` | `varchar(n)/text` | `TEXT` | `nvarchar`=UTF16 |
| Binary(n/-1) | `varbinary(n)/varbinary(max)` | `varbinary(n)/longblob` | `bytea` | `BLOB` | |
| Date | `date` | `date` | `date` | `DATE_TEXT` | |
| Time | `time(7)` | `time(6)` | `time` | `TIME_TEXT` | |
| Timestamp | `datetime2(7)` | `datetime(6)` | `timestamp` | `DATETIME_TEXT` | 无时区 |
| TimestampTZ | `datetimeoffset(7)` | `timestamp(6)`（UTC 会话） | `timestamptz` | `TIMESTAMPTZ_TEXT` | MySQL 范围 1970–2038 |
| UUID | `uniqueidentifier` | `char(36)` | `uuid` | `UUID_TEXT` | |
| JSON | `nvarchar(max)` | `json` | `jsonb` | `JSON_TEXT` | SQL Server 需 `ISJSON` 检查 |
| XML | `xml` | —（报不支持） | `xml` | `XML_TEXT` | |

---

## 附录 D：评估文档 P0 → 设计映射

| P0 | 设计落点 | 验证用例（§20.3） |
| :-- | :--- | :--- |
| #1 UPDATE 空 SET | §12.4 只 SET 变化列 + `changed` 为空跳过 | `TestUpdateUsesChangedColumns`、`TestEmptyUpdateNeverEmitted` |
| #2 `System.Byte[]` | §6 类型化值；§11 NDJSON | `TestBinaryRoundTrip` |
| #3 多行 CSV | §11 NDJSON | `TestMultilineAndBackslashN` |
| #4 `\N` | §11 NDJSON | `TestMultilineAndBackslashN` |
| #5 大小写撞键 | §6.5 值比较 + §8 不变式 1（不复用大小写不敏感字符串键） | `TestCaseOnlyPKDifference` |
| #6 跨 schema 撞键 | §8 不变式 1（FullName） | `TestCrossSchemaSameName` |
| #7 字面量无类型 | §13.4 类型化渲染 | `TestTypeLiteralGolden` |
| #8 locale | §6.3 编码 + 显式解析 | `TestLocaleIndependence` |
| #11 环外键丢表 | §13.1 两阶段 + §13.1.1 外键策略 | `TestCycleTwoPhase`、`TestExistingFkHandled` |
| #39 直连 INSERT 写 NULL | §12.4 整行 INSERT（含 identity） | `TestInsertCarriesAllColumns`、`TestIdentityPreserved` |

**计数**：10 条 P0 对应 13 个用例（#3/#4 共用、#1/#11/#39 各两条），全部放进 M0 坏例集，先红后绿。

### D.1 与评估文档的有意偏离

| 评估文档结论 | 本文做法 | 原因 / ADR |
| :--- | :--- | :--- |
| 表级校验和短路（P0） | 只在"两端都由本工具算摘要"时启用 | 数据库侧摘要无法证明"相等⇒逐行相等"，误判会静默丢变更（ADR-007） |
| 跨方言比对 | v2.0 只做结构报告，不做数据同步 | 类型映射非单射，读回类型不可判定（ADR-008） |
| 回滚脚本 | 只为可逆操作生成，不可逆处明确标注 | 原版"假回滚"的反面；不承诺做不到的事 |
| 对象覆盖含 matview/type/extension | v2.0 覆盖 view/procedure/function/trigger/sequence；其余延后 | 控制 M1–M3 范围，接口留好扩展点 |
| "两种模式结果完全一致" | 由同一份 `Plan` 渲染 `plan.sql` 与 `plan.json` | 消灭 GUI/CLI 双路径（评估 #26） |

---

## 附录 E：文档自检记录

> 本版在**没有 Go 工具链**的环境中编写，采用"静态结构检查 + 两份独立评审（逻辑一致性 / Go 技术正确性）+ 逐条修正"的方式自检。以下是可复核的记录。

### E.1 做了哪些检查

| 检查 | 方法 | 结果 |
| :--- | :--- | :--- |
| 结构完整性 | 统计代码围栏（必须偶数）、逐表核对 `\|` 列数、标题层级 | 修复 1 处断裂代码块（§13.5 的 `Phase/Step` 跑到围栏外）；修复后 64 个围栏、全部表格列数一致 |
| 交叉引用 | 提取全部 `§x.y` 与标题编号比对 | 修正 3 处指错（§1 的三问、§2.4 的评估文档编号、§6.5 的属性测试位置） |
| 术语与方向 | 全文检查 source/target、from/to、current/desired 的用法 | 报告字段由 `from/to` 改为 `current/desired`，并写死方向 |
| 数字一致性 | 工期、内存预算、用例计数、退出码逐项对照 | 统一 7–11 周（CLI 暂缓后）；N3 改为 384 MB（含 256 MiB 数据预算）；`--max-changes` 降到 50 万；测试 22 例 |
| 参数一致性 | 提取全部 `--flag`，对照命令表/全局表 | 补全 `--source-env/--target-env/--schema-map/--optimistic/--batch/--with-count` 等；`--fail-on` 取值统一 |
| 逻辑闭环 | 逐条走"读入→比对→计划→执行→再比对" | 发现并修复 7 个阻断级问题（E.2） |
| Go 可行性 | 独立 Go 评审核查片段、驱动 API、age/Wails/xxh3 用法 | 发现并修复 12 处（E.3） |

### E.2 逻辑评审发现并修复的阻断项

| # | 问题（修复前） | 后果 | 修复 |
| :-- | :--- | :--- | :--- |
| L1 | 行哈希按物理列序拼接，但"变化列"按列哈希求差 | 列序不同 → 全列"不同"却无变化列 → 空 SET 复活 | §6.4 行哈希按**列名排序**、只覆盖**共有列**；§12.4.1 定义列集规则；增加空 UPDATE 兜底 |
| L2 | 一边说"INSERT 整行"，测试又说"排除自增列" | 自增主键被重排，外键错位，永不收敛 | §12.4/§13.4 明确写入 identity 原值 + 方言开关；`--exclude-identity` 降级为显式告警 |
| L3 | schema-only 快照当 source 时，目标被当成"源端无行" | **静默删空目标表** | §11.3 增 `dataScope`；§12.5 硬性守卫 `E_NO_DATA_SIDE`；补回归用例 |
| L4 | 阶段 1 摘下依赖视图，阶段 6 只重建"有差异"的对象 | 无差异视图永久消失 | §13.1.1 记录 `droppedForDependency`，阶段 6 **无条件重建** |
| L5 | "auto：失败自动切外排"，但差异可能已写出 | 半份结果污染，无法回退 | §10.4 改为 `db\|external`；断言失败整次中止并提示重跑 |
| L6 | 阶段 3 同时插删，既有外键仍在，单一顺序不存在 | 数据阶段随机失败 | §13.1.1 受影响表的外键/唯一索引阶段 1 删、阶段 5 重建；§13.2 数据表序 |
| L7 | 跨方言示例 + 类型映射非单射 | 预检自相矛盾，示例跑不通 | §2.3/§7.3/ADR-008 明确 v2.0 **不做跨方言数据同步**；示例改同方言 |

### E.3 Go 评审发现并修复的设计级问题

| # | 问题（修复前） | 修复 |
| :-- | :--- | :--- |
| G1 | `Decimal{*big.Int}` 复制共享指针、可被原地改；`Value` 含 `[]byte` 却想用 `==` | §6.2 改为不可变 `Decimal{Neg,Coef string,Exp}`；时间拆成 `Timestamp{Sec,Nano}`/`Day`/`Nanos`；统一 `Equal/Compare`，`[]byte` 必 clone |
| G2 | `UnixNano` 在 1678–2262 外溢出；`KindTime` 装不下 MySQL `-838h` | §6.2/§6.3 改为秒+纳秒、日期用天数、时间用时长纳秒 |
| G3 | `TableDigest([]RowHash)` 要先收全表 | §6.4 改为流式 `Digest.Add` |
| G4 | `Merge` 拿不到表与列哈希，`DeltaSink` 无法实现 UPDATE | §10.4 签名补 `*model.Table`、列哈希、`BeginTable/Flush/Close(ctx)`、`Compare` 返回 error |
| G5 | `RowStream` 放 endpoint/diff 都会造成依赖环 | 新增叶子包 `internal/stream`，双方只依赖它 |
| G6 | endpoint 自定义 scheme 不能直接透传四种驱动 | §9.3 增 `internal/driver` 转换表 + 每驱动 `Ping` 测试 |
| G7 | "加密包必须落明文临时文件" | §11.1 改用 age `DecryptReaderAt`（v1.3+）；只有 stdin 才落盘 |
| G8 | Wails `Apply(req, chan)` 无法生成绑定 | §17 改为返回值 + `app.Event.Emit` + `Cancel(runID)` |
| G9 | `go 1.26.x` 不是合法 go 指令 | §21 改为 `go 1.26.0`，并注明 `go mod init` 默认写 1.25.0 |
| G10 | `formatVersion: 2` 却谈 major/minor | §11.3/附录 A 改为 `{major,minor}` 对象 |
| G11 | N5 说加密快照字节级可复现 | §2.2 限定"明文快照与 plan.json"；加密快照只保证解密后内容一致 |
| G12 | SQLite 多语句 `Exec` 只执行第一条、PRAGMA 每连接 | §9.2/§13.6 逐条执行 + DSN `_pragma` + WAL/busy_timeout |

### E.4 仍需实测（不在文档内解决）

1. SQL Server BIN2 与 Go UTF-8 在**补充平面字符**上的排序一致性（§23.2 第 3 问）。
2. MySQL 8.0.16 的 `utf8mb4_bin` PAD SPACE 行为（尾部空格、排序相等）。
3. N3/N4 的性能数字必须由 `internal/bench` 在真实网络与库上测出，文档只给目标。
4. Wails v3 在目标 Windows 版本 + WebView2 缺失场景的安装体验。
5. age `DecryptReaderAt` 在超大包上的内存与随机读性能。

### E.5 第二轮复核（本轮新增）

在第一轮修复之后又通读了一遍数据通路，发现 18 处**时序/类型/幂等**问题，均已修复：

| # | 修复前 | 后果 | 修复 |
| :-- | :--- | :--- | :--- |
| R1 | 列哈希含列名与类型 | 列名大小写不同或只改长度 → 全表行哈希都变 → 整表 UPDATE | §6.4 改为值哈希 + 规范列序；列名匹配交给结构层 |
| R2 | SQLite 用 `text`/`integer` 承载逻辑类型 | 大整数/高精度小数被亲和性转成 REAL 丢精度；类型读回漂移，二次 diff 不收敛 | §7.3/附录 C 改用 `*_TEXT` 声明名并说明亲和性 |
| R3 | MySQL `TimestampTZ` 映射 `datetime` | 同方言读回变 naive，类型永远不同 | 改为 `timestamp(6)` + 会话 UTC；范围外预检阻断 |
| R4 | target 独有列在阶段 5 才删 | 旧的 NOT NULL 列挡住阶段 4 的 INSERT | 移到阶段 3，装载前删除（破坏性） |
| R5 | 被摘下的视图用 target 旧定义重建 | 引用已删列编译失败，或重建了本该删除的视图 | 按 `existsInSource` + **source 定义**重建；不存在则不建 |
| R6 | 主键不一致时仍按旧主键 UPDATE/DELETE，之后改键 | 旧主键约束下必然冲突 | 默认阻断；`--data=none` 只改结构；`--allow-destructive` 整表替换 |
| R7 | `--resume` 只记录 completed | 中断在语句中间时重复执行，MySQL 已提交 DDL 会二次报错 | 状态改为 `started/completed/failed`，`started` 默认拒绝续跑 |
| R8 | `--optimistic` 不检查 `RowsAffected` | 并发改动被静默跳过 | 检查影响行数，`--on-conflict=abort\|skip` |
| R9 | 只写 identity 原值、不重设种子 | SQL Server 下次自增撞主键 | 阶段 5 `DBCC CHECKIDENT`/`setval` |
| R10 | 计算列/rowversion 参与数据比对 | 值由库生成，永远"有差异"、反复更新 | 排除出比对与 DML；`--include-generated` 才显式包含 |
| R11 | 快照导出无一致性读 | 不同表来自不同时刻 | 整个导出一个只读事务/快照，`--snapshot-window` 控超时 |
| R12 | apply 不校验目标身份 | 计划可能在测试库生成、误对生产执行 | `EndpointRef.Identity`（host:port/db 哈希）+ 执行前比对 |
| R13 | ZIP 条目使用当前时间 | 明文快照无法字节级复现（N5） | 修改时间固定为 1980-01-01 |
| R14 | 默认相信 `varchar` 主键的 BIN2 排序 | 非 Latin1 代码页与 UTF-8 字节序不一致 → 错配 | §10.4 增加代码页限制，预检提示/走外排 |
| R15 | 无主键表与主键变更没有明确数据路径 | 无法生成正确 DML 或永远不收敛 | §12.4 主键不一致策略；无主键走多重集/整表替换 |
| R16 | 独立 sequence 的当前值未处理 | 同步后自增冲突/跳号 | 明确列为"不同步"（§2.3/§23.1），identity 表用 R9 的 reseed |
| R17 | 预检拿 target 现值判断"最终约束" | 会把 source 已修好、尚未 apply 的 NULL/重复误判为阻断；不参与同步的表反而漏检 | §13.3 明确检查对象：同步表看 source，未同步表看 target |
| R18 | 视图依赖边没有来源定义 | 阶段 6 重建顺序错，MySQL 尤其无目录可用 | §13.2 规定目录优先、MySQL 受限解析、失败降级告警 |

### E.6 结论

- **结构、方向、参数、数字、交叉引用**已闭环，可交给实现。
- **代码片段仍未编译**（环境无 Go），M0 第一件事是落成可编译骨架并过 `go vet`；这不是文档缺陷，而是已声明的边界。
- 两轮复核共修复 **7 个阻断项 + 18 个时序/类型/幂等问题 + 12 个 Go 设计问题**；剩余问题都归入"实测"或"开放问题"，没有"知道了但没写"的项。
- **仍然会变的地方**：性能数字、BIN2 补充平面行为、Wails/WebView2 体验——这些只能靠 M1/M2 的真实环境验证，文档已把它们标为开放问题，而不是假装已知。

> 复核方式：`git diff` 查看本文件相对上一版的改动；搜索 `E.2`/`E.3` 的每条修复，均能在正文对应章节找到落点。
