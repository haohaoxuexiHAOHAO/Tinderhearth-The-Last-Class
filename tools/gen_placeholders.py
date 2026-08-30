#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""生成占位素材并维护素材槽位登记表（`ART-4`）。

作者的像素资源要一张一张画，不可能一次给全。所以界面先用占位件跑通逻辑，最终按槽位替换。
这条流程有两个已知的坑，本入口正是为了堵它们：

1. **占位件会悄悄变成成品。** 所以它们长得一眼假（洋红描边 + 左上角标），且**尺寸、帧数与
   锚点就是最终规格** —— 替换时不必改任何代码。
2. **来源与授权会被忘掉。** 所以每个槽位都进登记表，记路径、尺寸、帧数、来源、授权与是否
   待替换。`ENG-10` 用它比对尺寸与槽位，`ENG-12` 用它拦住占位件进发行包。

三条刻意的做法：

- **不引 Pillow。** 本机没有，而为了几个纯色方块加一个依赖不值。PNG 用 zlib 手写，约 40 行。
- **输出逐字节确定。** 固定压缩级别、不写时间戳；重跑不产生 diff，否则每次跑都污染提交。
- **每个占位件都带 1px 细节。** 一是[像素绘制原则](../../Tinderhearth-The-Last-Class-Docs/production/像素绘制原则.md)
  要求描边，二是纯色大块会被 `ENG-10` 的「每个 2×2 块同色＝放大来的」误判。

用法（从代码仓根目录运行）：
    python tools/gen_placeholders.py            # 生成 + 写登记表
    python tools/gen_placeholders.py --check    # 只核对现有文件与登记表是否一致，不写
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
import zlib
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "assets" / "placeholder"
# 登记表放 tools/ 而不是 assets/：tools/ 有 .gdignore，登记表因此进不了发行包。
# 它是开发期元数据，被打进 .pck 只会泄漏本机信息（同 `ENG-3` 那条泄漏教训）。
REGISTRY = ROOT / "tools" / "asset-registry.json"

# 占位配色。**一眼假是设计目标**，不是随手挑的：洋红在废土低饱和暗色盘里绝不会出现。
MAGENTA = (255, 0, 255, 255)
INK = (26, 21, 18, 255)
GREY = (110, 100, 92, 255)
LIGHT = (200, 190, 172, 255)
CLEAR = (0, 0, 0, 0)

_LINES: list[str] = []
_FAILS: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


def fail(text: str) -> None:
    say(f"[FAIL] {text}")
    _FAILS.append(text)


# ── PNG 写出（只用标准库）──────────────────────────────────────────────
def write_png(path: Path, w: int, h: int, pixels: list[list[tuple]]) -> None:
    """RGBA8 无过滤 PNG。逐字节确定：固定压缩级别，不写任何时间戳块。"""
    raw = bytearray()
    for y in range(h):
        raw.append(0)                       # 每行的过滤器类型：None
        for x in range(w):
            raw.extend(pixels[y][x])

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def canvas(w: int, h: int, fill=CLEAR) -> list[list[tuple]]:
    return [[fill for _ in range(w)] for _ in range(h)]


def rect(px, x0: int, y0: int, x1: int, y1: int, color) -> None:
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if 0 <= y < len(px) and 0 <= x < len(px[0]):
                px[y][x] = color


def outline(px, x0: int, y0: int, x1: int, y1: int, color) -> None:
    for x in range(x0, x1 + 1):
        px[y0][x] = color
        px[y1][x] = color
    for y in range(y0, y1 + 1):
        px[y][x0] = color
        px[y][x1] = color


def corner_tag(px) -> None:
    """左上角两像素的洋红角标 —— 缩到实机尺寸也认得出这是占位件。"""
    px[0][0] = MAGENTA
    px[1][0] = MAGENTA
    px[0][1] = MAGENTA


# ── 占位件 ────────────────────────────────────────────────────────────
def make_panel(w: int, h: int) -> list[list[tuple]]:
    """9-slice 面板底：1px 洋红外框 + 1px 内亮线，中间可拉伸。"""
    px = canvas(w, h, GREY)
    outline(px, 0, 0, w - 1, h - 1, MAGENTA)
    outline(px, 1, 1, w - 2, h - 2, INK)
    corner_tag(px)
    return px


def make_bar(w: int, h: int, filled: bool) -> list[list[tuple]]:
    px = canvas(w, h, INK if not filled else LIGHT)
    outline(px, 0, 0, w - 1, h - 1, MAGENTA if not filled else INK)
    return px


def make_slot(n: int) -> list[list[tuple]]:
    """技能位空框 16×16：外框 + 右下角 n 个 1px 点，用来分辨是第几个位。"""
    px = canvas(16, 16, CLEAR)
    outline(px, 0, 0, 15, 15, MAGENTA)
    for i in range(n):
        px[14][14 - i * 2] = LIGHT
    return px


def make_icon(n: int) -> list[list[tuple]]:
    """技能图标占位 16×16：n 条 1px 斜线，六个位一眼分得开。"""
    px = canvas(16, 16, CLEAR)
    rect(px, 2, 2, 13, 13, GREY)
    outline(px, 2, 2, 13, 13, MAGENTA)
    for i in range(n):
        for k in range(3, 13):
            y = 3 + ((k + i * 2) % 10)
            px[y][k] = LIGHT
    corner_tag(px)
    return px


def make_silhouette(kind: str) -> list[list[tuple]]:
    """32×32 角色占位：只给剪影与 1px 洋红描边。

    刻意不画脸与配件 —— 正典的 32px 铁律说那些在这个尺寸下本来就糊掉，占位件假装有
    只会误导对可读性的判断。它在这里的用途是量「HUD 有没有挡住人」。
    """
    px = canvas(32, 32, CLEAR)
    wide = kind == "enemy"
    body_x0, body_x1 = (8, 23) if wide else (11, 20)
    rect(px, body_x0, 12, body_x1, 29, INK)             # 躯干与腿
    rect(px, 12, 4, 19, 11, INK)                        # 头
    if kind == "hero":
        rect(px, 20, 13, 22, 22, INK)                   # 披风侧影，给个不对称
    if kind == "ally":
        rect(px, 9, 14, 10, 20, INK)
    outline(px, body_x0, 12, body_x1, 29, MAGENTA)
    outline(px, 12, 4, 19, 11, MAGENTA)
    corner_tag(px)
    return px


def make_tile(kind: str) -> list[list[tuple]]:
    """16×16 地形图块：区分地面、平台、墙，靠 1px 纹理线而不是纯色。"""
    px = canvas(16, 16, GREY if kind != "platform" else CLEAR)
    if kind == "ground":
        rect(px, 0, 0, 15, 2, LIGHT)
        for x in range(0, 16, 3):
            px[5][x] = INK
    elif kind == "platform":
        rect(px, 0, 0, 15, 3, GREY)
        rect(px, 0, 0, 15, 0, LIGHT)
        for x in range(1, 16, 4):
            px[3][x] = INK
    else:
        for y in range(0, 16, 4):
            rect(px, 0, y, 15, y, INK)
    px[15][15] = MAGENTA
    return px


def make_ring() -> list[list[tuple]]:
    """读条圆环底 16×16。进度那一圈由代码画，这里只给底与刻度。"""
    px = canvas(16, 16, CLEAR)
    for i in range(16):
        for j in range(16):
            d = (i - 7.5) ** 2 + (j - 7.5) ** 2
            if 36 <= d <= 56:
                px[i][j] = INK
    px[0][7] = MAGENTA
    px[15][7] = MAGENTA
    return px


# 槽位登记表。**尺寸与帧数就是最终规格**，作者替换时按这张表对。
SLOTS: list[dict] = [
    {"path": "ui/panel.png", "用途": "9-slice 面板底（对话框、手环、弹窗共用）",
     "宽": 24, "高": 24, "帧数": 1, "make": lambda: make_panel(24, 24)},
    {"path": "ui/bar-track.png", "用途": "资源条底，可横向拉伸",
     "宽": 4, "高": 6, "帧数": 1, "make": lambda: make_bar(4, 6, False)},
    {"path": "ui/bar-fill.png", "用途": "资源条填充，可横向拉伸",
     "宽": 4, "高": 6, "帧数": 1, "make": lambda: make_bar(4, 6, True)},
    {"path": "ui/objective.png", "用途": "目标进度图标",
     "宽": 16, "高": 16, "帧数": 1, "make": lambda: make_icon(1)},
    {"path": "ui/portrait-frame.png", "用途": "队友头像框",
     "宽": 20, "高": 20, "帧数": 1, "make": lambda: make_panel(20, 20)},
    {"path": "ui/cast-ring.png", "用途": "读条圆环底（世界空间，画在执行者身上）",
     "宽": 16, "高": 16, "帧数": 1, "make": make_ring},
    {"path": "chars/hero.png", "用途": "主角占位剪影（量 HUD 遮挡用）",
     "宽": 32, "高": 32, "帧数": 1, "make": lambda: make_silhouette("hero")},
    {"path": "chars/ally.png", "用途": "学员占位剪影",
     "宽": 32, "高": 32, "帧数": 1, "make": lambda: make_silhouette("ally")},
    {"path": "chars/enemy.png", "用途": "敌人占位剪影",
     "宽": 32, "高": 32, "帧数": 1, "make": lambda: make_silhouette("enemy")},
    {"path": "tiles/ground.png", "用途": "侧视地面图块",
     "宽": 16, "高": 16, "帧数": 1, "make": lambda: make_tile("ground")},
    {"path": "tiles/platform.png", "用途": "侧视单向平台图块",
     "宽": 16, "高": 16, "帧数": 1, "make": lambda: make_tile("platform")},
    {"path": "tiles/wall.png", "用途": "侧视墙体图块",
     "宽": 16, "高": 16, "帧数": 1, "make": lambda: make_tile("wall")},
]

for _i in range(1, 7):
    SLOTS.append({"path": f"ui/skill-slot-{_i}.png", "用途": f"第 {_i} 个技能位空框",
                  "宽": 16, "高": 16, "帧数": 1,
                  "make": (lambda n: lambda: make_slot(n))(_i)})
    SLOTS.append({"path": f"ui/skill-icon-{_i}.png", "用途": f"第 {_i} 个技能图标占位",
                  "宽": 16, "高": 16, "帧数": 1,
                  "make": (lambda n: lambda: make_icon(n))(_i)})


def png_size(path: Path) -> tuple[int, int] | None:
    """只读 IHDR 取尺寸，不解像素。"""
    data = path.read_bytes()
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return struct.unpack(">II", data[16:24])


# 每张纹理的导入参数必须是这三项。**它们的失效方式都是静默的** ——
# 画面只是「有点花」或「有点糊」，不报错。项目级默认写在 project.godot 的
# [importer_defaults]，这里核的是每个 .import 里实际落下来的值。
REQUIRED_IMPORT_PARAMS = {
    "compress/mode": "0",                   # 无损。VRAM 压缩会毁掉像素图
    "mipmaps/generate": "false",            # 缩小级别对像素风没有意义
    "detect_3d/compress_to": "0",           # 关掉「用在 3D 就转 VRAM 压缩」
}


def check_import_params(entries: list[dict]) -> None:
    """核对每张纹理的 .import 参数。没有 .import 就说明还没导入过，不算通过。"""
    missing = 0
    for entry in entries:
        imp = ROOT / "assets" / (entry["path"] + ".import")
        if not imp.is_file():
            missing += 1
            continue
        text = imp.read_text(encoding="utf-8")
        for key, want in REQUIRED_IMPORT_PARAMS.items():
            if f"{key}={want}" not in text:
                got = next((ln for ln in text.splitlines() if ln.startswith(f"{key}=")),
                           "（这一项根本不在）")
                fail(f"{entry['path']}：导入参数 {key} 应为 {want}，实际是 {got}")
    if missing:
        say(f"[..]   {missing} 个槽位还没有 .import（未导入过）—— "
            f"用 Godot 打开工程或跑 --headless --import 后再核")


def main() -> int:
    ap = argparse.ArgumentParser(description="生成占位素材与槽位登记表（ART-4）")
    ap.add_argument("--check", action="store_true", help="只核对，不写文件")
    args = ap.parse_args()

    say(f"槽位 {len(SLOTS)} 个｜落点 {OUT_DIR.relative_to(ROOT)}｜登记表 {REGISTRY.relative_to(ROOT)}")

    entries = []
    written = 0
    for slot in SLOTS:
        out = OUT_DIR / slot["path"]
        if not args.check:
            px = slot["make"]()
            if len(px) != slot["高"] or len(px[0]) != slot["宽"]:
                fail(f"{slot['path']}：生成尺寸 {len(px[0])}x{len(px)} 与登记 "
                     f"{slot['宽']}x{slot['高']} 不符")
                continue
            before = out.read_bytes() if out.is_file() else None
            write_png(out, slot["宽"], slot["高"], px)
            if before != out.read_bytes():
                written += 1

        if not out.is_file():
            fail(f"{slot['path']}：文件不存在")
            continue
        got = png_size(out)
        if got != (slot["宽"], slot["高"]):
            fail(f"{slot['path']}：文件尺寸 {got} 与登记 {(slot['宽'], slot['高'])} 不符")
            continue

        entries.append({
            "path": f"placeholder/{slot['path']}",
            "用途": slot["用途"],
            "宽": slot["宽"],
            "高": slot["高"],
            "帧数": slot["帧数"],
            "来源": "tools/gen_placeholders.py 生成",
            "授权": "本项目自有（脚本产出的纯色几何件）",
            "待替换": True,
            "可进发行包": False,
        })

    if not args.check:
        REGISTRY.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "说明": "素材槽位登记表（ART-4）。尺寸与帧数就是最终规格，替换时按这张表对。"
                    "「可进发行包」为 false 的条目由 ENG-12 的导出守卫拦住。",
            "生成入口": "python tools/gen_placeholders.py",
            "槽位": entries,
        }
        REGISTRY.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8", newline="\n")

    check_import_params(entries)

    # 登记表与实际文件的双向比对：多出来的文件同样要报，否则漏登记等于绕过守卫。
    on_disk = {f"placeholder/{p.relative_to(OUT_DIR).as_posix()}"
               for p in OUT_DIR.rglob("*.png")} if OUT_DIR.is_dir() else set()
    registered = {e["path"] for e in entries}
    for extra in sorted(on_disk - registered):
        fail(f"{extra}：文件在但登记表里没有")

    say(f"\n覆盖量：登记 {len(SLOTS)} 个槽位，核过 {len(entries)} 个，"
        f"本次内容有变化的 {written} 个；磁盘上 {len(on_disk)} 个 .png")
    if _FAILS:
        say(f"[FAIL] 共 {len(_FAILS)} 条不成立")
    else:
        say("[OK] 槽位、尺寸与登记表三者一致")
    print(f"EXIT={1 if _FAILS else 0}")
    return 1 if _FAILS else 0


if __name__ == "__main__":
    raise SystemExit(main())
