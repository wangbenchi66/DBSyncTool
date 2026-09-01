# Issue 追踪器：本地 Markdown

本仓库的 Issue 和规格说明以 Markdown 文件形式存放在 `.scratch/` 目录下。

## 约定

- 每个功能一个目录：`.scratch/<功能名>/`
- 规格说明文件：`.scratch/<功能名>/spec.md`
- 实现类 Issue 每条一个文件：`.scratch/<功能名>/issues/<NN>-<slug>.md`，从 `01` 开始编号，禁止合并为单一 tickets 文件
- 每个 Issue 文件顶部用 `Status:` 行记录处理状态
- 评论和讨论历史追加到文件底部的 `## 评论` 标题下

## 当技能说"发布到 Issue 追踪器"时

在 `.scratch/<功能名>/` 下创建新文件（目录不存在则一并创建）。

## 当技能说"获取相关 Ticket"时

读取对应路径的文件。用户通常会直接传入路径或 Issue 编号。

## 路径寻址操作

供 `/wayfinder` 使用。**地图**是一个包含所有子任务文件指针的文件。

- **地图**：`.scratch/<任务>/map.md`（包含备注 / 已有决策 / 待探索区域）
- **子任务**：`.scratch/<任务>/issues/NN-<slug>.md`，从 `01` 编号，正文包含问题描述。`Type:` 行记录类型（`research`/`prototype`/`grilling`/`task`）；`Status:` 行记录 `claimed`/`resolved`
- **阻塞**：文件顶部的 `Blocked by: NN, NN` 行。列出的所有文件均为 `resolved` 时，该任务解除阻塞
- **前沿**：扫描 `.scratch/<任务>/issues/` 找出状态为 open、未阻塞、未认领的文件，编号最小的优先
- **认领**：开始工作前将 `Status:` 设为 `claimed` 并保存
- **完成**：在 `## 答案` 标题下追加答案，将 `Status:` 设为 `resolved`，并在 `map.md` 的已有决策区追加上下文指针
