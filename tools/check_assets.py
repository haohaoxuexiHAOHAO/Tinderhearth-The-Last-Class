#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""素材守卫：假像素、错误缩放与槽位登记比对（`ENG-10`）。

只查**机器能判死**的四类，一条阈值类都不做：

1. **半透明像素**（alpha ∉ {0, 255}）。[像素绘制原则 §9] 把「透明度只使用完全透明或完全
   不透明」定为绝对规则，但一直没有执行体。
2. **整图放大件**（全图每个 2×2 块同色）。16×16 放大到 32×32 不增加任何信息量，却会骗过
   「尺寸对得上」这类检查。
3. **登记表与磁盘不一致**（多、少、尺寸不符、帧数除不尽）。
4. **纹理导入参数**（无损压缩、不生成 mipmap、关掉 detect_3d 转 VRAM 压缩）。这三项的失效
   方式都是静默的 —— 画面只是「有点花」或「有点糊」，不报错。

**阈值类明确不做**：亮部比例、对比、色数、孤点。理由不是懒，而是[像素绘制原则 §11] 自己
写的那条 —— 那类警告永远需要人回到目标背景与题材去解释一次，把它和能判死的检查混在一个
工具里，代价是「你会学着忽略它的输出，连真失败一起忽略」。

为什么现在就要有它：这类缺陷**静默累积**。第 1 张素材时修是免费的，两百张之后就是一次
审计。`UI-1` 的 HUD 样板是第一批素材进仓的地方，所以时机是现在。

用法（从代码仓根目录运行）：
    python tools/check_assets.py            # 全查
    python tools/check_assets.py --list     # 只列登记表内容，不判定

依赖 Pillow（`tools/requirements.txt`）。**缺了必须报错退出，不许跳过检查** —— 手写解码器
只认 RGBA8，遇到调色板或 16 位文件会静默跳过，而本工具的全部价值就是不漏检。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
REGISTRY = ROOT / "tools" / "asset-registry.json"

REQUIRED_IMPORT_PARAMS = {
    "compress/mode": "0",                   # 无损。VRAM 压缩会毁掉像素图
    "mipmaps/generate": "false",            # 缩小级别对像素风没有意义
    "detect_3d/compress_to": "0",           # 关掉「用在 3D 就转 VRAM 压缩」
}

MAX_REPORTED_COORDS = 6                     # 报坐标够定位就行，不刷屏

_LINES: list[str] = []
_FAILS: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


def fail(text: str) -> None:
    say(f"[FAIL] {text}")
    _FAILS.append(text)


def load_image(path: Path):
    """载入为 RGBA。**Pillow 缺失时直接退出**，不降级、不跳过。"""
    try:
        from PIL import Image
    except ModuleNotFoundError:
        print("[FAIL] 缺 Pillow —— 装：python -m pip install -r tools/requirements.txt")
        print("       不许跳过检查：会静默跳过的守卫比没有守卫更坏")
        print("EXIT=1")
        raise SystemExit(1)
    return Image.open(path).convert("RGBA")


def check_alpha(name: str, im) -> None:
    """alpha 只允许 0 或 255。报出前几个坐标，够定位就行。"""
    alpha = im.getchannel("A")
    values = set(alpha.getdata())
    bad = sorted(v for v in values if v not in (0, 255))
    if not bad:
        return
    w = im.width
    coords = [(i % w, i // w) for i, v in enumerate(alpha.getdata()) if v not in (0, 255)]
    shown = "、".join(f"({x},{y})" for x, y in coords[:MAX_REPORTED_COORDS])
    more = f" 等 {len(coords)} 处" if len(coords) > MAX_REPORTED_COORDS else ""
    fail(f"{name}：有半透明像素，alpha 出现 {bad[:8]}；位置 {shown}{more}")


def check_upscaled(name: str, im) -> None:
    """全图每个 2×2 块同色 ⇒ 这张图是放大来的。

    两个前提缺一不可，否则会误判：宽高都是偶数，且图里至少有两种颜色 —— 纯色图天然满足
    「每个 2×2 同色」，那不是放大的证据。占位素材因此一律带 1px 描边。
    """
    w, h = im.size
    if w % 2 or h % 2:
        return
    px = im.load()
    if len({px[x, y] for y in range(h) for x in range(w)}) < 2:
        return
    for y in range(0, h, 2):
        for x in range(0, w, 2):
            c = px[x, y]
            if px[x + 1, y] != c or px[x, y + 1] != c or px[x + 1, y + 1] != c:
                return
    fail(f"{name}：全图每个 2×2 块同色 —— 这张图是放大来的，改用 "
         f"{w // 2}×{h // 2} 的源尺寸")


def check_frames(name: str, im, entry: dict) -> None:
    """尺寸与帧结构。没写帧宽的按单帧算。"""
    w, h = im.size
    if (w, h) != (entry["宽"], entry["高"]):
        fail(f"{name}：文件 {w}×{h} 与登记 {entry['宽']}×{entry['高']} 不符")
        return
    fw = entry.get("帧宽", entry["宽"])
    fh = entry.get("帧高", entry["高"])
    frames = entry.get("帧数", 1)
    if fh != h:
        fail(f"{name}：帧高登记 {fh}，图高 {h} —— 对不上")
    if fw <= 0 or w % fw:
        fail(f"{name}：图宽 {w} 除不尽帧宽 {fw} —— 精灵表切不出整数帧")
        return
    if w // fw != frames:
        fail(f"{name}：按帧宽 {fw} 算是 {w // fw} 帧，登记 {frames} 帧")


def check_fonts(entries: list[dict]) -> int:
    """核字体：文件在、内容与登记的 SHA256 一致、旁边有许可证。

    为什么核内容而不只核存在：字体是二进制，换成另一个版本或另一个字形版本（`zh_hant`／`ja`）
    在 git diff 里只有一行「二进制文件有差异」，而字形覆盖与度量会跟着变 —— 那正是
    [ADR-0008] 把版本钉死的理由。钉住内容才让「审计过」这句话指向一份确定的文件。

    **渲染参数不在这里核。** 那十项设在 `.ttf.import`、期望在 `rules/Ui/PixelFont.cs`、
    实际值由引擎自己报，守卫是 `tools/check_hud.py`。这里再抄一份就是第三份真相。
    """
    if not entries:
        fail("登记表里一条字体都没有 —— 界面文字会退回引擎默认字体，而那不报错")
        return 0

    checked = 0
    for entry in entries:
        rel = entry["path"]
        path = ASSETS / rel
        checked += 1
        if not path.is_file():
            fail(f"{rel}：登记表里有但文件不在（取法见 README「像素字体怎么进来的」）")
            continue

        data = path.read_bytes()
        if len(data) != entry["字节数"]:
            fail(f"{rel}：{len(data)} 字节与登记 {entry['字节数']} 不符")
            continue
        digest = hashlib.sha256(data).hexdigest()
        if digest != entry["sha256"]:
            fail(f"{rel}：sha256 {digest[:16]}… 与登记 {entry['sha256'][:16]}… 不符 —— "
                 f"换了版本或换了字形版本，覆盖与度量都会跟着变；"
                 f"重跑设计仓 python tools/audit_fonts.py 再更新 ADR-0008 与登记表")
            continue

        license_rel = entry["许可证文件"]
        if not (ASSETS / license_rel).is_file():
            fail(f"{license_rel}：不在 —— OFL 第 2 条要求每份拷贝都带许可证与版权声明")
            continue

        say(f"  {rel}｜{len(data)} 字节｜上游 {entry['上游版本']}｜字号 {entry['字号']}"
            f"｜许可证 {license_rel}"
            f"｜{'可进包' if entry.get('可进发行包') else '不得进包'}")
    return checked


def check_import(name: str, rel: str) -> bool:
    """纹理导入参数。没有 .import 说明还没导入过，**不算通过**。"""
    imp = ASSETS / (rel + ".import")
    if not imp.is_file():
        return False
    text = imp.read_text(encoding="utf-8")
    for key, want in REQUIRED_IMPORT_PARAMS.items():
        if f"{key}={want}" not in text:
            got = next((ln for ln in text.splitlines() if ln.startswith(f"{key}=")),
                       "（这一项根本不在）")
            fail(f"{name}：导入参数 {key} 应为 {want}，实际是 {got}")
    return True


def run_checks(list_only: bool = False) -> int:
    if not REGISTRY.is_file():
        fail(f"登记表不在：{REGISTRY.relative_to(ROOT)} —— 先跑 "
             f"python tools/gen_placeholders.py")
        return 1

    data = json.loads(REGISTRY.read_text(encoding="utf-8"))
    generated = data.get("生成槽位", [])
    downloaded = data.get("下载素材", [])
    fonts = data.get("字体", [])
    entries = {e["path"]: e for e in generated + downloaded}
    say(f"登记表：生成槽位 {len(generated)} 条、下载素材 {len(downloaded)} 条、"
        f"字体 {len(fonts)} 条")

    if list_only:
        for e in generated + downloaded:
            say(f"  {e['path']:<44s} {e['宽']}×{e['高']} ×{e.get('帧数', 1)} 帧"
                f"｜{'待替换' if e.get('待替换') else '正式'}"
                f"｜{'可进包' if e.get('可进发行包') else '不得进包'}")
        for e in fonts:
            say(f"  {e['path']:<44s} {e['字节数']} 字节 字号 {e['字号']}"
                f"｜{'待替换' if e.get('待替换') else '正式'}"
                f"｜{'可进包' if e.get('可进发行包') else '不得进包'}")
        return 0

    on_disk = {p.relative_to(ASSETS).as_posix() for p in ASSETS.rglob("*.png")} \
        if ASSETS.is_dir() else set()
    for extra in sorted(on_disk - set(entries)):
        fail(f"{extra}：文件在但登记表里没有 —— 漏登记等于绕过守卫")
    for gone in sorted(set(entries) - on_disk):
        fail(f"{gone}：登记表里有但文件不在")

    scanned = 0
    no_import = []
    for rel in sorted(on_disk & set(entries)):
        im = load_image(ASSETS / rel)
        check_alpha(rel, im)
        check_upscaled(rel, im)
        check_frames(rel, im, entries[rel])
        if not check_import(rel, rel):
            no_import.append(rel)
        scanned += 1

    say("\n字体：")
    font_count = check_fonts(fonts)

    say(f"\n覆盖量：登记 {len(entries)} 条，磁盘 {len(on_disk)} 个 .png，"
        f"实际逐像素扫过 {scanned} 个；每个查了 4 类"
        f"（半透明、放大件、尺寸与帧数、导入参数）；"
        f"另核字体 {font_count} 份（字节数、SHA256、旁边有许可证）")
    if no_import:
        fail(f"{len(no_import)} 个素材没有 .import（还没导入过，导入参数无从核）："
             f"{'、'.join(no_import[:5])}")
    if scanned == 0:
        fail("一个素材都没扫到 —— 空转的检查也会「全绿」，所以这里必须失败")

    if _FAILS:
        say(f"[FAIL] 共 {len(_FAILS)} 条不成立")
        return 1
    say("[OK] 半透明像素 0、放大件 0、登记表与磁盘一致、导入参数全对、字体内容与登记一致")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="素材守卫（ENG-10）")
    ap.add_argument("--list", action="store_true", help="只列登记表内容，不判定")
    args = ap.parse_args()
    code = run_checks(args.list)
    print(f"EXIT={code}")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
