; 米家三合一切换器 安装包脚本 (Inno Setup 6，中文向导)
; 一键构建：powershell -ExecutionPolicy Bypass -File build.ps1
; 也可手动：先 dotnet publish（见 build.ps1），把 exe 放入 publish\ 后运行 ISCC installer.iss

#define MyAppName "米家三合一切换器"
#define MyAppVersion "1.0.0"
#define MyAppExeName "米家三合一切换器.exe"

[Setup]
AppId={{8E7A21C4-5F2B-4C6E-9A3D-1B0C7F5E4D21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
; 默认安装位置仅作为建议，向导中可自由修改
DefaultDirName=E:\米家游戏\切换器\米家三合一切换器
UsePreviousAppDir=yes
AppendDefaultDirName=no
DirExistsWarning=no
DisableDirPage=no
DisableProgramGroupPage=yes
OutputDir=安装包
OutputBaseFilename=MiSwitcher-Setup-{#MyAppVersion}
SetupIconFile=src\MiSwitcher\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ShowLanguageDialog=no

[Languages]
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Dirs]
; 安装后创建空的资源替换包文件夹，用户把对应游戏的替换包内容放入即可（更换时直接覆盖文件夹内容）
Name: "{app}\资源替换包\原神"
Name: "{app}\资源替换包\星穹铁道"
Name: "{app}\资源替换包\绝区零"

[Files]
Source: "publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
; 安装包仅内置芙芙背景，其余背景图可自行放入 {app}\背景图
Source: "assets\背景图\芙芙·当前背景.png"; DestDir: "{app}\背景图"; Flags: ignoreversion
; B服组件：PCGameSDK.dll 三游戏共用同一份，分别复制到三个游戏目录；登录框按游戏各自内置
Source: "assets\替换包内置\*"; DestDir: "{app}\资源替换包"; Excludes: "PCGameSDK.dll"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "assets\替换包内置\PCGameSDK.dll"; DestDir: "{app}\资源替换包\原神"; Flags: ignoreversion
Source: "assets\替换包内置\PCGameSDK.dll"; DestDir: "{app}\资源替换包\星穹铁道"; Flags: ignoreversion
Source: "assets\替换包内置\PCGameSDK.dll"; DestDir: "{app}\资源替换包\绝区零"; Flags: ignoreversion
; 替换包生成脚本（默认自动定位本安装目录的替换包位置）
Source: "tools\gen_pack.py"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
