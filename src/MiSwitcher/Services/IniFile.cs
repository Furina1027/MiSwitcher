using System.IO;
using System.Text;

namespace MiSwitcher.Services;

/// <summary>极简 INI 读取/改写，兼容 GBK 与 UTF-8（旧切换器配置为 GBK）。</summary>
public static class IniFile
{
    public static string? ReadValue(string file, string section, string key)
    {
        if (!File.Exists(file)) return null;
        foreach (var raw in ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line[..eq].Trim();
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return line[(eq + 1)..].Trim().Trim('"');
        }
        return null;
    }

    /// <summary>改写 [general] 节中的指定键（不存在则追加），保留其余内容。</summary>
    public static void WriteValues(string file, string section, IEnumerable<KeyValuePair<string, string>> updates)
    {
        var lines = File.Exists(file)
            ? ReadAllLines(file).ToList()
            : new List<string>();

        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in updates) wanted[kv.Key] = kv.Value;

        var currentSection = "";
        var sectionFound = false;
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var t = line.Trim();
            if (t.StartsWith('['))
            {
                if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase) && wanted.Count > handled.Count)
                {
                    // 该节结束仍未覆盖全部键 → 在节尾补齐
                    foreach (var kv in wanted)
                        if (handled.Add(kv.Key))
                            lines.Insert(i++, $"{kv.Key}={kv.Value}");
                }
                currentSection = t.Trim('[', ']').Trim();
                if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase)) sectionFound = true;
                continue;
            }
            var eq = t.IndexOf('=');
            if (eq <= 0) continue;
            var key = t[..eq].Trim();
            if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase) && wanted.TryGetValue(key, out var val))
            {
                lines[i] = $"{key}={val}";
                handled.Add(key);
            }
        }
        if (!sectionFound) lines.Add($"[{section}]");
        foreach (var kv in wanted)
            if (handled.Add(kv.Key))
                lines.Add($"{kv.Key}={kv.Value}");

        File.WriteAllText(file, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
    }

    private static string[] ReadAllLines(string file)
    {
        var bytes = File.ReadAllBytes(file);
        var text = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            : TryDecode(bytes, Encoding.UTF8) ?? TryDecode(bytes, Encoding.GetEncoding("GBK")) ?? Encoding.Default.GetString(bytes);
        return text.Replace("\r\n", "\n").Split('\n');
    }

    private static string? TryDecode(byte[] bytes, Encoding enc)
    {
        try
        {
            var s = enc.GetString(bytes);
            // 回读校验，避免把 GBK 当 UTF-8 解出乱码
            var round = enc.GetBytes(s);
            return round.AsSpan().SequenceEqual(bytes) ? s : null;
        }
        catch
        {
            return null;
        }
    }
}
