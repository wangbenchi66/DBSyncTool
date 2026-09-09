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

2. GitHub 收到 `v*` 标签后，`.github/workflows/release.yml` 自动运行：
   - 打包 `DBSync.Desktop-<版本>-win-x64.zip`（桌面版，win-x64 自包含单文件）
   - 打包 `dbsync-<版本>-win-x64.zip`（CLI，win-x64 自包含单文件）
   - zip 内含 exe 与旁置的原生库（Skia/SNI/SQLite/ANGLE 等），需**整包解压后**运行 exe
   - 上传到 **GitHub Releases** 页（变更说明由 git 提交自动生成）

3. 重复推送同一个标签不会覆盖，GitHub 会报错；需先删除已存在的同名 Release 与标签再重试。

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
