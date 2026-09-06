---
name: feedback-preserve-comments
description: 不要删除用户代码中的注释，包括方法体内的说明注释
metadata:
  type: feedback
---

保留代码中所有已有注释，不得删除。

**Why:** 用户明确要求，注释是他们代码的一部分，修改文件时必须原样保留。

**How to apply:** 任何时候编辑或重写 .js / .vue / .cs 等任何代码文件，必须保留原有的注释内容（包括方法体内的说明注释、行内注释、块注释等）。如果是重写整个文件，先读取原文件，把注释复制到新文件中对应位置。
