<div align="center">

<img src="docs/screenshots/logo.png" width="120" height="120" alt="DBSyncTool Logo" />

# DBSyncTool

### 数据库结构 / 数据差异比对与同步脚本生成工具

**Database Schema & Data Diff Tool with Sync Script Generation**

[![Release Version](https://img.shields.io/badge/Release-v1.0.0-2563EB.svg?style=flat-square&logo=github)](https://github.com/wangbenchi66/DBSyncTool/releases)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4.svg?style=flat-square&logo=windows)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/UI-Avalonia%2012%20%2B%20SukiUI-8B5CF6.svg?style=flat-square)](https://avaloniaui.net/)
[![License](https://img.shields.io/badge/License-MIT-10B981.svg?style=flat-square)](LICENSE)
[![Co-Authored](https://img.shields.io/badge/Co--Authored%20with-AI%20Agent-6366F1.svg?style=flat-square&logo=openai)](#acknowledgements)

<br/>

**[简体中文](README.md)** • **[English](README.en.md)**

<br/>

[📖 适用场景](#scenario) • [🌟 功能概览](#highlights) • [✨ 界面展示](#features) • [💻 命令行 CLI](#cli) • [🚀 快速开始](#download) • [🛠️ 本地构建](#build) • [📂 目录结构](#structure) • [💡 致谢](#acknowledgements)

</div>

---

## <a id="scenario"></a>📖 适用场景

DBSyncTool 用于比对两个数据库之间的**结构差异**和**数据差异**，并生成可交付运维执行的升级脚本。根据网络环境不同，提供两种工作模式：

### 快照模式 — 网络隔离、无法直连

适用于内网生产库与测试库之间**无法互相访问**的场景，工作流分两步：

1. **目标库侧**（可达目标库的机器）→ 导出基线快照文件（`.dbsync`）；
2. **源库侧**（可达源库的机器）→ 加载快照，逐表比对源库与目标库，产出差异清单与可执行 SQL。

> 🔒 **安全性**：
> - 快照文件支持**密码加密**，未授权无法读取内容；
> - 快照中**不包含数据库连接信息**，即使文件泄露也不会暴露服务器地址与凭据；
> - 全程无需开放数据库端口或建立跨网络通道，**零网络暴露面**。
>
> 🚀 **便捷性**：
> - 导出一个 `.dbsync` 文件即完成基线采集，通过 U 盘、内网共享、邮件等任意渠道传递；
> - 快照采用**流式导出**，百万级行的数据指纹采集不整表读入内存，大库也能顺畅导出；
> - 按表勾选"仅结构 / 结构 + 数据"，灵活控制快照体积与比对范围。

### 直连模式 — 两库可互访

当源库与目标库在同一网络内可以互相访问时，**无需导出快照**，直接选择两个连接即可在线比对，省去文件传递步骤。

> 💡 **通用设计**：
> - **所见即所得**：差异即 SQL，事务包裹、排除自增列等选项在预览中实时反映；
> - **多数据库支持**：SQL Server、MySQL、PostgreSQL、SQLite 统一工作流；
> - 两种模式共享同一套比对引擎与 SQL 生成器，结果完全一致。

---

## <a id="highlights"></a>🌟 功能概览

| 功能方向 | 当前能力 |
| :--- | :--- |
| **结构差异比对** | 列、类型、约束、索引、外键等逐项比对 |
| **数据差异比对** | 基于主键哈希指纹，支持百万级行流式导出 |
| **SQL 脚本生成** | 差异即 SQL，支持事务包裹、排除自增列、含回滚脚本 |
| **按表粒度控制** | 每张表可独立选择"仅结构 / 结构 + 数据" |
| **快照加密** | 密码保护快照文件，导出 / 加载需配对密码 |
| **直连比对** | 两库可互访时无需快照，直接比对 |
| **多数据库** | SQL Server、MySQL、PostgreSQL、SQLite |
| **桌面端 + CLI** | Avalonia 图形界面与 `dbsync` 命令行两种使用方式 |
| **自动更新** | GitHub Actions 按 tag 自动打包，桌面端启动检查新版本 |

---

## <a id="features"></a>✨ 界面展示

### 1. 📋 连接管理

维护多个数据库连接（数据库名支持搜索选择），密码经本机加密存储。

<div align="center">
  <img src="docs/screenshots/connections.png" width="680" alt="连接管理列表" />
  <br/><br/>
  <img src="docs/screenshots/connection-edit.png" width="480" alt="连接编辑窗口" />
</div>

---

### 2. 📦 导出快照（在目标库侧）

选择连接后自动加载该库的表，按表勾选"仅结构 / 结构 + 数据"，设置过滤条件、加密与导出路径，流式导出 `.dbsync` 基线快照。

<div align="center">
  <img src="docs/screenshots/export-snapshot.png" width="680" alt="导出快照页面" />
</div>

---

### 3. 🔍 比对（在源库侧）

- **加载对比**：加载快照文件后与源库比对；
- **直连对比**：源库与目标库可直接访问时，选择两库直接比对。

比对结果按"全部 / 不同 / 源库独有 / 目标库独有 / 数据差异"分类，勾选节点后即可在 SQL 预览区看到对应差异的升级脚本。

<div align="center">
  <img src="docs/screenshots/compare.png" width="680" alt="加载对比页面" />
  <br/><br/>
  <img src="docs/screenshots/direct-compare.png" width="680" alt="直连对比页面" />
</div>

---

### 4. 📜 历史记录与设置

历史记录保存比对过的快照与结果，可一键回填重新比对；设置页可调整导出 / 比对 / 脚本生成的默认行为。

<div align="center">
  <img src="docs/screenshots/history.png" width="680" alt="历史记录页面" />
  <br/><br/>
  <img src="docs/screenshots/settings.png" width="680" alt="设置页面" />
</div>

---

## <a id="cli"></a>💻 命令行 CLI

桌面端之外，`dbsync` 命令行可在无图形界面的服务器上使用：

```bash
# 导出快照
dbsync export --connection "服务器=...;数据库=..." --output snapshot.dbsync

# 比对快照与目标库（结果决定退出码：0 无差异 / 1 有差异 / 2 错误）
dbsync compare --snapshot snapshot.dbsync --connection "服务器=...;数据库=..."

# 生成升级脚本
dbsync script --snapshot snapshot.dbsync --connection "..." --output upgrade.sql

# 直接执行脚本到目标库
dbsync execute --snapshot snapshot.dbsync --connection "..."
```

---

## <a id="download"></a>🚀 快速开始与下载

| 版本包 | 适用场景 | 说明 | 下载入口 |
| :--- | :--- | :--- | :--- |
| **桌面端安装版（推荐）** | Windows 用户 | `DBSyncTool-Setup-<版本>-win-x64.exe`：Inno 安装向导，装到 Program Files、带开始菜单/快捷方式/卸载 | [⬇️ 下载安装包](https://github.com/wangbenchi66/DBSyncTool/releases) |
| **桌面端便携版** | Windows 免安装 | `DBSyncTool-<版本>-win-x64.zip`：自包含多文件（无需 .NET），整包解压后运行 `DBSync.Desktop.exe` | [⬇️ 下载便携版](https://github.com/wangbenchi66/DBSyncTool/releases) |
| **桌面端 fd 体积版** | 已装 .NET 10 Runtime | `DBSyncTool-<版本>-win-x64-fd.zip`：体积最小的便携版（不含 .NET 运行时），整包解压后运行 | [⬇️ 下载 fd 体积版](https://github.com/wangbenchi66/DBSyncTool/releases) |
| **CLI 命令行** | 服务器 / 自动化 | 单文件：`dbsync-<版本>-win-x64.exe` / `-linux-x64` / `-osx-x64`，无需 .NET；Linux/macOS 先 `chmod +x` | [⬇️ 下载 dbsync](https://github.com/wangbenchi66/DBSyncTool/releases) |
| **源码运行** | 开发者 | 需 .NET 10 SDK | 见下方[本地构建](#build) |

### 基础使用流程：

1. 下载**安装包**运行向导安装后启动；或下载**便携版 zip** 解压到可写目录后运行 `DBSync.Desktop.exe`；
2. 在「连接管理」页面添加数据库连接，密码经本机加密存储；
3. 在目标库侧「导出快照」，将 `.dbsync` 文件传递到源库侧；
4. 在源库侧「加载对比」，查看差异清单与 SQL 预览；
5. 按需导出升级脚本或直接执行。

> 💡 若两个库可以互相访问，可跳过快照步骤，直接使用「直连对比」。

---

## <a id="build"></a>🛠️ 本地构建与开发

### 环境要求
- Windows 10 / 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### 编译与运行

```bash
# 1. 克隆代码仓库
git clone https://github.com/wangbenchi66/DBSyncTool.git
cd DBSyncTool

# 2. 编译项目
dotnet build src/DBSyncTool.slnx

# 3. 运行桌面端
dotnet run --project src/DBSync.Desktop

# 4. 运行 CLI
dotnet run --project src/DBSync.CLI -- export --help

# 5. 运行单元测试
dotnet test src/DBSyncTool.slnx
```

### 手动发布

详见 [docs/release.md](docs/release.md)（含自动打包、版本号管理、自动更新机制说明）。

---

## <a id="structure"></a>📂 目录结构

```text
DBSyncTool/
├── .github/workflows/              # GitHub Actions CI/CD
│   └── release.yml                 # tag 触发自动打包与发布
├── src/
│   ├── DBSync.Core/                # 核心领域与算法
│   │   ├── Models/                 # 表、列、索引、外键、快照、差异等模型
│   │   ├── Schema/                 # 各数据库的结构读取器
│   │   ├── Comparers/              # 结构对比、数据对比、外键拓扑排序
│   │   ├── Data/                   # 行哈希与数据指纹
│   │   ├── Snapshot/               # 快照导出、加载、格式定义、加解密
│   │   ├── SqlGenerators/          # 各数据库方言的 SQL 生成器
│   │   ├── Execution/              # 脚本执行器
│   │   └── Versioning/             # 应用版本号解析与比较
│   ├── DBSync.Desktop/             # Avalonia 桌面端（SukiUI 主题）
│   │   ├── Views/                  # 主窗口、仪表盘、导出快照、比对等界面
│   │   ├── ViewModels/             # 页面视图模型
│   │   ├── Services/               # 窗口、加密、导出、更新检查等 UI 服务
│   │   ├── Storage/                # 本地配置与连接持久化
│   │   └── Helpers/                # IME 输入辅助等工具类
│   ├── DBSync.CLI/                 # 命令行入口（export / compare / script / execute）
│   └── DBSync.Tests/               # xUnit 单元测试
├── docs/                           # 设计与发布文档、界面截图
├── Directory.Build.props           # 统一版本号管理
├── LICENSE                         # MIT 许可证
└── README.md                       # 项目说明文档
```

---

## 技术栈

| 层面 | 选型 |
| :--- | :--- |
| 运行时 | .NET 10 |
| 桌面 UI | Avalonia UI 12 + SukiUI 主题 |
| MVVM | CommunityToolkit.Mvvm |
| 数据访问 | SqlSugar（Easy.SqlSugar.Core） |
| 日志 | Serilog（Easy.Serilog.Core） |
| 测试 | xUnit |
| 发布 | GitHub Actions（tag 触发自动打包） |

---

## 参与贡献

1. Fork 本仓库
2. 新建功能分支
3. 提交代码
4. 发起 Pull Request

代码注释与提交信息请使用中文。

---

## <a id="acknowledgements"></a>💡 致谢与维护说明

### 🤖 人机协同开发说明

本项目由开发者主导架构设计、交互逻辑规划与系统调优，AI 智能体协同完成代码构建与测试验证。

### 📌 维护说明

项目持续迭代中，欢迎通过 [GitHub Issue](https://github.com/wangbenchi66/DBSyncTool/issues) 提交 Bug 报告、使用反馈或功能建议。

---

## <a id="license"></a>📄 开源许可证

本项目采用 [MIT License](LICENSE) 开源。
