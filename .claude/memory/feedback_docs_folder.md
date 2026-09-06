---
name: feedback-docs-folder
description: 项目文档统一放在根目录的 docs 文件夹内，不直接放根目录
metadata:
  type: feedback
---

所有项目文档（.md 文件）必须放在项目根目录的 `docs/` 文件夹内。

**Why:** 用户明确要求，文档集中管理，避免根目录杂乱。

**How to apply:** 每次生成或创建文档时，路径使用 `{项目根目录}/docs/{文件名}.md`，不要直接放在根目录。
