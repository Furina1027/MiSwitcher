using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiSwitcher.Models;

namespace MiSwitcher.Services;

public sealed class GameSettings
{
    public string GamePath { get; set; } = "";
    public string PackPath { get; set; } = "";
    public string SkinFile { get; set; } = "";
    public double SkinOpacity { get; set; } = 0.78;
}

public sealed class AppSettings
{
    public string CurrentGameId { get; set; } = "genshin";
    // 默认皮肤为安装包内置的芙芙背景（相对文件名，FillDefaults 时解析为绝对路径）
    public string SkinFile { get; set; } = "芙芙·当前背景.png";
    public double SkinOpacity { get; set; } = 0.72;
    public Dictionary<string, GameSettings> Games { get; set; } = new();
    public bool ShowLog { get; set; } = false;

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "config.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    s.FillDefaults();
                    return s;
                }
            }
        }
        catch
        {
            // 配置损坏时回退默认
        }
        var fresh = new AppSettings();
        fresh.FillDefaults();
        return fresh;
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        File.WriteAllText(FilePath, json);
    }

    public GameSettings Game(string id)
    {
        if (!Games.TryGetValue(id, out var gs))
        {
            gs = new GameSettings();
            Games[id] = gs;
        }
        return gs;
    }

    /// <summary>用旧切换器的 konfig.ini / xonfig.ini 与目录约定补默认值。</summary>
    private void FillDefaults()
    {
        var root = AppLayout.AppDir;
        var parent = AppLayout.GamesRoot;

        // 相对文件名（默认皮肤）按程序目录下的「背景图」文件夹解析；文件不存在则回退渐变背景
        if (!string.IsNullOrWhiteSpace(SkinFile) && !Path.IsPathRooted(SkinFile))
        {
            var resolved = Path.Combine(AppLayout.SkinDir, SkinFile);
            SkinFile = File.Exists(resolved) ? resolved : "";
        }

        foreach (var g in GameCatalog.Games)
        {
            var gs = Game(g.Id);
            var oldDir = Path.Combine(parent, g.DefaultPackDir);

            if (string.IsNullOrWhiteSpace(gs.GamePath))
            {
                // 1) 旧切换器配置文件 2) 旧切换器所在目录同名约定
                var legacy = Path.Combine(oldDir, g.LegacyConfigFile);
                var p = IniFile.ReadValue(legacy, "游戏配置", g.LegacyConfigPathKey);
                gs.GamePath = (string.IsNullOrWhiteSpace(p) || !Directory.Exists(p)) ? "" : p;
            }

            if (string.IsNullOrWhiteSpace(gs.PackPath))
                // 默认使用程序目录下的「资源替换包\<游戏>」文件夹（安装程序会创建），内容可自行更换
                gs.PackPath = Path.Combine(AppLayout.PacksRoot, g.PackFolderName);
        }
    }
}
