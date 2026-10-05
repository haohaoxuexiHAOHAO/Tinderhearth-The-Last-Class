#!/usr/bin/env python3
"""双网格素材检查。

为什么要这个：双网格的 16 张是可互换的 —— 哪两张挨在一起取决于玩家在地里怎么刷，画的时候
定不了。于是「同一对角的那条边必须完全一样」是硬约束，违反它的表现是接缝处界线错开几像素。
**这件事在 Godot 里只看得出「不好看」，看不出是哪一张的哪条边偏了几像素**，所以要机器判。

判四件事：
  一、规格：64x64、alpha 全不透明、不是放大件
  二、四角：每格四个角必须是纯地形色（角代表数据格中心），全库只许出现两种
  三、映射：每格四角的实际组合要对上 src/World/Terrain/DualGridPainter.cs 里那张表
           —— 表从 C# 里解析，不在本文件里再抄一份
  四、外圈：每格最外一圈要与规范边逐像素相同

规范边只在本文件 SHADES 与 build_strips() 一处定义；`--emit-ring` 按同一定义写出可直接
贴进 Aseprite 的外圈模板，所以「检查用的」与「画画用的」不会飘。

用法（从代码仓根目录跑）：
    python tools/check_dualgrid.py
    python tools/check_dualgrid.py --emit-ring <输出目录>

本脚本没有自证入口，也没有接进 verify.py。
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")

try:
    from PIL import Image
except ImportError:
    print("[FAIL] 需要 Pillow：pip install -r tools/requirements.txt")
    print("EXIT=1")
    raise SystemExit(1)

ROOT = Path(__file__).resolve().parent.parent
TILES = ROOT / "assets" / "self-drawn" / "tiles"
PAINTER = ROOT / "src" / "World" / "Terrain" / "DualGridPainter.cs"
TILE = 16
SIDE = 4                      # 4x4 共 16 张
HALF = TILE // 2
EDGES = ("上边", "右边", "下边", "左边")

# 两侧各自的阴影色。这是美术选择，必须声明 —— 受光的两种地形色从素材的角像素反推，不写死。
SHADES = {
    "grass-dirt.png": {"a": (116, 131, 21), "b": (171, 104, 60)},
    "grass-water.png": {"a": (116, 131, 21), "b": (63, 132, 141)},
}

fails: list[str] = []
notes: list[str] = []


def fail(msg: str) -> None:
    print(f"  [FAIL] {msg}")
    fails.append(msg)


def ok(msg: str) -> None:
    print(f"  [OK]   {msg}")


def code_to_atlas() -> list[tuple[int, int]] | None:
    """从 DualGridPainter.cs 解析四角编码到图集格的映射表。

    表在 C# 里是唯一来源，本脚本只读不抄 —— 抄一份就等于给同一个事实开第二个家，
    而漏改不报错、表现只是边缘拼错。
    """
    if not PAINTER.exists():
        fail(f"找不到 {PAINTER.relative_to(ROOT)}，映射那一关没法判")
        return None
    text = PAINTER.read_text(encoding="utf-8")
    m = re.search(r"CodeToAtlasOffset\s*=\s*\[(.*?)\];", text, re.S)
    if not m:
        fail("在 DualGridPainter.cs 里找不到 CodeToAtlasOffset 的初始化块")
        return None
    pairs = re.findall(r"new\(\s*(\d+)\s*,\s*(\d+)\s*\)", m.group(1))
    table = [(int(x), int(y)) for x, y in pairs]
    if len(table) != 16:
        fail(f"解析出 {len(table)} 项，映射表该有 16 项")
        return None
    return table


def coords(edge: str) -> list[tuple[int, int]]:
    if edge == "上边":
        return [(x, 0) for x in range(TILE)]
    if edge == "下边":
        return [(x, TILE - 1) for x in range(TILE)]
    if edge == "左边":
        return [(0, y) for y in range(TILE)]
    return [(TILE - 1, y) for y in range(TILE)]


def build_strips(lit: tuple, shade: dict) -> dict:
    """规范边。只此一处定义。

    混合边：受光色×7 + 自己那侧的阴影 + 对侧的阴影 + 另一种受光色×7。于是两侧严格 8:8，
    而 8:8 不是随便定的 —— 双网格里图块的四角落在数据格中心上，两个数据格之间的界线
    几何上就在图块边的正中。两端同色的边整条纯色，于是装饰性碎像素不许落在边上。
    """
    a, b = lit
    sa, sb = shade["a"], shade["b"]
    mixed_ab = [a] * (HALF - 1) + [sa, sb] + [b] * (HALF - 1)
    mixed_ba = [b] * (HALF - 1) + [sb, sa] + [a] * (HALF - 1)
    out = {}
    for axis in ("竖", "横"):
        out[(axis, (0, 0))] = [a] * TILE
        out[(axis, (1, 1))] = [b] * TILE
        out[(axis, (0, 1))] = list(mixed_ab)
        out[(axis, (1, 0))] = list(mixed_ba)
    return out


def check_set(name: str, table: list | None, emit_dir: Path | None) -> None:
    path = TILES / name
    print(f"\n########## {name} ##########")
    if not path.exists():
        fail(f"{path.relative_to(ROOT)} 不存在")
        return

    raw = Image.open(path)
    want = (TILE * SIDE, TILE * SIDE)
    if raw.size != want:
        fail(f"尺寸 {raw.size}，要 {want}")
        return
    ok(f"尺寸 {raw.size}")

    if "A" in raw.getbands():
        lo, hi = raw.convert("RGBA").getchannel("A").getextrema()
        if (lo, hi) != (255, 255):
            fail(f"天然地形要铺满，alpha 该全是 255，实测 {lo}..{hi}")
        else:
            ok("alpha 全不透明")

    img = raw.convert("RGB")
    px = img.load()
    if all(px[x * 2, y * 2] == px[x * 2 + 1, y * 2]
           == px[x * 2, y * 2 + 1] == px[x * 2 + 1, y * 2 + 1]
           for y in range(img.height // 2) for x in range(img.width // 2)):
        fail("每个 2x2 块都纯色，说明是 2 倍放大件")
    else:
        ok("不是 2 倍放大件")

    tiles = {}
    for row in range(SIDE):
        for col in range(SIDE):
            tiles[(col, row)] = img.crop(
                (col * TILE, row * TILE, col * TILE + TILE, row * TILE + TILE))

    corner_pts = ((0, 0), (TILE - 1, 0), (0, TILE - 1), (TILE - 1, TILE - 1))
    seen: dict = {}
    for pos, t in tiles.items():
        for p in corner_pts:
            seen[t.getpixel(p)] = seen.get(t.getpixel(p), 0) + 1
    if len(seen) != 2:
        fail(f"四角只该出现 2 种纯地形色，实测 {len(seen)} 种：{sorted(seen)}")
        return
    ok(f"四角只用了 2 种纯地形色，各 {list(seen.values())} 次")

    # 素材本身没有「谁是 A」这回事 —— 那是场景里 DualGridPair.TerrainA/TerrainB 填的。
    # 所以两种标法都试，能对上 C# 那张表的就是这套素材的 A/B，并把结论报出来供填检查器。
    def measure(a_rgb, b_rgb) -> dict:
        out = {}
        for pos, t in tiles.items():
            bits = [1 if t.getpixel(p) == b_rgb else 0 for p in corner_pts]
            out[pos] = (bits[0] << 3) | (bits[1] << 2) | (bits[2] << 1) | bits[3]
        return out

    c1, c2 = sorted(seen)
    candidates = []
    for a_rgb, b_rgb in ((c1, c2), (c2, c1)):
        m = measure(a_rgb, b_rgb)
        bijective = sorted(m.values()) == list(range(16))
        misplaced = (16 if table is None
                     else sum(1 for code, pos in enumerate(table) if m[pos] != code))
        candidates.append((misplaced, a_rgb, b_rgb, m, bijective))

    if not any(c[4] for c in candidates):
        fail(f"16 种角组合该各出现一次，实测 {sorted(candidates[0][3].values())}")
        return
    ok("16 种角组合恰好各一次")

    candidates.sort(key=lambda c: c[0])
    misplaced, a, b, measured, _ = candidates[0]
    if table is not None:
        if misplaced:
            for code, pos in enumerate(table):
                if measured[pos] != code:
                    print(f"           图集格 {pos}：C# 表要 {code:04b}，"
                          f"实测 {measured[pos]:04b}")
            fail(f"两种 A/B 标法都对不上 DualGridPainter.cs 的映射表"
                 f"（较优的一种仍错位 {misplaced} 处）")
        else:
            ok("与 DualGridPainter.cs 的映射表逐格一致")
    notes.append(f"{name}：A（编码里的 0）={a}　B（编码里的 1）={b}"
                 f" —— 场景里这一对的 TerrainA 要填 A 那种地形")

    shade = SHADES.get(name)
    if shade is None:
        fail(f"SHADES 里没有 {name} 的阴影色声明，外圈那一关没法判")
        return
    strips = build_strips((a, b), shade)

    nm = ("A", "B")
    groups: dict = {}
    for pos in tiles:
        c = measured[pos]
        tl, tr, bl, br = c >> 3 & 1, c >> 2 & 1, c >> 1 & 1, c & 1
        pairs = {"上边": (tl, tr), "下边": (bl, br),
                 "左边": (tl, bl), "右边": (tr, br)}
        for edge in EDGES:
            axis = "竖" if edge in ("左边", "右边") else "横"
            strip = [tiles[pos].getpixel(p) for p in coords(edge)]
            groups.setdefault((axis, pairs[edge]), []).append((pos, edge, strip))

    off = []
    for (axis, key), members in sorted(groups.items()):
        want_strip = strips[(axis, key)]
        wrong = [(pos, edge, sum(1 for i in range(TILE)
                                 if s[i] != want_strip[i]))
                 for pos, edge, s in members]
        wrong = [w for w in wrong if w[2]]
        tag = f"{axis}边 {nm[key[0]]}→{nm[key[1]]}"
        if wrong:
            off.extend(wrong)
            print(f"  [FAIL] {tag}：{len(wrong)}/{len(members)} 条与规范边不同")
            for pos, edge, n in wrong:
                print(f"           第 {pos[1] * SIDE + pos[0] + 1} 格 {edge}"
                      f" 差 {n} 个像素")
        else:
            ok(f"{tag}：{len(members)} 条全部与规范边一致")
    if off:
        fail(f"外圈共 {sum(n for _, _, n in off)} 个像素与规范边不同"
             f"（涉及 {len(off)} 条边）")

    if emit_dir is not None:
        ring = Image.new("RGBA", want, (0, 0, 0, 0))
        for pos in tiles:
            c = measured[pos]
            tl, tr, bl, br = c >> 3 & 1, c >> 2 & 1, c >> 1 & 1, c & 1
            pairs = {"上边": (tl, tr), "下边": (bl, br),
                     "左边": (tl, bl), "右边": (tr, br)}
            ox, oy = pos[0] * TILE, pos[1] * TILE
            for edge in EDGES:
                axis = "竖" if edge in ("左边", "右边") else "横"
                s = strips[(axis, pairs[edge])]
                for i, (x, y) in enumerate(coords(edge)):
                    ring.putpixel((ox + x, oy + y), s[i] + (255,))
        emit_dir.mkdir(parents=True, exist_ok=True)
        stem = name.replace(".png", "")
        ring.save(emit_dir / f"{stem}-ring.png")
        ring.resize((want[0] * 8, want[1] * 8), Image.Resampling.NEAREST).save(
            emit_dir / f"{stem}-ring-x8.png")
        notes.append(f"{name}：已写出 {stem}-ring.png 与 -x8 预览")


def main() -> int:
    ap = argparse.ArgumentParser(description="双网格素材检查")
    ap.add_argument("--emit-ring", metavar="目录",
                    help="按同一套规范边写出可贴进 Aseprite 的外圈模板")
    args = ap.parse_args()
    emit = Path(args.emit_ring).resolve() if args.emit_ring else None

    table = code_to_atlas()
    for name in SHADES:
        check_set(name, table, emit)

    print()
    for n in notes:
        print(f"覆盖量：{n}")
    print(f"覆盖量：检查 {len(SHADES)} 套素材、每套 16 张、每张 4 条边")
    if fails:
        print(f"结果：{len(fails)} 项必须修复")
        print("EXIT=1")
        return 1
    print("结果：0 项必须修复")
    print("[OK] 两套素材的外圈都与规范边一致")
    print("EXIT=0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
