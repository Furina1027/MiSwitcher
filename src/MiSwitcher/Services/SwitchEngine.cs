using System.Diagnostics;
using System.IO;
using MiSwitcher.Models;

namespace MiSwitcher.Services;

public sealed class SwitchProgress
{
    public string Stage { get; init; } = "";
    public double Percent { get; init; }
    public string Detail { get; init; } = "";
}

public sealed class SwitchResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public List<string> Log { get; init; } = new();
    public ServerKind? Detected { get; init; }
}

/// <summary>
/// 三游戏通用切换引擎。机制逆向自原三服切换器：
/// (原神)Data 文件夹改名 → 清理其他服残留 → 从资源替换包镜像覆盖 →
/// B服写入 PCGameSDK.dll + BLPlatform64 登录框 → 改写 config.ini 渠道字段。
/// 不做切换前备份：替换包可随时通过 chunk 脚本重新生成，两服镜像即互为恢复源。
/// </summary>
public sealed class SwitchEngine
{
    private readonly record struct FileOp(string Src, string Dst, long Size, string Rel);

    private readonly GameDefinition _game;
    private readonly string _gameDir;
    private readonly string _packDir;

    public SwitchEngine(GameDefinition game, string gameDir, string packDir)
    {
        _game = game;
        _gameDir = gameDir.TrimEnd('\\', '/');
        _packDir = packDir.TrimEnd('\\', '/');
    }

    // ============ 检测 ============

    public ServerKind Detect(out string detail)
    {
        detail = "";
        var config = Path.Combine(_gameDir, "config.ini");
        var cps = IniFile.ReadValue(config, "general", "cps")?.Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(cps))
        {
            foreach (var kv in _game.Servers)
            {
                if (kv.Value.CpsAliases.Contains(cps))
                {
                    detail = $"config.ini cps={kv.Value.Cps}";
                    return kv.Key;
                }
            }
            detail = $"config.ini cps={cps}（未识别，按文件特征判断）";
        }
        else
        {
            detail = "未读取到 config.ini 的 cps，按文件特征判断";
        }

        // 文件特征兜底
        var curData = ExistingDataFolder();
        var sdk = Path.Combine(_gameDir, SdkTargetDir(curData == "" ? _game.DataFolderOfficial : curData), "PCGameSDK.dll");
        if (File.Exists(sdk)) return ServerKind.Bilibili;

        if (_game.RenameDataFolder && Directory.Exists(Path.Combine(_gameDir, _game.DataFolderInternational)))
            return ServerKind.International;
        if (File.Exists(Path.Combine(_gameDir, _game.ExeInternational)) &&
            !_game.RenameDataFolder && cps is "hoyoverse_pc" or "zzz_oversea_gw_pc")
            return ServerKind.International;

        return ServerKind.Official;
    }

    public string? GameVersion()
    {
        var v = IniFile.ReadValue(Path.Combine(_gameDir, "config.ini"), "general", "game_version");
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    public string? PackVersion()
    {
        var v = IniFile.ReadValue(Path.Combine(_packDir, "版本号.ini"), "配置", "替换包版本号");
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    public bool IsGameRunning()
    {
        foreach (var name in _game.GameExeNames)
            if (Process.GetProcessesByName(name).Length > 0) return true;
        return false;
    }

    public void KillGame()
    {
        foreach (var name in _game.GameExeNames)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(true); p.WaitForExit(5000); } catch { /* 已退出 */ }
            }
    }

    public void Launch(ServerKind kind)
    {
        var exe = Path.Combine(_gameDir, _game.GameExe(kind));
        if (!File.Exists(exe))
        {
            if (File.Exists(Path.Combine(_gameDir, _game.GameExe(Other(kind)))))
                exe = Path.Combine(_gameDir, _game.GameExe(Other(kind)));
            else
                throw new FileNotFoundException("未找到游戏可执行文件：" + exe);
        }
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = _gameDir, UseShellExecute = true });
    }

    // ============ 切换主流程 ============

    public async Task<SwitchResult> SwitchAsync(ServerKind target,
        IProgress<SwitchProgress> progress, CancellationToken ct)
    {
        var log = new List<string>();
        try
        {
            void Log(string msg) { log.Add(msg); progress.Report(new SwitchProgress { Stage = msg, Detail = msg }); }

            var current = Detect(out var det);
            Log($"当前状态：{_game.Server(current).Name}（{det}）");
            if (current == target)
                return new SwitchResult { Success = true, Message = "已经是该服务器，无需切换。", Log = log, Detected = current };

            progress.Report(new SwitchProgress { Stage = "校验目录", Percent = 2 });
            if (!Directory.Exists(_gameDir)) return Fail(log, "游戏目录不存在：" + _gameDir);
            if (!Directory.Exists(_packDir)) return Fail(log, "资源替换包目录不存在：" + _packDir);
            var packServer = Path.Combine(_packDir, _game.PackServerFolder(target));
            if (!Directory.Exists(packServer)) return Fail(log, $"替换包内缺少 {_game.PackServerFolder(target)} 文件夹");

            // 版本校验提示信息（UI 层决定是否阻断，这里记录）
            var gv = GameVersion(); var pv = PackVersion();
            if (gv != null && pv != null && gv != pv)
                Log($"⚠ 版本不一致：游戏 {gv} / 替换包 {pv}，继续切换可能需要更新资源替换包");

            var officialRels = MirrorFiles(Path.Combine(_packDir, _game.PackServerFolderOfficial));
            var intlRels = Directory.Exists(Path.Combine(_packDir, _game.PackServerFolderInternational))
                ? MirrorFiles(Path.Combine(_packDir, _game.PackServerFolderInternational))
                : new List<string>();
            var targetRels = target == ServerKind.International ? intlRels : officialRels;

            // ---------- 1. 校验数据文件夹 ----------
            progress.Report(new SwitchProgress { Stage = "校验游戏目录", Percent = 5 });
            var curData = ExistingDataFolder();
            if (curData == "") return Fail(log, "未在游戏目录找到 YuanShen_Data / GenshinImpact_Data 等数据文件夹，请确认游戏路径正确");

            // ---------- 2. 结束游戏进程 ----------
            if (IsGameRunning())
            {
                Log("检测到游戏正在运行，已自动结束进程");
                KillGame();
                await Task.Delay(800, ct);
            }

            // ---------- 3. Data 文件夹改名（仅原神） ----------
            var targetData = _game.DataFolder(target);
            var curDataFull = Path.Combine(_gameDir, curData);
            var targetDataFull = Path.Combine(_gameDir, targetData);
            if (curData != targetData)
            {
                if (Directory.Exists(targetDataFull))
                {
                    return Fail(log, $"同时存在 {curData} 与 {targetData} 两个数据文件夹（上次切换可能未完成）。\n" +
                                     "请先手动删除容量较小的那个（通常 <2GB 的为残留），只保留约完整容量的文件夹后重试。");
                }
                Log($"重命名数据文件夹：{curData} → {targetData}");
                Directory.Move(curDataFull, targetDataFull);
            }

            // ---------- 4. 清理其他服残留 ----------
            progress.Report(new SwitchProgress { Stage = "清理其他服残留文件", Percent = 32 });
            var deleteUnion = officialRels.Concat(intlRels).Distinct(StringComparer.OrdinalIgnoreCase);
            if (target != ServerKind.Bilibili)
                deleteUnion = deleteUnion.Concat(BiliSdkRels(targetData));
            foreach (var rel in deleteUnion)
            {
                if (targetRels.Contains(rel)) continue;
                // Persistent 热更一律不删：目标镜像提供的 Persistent 由第 5 步覆盖即可，
                // 镜像没有的保留在本机（删除会导致客户端跨服时重新下载全部热更）
                var relNorm = rel.ToLowerInvariant();
                if (relNorm.Contains("\\persistent\\") || relNorm.EndsWith("\\persistent")) continue;
                var full = Path.Combine(_gameDir, rel);
                if (File.Exists(full))
                {
                    EnsureWritable(full);
                    File.Delete(full);
                    log.Add("删除：" + rel);
                }
            }
            if (_game.ExtraDeletes.TryGetValue(target, out var extras))
                foreach (var rel in extras)
                {
                    var full = Path.Combine(_gameDir, Expand(rel, targetData));
                    if (File.Exists(full))
                    {
                        EnsureWritable(full);
                        File.Delete(full);
                        log.Add("删除：" + Expand(rel, targetData));
                    }
                }

            // Persistent 清理开关：当前关闭（用户测试纯镜像覆盖）。
            // 恢复方法：把下面的常量改为 true —— 跨区（国服↔国际）切换且目标镜像未提供
            // Persistent 时清掉残留（ChannelName 决定客户端身份，残留会 4214）；
            // 官服/B服 互切不处理，镜像提供 Persistent 时始终由第 5 步照常覆盖。
            const bool cleanPersistentOnRegionSwitch = false;
            if (cleanPersistentOnRegionSwitch
                && (current == ServerKind.International) != (target == ServerKind.International)
                && !Directory.Exists(Path.Combine(packServer, targetData, "Persistent")))
            {
                var persDir = Path.Combine(targetDataFull, "Persistent");
                if (Directory.Exists(persDir))
                {
                    DeleteReadOnlyTree(persDir);
                    log.Add("删除：Persistent（跨区切换且目标镜像未提供，客户端将自动重新下载热更）");
                }
            }

            // ---------- 5. 从替换包镜像覆盖（官服↔B服 二进制相同则跳过） ----------
            var sameBinary = (current == ServerKind.Official && target == ServerKind.Bilibili) ||
                             (current == ServerKind.Bilibili && target == ServerKind.Official);
            if (sameBinary)
            {
                Log("官服/B服 使用同一套游戏文件，跳过文件替换");
            }
            else
            {
                var ops = targetRels.Select(rel => new FileOp(
                    Path.Combine(packServer, rel), Path.Combine(_gameDir, rel), 0, rel)).ToList();
                long total = 0;
                foreach (var op in ops) total += new FileInfo(op.Src).Length;
                await CopyWithProgressAsync(ops, total, log, p =>
                    progress.Report(new SwitchProgress { Stage = "替换目标服文件", Percent = 35 + p * 0.45, Detail = p.ToString("0") + "%" }), ct);
            }

            // ---------- 6. B服 SDK 与登录框 ----------
            if (target == ServerKind.Bilibili)
            {
                progress.Report(new SwitchProgress { Stage = "写入 B 服 SDK 与登录框", Percent = 82 });
                await InstallBiliSdkAsync(targetData, log, ct);
            }

            // ---------- 7. 写 config.ini 渠道 ----------
            progress.Report(new SwitchProgress { Stage = "写入 config.ini 渠道配置", Percent = 92 });
            var srv = _game.Server(target);
            IniFile.WriteValues(Path.Combine(_gameDir, "config.ini"), "general", new Dictionary<string, string>
            {
                ["channel"] = srv.Channel,
                ["sub_channel"] = srv.SubChannel,
                ["cps"] = srv.Cps,
            });
            log.Add($"config.ini：channel={srv.Channel} sub_channel={srv.SubChannel} cps={srv.Cps}");

            // ---------- 8. 校验 ----------
            var after = Detect(out var det2);
            if (after != target) return Fail(log, "切换后校验未通过（" + det2 + "），请查看日志。");
            progress.Report(new SwitchProgress { Stage = "完成", Percent = 100 });
            log.Add($"✔ 已切换到 {_game.Server(target).Name}");
            return new SwitchResult { Success = true, Message = $"已切换到{_game.Server(target).Name}", Log = log, Detected = target };
        }
        catch (OperationCanceledException)
        {
            return new SwitchResult { Success = false, Message = "已取消。", Log = log };
        }
        catch (UnauthorizedAccessException ex)
        {
            return Fail(log, "没有权限：" + ex.Message + "\n请关闭程序后以管理员身份运行再试。");
        }
        catch (IOException ex)
        {
            return Fail(log, "文件被占用或 IO 错误：" + ex.Message + "\n请彻底关闭游戏/杀软扫描后重试。");
        }
    }

    // ============ 子步骤 ============

    /// <summary>删除目录（游戏会把缓存文件设为只读，先统一清属性再删）。</summary>
    private static void DeleteReadOnlyTree(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* 单个失败不阻断 */ }
        }
        Directory.Delete(dir, true);
    }

    private async Task InstallBiliSdkAsync(string targetData, List<string> log, CancellationToken ct)
    {
        var sdkDir = Path.Combine(_gameDir, SdkTargetDir(targetData));
        Directory.CreateDirectory(sdkDir);

        // PCGameSDK.dll：替换包根 → 旧切换器目录
        var dll = Path.Combine(sdkDir, _game.PackSdkDllName);
        var sdkSrc = Candidates()
            .Select(c => Path.Combine(c, _game.PackSdkDllName))
            .FirstOrDefault(File.Exists);
        if (_game.PackSdkDllName != "" && sdkSrc != null)
        {
            File.Copy(sdkSrc, dll, true);
            log.Add("写入 B 服 SDK：" + Path.GetFileName(sdkSrc));
        }
        else if (_game.PackSdkDllName != "")
        {
            log.Add("⚠ 未找到 PCGameSDK.dll 源文件（替换包/旧目录均无），跳过 SDK 写入");
        }

        // BLPlatform64 登录框：替换包 → 旧切换器顶层文件夹
        var blTarget = Path.Combine(sdkDir, "BLPlatform64");
        var blSrc = new[]
        {
            string.IsNullOrEmpty(_game.PackBiliLoginFolder) ? null : Path.Combine(_packDir, _game.PackBiliLoginFolder, "BLPlatform64"),
            Path.Combine(LegacyDir(), _game.LegacyBiliLoginDir, "BLPlatform64"),
        }.FirstOrDefault(Directory.Exists);

        if (blSrc != null)
        {
            var ops = new List<FileOp>();
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(blSrc, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(blSrc, f);
                ops.Add(new FileOp(f, Path.Combine(blTarget, rel), new FileInfo(f).Length, "BLPlatform64\\" + rel));
                total += new FileInfo(f).Length;
            }
            log.Add($"复制 B 服登录框 {ops.Count} 个文件 ← {blSrc}");
            await CopyWithProgressAsync(ops, total, log, _ => { }, ct);
        }
        else
        {
            log.Add("⚠ 未找到 BLPlatform64 登录框源文件夹，B 服登录框可能不完整");
        }

        string[] Candidates()
        {
            var c = new List<string> { _packDir, LegacyDir() };
            return c.ToArray();
        }
    }

    private string LegacyDir()
    {
        // 旧切换器所在目录：<上级>\原神三服切换器 等
        return Path.Combine(AppLayout.GamesRoot, _game.DefaultPackDir);
    }

    /// <summary>游戏会给部分文件（如 Persistent）加只读属性，覆盖/删除前先清掉，
    /// 否则即使是管理员也会被拒绝访问。</summary>
    private static void EnsureWritable(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch { /* 清不掉时让后续操作抛出原始错误 */ }
    }

    private async Task CopyWithProgressAsync(List<FileOp> ops, long total, List<string> log,
        Action<double> report, CancellationToken ct)
    {
        long done = 0;
        var buffer = new byte[1024 * 1024];
        foreach (var op in ops)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(op.Dst)!);
            EnsureWritable(op.Dst);
            if (op.Size > 48 * 1024 * 1024)
                log.Add($"复制 {op.Rel}（{op.Size / 1024.0 / 1024.0:0} MB）");

            await using var src = new FileStream(op.Src, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var dst = new FileStream(op.Dst, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                report(total == 0 ? 100 : done * 100.0 / total);
            }
        }
    }

    // ============ 工具 ============

    private List<string> MirrorFiles(string dir)
    {
        var list = new List<string>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            list.Add(Path.GetRelativePath(dir, f));
        return list;
    }

    private IEnumerable<string> BiliSdkRels(string dataFolder)
    {
        if (string.IsNullOrEmpty(_game.PackSdkDllName) && string.IsNullOrEmpty(_game.PackBiliLoginFolder))
            yield break;
        var baseDir = SdkTargetDir(dataFolder);
        if (_game.PackSdkDllName != "")
            yield return Path.Combine(baseDir, _game.PackSdkDllName);
        // 登录框为整个文件夹：按存在与否展开（删除用）
        var blFull = Path.Combine(_gameDir, baseDir, "BLPlatform64");
        if (Directory.Exists(blFull))
            foreach (var f in Directory.EnumerateFiles(blFull, "*", SearchOption.AllDirectories))
                yield return Path.Combine(baseDir, "BLPlatform64", Path.GetRelativePath(blFull, f));
    }

    private string SdkTargetDir(string dataFolder) => _game.SdkTargetRelativeDir.Replace("{DATA}", dataFolder);

    private string Expand(string rel, string dataFolder) => rel.Replace("{DATA}", dataFolder);

    private string ExistingDataFolder()
    {
        foreach (var d in new[] { _game.DataFolderOfficial, _game.DataFolderInternational })
            if (Directory.Exists(Path.Combine(_gameDir, d))) return d;
        return "";
    }

    private static ServerKind Other(ServerKind k) => k == ServerKind.International ? ServerKind.Official : ServerKind.International;

    private static SwitchResult Fail(List<string> log, string msg) =>
        new() { Success = false, Message = msg, Log = log };
}
