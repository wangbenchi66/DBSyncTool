# 01: 解决方案脚手架 + 核心领域模型

**Status:** ready-for-agent

**Blocked by:** 无（可立即开始）

**What to build:** 建立整个解决方案的基础结构和所有核心数据模型。完成后，后续所有票都可以在此基础上开工，无需等待任何运行时逻辑。

- [ ] 创建 .NET 8 解决方案，包含三个项目：`DBSync.Core`（类库）、`DBSync.Desktop`（Avalonia UI 应用）、`DBSync.Tests`（xUnit 测试项目）
- [ ] 在 `DBSync.Core` 中添加 Easy.SqlSugar.Core、Easy.Cache.Core、Easy.Serilog.Core、Easy.Bogus.Core、Microsoft.Extensions.DependencyInjection NuGet 包引用
- [ ] 定义核心不可变数据模型：`TableModel`（含列、索引、主键、外键列表）、`ColumnModel`、`IndexModel`、`ForeignKeyModel`、`RowHash`（主键值 + 哈希字符串）
- [ ] 定义差异模型：`SchemaDiff`（新增表、删除表、变更表列表）、`TableDiff`（新增列、删除列、修改列、索引变更）、`DataDiff`（INSERT 候选行列表、仅报告的删除/变更行列表）
- [ ] 定义快照模型：`Snapshot`（manifest 元数据 + 表结构字典 + 数据指纹字典）、`SnapshotManifest`（version、dbType、exportedAt、tables、passwordHint?）
- [ ] 定义核心接口：`ISchemaReader`、`ISqlGenerator`、`ISnapshotExporter`、`ISnapshotLoader`
- [ ] 配置依赖注入容器入口（`ServiceCollectionExtensions`）
- [ ] `DBSync.Tests` 项目引用 `DBSync.Core`，添加 Easy.Bogus.Core 用于生成测试数据，能成功编译即可
