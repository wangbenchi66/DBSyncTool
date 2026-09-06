---
name: feedback-xml-comments
description: 每个方法都要加 XML 注释，格式固定，不能缩为单行
metadata:
  type: feedback
---

每个方法必须加 XML doc 注释，格式要求：

```csharp
/// <summary>
/// 方法描述，不需要很详细但要说清楚做什么
/// </summary>
/// <param name="req">参数含义</param>
/// <returns></returns>
```

字段注释格式：

```csharp
/// <summary>
/// 字段描述
///</summary>
public string FieldName { get; set; }
```

**Why:** 用户明确要求，所有方法和字段都需要注释，描述清楚用途即可。

**How to apply:**
- 写新方法或改造现有方法时无论多简单都要加完整格式的 XML 注释
- summary / param / returns 各自独占行，不能缩为单行（如 `/// <summary>xxx</summary>` 是错误格式）
- 字段注释的关闭标签是 `///</summary>`（无空格），不是 `/// </summary>`
