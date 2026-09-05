# CLAUDE.md

本文件用于指导在本仓库中工作的自动化代理。

## 项目概览

`DBSyncTool` 是一个基于 `.NET 10` 的数据库同步工具，核心目标是：

1. 从目标库导出基线快照
2. 在源库中加载快照并生成结构或数据差异
3. 输出可执行的升级脚本

当前仓库包含三块主要内容：

- `src/DBSync.Core`：核心领域与算法
- `src/DBSync.Desktop`：Avalonia 桌面端
- `src/DBSync.CLI`：命令行入口
- `src/DBSync.Tests`：单元测试

## 技术栈

- .NET 10
- Avalonia UI 12
- CommunityToolkit.MVVM
- Easy.SqlSugar.Core
- Easy.Cache.Core
- Easy.Serilog.Core
- Easy.Bogus.Core
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
  - `Extensions`：核心依赖注入扩展

- `src/DBSync.Desktop`
  - `Views`：主窗口、仪表盘、连接管理、同步工作台、历史记录、设置等界面
  - `ViewModels`：页面视图模型
  - `Services`：窗口、加密、导出等 UI 服务
  - `Storage`：本地配置与连接持久化

- `src/DBSync.CLI`
  - 支持 `export`、`compare`、`script`、`execute`

## 当前界面状态

- 主界面采用侧边导航
- 已有仪表盘、连接管理、同步工作台、历史记录、设置页
- 连接编辑窗的密码输入当前为临时隐藏状态

## 关键约束

1. 核心层不要引入 Windows 专有 API
2. 导出快照必须流式处理，避免整表读入内存
3. 快照文件中的密码提示只用于提示，不参与加解密
4. 结构对比要遵守基线表在前、目标表在后的方向
5. 不要删除用户未要求清理的方法和注释
6. 保持改动尽量小，优先复用现有模式

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

