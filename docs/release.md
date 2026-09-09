# 发布与自动更新说明

## 触发一次发布

1. 打标签并推送。推荐在 **Gitee** 侧操作（本仓库为 Gitee 源仓库，GitHub 为其镜像）：

   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

   - 前提：Gitee 仓库的“推送镜像 / 仓库镜像”已配置同步到 GitHub `wangbenchi66/DBSyncTool`，且**包含 tags**。
   - 若镜像未同步 tags，也可把 GitHub 加为远程后直接推标签：

     ```bash
     git remote add github https://github.com/wangbenchi66/DBSyncTool.git
     git push github v1.0.0
     ```

2. GitHub 收到 `v*` 标签后，`.github/workflows/release.yml` 自动构建并发布以下产物到 **GitHub Releases** 页（变更说明由 git 提交自动生成）：

   **桌面端（Windows x64）**
   - `DBSyncTool-<版本>-win-x64.exe` —— 绿色便携**单文件**，双击直接运行，免安装、无需 .NET；Skia/SNI/SQLite/ANGLE 等原生库已内嵌 exe，首次启动自解压到系统临时目录运行，属正常现象
   - `DBSyncTool-<版本>-win-x64-fd.zip` —— **fd 体积版**便携整目录，体积最小（不含 .NET 运行时），需目标机已装 **.NET 10 Runtime**；整包解压后运行（内为 exe + dll + 原生库）
   - `DBSyncTool-Setup-<版本>-win-x64.exe` —— Inno Setup **安装向导**，装到 `Program Files\DBSyncTool`，带开始菜单/可选桌面快捷方式与卸载入口（需管理员权限）

   **CLI（x64 单文件，三平台）**
   - `dbsync-<版本>-win-x64.exe` / `dbsync-<版本>-linux-x64` / `dbsync-<版本>-osx-x64`

   > 自 v1.1 起**不再发布**旧形态的“整目录 zip”；程序数据（设置 / 连接 / 日志）统一存放于用户目录 `%APPDATA%\DBSyncTool`，删除/卸载后不留残留在程序目录。

3. 重复推送同一个标签不会覆盖，GitHub 会报错；需先删除已存在的同名 Release 与标签再重试。

## 产物下载与使用说明

- **普通用户（Windows）**：下载 `DBSyncTool-Setup-<版本>-win-x64.exe` 安装，之后从开始菜单/桌面快捷方式启动。
- **免安装 / 携带版**：下载 `DBSyncTool-<版本>-win-x64.exe` 放到任意可写目录直接双击运行。
- **已装 .NET 10 Runtime / 体积优先**：下载 `DBSyncTool-<版本>-win-x64-fd.zip`，整包解压后运行（体积最小，带 dll 与原生库）。
- **Linux / macOS CLI**：`dbsync-<版本>-linux-x64` 等文件下载后先 `chmod +x` 再执行；macOS 因产物未做 ad-hoc 签名，首次运行需「右键 → 打开」或执行 `xattr -dr com.apple.quarantine <文件>`。

### 本地手动试编译 Inno 安装包

```bash
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" build\setup.iss ^
  /DMyAppVersion=<版本> ^
  /DDesktopDir=<publish\desktop-install 绝对路径> ^
  /DIconPath=<src\DBSync.Desktop\Assets\app-icon.ico 绝对路径>
```

## 手动补跑（可选）

Actions 页对该 workflow 使用 **Run workflow** 时，把下拉里的 ref 选到某个 `v*` 标签再运行，即可手动重发。若选到分支（无标签），各 job 因版本号条件不满足会直接跳过。

## 桌面端自动检查更新

- 应用启动后静默请求 `https://api.github.com/repos/wangbenchi66/DBSyncTool/releases/latest`；
- 发现比当前版本更高的**正式** Release 时弹出“发现新版本”窗口，可“前往下载”（系统浏览器打开 Release 页）或“稍后”（该版本不再提醒，直到出现更高版本）；
- 网络不可达、无 Release、请求失败时**静默忽略**，不做任何提示。

### 前提

- GitHub 仓库 `wangbenchi66/DBSyncTool` 需为**公开**仓库（桌面端不带令牌访问 Releases API）；
- 用户网络需能访问 `api.github.com`。

## 版本号管理

- 单一版本源位于仓库根目录 `Directory.Build.props` 的 `<Version>`（当前 1.0.0）；
- 程序集版本、界面侧栏/关于、CLI 版本显示均统一读取该值；
- 发布时 workflow 用标签版本覆盖（`-p:Version=<标签去 v>`），因此发布产物程序集版本与 tag 一致。

## 换仓库地址时需同步修改

`src/DBSync.Desktop/Services/UpdateChecker.cs` 中的 `ReleaseApiUrl` 常量。
