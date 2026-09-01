# 07: 数据比较器（仅 INSERT 逻辑）

**Status:** resolved

**Blocked by:** 01 - 解决方案脚手架 + 核心领域模型

**What to build:** 实现纯函数 `DataComparer`，对比基线指纹集和当前指纹集，产出只包含新增行的 `DataDiff`。无需数据库连接，全部可单元测试。

- [x] 实现 `DataComparer.Compare(IEnumerable<RowHash> baseline, IEnumerable<RowHash> source) → DataDiff`
- [x] 识别新增行（source 有、baseline 无）→ 放入 `DataDiff.RowsToInsert`
- [x] 识别删除行（baseline 有、source 无）→ 放入 `DataDiff.DeletedRows`（仅报告，不生成 SQL）
- [x] 识别更新行（主键相同，哈希不同）→ 放入 `DataDiff.ChangedRows`（仅报告，不生成 SQL）
- [x] 无主键表：接受 `NoPrimaryKey` 标记，返回空 `DataDiff` 并设置 `Skipped = true`
- [x] 全部单元测试，使用 Easy.Bogus.Core 生成 `RowHash` 测试集
- [x] 测试覆盖：完全相同（无差异）、全部新增、部分更新、混合场景、空集合边界

## 答案

已实现纯函数 `DataComparer`：

- 以 `RowHash.PrimaryKeyString` 作为行唯一键，对比基线与源库指纹。
- `RowsToInsert` 返回源库有、基线没有的行。
- `DeletedRows` 返回基线有、源库没有的行，仅用于报告。
- `ChangedRows` 返回主键相同但哈希不同的源库行，仅用于报告。
- `noPrimaryKey: true` 时返回 `DataDiff.NoPrimaryKey`。

验证结果：

- `dotnet test 'src\DBSyncTool.slnx' --no-restore --filter FullyQualifiedName~DataComparerTests`：通过，6 通过。
- `dotnet build 'src\DBSyncTool.slnx' --no-restore`：通过，保留既有 `SQLitePCLRaw.lib.e_sqlite3 2.1.10` 高危漏洞警告。
