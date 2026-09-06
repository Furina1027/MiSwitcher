# -*- coding: utf-8 -*-
"""米家三合一切换器 资源替换包生成脚本（chunk 下载版，完全独立）

用米哈游 Sophon 分块下载接口，生成三个游戏的「官服 / 国际服」镜像文件夹：
  1. 取 CN / 国际服 启动器分支凭据 (getGameBranches)
  2. getBuild 取双方「游戏资源」manifest（zstd 压缩的 protobuf，本脚本手工解码）
  3. 按路径（数据文件夹名归一）对比，筛出两服内容不同的文件
  4. 仅下载这些差异文件，组装出 官服镜像 / 国际服镜像，写 版本号.ini

不触碰替换包目录里的其他内容（B服 SDK/登录框、Persistent 采集等原样保留）。

依赖：仅 `pip install zstandard`（manifest 与 chunk 均为 zstd 压缩，Python 3.13 以下
无标准库实现）；除 zstandard 外无任何第三方依赖。

用法:
  python tools/gen_pack.py                        # 三游戏全量, 输出到切换器实际配置的替换包位置
  python tools/gen_pack.py --games hk4e,nap       # 指定游戏 (hk4e / hkrpg / nap)
  python tools/gen_pack.py --out <目录>           # 指定输出根目录(其下 原神/星穹铁道/绝区零)
  python tools/gen_pack.py --dry-run              # 只打印差异清单不下载
  python tools/gen_pack.py --side cn|intl|both    # 只生成某一侧, 默认 both
  python tools/gen_pack.py --cn-snapshot          # CN 用本地历史快照(免联网凭据)

输出位置自动定位: 安装包自带本脚本于 <安装目录>\\tools\\，默认直接写入切换器正在使用的
替换包位置 —— 优先取安装目录 config.json 中各游戏配置的 PackPath（含自定义位置），
其次 <安装目录>\\资源替换包\\<游戏>；脚本被单独拷到别处时通过注册表卸载信息定位安装目录。
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import shutil
import sys
import threading
import time
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

import zstandard

PKG_REPO = None                                           # 本地历史快照仓库(--cn-snapshot 时用 --pkg-repo 传入)
APP_ID = '{8E7A21C4-5F2B-4C6E-9A3D-1B0C7F5E4D21}_is1'      # 切换器 Inno 卸载注册表键


def find_app_dir() -> Path | None:
    """定位切换器安装目录：脚本自身位置（<安装目录>\\tools\\）→ 注册表卸载信息。"""
    here = Path(__file__).resolve().parent.parent
    if (here / '米家三合一切换器.exe').exists() or (here / 'config.json').exists():
        return here
    try:
        import winreg
    except ImportError:
        return None
    for root in (winreg.HKEY_CURRENT_USER, winreg.HKEY_LOCAL_MACHINE):
        try:
            with winreg.OpenKey(root, rf'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{APP_ID}') as k:
                loc, _ = winreg.QueryValueEx(k, 'InstallLocation')
            if loc and Path(loc).exists():
                return Path(loc)
        except OSError:
            continue
    return None


def load_config_pack_paths(app_dir: Path) -> dict:
    """从安装目录 config.json 读取各游戏实际配置的替换包路径（含用户自定义路径）。"""
    try:
        cfg = json.loads((app_dir / 'config.json').read_text(encoding='utf-8-sig'))
        return {gid: gs['PackPath'] for gid, gs in cfg.get('Games', {}).items() if gs.get('PackPath')}
    except Exception:  # noqa: BLE001 - config 缺失/损坏时回退默认位置
        return {}

CN_HOST, CN_LAUNCHER = 'https://hyp-api.mihoyo.com', 'jGHBHlcOq1'
SG_HOST, SG_LAUNCHER = 'https://sg-hyp-api.hoyoverse.com', 'VYTpXlbWo8'
CN_BUILD = 'https://downloader-api.mihoyo.com/downloader/sophon_chunk/api/getBuild'
SG_BUILD = 'https://sg-downloader-api.hoyoverse.com/downloader/sophon_chunk/api/getBuild'

GAMES = {
    'hk4e': dict(name='原神', dir='原神', cn='1Z8W5NHUQb', glob='gopR6Cufr3',
                 mirror_cn='原神米哈游官服', mirror_intl='原神国际服',
                 data=('yuanshen_data', 'genshinimpact_data')),
    'hkrpg': dict(name='星穹铁道', dir='星穹铁道', cn='64kMb5iAWu', glob='4ziysqXOQ8',
                  mirror_cn='星铁米哈游官服', mirror_intl='星铁国际服', data=None),
    'nap': dict(name='绝区零', dir='绝区零', cn='x6znKlJ0xK', glob='U5hbdsT9W7',
                mirror_cn='绝区零米哈游官服', mirror_intl='绝区零国际服', data=None),
}

UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36'


def log(msg: str) -> None:
    print(time.strftime('%H:%M:%S ') + msg, flush=True)


# ============ HTTP（标准库实现，带重试与可选校验） ============

def http_get(url: str, tries: int = 5, timeout: int = 60,
             expect_size: int | None = None, expect_md5: str | None = None) -> bytes:
    last: Exception | None = None
    for attempt in range(1, tries + 1):
        try:
            req = urllib.request.Request(url, headers={'User-Agent': UA})
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                data = resp.read()
            if expect_size is not None and len(data) != expect_size:
                raise RuntimeError(f'大小不匹配: 期望 {expect_size}, 实际 {len(data)}')
            if expect_md5 and hashlib.md5(data).hexdigest().lower() != expect_md5.lower():
                raise RuntimeError('MD5 不匹配')
            return data
        except Exception as exc:  # noqa: BLE001 - 网络类异常统一重试
            last = exc
            time.sleep(min(2 * attempt, 8))
    raise RuntimeError(f'下载失败（{tries} 次）: {url}\n  原因: {last}')


def http_get_json(url: str, params: dict | None = None) -> dict:
    if params:
        url += '?' + urllib.parse.urlencode(params)
    return json.loads(http_get(url))


def md5_hex(data: bytes) -> str:
    return hashlib.md5(data).hexdigest()


def zstd_decompress(data: bytes) -> bytes:
    try:
        return zstandard.ZstdDecompressor().decompress(data)
    except zstandard.ZstdError:
        # 帧头缺少内容大小时退回流式解压
        with zstandard.ZstdDecompressor().stream_reader(io.BytesIO(data)) as reader:
            return reader.read()


# ============ SophonChunkManifest protobuf 手工解码 ============
# proto 定义（字段号与官方协议一致）:
#   SophonChunkManifest { repeated SophonChunkFile chuncks = 1; }
#   SophonChunkFile { string file=1; repeated SophonChunk chunks=2; bool is_folder=3;
#                     int64 size=4; string md5=5; }
#   SophonChunk { string id=1; string uncompressed_md5=2; int64 offset=3;
#                 int64 compressed_size=4; int64 uncompressed_size=5; int64 unknown=6;
#                 string compressed_md5=7; }

def _read_varint(buf: bytes, i: int) -> tuple[int, int]:
    value = shift = 0
    while True:
        b = buf[i]
        i += 1
        value |= (b & 0x7F) << shift
        if not b & 0x80:
            return value, i
        shift += 7


def _iter_fields(buf: bytes):
    """逐个产出 (field_number, wire_type, value)；0=varint, 2=length-delimited。"""
    i = 0
    while i < len(buf):
        tag, i = _read_varint(buf, i)
        field_no, wire_type = tag >> 3, tag & 7
        if wire_type == 0:
            value, i = _read_varint(buf, i)
        elif wire_type == 2:
            length, i = _read_varint(buf, i)
            value = buf[i:i + length]
            i += length
        else:
            raise ValueError(f'不支持的 wire type {wire_type}（field {field_no}）')
        yield field_no, wire_type, value


def parse_chunk_manifest(data: bytes) -> list[dict]:
    files: list[dict] = []
    for fn, wt, val in _iter_fields(data):
        if fn != 1 or wt != 2:
            continue
        entry = {'file': '', 'chunks': [], 'is_folder': False, 'size': 0, 'md5': ''}
        for fn2, wt2, val2 in _iter_fields(val):
            if fn2 == 1 and wt2 == 2:
                entry['file'] = val2.decode('utf-8')
            elif fn2 == 2 and wt2 == 2:
                chunk = {'id': '', 'uncompressed_md5': '', 'offset': 0,
                         'compressed_size': 0, 'uncompressed_size': 0, 'compressed_md5': ''}
                for fn3, wt3, val3 in _iter_fields(val2):
                    if fn3 == 1 and wt3 == 2:
                        chunk['id'] = val3.decode('utf-8')
                    elif fn3 == 2 and wt3 == 2:
                        chunk['uncompressed_md5'] = val3.decode('utf-8')
                    elif fn3 == 3 and wt3 == 0:
                        chunk['offset'] = val3
                    elif fn3 == 4 and wt3 == 0:
                        chunk['compressed_size'] = val3
                    elif fn3 == 5 and wt3 == 0:
                        chunk['uncompressed_size'] = val3
                    elif fn3 == 7 and wt3 == 2:
                        chunk['compressed_md5'] = val3.decode('utf-8')
                entry['chunks'].append(chunk)
            elif fn2 == 3 and wt2 == 0:
                entry['is_folder'] = bool(val2)
            elif fn2 == 4 and wt2 == 0:
                entry['size'] = val2
            elif fn2 == 5 and wt2 == 2:
                entry['md5'] = val2.decode('utf-8')
        files.append(entry)
    return files


def join_url(prefix: str, *parts: str) -> str:
    return '/'.join([prefix.rstrip('/'), *[p for p in parts if p]])


# ============ 启动器 / 下载器接口 ============

def get_main_branch(host: str, launcher: str, game_id: str) -> dict:
    data = http_get_json(f'{host}/hyp/hyp-connect/api/getGameBranches',
                         {'launcher_id': launcher, 'game_ids[]': game_id,
                          'filter_adv': 'true', 'launcher_lang': 'zh-cn'})
    for gb in data['data']['game_branches']:
        if gb['game']['id'] == game_id and gb.get('main'):
            return gb['main']
    raise RuntimeError(f'{game_id}: 未取到 main 分支')


def get_build(base: str, creds: dict) -> dict:
    payload = http_get_json(base, {'branch': 'main', 'package_id': creds['package_id'],
                                   'password': creds['password']})
    if payload.get('retcode') != 0:
        raise RuntimeError(f'getBuild 失败 {payload.get("retcode")}: {payload.get("message")}')
    return payload


def load_cn_snapshot(game: str, repo: Path | None) -> tuple[dict, str]:
    """读取本地历史快照作为 CN getBuild 响应（免凭据）。"""
    if repo is None:
        raise RuntimeError('--cn-snapshot 需要 --pkg-repo 指定 本地历史快照仓库路径')
    d = repo / 'chunk'
    snaps = sorted(d.glob(f'{game}_*.json'),
                   key=lambda p: [int(x) for x in re.findall(r'\d+', p.stem)])
    if not snaps:
        raise FileNotFoundError(f'{d} 下没有 {game} 快照')
    payload = json.loads(snaps[-1].read_text(encoding='utf-8'))
    return payload, snaps[-1].stem.split('_', 1)[1]


def download_manifest(item: dict) -> list[dict]:
    """下载单个 manifest 分类并解析，带 checksum/大小校验。"""
    man = item['manifest']
    man_dl = item.get('manifest_download') or {}
    url = join_url(man_dl.get('url_prefix', ''), man['id'], man_dl.get('url_suffix', ''))
    raw = http_get(url, expect_size=int(man.get('compressed_size', 0) or 0))
    data = zstd_decompress(raw)
    if man.get('checksum') and md5_hex(data).lower() != str(man['checksum']).lower():
        raise RuntimeError(f'manifest {man["id"]} 解压后 MD5 不匹配')
    if man.get('uncompressed_size') and len(data) != int(man['uncompressed_size']):
        raise RuntimeError(f'manifest {man["id"]} 解压后大小不匹配')
    return parse_chunk_manifest(data)


def build_map(payload: dict, data) -> dict:
    """「游戏资源」分类全部文件: 归一化路径 -> (文件记录, chunk url 前缀, 后缀)。"""
    res = {}
    for item in payload['data']['manifests']:
        if item.get('matching_field') != 'game':
            continue
        chunk_dl = item.get('chunk_download') or {}
        prefix, suffix = chunk_dl.get('url_prefix', ''), chunk_dl.get('url_suffix', '')
        for fe in download_manifest(item):
            if not fe['is_folder'] and fe['file'] and fe['chunks']:
                res[normalize(fe['file'], data)] = (fe, prefix, suffix)
    return res


def normalize(path: str, data: tuple[str, str] | None) -> str:
    p = path.lower()
    if data:
        for d in data:
            if p.startswith(d + '/'):
                return '<DATA>' + p[len(d):]
    return p


def diff_side(mine: dict, other: dict) -> list:
    """mine 中与 other 内容不同（或 other 没有）的文件记录。

    排除 StreamingAssets/Blocks/ 下的内容块（数量几千个、由客户端按版本标记自行修复），
    其余差异文件全部收录（含 StreamingAssets 顶层的散装 blk——它们可能是登录界面等
    区域敏感资源），与原版替换包的收录范围一致。
    """
    res = []
    for key, rec in mine.items():
        if '/streamingassets/blocks/' in rec[0]['file'].lower():
            continue
        o = other.get(key)
        if o is None or (o[0]['md5'].lower(), o[0]['size']) != (rec[0]['md5'].lower(), rec[0]['size']):
            res.append(rec)
    return res


# ============ chunk 下载与组装 ============

def file_matches(path: Path, fe: dict) -> bool:
    """已存在文件是否与清单一致（大小+MD5），用于断点续传跳过。"""
    if not path.is_file() or path.stat().st_size != fe['size']:
        return False
    if not fe['md5']:
        return True
    h = hashlib.md5()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest().lower() == fe['md5'].lower()


def download_chunk(prefix: str, suffix: str, chunk: dict) -> bytes:
    raw = http_get(join_url(prefix, chunk['id'], suffix),
                   expect_size=chunk['compressed_size'] or None,
                   expect_md5=chunk['compressed_md5'] or None)
    data = zstd_decompress(raw)
    if chunk['uncompressed_size'] and len(data) != chunk['uncompressed_size']:
        raise RuntimeError(f'chunk {chunk["id"]} 解压大小不匹配')
    if chunk['uncompressed_md5'] and md5_hex(data).lower() != chunk['uncompressed_md5'].lower():
        raise RuntimeError(f'chunk {chunk["id"]} 解压 MD5 不匹配')
    return data


def download_file(fe: dict, prefix: str, suffix: str, out_dir: Path) -> bool:
    """下载一个文件的全部 chunk、按偏移合并、校验 MD5 后落盘。"""
    if fe['is_folder'] or not fe['chunks']:
        return True
    parts: list[tuple[int, bytes]] = []
    with ThreadPoolExecutor(max_workers=8) as pool:
        futures = {pool.submit(download_chunk, prefix, suffix, c): c for c in fe['chunks']}
        for future in as_completed(futures):
            try:
                parts.append((futures[future]['offset'], future.result()))
            except Exception as exc:  # noqa: BLE001
                log(f'    ✗ chunk 失败 {futures[future]["id"]}: {exc}')
                return False
    parts.sort(key=lambda p: p[0])
    buf = bytearray(fe['size'])
    for offset, data in parts:
        buf[offset:offset + len(data)] = data
    if fe['md5'] and md5_hex(bytes(buf)).lower() != fe['md5'].lower():
        log(f'    ✗ 重组后 MD5 不匹配: {fe["file"]}')
        return False
    out = out_dir / fe['file']
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(bytes(buf))
    return True


# ============ 生成主流程 ============

def generate(game: str, cfg: dict, out_root: Path, side: str, dry: bool, cn_snapshot: bool,
             pkg_repo: Path | None = None) -> bool:
    log(f'===== {cfg["name"]} ({game})')
    cn_map: dict = {}
    gl_map: dict = {}
    cn_tag = gl_tag = '未知'

    # 差异计算需要双方清单 —— 即使 --side 只下载一侧，另一侧清单也必须解析
    if cn_snapshot:
        payload, cn_tag = load_cn_snapshot(game, pkg_repo)
        log(f'CN 使用本地快照 {cn_tag}')
    else:
        creds = get_main_branch(CN_HOST, CN_LAUNCHER, cfg['cn'])
        cn_tag = creds['tag']
        payload = get_build(CN_BUILD, creds)
        log(f'CN 当前版本 {cn_tag}')
    t0 = time.time()
    cn_map = build_map(payload, cfg['data'])
    log(f'CN 游戏资源清单 {len(cn_map)} 文件 (manifest 下载 {time.time() - t0:.0f}s)')

    creds = get_main_branch(SG_HOST, SG_LAUNCHER, cfg['glob'])
    gl_tag = creds['tag']
    payload = get_build(SG_BUILD, creds)
    log(f'国际服 当前版本 {gl_tag}')
    t0 = time.time()
    gl_map = build_map(payload, cfg['data'])
    log(f'国际服 游戏资源清单 {len(gl_map)} 文件 (manifest 下载 {time.time() - t0:.0f}s)')

    targets = []
    if side in ('cn', 'both'):
        targets.append((cn_map, gl_map, cfg['mirror_cn']))
    if side in ('intl', 'both'):
        targets.append((gl_map, cn_map, cfg['mirror_intl']))
    # 差异计算必须基于双方清单：即使只下载一侧，另一侧清单也仅用于对比（不下载文件）

    all_ok = True
    for mine, other, mirror in targets:
        wanted = diff_side(mine, other)
        total = sum(rec[0]['size'] for rec in wanted)
        log(f'[{mirror}] 差异文件 {len(wanted)} 个, 共 {total / 1048576:.0f} MB')
        if dry:
            for rec in sorted(wanted, key=lambda x: -x[0]['size']):
                print(f'    {rec[0]["size"] / 1048576:9.1f} MB  {rec[0]["file"]}')
            continue

        dest = out_root / cfg['dir'] / mirror
        dest.mkdir(parents=True, exist_ok=True)
        wanted_keys = {rec[0]['file'].lower() for rec in wanted}

        def is_protected(rel: str) -> bool:
            """不受清理影响的既有内容（chunk 拿不到、由采集或旧包提供）。"""
            r = rel.lower()
            if any(part == 'persistent' for part in r.split('/')):
                return True  # 「采集 Persistent」收集的热更
            parts = r.split('/')
            # StreamingAssets 顶层的小版本标记（*_revision / *_version 等，驱动客户端自愈 .blk）
            if len(parts) == 3 and parts[-2] == 'streamingassets' and not parts[-1].endswith('.blk'):
                return True
            return False

        # 断点续传：已存在且大小/MD5 与清单一致的文件直接跳过
        todo = []
        skipped = 0
        for rec in wanted:
            fe, prefix, suffix = rec
            if file_matches(dest / fe['file'], fe):
                skipped += 1
            else:
                todo.append(rec)
        todo_bytes = sum(rec[0]['size'] for rec in todo)
        # 清理非本次清单且不受保护的旧文件（旧版本残留）
        for p in [p for p in dest.rglob('*') if p.is_file()
                  and p.relative_to(dest).as_posix().lower() not in wanted_keys
                  and not is_protected(p.relative_to(dest).as_posix())]:
            p.unlink()
        if skipped:
            log(f'[{mirror}] 断点续传：{skipped} 个文件已存在且校验通过，跳过')
        log(f'[{mirror}] 待下载 {len(todo)} 个文件, 共 {todo_bytes / 1048576:.0f} MB')
        if todo:
            ok = fail = 0
            done_bytes = 0
            lock = threading.Lock()

            def run_one(rec):
                nonlocal ok, fail, done_bytes
                fe, prefix, suffix = rec
                good = download_file(fe, prefix, suffix, dest)
                with lock:
                    done_bytes += fe['size']
                    if good:
                        ok += 1
                    else:
                        fail += 1
                    log(f'  [{ok + fail}/{len(todo)}] {done_bytes / 1048576:.0f}/{todo_bytes / 1048576:.0f} MB '
                        f'{"✓" if good else "✗"} {fe["file"]}')
                return good

            # 跨文件并行（每文件内部另有 8 个 chunk 并发），失败文件可重跑本命令自动续传
            with ThreadPoolExecutor(max_workers=3) as pool:
                results = list(pool.map(run_one, sorted(todo, key=lambda x: -x[0]['size'])))
            if all(results):
                log(f'[{mirror}] 完成: {ok} 个文件 (跳过已有 {skipped})')
            else:
                all_ok = False
                log(f'[{mirror}] 完成: {ok} 成功 / {fail} 失败 —— 重跑本命令可自动续传')
        else:
            log(f'[{mirror}] 无需下载，已是最新')

    if not dry and side in ('cn', 'both'):
        ini = out_root / cfg['dir'] / '版本号.ini'
        ini.write_bytes(('[配置]\r\n替换包版本号=' + cn_tag + '\r\n').encode('gbk'))
        log(f'版本号.ini -> {cn_tag}')
    return all_ok


def main() -> int:
    ap = argparse.ArgumentParser(description='生成米家三合一切换器资源替换包（chunk 下载，完全独立）')
    ap.add_argument('--games', default='hk4e,hkrpg,nap')
    ap.add_argument('--out', type=Path, default=None,
                    help='输出根目录（其下按 原神/星穹铁道/绝区零 落盘）；不指定则自动定位切换器安装目录')
    ap.add_argument('--side', choices=['cn', 'intl', 'both'], default='both')
    ap.add_argument('--dry-run', action='store_true', help='只打印差异清单，不下载')
    ap.add_argument('--cn-snapshot', action='store_true', help='CN 使用本地历史快照免凭据')
    ap.add_argument('--pkg-repo', type=Path, default=None,
                    help='本地历史快照仓库路径（--cn-snapshot 时必填）')
    args = ap.parse_args()

    games = [g.strip() for g in args.games.split(',') if g.strip() in GAMES]
    if not games:
        print('没有有效的游戏 ID'); return 2

    # 输出位置解析：
    #   1) --out 显式指定 -> <out>\<游戏>
    #   2) 自动定位切换器安装目录（脚本位置 / 注册表卸载信息），
    #      优先使用 config.json 里各游戏实际配置的替换包路径（含自定义位置），
    #      config.json 不存在（切换器未运行过）则用 <安装目录>\资源替换包\<游戏>
    app_dir: Path | None = None
    pack_paths: dict = {}
    if args.out is None:
        app_dir = find_app_dir()
        if not app_dir:
            print('未找到切换器安装目录（也未指定 --out）。请先安装切换器，或用 --out 指定输出目录。')
            return 2
        pack_paths = load_config_pack_paths(app_dir)
        log(f'切换器安装目录: {app_dir}')
        if not pack_paths:
            log('未读到 config.json 的替换包配置（切换器可能未运行过），使用默认位置')

    ok = True
    for g in games:
        cfg = dict(GAMES[g])
        if args.out is not None:
            out_root = args.out
        else:
            pp = pack_paths.get(g)
            if pp:
                out_root, cfg['dir'] = Path(pp).parent, Path(pp).name
            else:
                out_root = app_dir / '资源替换包'
            log(f'{cfg["name"]} 输出位置: {out_root / cfg["dir"]}')
        try:
            ok &= generate(g, cfg, out_root, args.side, args.dry_run, args.cn_snapshot, args.pkg_repo)
        except Exception as exc:  # noqa: BLE001
            ok = False
            log(f'✗ {g} 失败: {exc}')
    log('全部完成' if ok else '存在失败，请查看上方日志')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
