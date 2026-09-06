# 米家三合一切换器

原神 / 星穹铁道 / 绝区零 —— 三服（米哈游官服 · B服 · 国际服）一键切换器，含全局皮肤系统（.NET 9 / WPF）。

## 下载

从 [Releases](https://github.com/Furina1027/MiSwitcher/releases/latest) 下载最新安装包（含中文向导、默认背景图「芙芙·当前背景」与三游戏 B服组件）。安装时默认装入 `米家游戏\切换器\米家三合一切换器`，可自行修改；启动时的 UAC 提权弹窗属正常行为。安装后见下方「资源替换包」生成替换包即可使用。

## 功能

- **三游戏统一管理**：左侧栏切换 原神 / 星穹铁道 / 绝区零，各自主题色（金 / 紫 / 青）
- **三服切换**：官服 ↔ B服 ↔ 国际服，自动完成 文件替换 → 配置改写 → 校验
- **自动检测**：读取游戏 `config.ini` 的 `channel / sub_channel / cps` 判定当前服务器，同时显示游戏版本与替换包版本
- **B 服组件自动处理**：切到 B 服自动写入 `PCGameSDK.dll` 并复制 `BLPlatform64` 登录框；切离自动删除
- **采集 Persistent**：进入某服务器等热更下载完成后，一键把 `Persistent` 文件夹采集进对应镜像，跨区切换不再重新下载热更；引擎对 Persistent 零删除（镜像有什么就覆盖什么）
- **全局皮肤**：背景支持 PNG/JPG/GIF 动图（`背景图` 文件夹），默认按游戏主题色渐变；皮肤弹窗内可调「背景不透明度」
- **路径设置**：游戏目录与资源替换包目录可视配置，首次启动自动从旧切换器的 `konfig.ini / xonfig.ini` 继承游戏路径
- **默认管理员权限启动**：切换需要移动/删除游戏文件与结束进程，清单已声明 `requireAdministrator`

## 目录约定

```
<切换器安装目录>\
├── 米家三合一切换器.exe
├── config.json                ← 首次运行自动生成（游戏路径/替换包路径/皮肤偏好）
├── 背景图\                    ← 背景图库（放入任意 图片/GIF 即出现在皮肤选择器）
├── tools\gen_pack.py          ← 替换包生成脚本（chunk 下载，用法见下）
├── 资源替换包\
│   ├── 原神\                  ← 官服/国际服镜像 + B服组件 + Persistent 采集
│   ├── 星穹铁道\
│   └── 绝区零\
└── （各游戏的替换包路径可在「游戏路径设置」中自定义）
```

> 替换包版本必须与游戏版本一致（主界面会显示两者版本）。游戏更新后用脚本重新生成即可。

## 资源替换包

每个游戏的子文件夹里存放：

- **官服镜像 / 国际服镜像**：两服内容不同的文件（二进制/元数据/区域资源），切换时由引擎互相覆盖
- **B服组件**：`PCGameSDK.dll`（三游戏共用同一份，安装包内置）与 `BLPlatform64` 登录框
- **Persistent**（可选）：由「采集 Persistent」按钮或手动放入，跨区切换时随镜像生效

游戏更新后用脚本重新生成镜像（CN + 国际服，约 5~6 GB 下载）：

```powershell
python tools\gen_pack.py
# 常用参数：--games hk4e,hkrpg,nap / --side cn|intl|both / --dry-run / --out <目录>
#           --cn-snapshot 配合 --pkg-repo 可用本地历史快照免联网凭据
```

脚本内置米哈游 Sophon chunk 下载的独立实现（仅需 `pip install zstandard`），只下载两服内容不同的
文件；`StreamingAssets\Blocks\` 下的内容块不收录（客户端会按版本标记自行修复），Persistent 与
B服组件原样保留。输出位置自动定位切换器安装目录，也可用 `--out` 指定。

## 切换机制

1. 读 `config.ini` 的 `cps` 判定当前服
2. （仅原神）把数据文件夹改名：`YuanShen_Data ↔ GenshinImpact_Data`
3. 删除其他服残留（如 B 服 SDK DLL、登录框、原神国际服多余的 `hdiffz.dll`；Persistent 一律保留）
4. 从镜像覆盖游戏目录（官服 ↔ B 服二进制相同，跳过文件复制）
5. 目标服为 B 服时：写入 `PCGameSDK.dll` + 复制 `BLPlatform64` 登录框到游戏 Plugins 目录
6. 改写 `config.ini` 的 `channel / sub_channel / cps`：

| 游戏 | 官服 | B服 | 国际服 |
|---|---|---|---|
| 原神 | 1 / 1 / `mihoyo` | 14 / 0 / `bilibili` | 1 / 0 / `hoyoverse_PC` |
| 星穹铁道 | 1 / 1 / `gw_PC` | 14 / 0 / `bilibili_PC` | 1 / 1 / `hoyoverse_PC` |
| 绝区零 | 1 / 1 / `zzz_mktbackup2_pc` | 14 / 0 / `zzz_bilibili_pc` | 1 / 0 / `zzz_oversea_gw_pc` |

7. 复检 `cps` 确认切换成功

## 常见问题

- **提示文件被占用 / 改名失败**：彻底关闭游戏与杀软扫描后再试（星铁/绝区零对文件占用敏感）
- **同时出现两个 Data 文件夹**（原神）：上次切换中断所致，手动删除容量较小的残留文件夹（通常 <2GB）后重试
- **切换后进游戏报「游戏数据异常 31-4302」**：游戏目录多了/少了文件，检查镜像与游戏版本是否一致
- **跨区切换后首次进服大量下载**：按需内容随进度下载属正常现象；下载完成后「采集 Persistent」即可固化

## 构建与打包

### 环境要求

| 工具 | 用途 | 说明 |
|---|---|---|
| .NET 9 SDK | 编译主程序（WPF） | [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/9.0) |
| Inno Setup 6.3+ | 制作安装包 | 默认装在 `%LOCALAPPDATA%\Programs\Inno Setup 6\`，装在别处请改 `build.ps1` 里的 `$iscc` 路径 |
| Git LFS | clone/提交 时还原大文件 | Git for Windows 自带，首次使用前执行一次 `git lfs install` |
| Python 3.10+（可选） | 替换包生成脚本 | 需 `pip install zstandard` |

> 仓库中的 `libcef.dll`（112MB）由 Git LFS 管理：未装 LFS 时 clone 下来只是一个指针文本，
> 执行 `git lfs install && git lfs pull` 即可还原。

### 从源码运行（调试）

```powershell
git clone https://github.com/Furina1027/MiSwitcher.git
cd MiSwitcher
git lfs install
git lfs pull                     # 还原 LFS 大文件
cd src\MiSwitcher
dotnet run -c Release            # 直接运行（NuGet 依赖 WpfAnimatedGif，自动还原）
```

### 打包安装器

```powershell
# 一键：单文件发布 -> 编译 Inno Setup 安装包
powershell -ExecutionPolicy Bypass -File build.ps1
```

`build.ps1` 依次执行：

1. `dotnet publish`（单文件自包含，无需目标机安装 .NET 运行时）
2. 复制主程序到 `publish\`
3. 调用 ISCC 编译 `installer.iss` → 产物：`安装包\米家三合一切换器-Setup-<版本>.exe`

不想用脚本时可手动执行等价命令（完整参数见 `build.ps1`）：

```powershell
cd src\MiSwitcher
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
copy .in\Release
et9.0-windows\win-x64\publish\米家三合一切换器.exe ..\..\publish& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" ..\..\installer.iss
```

安装包内置：主程序、中文安装向导、默认背景图「芙芙·当前背景」、三游戏 B服组件
（PCGameSDK.dll + BLPlatform64 登录框，三游戏共用同一份，安装时分别复制）。

### 替换包生成（可选）

给镜像补齐/更新两服差异文件用，用法见上方「资源替换包」一节。

## 致谢

- [Inno Setup](https://jrsoftware.org/isinfo.php) —— 安装包制作
- Inno Setup 简体中文语言文件（`ChineseSimplified.isl`，官方仓库非官方翻译）—— 安装向导汉化

## 免责声明

仅供个人学习与本地使用；切换涉及替换游戏文件，请遵守米哈游用户协议，风险自负。
