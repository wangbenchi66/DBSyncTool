# 07: 数据比较器（仅 INSERT 逻辑）

**Status:** ready-for-agent

**Blocked by:** 01 - 解决方案脚手架 + 核心领域模型

**What to build:** 实现纯函数 `DataComparer`，对比基线指纹集和当前指纹集，产出只包含新增行的 `DataDiff`。无需数据库连接，全部可单元测试。

- [ ] 实现 `DataComparer.Compare(IEnumerable<RowHash> baseline, IEnumerable<RowHash> source) → DataDiff`
- [ ] 识别新增行（source 有、baseline 无）→ 放入 `DataDiff.RowsToInsert`
- [ ] 识别删除行（baseline 有、source 无）→ 放入 `DataDiff.DeletedRows`（仅报告，不生成 SQL）
- [ ] 识别更新行（主键相同，哈希不同）→ 放入 `DataDiff.ChangedRows`（仅报告，不生成 SQL）
- [ ] 无主键表：接受 `NoPrimaryKey` 标记，返回空 `DataDiff` 并设置 `Skipped = true`
- [ ] 全部单元测试，使用 Easy.Bogus.Core 生成 `RowHash` 测试集
- [ ] 测试覆盖：完全相同（无差异）、全部新增、部分更新、混合场景、空集合边界
