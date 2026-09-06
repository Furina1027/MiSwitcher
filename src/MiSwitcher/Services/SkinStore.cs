using System.IO;
using System.Windows.Media.Imaging;

namespace MiSwitcher.Services;

public sealed class SkinItem
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Source { get; init; } = "";   // 来源分组
    public bool IsVideo { get; init; }          // GIF 动图

    public override string ToString() => Name;
}

/// <summary>皮肤库：.ts1/.ts2/.ts8 实质为 GIF 动图（本机逆向确认），扫描后可直接当动态壁纸用。</summary>
public static class SkinStore
{
    private static readonly (string Dir, string Label)[] LegacySkins =
    {
        ("原神三服切换器", "原神皮肤"),
        ("星铁三服切换器", "星铁皮肤"),
        ("绝区零三服切换器", "绝区零皮肤"),
    };

    public static List<SkinItem> Scan()
    {
        var list = new List<SkinItem>();
        // 自带皮肤目录
        var own = AppLayout.SkinDir;
        if (Directory.Exists(own))
            foreach (var f in Directory.EnumerateFiles(own))
                if (IsImage(f))
                    list.Add(new SkinItem
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Path = f,
                        Source = "我的皮肤",
                        IsVideo = IsGif(f),
                    });

        foreach (var (dirName, label) in LegacySkins)
        {
            var dir = Path.Combine(AppLayout.GamesRoot, dirName, "切换器皮肤");
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                if (!IsGif(f)) continue;   // .ts1/.ts2/.ts8 仅接受真实 GIF
                list.Add(new SkinItem
                {
                    Name = Path.GetFileNameWithoutExtension(f),
                    Path = f,
                    Source = label,
                    IsVideo = true,
                });
            }
        }

        return list
            .GroupBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(s => s.Source).ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public static BitmapSource? LoadThumb(string file)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.DecodePixelWidth = 320;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(file);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsImage(string f) =>
        f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static bool IsGif(string f) => f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
}
