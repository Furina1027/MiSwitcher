using System.IO;

namespace MiSwitcher.Services;

/// <summary>定位应用根目录（兼容开发期 bin 目录运行与发布后单目录运行）。</summary>
public static class AppLayout
{
    private static string? _base;

    /// <summary>米家三合一切换器 应用目录（含背景图\、资源替换包\）。</summary>
    public static string AppDir
    {
        get
        {
            if (_base != null) return _base;
            // 注意：BaseDirectory 带尾部反斜杠时，DirectoryInfo.FullName 会保留，
            // 而 Directory.GetParent 对带尾斜杠的路径会把末段当文件名返回原目录 —— 必须先裁掉
            var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('\\', '/'));
            while (dir != null && dir.Name != "米家三合一切换器")
                dir = dir.Parent;
            _base = (dir?.FullName ?? AppContext.BaseDirectory).TrimEnd('\\', '/');
            return _base;
        }
    }

    /// <summary>切换器总目录（<安装目录的上级>），三个旧切换器文件夹所在地。</summary>
    public static string GamesRoot => Directory.GetParent(AppDir)!.FullName.TrimEnd('\\', '/');

    /// <summary>资源替换包根目录（安装后由安装程序创建空文件夹，用户可自行放入/更换各游戏替换包）。</summary>
    public static string PacksRoot => Path.Combine(AppDir, "资源替换包");

    public static string SkinDir => Path.Combine(AppDir, "背景图");
}
