---
name: feedback-coding-conventions
description: 项目命名、缩进格式、注释、不删除代码等全套编码规范
metadata:
  type: feedback
---

## 命名规范

### 类名后缀职责

| 后缀 | 职责 |
|------|------|
| `XxxRepository` / `IXxxRepository` | 数据仓储 |
| `XxxService` / `IXxxService` | 业务服务 |
| `XxxApis` | 动态 API 端点（自动去掉 Apis 后缀映射路由） |
| `XxxSetup` | DI / 中间件注册扩展 |
| `XxxManager` | 跨类共享状态管理（如 Token） |
| `XxxFactory` | 创建 / 持有对象实例 |

接口前缀 `I`，实现类与接口同名去掉 `I`。

### 缓存 Key

使用冒号分隔层级：

```
Wx:Token:OfficialAccount
Wx:Login:QrCode:{ticket}
```

---

## 缩进与格式

- 基础：**4 空格缩进**，Visual Studio 默认 **Allman 风格**
- 字段赋值：**不做手动等号对齐**，保持正常单空格

```csharp
// ✅ 正确
_factory = factory;
_tokenManager = tokenManager;
_cacheService = cacheService;

// ❌ 错误：不要手动对齐
_factory      = factory;
_tokenManager = tokenManager;
_cacheService = cacheService;
```

### 多参数链式调用换行规则

- 第一个参数跟在 `(` 后同行
- Lambda / 复杂参数内容另起一行，相对括号内缩进 4 空格
- 末尾简单参数（如超时秒数）另起一行，与内层参数平齐
- 收口 `);` 与末尾参数同缩进

```csharp
// ✅ 正确示例
return _cacheService.GetOrAddAsync(WeChatCacheKeys.WebCode(code), () =>
    _factory.OfficialAccount.ExecuteSnsOAuth2AccessTokenAsync(
        new SnsOAuth2AccessTokenRequest
        {
            Code      = code,
            GrantType = "authorization_code"
        }),
    7200);
```

### 对象初始化器

`{` 另起一行，属性各占一行，`}` 与 `new` 对齐：

```csharp
var req = new FooRequest
{
    Name  = "bar",
    Value = 42
};
```

### 不压行

方法体超过一个表达式时必须换行，不强行写成一行。链式调用按逻辑断行。

---

## 注释规范

所有 `public` / `private` 方法必须写完整 XML 注释：

```csharp
/// <summary>
/// 通过 code 换取网页授权 access_token
/// </summary>
/// <param name="code">微信回调返回的 code，有效期 5 分钟且只能使用一次</param>
/// <returns>包含 access_token、openId 等信息的响应对象</returns>
public Task<SnsOAuth2AccessTokenResponse> GetWebAccessTokenAsync(string code)
```

字段 / 属性注释格式（关闭标签 `///</summary>` 无空格）：

```csharp
/// <summary>
/// 恒生交易账号
///</summary>
public string TradeAcco { get; set; }
```

**Why:** summary / param / returns 各自独占行，不能缩为单行（`/// <summary>xxx</summary>` 是错误格式）。

---

## 不删除原则

不删除未使用的方法和注释，除非：
1. 用户明确说明要进行代码清理或优化
2. 且得到用户确认具体要删哪些

**How to apply:** 重构或修改时只动需要改的部分，保留原有注释和未调用的方法。
