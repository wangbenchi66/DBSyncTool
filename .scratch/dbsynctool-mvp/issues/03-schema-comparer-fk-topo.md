# 03: Schema 比较器 + 外键拓扑排序

**Status:** ready-for-agent

**Blocked by:** 01 - 解决方案脚手架 + 核心领域模型

**What to build:** 实现纯函数 `SchemaComparer`，对比两组表结构产生差异结果，并实现外键依赖图的拓扑排序。无需数据库连接，全部可单元测试。

- [ ] 实现 `SchemaComparer.Compare(IEnumerable<TableModel> baseline, IEnumerable<TableModel> source) → SchemaDiff`
- [ ] 检测新增表（基线有，源库无）
- [ ] 检测删除表（基线无，源库有），标记为需警告，默认排除在 DDL 输出之外
- [ ] 检测表结构变更：列新增、列删除、列类型/长度/精度修改、可空性变更、默认值变更
- [ ] 检测约束变更：主键列变更、唯一约束新增/删除
- [ ] 检测索引变更：新增索引、删除索引、索引列或唯一性变更
- [ ] 实现 `FkTopologicalSorter`：接受 `TableModel` 列表，返回按外键依赖排序的列表（父表优先）
- [ ] 循环依赖检测：发现环时将该组表从正常序列中分离，标记为 `CyclicDependencyWarning`
- [ ] DROP TABLE 顺序为 CREATE 顺序的逆序
- [ ] 全部单元测试，使用 Easy.Bogus.Core 构造 `TableModel` 测试数据
- [ ] 测试用例覆盖：无依赖的表集合、线性依赖链、菱形依赖、循环依赖
