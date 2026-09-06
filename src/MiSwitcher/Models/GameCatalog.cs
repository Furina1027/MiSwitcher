namespace MiSwitcher.Models;

public enum ServerKind
{
    Official,       // 米哈游官服（国服）
    Bilibili,       // B服
    International,  // 国际服
}

public sealed class ServerInfo
{
    public ServerKind Kind { get; init; }
    public string Name { get; init; } = "";      // 官服 / B服 / 国际服
    public string Channel { get; init; } = "";   // config.ini [general] channel
    public string SubChannel { get; init; } = "";// config.ini [general] sub_channel
    public string Cps { get; init; } = "";       // config.ini [general] cps（写入值）
    public string[] CpsAliases { get; init; } = Array.Empty<string>(); // 识别用（小写）

    public override string ToString() => Name;
}

public sealed class GameDefinition
{
    public string Id { get; init; } = "";                    // genshin / starrail / zzz
    public string DisplayName { get; init; } = "";           // 原神
    public string Subtitle { get; init; } = "";              // 副标题
    public string Accent { get; init; } = "#E8B45A";         // 主题强调色
    public string AccentDeep { get; init; } = "#8A5A1E";
    public string IconPath { get; init; } = "";
    public string Glyph { get; init; } = "\uE735";           // MDL2 备用图标

    // 游戏目录 / exe / Data 文件夹
    public string[] GameExeNames { get; init; } = Array.Empty<string>();       // 当前服游戏进程名（不含 .exe）
    public string ExeOfficial { get; init; } = "";           // 官服/B服 启动 exe
    public string ExeInternational { get; init; } = "";      // 国际服 启动 exe（与官服相同则一致）
    public string DataFolderOfficial { get; init; } = "";    // 官服/B服 的 Data 文件夹
    public string DataFolderInternational { get; init; } = "";
    public bool RenameDataFolder { get; init; }              // 仅原神 true（YuanShen_Data ↔ GenshinImpact_Data）

    // 资源替换包
    public string DefaultPackDir { get; init; } = "";        // 相对于本程序上级目录的旧切换器文件夹
    public string PackFolderName { get; init; } = "";        // 程序目录「资源替换包」下的游戏子文件夹名
    public string PackServerFolderOfficial { get; init; } = "";       // 替换包内官服文件夹（B服与其共用二进制）
    public string PackServerFolderInternational { get; init; } = "";  // 替换包内国际服文件夹
    public string PackBiliLoginFolder { get; init; } = "";   // 替换包内 B 服登录框文件夹名（可为空）
    public string PackSdkDllName { get; init; } = "";        // 替换包根目录的 B 服 SDK dll（可空）
    public string SdkTargetRelativeDir { get; init; } = "";  // 游戏目录内 SDK 目标目录，{DATA} 为占位符

    // 旧切换器配置（用于自动发现游戏路径与 B 服登录框备用源）
    public string LegacyConfigFile { get; init; } = "";      // konfig.ini / xonfig.ini
    public string LegacyConfigPathKey { get; init; } = "";   // 原神路径 / 星铁路径 / 绝区零路径
    public string LegacyBiliLoginDir { get; init; } = "";    // 旧切换器顶层 B 服登录框文件夹名

    // 额外删除（切换到某服时需删除的文件），{DATA} 占位
    public Dictionary<ServerKind, string[]> ExtraDeletes { get; init; } = new();

    public IReadOnlyDictionary<ServerKind, ServerInfo> Servers { get; init; } =
        new Dictionary<ServerKind, ServerInfo>();

    public ServerInfo Server(ServerKind kind) => Servers[kind];

    public string DataFolder(ServerKind kind) => kind == ServerKind.International
        ? DataFolderInternational
        : DataFolderOfficial;

    public string GameExe(ServerKind kind) => kind == ServerKind.International
        ? ExeInternational
        : ExeOfficial;

    public string PackServerFolder(ServerKind kind) => kind == ServerKind.International
        ? PackServerFolderInternational
        : PackServerFolderOfficial;   // B服 与官服共用游戏二进制（差异仅 config + B 服 SDK/登录框）
}

public static class GameCatalog
{
    public static readonly List<GameDefinition> Games = new()
    {
        new GameDefinition
        {
            Id = "genshin",
            DisplayName = "原神",
            Subtitle = "Genshin Impact · 三服切换",
            Accent = "#E8B45A",
            AccentDeep = "#7A5418",
            IconPath = "Assets/genshin.png",
            Glyph = "\uE735",
            GameExeNames = new[] { "YuanShen", "GenshinImpact" },
            ExeOfficial = "YuanShen.exe",
            ExeInternational = "GenshinImpact.exe",
            DataFolderOfficial = "YuanShen_Data",
            DataFolderInternational = "GenshinImpact_Data",
            RenameDataFolder = true,
            DefaultPackDir = "原神三服切换器",
            PackFolderName = "原神",
            PackServerFolderOfficial = "原神米哈游官服",
            PackServerFolderInternational = "原神国际服",
            PackBiliLoginFolder = "原神B服新登录框窗口",
            PackSdkDllName = "PCGameSDK.dll",
            SdkTargetRelativeDir = "{DATA}\\Plugins",
            LegacyConfigFile = "konfig.ini",
            LegacyConfigPathKey = "原神路径",
            LegacyBiliLoginDir = "原神B服新登录框窗口",
            ExtraDeletes = new Dictionary<ServerKind, string[]>
            {
                // 5.3 起国际服比国服少 hdiffz.dll（见替换包 版本号.ini 说明）
                [ServerKind.International] = new[] { "{DATA}\\Plugins\\hdiffz.dll" },
            },
            Servers = new Dictionary<ServerKind, ServerInfo>
            {
                [ServerKind.Official] = new() { Kind = ServerKind.Official, Name = "官服", Channel = "1", SubChannel = "1", Cps = "mihoyo", CpsAliases = new[] { "mihoyo" } },
                [ServerKind.Bilibili] = new() { Kind = ServerKind.Bilibili, Name = "B服", Channel = "14", SubChannel = "0", Cps = "bilibili", CpsAliases = new[] { "bilibili" } },
                [ServerKind.International] = new() { Kind = ServerKind.International, Name = "国际服", Channel = "1", SubChannel = "0", Cps = "hoyoverse_PC", CpsAliases = new[] { "hoyoverse_pc" } },
            },
        },
        new GameDefinition
        {
            Id = "starrail",
            DisplayName = "星穹铁道",
            Subtitle = "Honkai: Star Rail · 三服切换",
            Accent = "#9F7BEA",
            AccentDeep = "#4A3580",
            IconPath = "Assets/starrail.png",
            Glyph = "\uE706",
            GameExeNames = new[] { "StarRail" },
            ExeOfficial = "StarRail.exe",
            ExeInternational = "StarRail.exe",
            DataFolderOfficial = "StarRail_Data",
            DataFolderInternational = "StarRail_Data",
            RenameDataFolder = false,
            DefaultPackDir = "星铁三服切换器",
            PackFolderName = "星穹铁道",
            PackServerFolderOfficial = "星铁米哈游官服",
            PackServerFolderInternational = "星铁国际服",
            PackBiliLoginFolder = "星铁B服新登录框窗口",
            PackSdkDllName = "PCGameSDK.dll",
            SdkTargetRelativeDir = "{DATA}\\Plugins\\x86_64",
            LegacyConfigFile = "xonfig.ini",
            LegacyConfigPathKey = "星铁路径",
            LegacyBiliLoginDir = "星铁B服新登录框窗口",
            ExtraDeletes = new Dictionary<ServerKind, string[]>(),
            Servers = new Dictionary<ServerKind, ServerInfo>
            {
                [ServerKind.Official] = new() { Kind = ServerKind.Official, Name = "官服", Channel = "1", SubChannel = "1", Cps = "gw_PC", CpsAliases = new[] { "gw_pc", "gw_pc999" } },
                [ServerKind.Bilibili] = new() { Kind = ServerKind.Bilibili, Name = "B服", Channel = "14", SubChannel = "0", Cps = "bilibili_PC", CpsAliases = new[] { "bilibili_pc" } },
                [ServerKind.International] = new() { Kind = ServerKind.International, Name = "国际服", Channel = "1", SubChannel = "1", Cps = "hoyoverse_PC", CpsAliases = new[] { "hoyoverse_pc" } },
            },
        },
        new GameDefinition
        {
            Id = "zzz",
            DisplayName = "绝区零",
            Subtitle = "Zenless Zone Zero · 三服切换",
            Accent = "#37C6CE",
            AccentDeep = "#1A5F64",
            IconPath = "Assets/zzz.png",
            Glyph = "\uE7F4",
            GameExeNames = new[] { "ZenlessZoneZero" },
            ExeOfficial = "ZenlessZoneZero.exe",
            ExeInternational = "ZenlessZoneZero.exe",
            DataFolderOfficial = "ZenlessZoneZero_Data",
            DataFolderInternational = "ZenlessZoneZero_Data",
            RenameDataFolder = false,
            DefaultPackDir = "绝区零三服切换器",
            PackFolderName = "绝区零",
            PackServerFolderOfficial = "绝区零米哈游官服",
            PackServerFolderInternational = "绝区零国际服",
            PackBiliLoginFolder = "绝区零B服新登录框窗口",
            PackSdkDllName = "PCGameSDK.dll",
            SdkTargetRelativeDir = "{DATA}\\Plugins\\x86_64",
            LegacyConfigFile = "konfig.ini",
            LegacyConfigPathKey = "绝区零路径",
            LegacyBiliLoginDir = "绝区零B服新登录框窗口",
            ExtraDeletes = new Dictionary<ServerKind, string[]>(),
            Servers = new Dictionary<ServerKind, ServerInfo>
            {
                [ServerKind.Official] = new() { Kind = ServerKind.Official, Name = "官服", Channel = "1", SubChannel = "1", Cps = "zzz_mktbackup2_pc", CpsAliases = new[] { "zzz_mktbackup2_pc" } },
                [ServerKind.Bilibili] = new() { Kind = ServerKind.Bilibili, Name = "B服", Channel = "14", SubChannel = "0", Cps = "zzz_bilibili_pc", CpsAliases = new[] { "zzz_bilibili_pc" } },
                [ServerKind.International] = new() { Kind = ServerKind.International, Name = "国际服", Channel = "1", SubChannel = "0", Cps = "zzz_oversea_gw_pc", CpsAliases = new[] { "zzz_oversea_gw_pc" } },
            },
        },
    };

    public static GameDefinition Get(string id) => Games.First(g => g.Id == id);
}
