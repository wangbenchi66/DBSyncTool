; DBSyncTool 桌面端 Windows 安装脚本（Inno Setup 6）
;
; 由 CI 注入以下编译常量：
;   /DMyAppVersion=1.0.0                  （发布版本号，取 tag 去 v）
;   /DDesktopDir=<desktop-install 绝对路径> （整目录发布，含旁置原生库）
;   /DIconPath=<app-icon.ico 绝对路径>
;
; 本地手动试编译示例（在仓库根目录执行，DesktopDir/IconPath 按实际路径调整）：
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" build\setup.iss ^
;       /DMyAppVersion=1.0.0 ^
;       /DDesktopDir=E:\Code\个人项目\DBSyncTool\publish\desktop-install ^
;       /DIconPath=E:\Code\个人项目\DBSyncTool\src\DBSync.Desktop\Assets\app-icon.ico

; 未注入时使用兜底默认值，保证脱离 CI 也能直接编译
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef DesktopDir
  #define DesktopDir "..\publish\desktop"
#endif
#ifndef IconPath
  #define IconPath "..\src\DBSync.Desktop\Assets\app-icon.ico"
#endif

; Windows 文件版本号需要 4 段（major.minor.build.revision），版本号按 X.Y.Z 在 VersionInfo* 处直接尾补 .0
;（注意：Inno 预处理器不会对 #define 内嵌套的 {#...} 递归求值，故不能使用间接宏拼 4 段版本）

[Setup]
AppId={{7E6F2B8A-3D4C-4A51-9D0E-5B1C8F2A6E77}
AppName=DBSyncTool
AppVerName=DBSyncTool {#MyAppVersion}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName=DBSyncTool
VersionInfoProductVersion={#MyAppVersion}.0
AppPublisher=DBSyncTool
AppComments=数据库同步工作台
DefaultDirName={autopf}\DBSyncTool
DefaultGroupName=DBSyncTool
UninstallDisplayIcon={app}\DBSync.Desktop.exe
UninstallDisplayName=DBSyncTool {#MyAppVersion}
; 仅 x64 架构（兼容 Inno 6.0+；6.3+ 的 x64compatible 在此更宽松，但为兼容老版本编译器统一用 x64）
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
SetupIconFile={#IconPath}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
OutputDir=..
OutputBaseFilename=DBSyncTool-Setup-{#MyAppVersion}-win-x64
DisableProgramGroupPage=no

[Languages]
; 简体中文为主语言（列在最前即默认），语言文件已随仓库提交，不依赖 Inno 安装目录自带
Name: "chinesesimp"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Files]
; 安装整目录发布内容（含桌面 exe 与旁置原生库），保留 runtimes\ 等子目录结构
Source: "{#DesktopDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\DBSyncTool"; Filename: "{app}\DBSync.Desktop.exe"; WorkingDir: "{app}"
Name: "{group}\卸载 DBSyncTool"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DBSyncTool"; Filename: "{app}\DBSync.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\DBSync.Desktop.exe"; Description: "启动 数据库同步工作台"; Flags: nowait postinstall skipifsilent
