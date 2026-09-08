# 内存占用和打包体积优化方案

## 现状分析

- 发布产物总大小：**257 MB**
  - DBSync.Desktop.exe：137 MB（单文件包，未压缩）
  - libSkiaSharp.pdb：81 MB（调试符号，不应发布）
  - libHarfBuzzSharp.pdb：20 MB（调试符号，不应发布）
  - 原生 DLL（Skia/HarfBuzz/SQLite/ANGLE 等）：~21 MB
- 解析后依赖库：134 个（Desktop），83 个（Core）
- 大量未使用的依赖被打包：Redis 客户端、Autofac、Castle.Core、ASP.NET MVC、Oracle/DM/KingbaseES 数据库驱动

## 优化方案

### 第一优先级：发布配置（零代码改动）

修改 `src/DBSync.Desktop/Properties/PublishProfiles/FolderProfile.pubxml`：

| 设置 | 值 | 效果 |
|------|-----|------|
| `PublishTrimmed` | true | 裁剪未使用的 IL 代码 |
| `TrimMode` | partial | 只裁剪标记为可裁剪的程序集，降低反射兼容风险 |
| `EnableCompressionInSingleFile` | true | 压缩单文件包（30-40% 压缩率） |
| `DebugType` | none | 不生成 .pdb 文件，消除 101 MB 调试符号 |
| `PublishReadyToRun` | true | 预编译热路径，加快冷启动 |
| `InvariantGlobalization` | true | 不加载 ICU 全球化数据（~30 MB） |

**预计发布体积从 257 MB 降到 60-80 MB。**

注意事项：
- `PublishTrimmed` 可能裁掉 SqlSugarCore/Avalonia 的反射调用，使用 `TrimMode=partial` 降低风险
- 如果裁剪后运行出错，需要在 csproj 中添加 `<TrimmerRootAssembly>` 保留特定程序集
- `InvariantGlobalization` 会导致文化敏感的字符串排序行为变化，数据库工具场景影响不大

### 第二优先级：运行时内存优化（少量改动）

**a) GC 配置优化**

在 Desktop.csproj 中：
```xml
<ServerGarbageCollection>false</ServerGarbageCollection>
```
桌面应用使用工作站 GC 即可，服务器 GC 为高吞吐设计，内存占用更高。

**b) 移除 Easy.Cache.Core 依赖**

`Easy.Cache.Core` 在代码中完全未使用，但它拉入了以下重依赖：
- CSRedisCore（Redis 客户端）
- Castle.Core（动态代理）
- WBC66.Autofac.Core（Autofac IoC 容器）
- Microsoft.AspNetCore.Mvc.Abstractions（ASP.NET MVC）

移除后可减少约 15-20 个传递依赖，降低内存和包大小。

**c) ViewModel 懒加载（可选，改动较大）**

当前所有页面 ViewModel 注册为 Singleton，应用启动时全部实例化。可改为按需创建，未访问的页面不占内存。涉及文件：`ServiceCollectionExtensions.cs`、`MainWindowViewModel.cs`。

### 第三优先级：深度依赖瘦身（改动大）

- `Easy.SqlSugar.Core` → SqlSugarCore 捆绑了 Oracle、DM、KingbaseES 等用不到的驱动，无法单独移除
- `Easy.Serilog.Core` → 可能拉入不必要的 Sink，可直接引用 Serilog + 需要的 Sink
- `Easy.Common.Core` → 拉入 Mapster（对象映射），如果只用了少量工具方法可考虑替换

### 不建议做的

| 方案 | 原因 |
|------|------|
| NativeAOT | Avalonia 12 + SukiUI 对 AOT 支持有限 |
| 重新启用 ListBox 虚拟化 | 已确认会导致 SukiUI 渐入动画问题 |
| 移除 SqlSugarCore | SQL Server 的 SchemaReader 依赖它，替换成本高 |

## 验证方法

```bash
# 发布
dotnet publish src/DBSync.Desktop -c Release

# 检查体积
ls -lh src/DBSync.Desktop/bin/Release/net10.0/publish/win-x64/

# 运行后检查内存
# 任务管理器 → 详细信息 → DBSync.Desktop.exe → 内存（专用工作集）
```

## 执行顺序建议

1. 移除 Easy.Cache.Core（零风险）
2. 更新发布配置（FolderProfile.pubxml）
3. 在 csproj 中添加 GC 和裁剪设置
4. 发布验证
5. 如有裁剪问题，逐步添加 TrimmerRootAssembly
