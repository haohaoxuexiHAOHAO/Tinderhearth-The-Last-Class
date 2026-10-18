#!/usr/bin/env python3
"""双网格素材检查。

为什么要这个：双网格的 16 张是可互换的 —— 哪两张挨在一起取决于玩家在地里怎么刷，画的时候
定不了。于是「同一对角的那条边必须完全一样」是硬约束，违反它的表现是接缝处界线错开几像素。
**这件事在 Godot 里只看得出「不好看」，看不出是哪一张的哪条边偏了几像素**，所以要机器判。

判这几件事：
  一、规格：64x64、alpha 全不透明、不是放大件
  二、四角：每格四个角必须是纯地形色（角代表数据格中心），全库只许出现两种，且与声明的同色
  三、映射：每格四角的实际组合要对上 src/World/Terrain/DualGridPainter.cs 里那张表
           —— 表从 C# 里解析，不在本文件里再抄一份
  四、纯色：四角全同的那两张必须整张一种颜色
  五、外圈：每格最外一圈要与规范边逐像素相同

规范边只在本文件 TERRAINS、SETS 与 build_strips() 一处定义；`--emit-ring` 按同一定义写出可直接
贴进 Aseprite 的外圈模板，所以「检查用的」与「画画用的」不会飘。**模板只从映射表与声明算、
不读素材**：换色或新加一套的时候，素材要么还是旧色、要么根本还没有，所以顺序是先改声明、
出模板、再照着画 —— 要是模板得先从一张合格的素材里量，这个顺序就走不通。

报告里的颜色一律写色板项名：素材是照着 palette.gpl 画的，对照的也该是项名，而不是一串 RGB。
色板里没有的颜色报出离它最近的那一项与距离。

用法（从代码仓根目录跑）：
    python tools/check_dualgrid.py
    python tools/check_dualgrid.py --emit-ring <输出目录>

本脚本没有自证入口。它是 verify.py 的一步。
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

from palettelib import nearest, pick as palette_pick, read_palette

ROOT = Path(__file__).resolve().parent.parent
TILES = ROOT / "assets" / "self-drawn" / "tiles"
PAINTER = ROOT / "src" / "World" / "Terrain" / "DualGridPainter.cs"
TILE = 16
SIDE = 4                      # 4x4 共 16 张
HALF = TILE // 2
EDGES = ("上边", "右边", "下边", "左边")

# 色板的读取与度量在 palettelib，**本脚本只引项名、不抄数字也不自己解析**。
# 原先这一段自己读 `.gpl`，而加色带时要问的那两个问题（取不取得到、撞不撞既有的）
# 要读同一份表 —— 两份解析迟早漂移，而漂移不报错，表现只是两个工具对同一个色板
# 给出不同答案。那条「不抄数字」的理由连同它撞过的那一次都在 palettelib 里。
PALETTE_ENTRIES = read_palette()


def pick(entry: str) -> tuple[int, int, int]:
    return palette_pick(PALETTE_ENTRIES, entry)


def name_of(rgb: tuple[int, int, int]) -> str:
    """报告里怎么称呼一个颜色：色板里有就写项名，没有就写 RGB 加最近的那一项与距离。"""
    for entry, value in PALETTE_ENTRIES.items():
        if value == rgb:
            return entry
    entry, _, dist = nearest(rgb, PALETTE_ENTRIES)
    return f"{rgb}（不在色板上，最近 {entry}，差 {dist:.0f}）"


def runs(strip: list) -> str:
    """把一条边写成「moss 3×7 + moss 2 + …」，好跟绘图软件里的色板对着看。"""
    parts: list[str] = []
    i = 0
    while i < len(strip):
        j = i
        while j < len(strip) and strip[j] == strip[i]:
            j += 1
        parts.append(name_of(strip[i]) + (f"×{j - i}" if j - i > 1 else ""))
        i = j
    return " + ".join(parts)


# 地形名到它在色板里是哪一项。**跨套共用的地形在这里只出现一次**，这是「同一种地形在两套素材里
# 必须同色」那条判据的全部实现 —— 一格四角只有一种地形时，它落给第一个认得它的那一对，所以两套
# 画得不同色会让同一片纯水出现两种蓝，而引擎不报错。
#
# 这里只说「哪种地形用色板的哪一档」，不说那一档是什么颜色 —— 后者在 `.gpl` 里。两件事，两个家。
# 取哪一档受设计仓 production/像素绘制原则.md 的「大面材质的主色取色带靠暗那一半」约束：
# 地面铺满一屏，主色不取色带最亮的那两档，亮端留给受光面与高光。
TERRAINS = {
    "草": pick("moss 3"),
    "裸土": pick("soil 4"),
    "浅水": pick("water 4"),
    "深水": pick("water 2"),
}

# 每套素材是哪一对地形，外加两侧各自的过渡像素（这是美术选择，必须声明）。
#
# `shade_a` 与 `shade_b` 各是一串色板项，**按「从外侧往界线方向」排**，归哪一侧按它是谁的影判：
# 草土、草水两套里深绿是草自己的阴影（草侧），棕与蓝是投在对侧地面上的影（对侧）。
# 空列表是合法的：那一侧就从纯色直接接到界线 —— 深浅水那一套的浅水侧就是这样，过渡只在深水侧。
#
# `--emit-ring` 出的模板里，`a` 那种地形记作编码 0。
SETS = {
    "grass-dirt.png": {
        "a": "草", "b": "裸土",
        "shade_a": [pick("moss 2")], "shade_b": [pick("soil 3")],
    },
    "grass-shallow.png": {
        "a": "草", "b": "浅水",
        "shade_a": [pick("moss 2")], "shade_b": [pick("water 3")],
    },
    "shallow-deep.png": {
        "a": "浅水", "b": "深水",
        "shade_a": [], "shade_b": [pick("water 3")],
    },
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
    sa, sb = list(shade["a"]), list(shade["b"])
    # 每一侧「纯色 + 自己那几格过渡」正好占半条边，于是界线落在正中。过渡都贴着界线放：
    # 前一侧的排在它那半的末尾，后一侧的排在它那半的开头。
    mixed_ab = ([a] * (HALF - len(sa)) + sa
                + sb + [b] * (HALF - len(sb)))
    mixed_ba = ([b] * (HALF - len(sb)) + sb[::-1]
                + sa[::-1] + [a] * (HALF - len(sa)))
    out = {}
    for axis in ("竖", "横"):
        out[(axis, (0, 0))] = [a] * TILE
        out[(axis, (1, 1))] = [b] * TILE
        out[(axis, (0, 1))] = list(mixed_ab)
        out[(axis, (1, 0))] = list(mixed_ba)
    return out


def check_set(name: str, table: list[tuple[int, int]]) -> None:
    path = TILES / name
    print(f"\n########## {name} ##########")
    if not path.exists():
        fail(f"{path.relative_to(ROOT)} 不存在（新加的一套可以先用 --emit-ring 出外圈模板再画）")
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
        fail(f"四角只该出现 2 种纯地形色，实测 {len(seen)} 种："
             + "、".join(name_of(c) for c in sorted(seen)))
        return
    ok("四角只用了 2 种纯地形色：" + "、".join(f"{name_of(c)} {n} 次" for c, n in seen.items()))

    cfg = SETS[name]
    declared = {TERRAINS[cfg["a"]], TERRAINS[cfg["b"]]}
    said = (f"{cfg['a']}＝{name_of(TERRAINS[cfg['a']])}、"
            f"{cfg['b']}＝{name_of(TERRAINS[cfg['b']])}")
    if set(seen) != declared:
        fail("四角用的色与声明的不符：实测 " + "、".join(name_of(c) for c in sorted(seen))
             + f"，声明的是 {said}。同一种地形在各套素材里必须同色，否则纯色格会出现两种颜色；"
             "有意换色就先改本文件的 TERRAINS，共用这种地形的每一套都要跟着重画")
        return
    ok(f"四角用的色与声明一致（{said}）")
    # 实测的两种色各是哪种地形。A／B 谁是谁要等下面比完映射表才知道，报告里的地形名从这里取。
    terrain_of = {TERRAINS[cfg["a"]]: cfg["a"], TERRAINS[cfg["b"]]: cfg["b"]}

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
        misplaced = sum(1 for code, pos in enumerate(table) if m[pos] != code)
        candidates.append((misplaced, a_rgb, b_rgb, m, bijective))

    if not any(c[4] for c in candidates):
        fail(f"16 种角组合该各出现一次，实测 {sorted(candidates[0][3].values())}")
        return
    ok("16 种角组合恰好各一次")

    candidates.sort(key=lambda c: c[0])
    misplaced, a, b, measured, _ = candidates[0]
    if misplaced:
        for code, pos in enumerate(table):
            if measured[pos] != code:
                print(f"           图集格 {pos}：C# 表要 {code:04b}，"
                      f"实测 {measured[pos]:04b}")
        fail(f"两种 A/B 标法都对不上 DualGridPainter.cs 的映射表"
             f"（较优的一种仍错位 {misplaced} 处）")
    else:
        ok("与 DualGridPainter.cs 的映射表逐格一致")
    notes.append(f"{name}：A（编码里的 0）＝{terrain_of[a]}（{name_of(a)}）"
                 f"　B（编码里的 1）＝{terrain_of[b]}（{name_of(b)}）"
                 " —— 场景里这一对的 TerrainA 要填 A 那种地形")

    # 阴影色跟着 A／B 哪一侧走，而 A／B 是上面比对出来的，所以这里按实测的 a 判方向。
    flipped = a != TERRAINS[cfg["a"]]
    shade = ({"a": cfg["shade_b"], "b": cfg["shade_a"]} if flipped
             else {"a": cfg["shade_a"], "b": cfg["shade_b"]})
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

    # 「四角同色」那两张会被盖在每一格开阔地面上，所以它们必须是整条纯色。
    #
    # 两个后果各自都够：**铺开会显出方格纸** —— 带几个细节像素的话，整片水或整片土的每一格
    # 都在同一位置有同样几个点；**跨套会不一致** —— 同一种地形出现在两套里时，纯色格落给
    # 第一个认得它的那一对，两套画得不同就是同一片地两种长相。
    #
    # 细节不是不许画，是不许画在这一张上：开阔地面的高光与碎石归装饰层（多个备选图块随机刷，
    # 顺带把重复感压掉），交界那几张的收边与阴影照旧画在图里 —— 覆盖层按整格贴，
    # 贴不出「只在水那一侧」。判据见设计仓 production/场景绘制约定.md 的「俯视布景」一节。
    for code, base in ((0, a), (15, b)):
        pos = table[code]
        label = terrain_of[base]
        colors = {tiles[pos].getpixel((x, y))
                  for y in range(TILE) for x in range(TILE)}
        extra = sorted(colors - {base})
        if extra:
            fail(f"四角全是{label}那一张（图集 {pos}）不是纯色，混了 {len(extra)} 种别的颜色"
                 f"（{'、'.join(name_of(c) for c in extra)}）。"
                 "开阔地面每一格都用它，带细节会按格重复成方格纸，"
                 "而同一种地形出现在两套里时还会两边长得不一样")
        else:
            ok(f"四角全是{label}那一张是整条纯色")

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
            print(f"  [FAIL] {tag}：{len(wrong)}/{len(members)} 条与规范边不同"
                  f"（规范边是 {runs(want_strip)}）")
            for pos, edge, n in wrong:
                print(f"           第 {pos[1] * SIDE + pos[0] + 1} 格 {edge}"
                      f" 差 {n} 个像素")
        else:
            ok(f"{tag}：{len(members)} 条全部与规范边一致")
    if off:
        fail(f"外圈共 {sum(n for _, _, n in off)} 个像素与规范边不同"
             f"（涉及 {len(off)} 条边）")

def emit_ring(name: str, table: list[tuple[int, int]], emit_dir: Path) -> None:
    """按映射表与声明写出这一套的外圈模板，不读素材。

    外圈只由「每格四角是哪种组合」与规范边决定：前者是映射表，后者是声明，两样都用不着一张
    画好的图。所以换色时可以先改声明再出模板，新加一套时素材还没有也出得来。
    """
    cfg = SETS[name]
    a, b = TERRAINS[cfg["a"]], TERRAINS[cfg["b"]]
    strips = build_strips((a, b), {"a": cfg["shade_a"], "b": cfg["shade_b"]})
    size = (TILE * SIDE, TILE * SIDE)
    ring = Image.new("RGBA", size, (0, 0, 0, 0))
    for code, pos in enumerate(table):
        tl, tr, bl, br = code >> 3 & 1, code >> 2 & 1, code >> 1 & 1, code & 1
        pairs = {"上边": (tl, tr), "下边": (bl, br),
                 "左边": (tl, bl), "右边": (tr, br)}
        ox, oy = pos[0] * TILE, pos[1] * TILE
        for edge in EDGES:
            axis = "竖" if edge in ("左边", "右边") else "横"
            s = strips[(axis, pairs[edge])]
            for i, (x, y) in enumerate(coords(edge)):
                ring.putpixel((ox + x, oy + y), s[i] + (255,))
    emit_dir.mkdir(parents=True, exist_ok=True)
    stem = name.removesuffix(".png")
    ring.save(emit_dir / f"{stem}-ring.png")
    ring.resize((size[0] * 8, size[1] * 8), Image.Resampling.NEAREST).save(
        emit_dir / f"{stem}-ring-x8.png")
    notes.append(f"{name}：已按声明写出 {stem}-ring.png 与 -x8 预览"
                 f"（{cfg['a']} {name_of(a)}、{cfg['b']} {name_of(b)}）")


def main() -> int:
    ap = argparse.ArgumentParser(description="双网格素材检查")
    ap.add_argument("--emit-ring", metavar="目录",
                    help="按映射表与声明写出可贴进 Aseprite 的外圈模板（不读素材，画之前就能出）")
    args = ap.parse_args()
    emit = Path(args.emit_ring).resolve() if args.emit_ring else None

    table = code_to_atlas()
    if table is None:
        # 映射、纯色、外圈与模板都按这张表找格子，没有它往下判只会判错位置。
        print("\n覆盖量：映射表读不出来，一套素材都没判")
        print(f"结果：{len(fails)} 项必须修复")
        print("EXIT=1")
        return 1
    for name in SETS:
        check_set(name, table)
        if emit is not None:
            emit_ring(name, table, emit)

    print()
    for n in notes:
        print(f"覆盖量：{n}")
    print(f"覆盖量：检查 {len(SETS)} 套素材、每套 16 张、每张 4 条边")
    if fails:
        print(f"结果：{len(fails)} 项必须修复")
        print("EXIT=1")
        return 1
    print("结果：0 项必须修复")
    print(f"[OK] {len(SETS)} 套素材的外圈都与规范边一致")
    print("EXIT=0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
