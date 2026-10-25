#!/usr/bin/env python3
"""界面素材生成器：把作者画的界面模板切成约定里的件，补齐没画的状态与小件，写进 assets/self-drawn/ui/。

为什么是生成器而不是手切：模板是几张整图（一整张面板、一条 8 格栏、32 个图标按钮挤在一张上），
游戏要的是一件一个文件、九宫格拉得开、几种状态同一张画布。每改一次模板就得重切一遍。

三类件：
  切   —— 从模板原样取，像素不动；九宫格件按边距拼回最小画布。
  派生 —— 同一件的其他状态或配套的那一层，由原件算出来：按色板换档（形状不变），或取线稿围住的格。
  手画 —— 早先画的一批小件，字符格在 ui_art_sprites.py，原样留着由作者审查修改。作者修过的那几张
          照作者那一版誊回了字符格，格子上方注明「作者修过」。**往后不再加新的手画件**：要画的东西
          由作者画，生成器只做拆解与拼接（设计仓 production/界面绘制约定.md「界面件从哪来」）。
  誊   —— 作者画在模板里、却没法原样切的件（图标按钮上的图标与按钮底画在一起），照作者那一版
          逐像素誊成字符格，也在 ui_art_sprites.py。

写盘之前每一张判三件事：alpha 只有 0 与 255、每个不透明像素都是色板里的一档、尺寸与清单一致。
有一张不过就停，不写半套。

模板在工作区根的 art-source/self-material/ui/，不在任何仓库里（见那里的 README）。

谁改过一张图靠像素指纹认：生成器写出的每张 PNG 里记一条按像素算的指纹（见 FINGERPRINT_KEY）。
磁盘上那张的像素与它记的指纹对得上，就是生成器自己写的、没人动过，可以换成新的；没有指纹或对不上，
就是作者手改过，跳过并列出来。要换回生成器那一版，删掉那张再跑。

用法（从代码仓根目录跑）：
    python tools/ui_art.py                   生成、校验、写盘
    python tools/ui_art.py --list            列每一件的尺寸、九宫格边距、来源与用途（不写盘）
    python tools/ui_art.py --preview <png>   另写一张带名字的总览图（放大 3 倍）给人看（界面件不写盘）
    python tools/ui_art.py --grid panel/coin 把磁盘上那张誊成字符格打出来（作者改过的图跟进生成器时用）
    python tools/ui_art.py --dry-run         只生成与校验，报每张会写、认领、不变还是跳过，不写盘

本脚本不是守卫，不进 verify.py：它判的是它自己生成的东西。本脚本没有自证入口。
"""

from __future__ import annotations

import argparse
import hashlib
import re
import sys
from collections import deque
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps
from PIL.PngImagePlugin import PngInfo

import ui_art_sprites as sp
from palettelib import ROOT, luma, pick, read_palette

if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")

TEMPLATES = ROOT.parent / "art-source" / "self-material" / "ui"
OUT = ROOT / "assets" / "self-drawn" / "ui"
PAL = read_palette()
CLEAR = (0, 0, 0, 0)
Pt = tuple[int, int]


# ── 取色、取模板、基本的像素操作 ─────────────────────────────────────────────

def rgba(name: str) -> tuple[int, int, int, int]:
    """按色板项名取色。拼错项名当场停，所以这里写不出色板外的颜色。"""
    return pick(PAL, name) + (255,)


def blank(w: int, h: int) -> Image.Image:
    return Image.new("RGBA", (w, h), CLEAR)


def tpl(name: str) -> Image.Image:
    path = TEMPLATES / name
    if not path.exists():
        raise SystemExit(f"[FAIL] 找不到模板 {path} —— 模板放在工作区根的 art-source/self-material/ui/")
    return Image.open(path).convert("RGBA")


def crop(im: Image.Image, x: int, y: int, w: int, h: int) -> Image.Image:
    return im.crop((x, y, x + w, y + h))


def paint(im: Image.Image, pts, name: str) -> Image.Image:
    color = rgba(name)
    for p in pts:
        im.putpixel(p, color)
    return im


def recolor(im: Image.Image, mapping: dict[str, str]) -> Image.Image:
    """按色板项名换档。映射同时生效（按原图的颜色查），所以 A→B、B→C 不会连锁成 A→C。"""
    table = {rgba(k): rgba(v) for k, v in mapping.items()}
    out = im.copy()
    px = out.load()
    for y in range(out.height):
        for x in range(out.width):
            if px[x, y] in table:
                px[x, y] = table[px[x, y]]
    return out


def flood_clear(im: Image.Image, names: set[str]) -> Image.Image:
    """从四边出发，把与边相连、颜色属于 names 的像素清成透明（4 邻接，透明像素也可穿过）。

    模板里的件是画在面板底色上的，切下来时外面那圈底色要去掉，而件里面同色的部分（被描边围住的）
    要留着 —— 「与边相连」正好分开这两样。
    """
    targets = {rgba(n) for n in names} | {CLEAR}
    out = im.copy()
    px = out.load()
    w, h = out.size
    queue = deque([(x, y) for x in range(w) for y in (0, h - 1)]
                  + [(x, y) for y in range(h) for x in (0, w - 1)])
    seen: set[Pt] = set()
    while queue:
        x, y = queue.popleft()
        if (x, y) in seen or not (0 <= x < w and 0 <= y < h):
            continue
        seen.add((x, y))
        if px[x, y] not in targets:
            continue
        px[x, y] = CLEAR
        queue.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    return out


def diff_layer(a: Image.Image, b: Image.Image) -> Image.Image:
    """a 里与 b 不同的那些像素，其余透明。用来从「满」减「空」得到填充那一层。"""
    out = blank(*a.size)
    pa, pb, po = a.load(), b.load(), out.load()
    for y in range(a.height):
        for x in range(a.width):
            if pa[x, y] != pb[x, y]:
                po[x, y] = pa[x, y]
    return out


def compose(base: Image.Image, top: Image.Image, x: int = 0, y: int = 0) -> Image.Image:
    """把 top 叠到 base 上。alpha 只有 0 与 255，所以叠出来不会有插值色。"""
    out = base.copy()
    out.paste(top, (x, y), top)
    return out


def from_grid(rows: list[str], legend: dict[str, str] | None = None) -> Image.Image:
    """字符格 → 图。行宽不齐、字符不在图例里都当场停。"""
    table = dict(sp.LEGEND)
    table.update(legend or {})
    w = len(rows[0])
    out = blank(w, len(rows))
    for y, row in enumerate(rows):
        if len(row) != w:
            raise SystemExit(f"[FAIL] 字符格第 {y} 行宽 {len(row)}，应为 {w}：{row!r}")
        for x, ch in enumerate(row):
            if ch == ".":
                continue
            if ch not in table:
                raise SystemExit(f"[FAIL] 字符格里有图例没有的字符 {ch!r}（第 {y} 行）")
            out.putpixel((x, y), rgba(table[ch]))
    return out


def outline(im: Image.Image, name: str, diagonal: bool = True) -> Image.Image:
    """给不透明像素外面补一圈描边。只给迷你字用 —— 别的小件描边是手画的。"""
    out = im.copy()
    src = im.load()
    steps = [(1, 0), (-1, 0), (0, 1), (0, -1)]
    if diagonal:
        steps += [(1, 1), (1, -1), (-1, 1), (-1, -1)]
    color = rgba(name)
    for y in range(im.height):
        for x in range(im.width):
            if src[x, y][3]:
                continue
            for dx, dy in steps:
                nx, ny = x + dx, y + dy
                if 0 <= nx < im.width and 0 <= ny < im.height and src[nx, ny][3]:
                    out.putpixel((x, y), color)
                    break
    return out


def to_gray(im: Image.Image) -> Image.Image:
    """把每个不透明像素换成 ash 那条里明度最近的一档 —— 置灰仍在色板里，不靠代码乘色。

    代码那一侧的 modulate 是乘法，乘出来的颜色不在色板里，所以「灰着」要画出来。
    """
    ash = [rgba(f"ash {i}") for i in range(1, 9)]
    out = im.copy()
    px = out.load()
    for y in range(out.height):
        for x in range(out.width):
            if px[x, y][3]:
                lum = luma(px[x, y][:3])
                px[x, y] = min(ash, key=lambda c: abs(luma(c[:3]) - lum))
    return out


def octagon(w: int, h: int, cut: int, dx: int = 0, dy: int = 0) -> set[Pt]:
    """四角各切掉 cut 级台阶的八角形：第 0 行缩进 cut，往下每行少 1。作者的格子与按钮都是这个形。"""
    pts: set[Pt] = set()
    for y in range(h):
        c = max(0, cut - y, cut - (h - 1 - y))
        pts.update((x + dx, y + dy) for x in range(c, w - c))
    return pts


def disc(cx: float, cy: float, r: float) -> set[Pt]:
    """像素中心落在圆里的那些格。"""
    return {(x, y) for x in range(int(cx - r) - 1, int(cx + r) + 2)
            for y in range(int(cy - r) - 1, int(cy + r) + 2)
            if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r}


def edge(pts: set[Pt]) -> set[Pt]:
    """集合里与集合外 4 邻接的那一圈。"""
    return {(x, y) for x, y in pts
            if any((x + ax, y + ay) not in pts for ax, ay in ((1, 0), (-1, 0), (0, 1), (0, -1)))}


def rings(pts: set[Pt], count: int) -> list[set[Pt]]:
    """从外往里剥 count 圈，最后一项是剩下的芯。"""
    out = []
    rest = set(pts)
    for _ in range(count):
        ring = edge(rest)
        out.append(ring)
        rest -= ring
    out.append(rest)
    return out


def grid_points(rows: list[str]) -> set[Pt]:
    """字符格里不是 `.` 的那些格。只要形状、颜色由调用方给的时候用。"""
    return {(x, y) for y, row in enumerate(rows) for x, ch in enumerate(row) if ch != "."}


def inside(im: Image.Image) -> set[Pt]:
    """线稿围住的格：从画布外一圈沿透明格 4 邻接灌进去，灌不到的透明格就是里面。

    品质星的填充就取这一片 —— 填充与线稿各抄一份，改了线稿就会对不上（线里漏填、填到线下）。
    """
    w, h = im.size
    seen: set[Pt] = set()
    todo = [(-1, -1)]
    while todo:
        x, y = todo.pop()
        if (x, y) in seen or not (-1 <= x <= w and -1 <= y <= h):
            continue
        if 0 <= x < w and 0 <= y < h and im.getpixel((x, y))[3]:
            continue
        seen.add((x, y))
        todo += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
    return {(x, y) for y in range(h) for x in range(w)
            if (x, y) not in seen and not im.getpixel((x, y))[3]}


def tip(im: Image.Image) -> Pt:
    """最上一行最左那个不透明像素：箭头的箭尖、手形的食指尖，就是它们的热点。

    从图上取、不另抄一份：作者把箭头挪了位置，热点跟着走。
    """
    for y in range(im.height):
        for x in range(im.width):
            if im.getpixel((x, y))[3]:
                return x, y
    raise SystemExit("[FAIL] 指针整张透明，取不到热点")


def shrink9(im: Image.Image, l: int, t: int, r: int, b: int, cw: int = 4, ch: int = 4,
            sx: int | None = None, sy: int | None = None) -> Image.Image:
    """把一张大图收成最小的九宫格：四角原样，四边各取一条重复，中间取一个像素铺满。

    sx、sy 指定从哪一列、哪一行取边 —— 作者的大图在边的中段偶尔有装饰（内容区底边那一段凹进去的），
    取到那里拉出来就是错的，所以由调用方挑一条干净的。t 或 b 为 0 时那一侧没有角。
    """
    w, h = im.size
    sx = l + (w - l - r) // 2 if sx is None else sx
    sy = t + (h - t - b) // 2 if sy is None else sy
    out = blank(l + cw + r, t + ch + b)

    def put(src_box: tuple[int, int, int, int], at: Pt) -> None:
        x0, y0, x1, y1 = src_box
        if x1 > x0 and y1 > y0:
            out.paste(im.crop(src_box), at)

    put((0, 0, l, t), (0, 0))
    put((w - r, 0, w, t), (l + cw, 0))
    put((0, h - b, l, h), (0, t + ch))
    put((w - r, h - b, w, h), (l + cw, t + ch))
    for i in range(cw):
        put((sx, 0, sx + 1, t), (l + i, 0))
        put((sx, h - b, sx + 1, h), (l + i, t + ch))
    for j in range(ch):
        put((0, sy, l, sy + 1), (0, t + j))
        put((w - r, sy, w, sy + 1), (l + cw, t + j))
    center = im.getpixel((sx, sy))
    for i in range(cw):
        for j in range(ch):
            out.putpixel((l + i, t + j), center)
    return out


def shrink3h(im: Image.Image, l: int, r: int, cw: int = 4, sx: int | None = None) -> Image.Image:
    """只沿横向拉的三段件：左右两头整列原样，中间取一列重复。"""
    w, h = im.size
    sx = l + (w - l - r) // 2 if sx is None else sx
    out = blank(l + cw + r, h)
    out.paste(im.crop((0, 0, l, h)), (0, 0))
    out.paste(im.crop((w - r, 0, w, h)), (l + cw, 0))
    for i in range(cw):
        out.paste(im.crop((sx, 0, sx + 1, h)), (l + i, 0))
    return out


def shrink3v(im: Image.Image, t: int, b: int, ch: int = 4, sy: int | None = None) -> Image.Image:
    """只沿纵向拉的三段件：上下两头整行原样，中间取一行重复。"""
    w, h = im.size
    sy = t + (h - t - b) // 2 if sy is None else sy
    out = blank(w, t + ch + b)
    out.paste(im.crop((0, 0, w, t)), (0, 0))
    out.paste(im.crop((0, h - b, w, h)), (0, t + ch))
    for j in range(ch):
        out.paste(im.crop((0, sy, w, sy + 1)), (0, t + j))
    return out


# ── 清单 ─────────────────────────────────────────────────────────────────────

@dataclass
class Piece:
    group: str
    name: str
    image: Image.Image
    use: str
    source: str                                   # 切／派生／手画
    nine: tuple[int, int, int, int] | None = None  # 九宫格边距：左、上、右、下；None 是不拉的件
    text: bool = False                             # 里面放字：清单里多打一格「放一行字至少高多少」


PIECES: list[Piece] = []


def add(group: str, name: str, image: Image.Image, use: str, source: str,
        nine: tuple[int, int, int, int] | None = None, size: Pt | None = None,
        text: bool = False) -> Image.Image:
    if size is not None and image.size != size:
        raise SystemExit(f"[FAIL] {group}/{name} 尺寸 {image.size}，清单要求 {size}")
    if any(p.group == group and p.name == name for p in PIECES):
        raise SystemExit(f"[FAIL] 清单里 {group}/{name} 出现了两次")
    if text and nine is None:
        raise SystemExit(f"[FAIL] {group}/{name} 标了放字却不是九宫格，撑不开就放不下字")
    PIECES.append(Piece(group, name, image, use, source, nine, text))
    return image


def ui_metric(name: str) -> int:
    """排版单位的家在代码仓 rules/UI/UIMetrics.cs，这里只读它、不另存一份；找不到就停。"""
    source = (ROOT / "rules" / "UI" / "UIMetrics.cs").read_text(encoding="utf-8")
    found = re.search(rf"public const int {name} = (\d+);", source)
    if found is None:
        raise SystemExit(f"[FAIL] rules/UI/UIMetrics.cs 里找不到 {name}")
    return int(found.group(1))


# ── 面板外框：按作者那张面板量出来的轮廓重画，任意尺寸都与原图同形 ───────────

# 左上角那道圆弧：从顶边往下每一行，左边界离画布左边多少像素（实测自 interface-person 那张面板）
FRAME_TOP_CURVE = [17, 13, 10, 8, 7, 6, 5, 4, 3, 3, 2, 2, 2, 1, 1, 1, 1, 0]
# 底边那一段：先是侧边往里收的两级台阶，接着是 11 行厚的底座（FRAME_LIP_FROM 起）与它的圆弧
FRAME_BOTTOM_CURVE = [0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5, 6, 7, 8, 10, 13, 17]
FRAME_LIP_FROM = 11


def panel_frame(w: int, h: int, body: str, lip: str, line: str) -> Image.Image:
    """画一块作者那种面板：圆角、一圈描边、底下一条厚底座。描边就是「挨着外面的那一圈像素」。"""
    top, bottom = len(FRAME_TOP_CURVE), len(FRAME_BOTTOM_CURVE)
    if w < 2 * FRAME_TOP_CURVE[0] + 2 or h < top + bottom:
        raise SystemExit(f"[FAIL] 面板外框至少 {2 * FRAME_TOP_CURVE[0] + 2}×{top + bottom}")
    shape: set[Pt] = set()
    lip_rows: set[int] = set()
    for y in range(h):
        if y < top:
            left = FRAME_TOP_CURVE[y]
        elif y >= h - bottom:
            i = y - (h - bottom)
            left = FRAME_BOTTOM_CURVE[i]
            if i >= FRAME_LIP_FROM:
                lip_rows.add(y)
        else:
            left = 0
        shape.update((x, y) for x in range(left, w - left))
    out = blank(w, h)
    paint(out, {p for p in shape if p[1] in lip_rows}, lip)
    paint(out, {p for p in shape if p[1] not in lip_rows}, body)
    return paint(out, edge(shape), line)


def check_frame_against_template() -> None:
    """按模板面板原尺寸重画一遍，逐像素比外轮廓与底座 —— 量错一个数这里就停。"""
    ref = crop(tpl("interface-person1.png"), 16, 48, 288, 192)
    mine = panel_frame(288, 192, "ember 2", "ember 1", "ember 1")
    pr, pm = ref.load(), mine.load()
    bad = [(x, y) for y in range(192) for x in range(288)
           if (pr[x, y][3] == 0) != (pm[x, y][3] == 0)
           or (y >= 192 - 11 and pm[x, y][3] and pr[x, y] != pm[x, y])]
    if bad:
        raise SystemExit(f"[FAIL] 重画的面板外框与模板对不上 {len(bad)} 处，前几处：{bad[:5]}")


def ringed(shape: set[Pt], size: int, line: str, ring: str, fill: str) -> Image.Image:
    """一圈描边、里面一整圈同一色的边、再里面是底色 —— 照作者的图标按钮：框类件不分左右亮暗。"""
    out_edge, rim, core = rings(shape, 2)
    im = blank(size, size)
    paint(im, out_edge, line)
    paint(im, rim, ring)
    return paint(im, core, fill)


def box9(fill: str) -> Image.Image:
    """作者左栏那种盒子：rust 1、ember 1、ember 2 三圈，四角各切一格斜角。"""
    rows = [
        "..rrr..",
        ".rkkkr.",
        "rkbbbkr",
        "rkbFbkr",
        "rkbbbkr",
        ".rkkkr.",
        "..rrr..",
    ]
    return from_grid(rows, {"F": fill})


def slot_shape(dx: int = 0, dy: int = 0) -> set[Pt]:
    """作者那个 22×22 格子的外形（四角切 4 级）。"""
    return octagon(22, 22, 4, dx, dy)


# ── kit/：框与盒子 ───────────────────────────────────────────────────────────

def build_kit_frames() -> None:
    check_frame_against_template()
    add("kit", "frame_thick_dark", panel_frame(40, 44, "ember 2", "ember 1", "ember 1"),
        "面板外壳（个人界面那一套顶层页签就搭在它顶边上）、背包、弹窗", "切（按模板轮廓重画）",
        nine=(18, 18, 18, 22), size=(40, 44))
    add("kit", "frame_thick_light", panel_frame(40, 44, "soil 7", "soil 4", "ember 1"),
        "浅色那一族的厚框：压在面板上的弹窗、对话框", "派生", nine=(18, 18, 18, 22), size=(40, 44),
        text=True)

    person = tpl("interface-person1.png")
    inset = flood_clear(crop(person, 110, 79, 190, 147), {"ember 2"})
    add("kit", "inset", recolor(shrink9(inset, 14, 13, 14, 14, sx=40, sy=71), PAPER),
        "面板里的内容区：子页签下面那一大块；浅底，字用深色", "派生", nine=(14, 13, 14, 14), text=True)
    add("kit", "box", box9("soil 7"), "左栏那种小盒子：个人介绍、数值区；浅底，字用深色", "派生",
        nine=(3, 3, 3, 3), size=(7, 7), text=True)
    add("kit", "box_title", box9("ember 4"), "盒子顶上那条亮色名字带：角色名、小标题", "切（按模板重画）",
        nine=(3, 3, 3, 3), size=(7, 7), text=True)

    detail = tpl("box4.png")
    add("kit", "frame_thin_light",
        recolor(shrink9(crop(detail, 16, 0, 48, 48), 5, 5, 5, 5), DETAIL_PAPER),
        "说明框：物品、技能、天赋、状态的详情与各种长文本；分区用 divider_h_light／divider_v_light",
        "派生", nine=(5, 5, 5, 5), size=(14, 14), text=True)
    thin = octagon(12, 12, 2)
    out_edge, rim1, rim2, core = rings(thin, 3)
    dark = blank(12, 12)
    paint(dark, out_edge, "ember 1")
    paint(dark, rim1, "ember 4")
    paint(dark, rim2, "ember 3")
    paint(dark, core, "ember 1")
    add("kit", "frame_thin_dark", dark,
        "HUD 各块的底、小弹层；画法照随身栏那条栏的边，底取最深那档，压在杂乱的场景上字也读得清",
        "手画", nine=(4, 4, 4, 4), size=(12, 12), text=True)

    add("kit", "ribbon", shrink3h(crop(tpl("box5.png"), 83, 22, 90, 26), 15, 15),
        "标题绸带：页标题、升档提示", "切", nine=(15, 0, 15, 0), size=(34, 26))

    add("kit", "divider_h", from_grid(["kkkkkkkk", "gggggggg"]), "深色框里的横分隔线", "手画",
        nine=(2, 0, 2, 0), size=(8, 2))
    add("kit", "divider_v", from_grid(["kg"] * 8), "深色框里的竖分隔线", "手画",
        nine=(0, 2, 0, 2), size=(2, 8))
    light = {"1": "skin 2", "2": "skin 4"}
    add("kit", "divider_h_light", from_grid(["11111111", "22222222"], light), "说明框里的横分隔线",
        "手画", nine=(2, 0, 2, 0), size=(8, 2))
    add("kit", "divider_v_light", from_grid(["12"] * 8, light), "说明框里的竖分隔线", "手画",
        nine=(0, 2, 0, 2), size=(2, 8))


# ── kit/：按钮、图标按钮与它们的图标 ─────────────────────────────────────────

def build_kit_buttons() -> None:
    # 带字的按钮照作者的图标按钮画：里面那圈边比底深一档，焦点那一态这一圈换亮（作者的焦点也是亮这一圈）。
    # 底色不动 —— 按钮上的字配 ember 4 底，对比度在界面绘制约定那张字色表里
    btn = octagon(12, 12, 3)
    states = {
        "normal": ("ember 3", "ember 4"),
        "focus": ("ember 6", "ember 5"),
        "pressed": ("ember 2", "ember 3"),
        "disabled": ("wood 3", "wood 4"),
    }
    for state, (ring, fill) in states.items():
        add("kit", f"button_{state}", ringed(btn, 12, "ember 1", ring, fill),
            "带字的按钮；焦点与鼠标悬停同一张；字由字体画" if state == "normal" else "带字的按钮",
            "手画", nine=(4, 4, 4, 4), size=(12, 12), text=True)

    # 按钮底取模板里空着的那一格（第二行第一格）。图标不从模板里减底取，见 ui_art_sprites.BUTTON_ICONS
    sheet = tpl("button1.png")
    base = crop(sheet, 0, 32, 16, 16)
    add("kit", "iconbutton_normal", base, "图标按钮的底（作者那批八角按钮）", "切", size=(16, 16))
    add("kit", "iconbutton_focus", recolor(base, {"ember 2": "ember 5"}), "图标按钮：焦点／悬停",
        "派生", size=(16, 16))
    add("kit", "iconbutton_pressed", recolor(base, {"ember 3": "ember 2", "ember 2": "ember 1"}),
        "图标按钮：按下（图标由代码下移一像素）", "派生", size=(16, 16))
    add("kit", "iconbutton_disabled", recolor(base, {"ember 3": "wood 3", "ember 2": "wood 2"}),
        "图标按钮：不可用", "派生", size=(16, 16))
    for name, rows in sp.BUTTON_ICONS.items():
        use = "对话的「跳过」；与快进分得开" if name == "skip" else "叠在图标按钮底上的那一层"
        add("kit", f"button_icon_{name}", from_grid(rows), use, "誊", size=(16, 16))

    box = octagon(12, 12, 2, 2, 2)
    off = blank(16, 16)
    out_edge, rim, core = rings(box, 2)
    paint(off, out_edge, "ember 1")
    paint(off, rim, "ember 2")
    paint(off, core, "ember 3")
    add("kit", "check_off", off, "设置里的勾选框：没勾", "手画", size=(16, 16))
    add("kit", "check_on", compose(off, from_grid(sp.MINI["done"]), 4, 4), "设置里的勾选框：勾上",
        "手画", size=(16, 16))


# ── kit/：格子、孔、页签、列表行 ─────────────────────────────────────────────

def build_kit_slots() -> None:
    bar = tpl("box3.png")
    # 连 ember 3 一起清：裁的时候左边两个角各带进来一点栏底的颜色，不清就是两个孤零零的点
    slot = flood_clear(crop(bar, 7, 37, 22, 22), {"ember 2", "ember 3"})
    add("kit", "slot_normal", slot, "技能栏、随身栏、背包、仓库、宝物栏共用的格子；图标放在 (3,3)",
        "切", size=(22, 22))
    disabled = recolor(slot, {"ember 3": "wood 3"})
    add("kit", "slot_disabled", disabled, "格子：暂时用不了", "派生", size=(22, 22))
    add("kit", "slot_locked", compose(disabled, from_grid(sp.MINI["lock"]), 7, 7),
        "格子：还没开的格（背包下一级才开的那几格）", "派生", size=(22, 22))

    select = blank(26, 26)
    outer, mid, inner = octagon(26, 26, 6), octagon(24, 24, 5, 1, 1), slot_shape(2, 2)
    paint(select, outer - mid, "ember 1")
    paint(select, mid - inner, "ember 5")
    add("kit", "slot_select", select, "选中框：叠在格子外面一圈，左上角对齐格子左上角往外 2 像素",
        "手画", size=(26, 26))
    focus = blank(22, 22)
    paint(focus, octagon(20, 20, 3, 1, 1) - octagon(18, 18, 2, 2, 2), "ember 6")
    add("kit", "slot_focus", focus, "焦点／悬停：格子里面一圈亮边，与选中框同时在也看得出", "手画",
        size=(22, 22))
    rarity = blank(22, 22)
    paint(rarity, octagon(18, 18, 2, 2, 2) - octagon(16, 16, 1, 3, 3), "highlight 2")
    add("kit", "rarity_edge", rarity, "稀有度：紧贴图标那一圈，纯白、代码按档染", "手画", size=(22, 22))

    # 详情框里那一格，照作者画的参考图：图标四周只空一像素，描边直边各 16、四角各一个点。
    # 背包那种格子留 2 像素是给选中框、焦点框与稀有度框腾地方，这一格不可点、不用腾。
    frame_shape = octagon(20, 20, 2)
    icon_frame = paint(blank(20, 20), frame_shape, "soil 8")
    add("kit", "icon_frame", paint(icon_frame, edge(frame_shape), "ember 1"),
        "详情框左上那一格：物品、技能、天赋、状态共用；16×16 图标原样放在 (2,2)；"
        "不染色，物品的稀有度写在名字下面那一行", "手画", size=(20, 20))

    hole = disc(11, 11, 10.8)
    out_edge, rim, core = rings(hole, 2)
    # 孔是凹下去的：光从上面来，照亮的是孔里靠下的那一段内壁，左右对称
    socket = blank(22, 22)
    paint(socket, out_edge, "ember 1")
    paint(socket, {(x, y) for x, y in rim if y < 14}, "ember 1")
    paint(socket, {(x, y) for x, y in rim if y >= 14}, "ember 4")
    paint(socket, core, "ember 2")
    add("kit", "socket_open", socket, "宝石孔（两种孔共用）：圆的，与装物品的方格分得开", "手画",
        size=(22, 22))
    add("kit", "socket_locked", compose(socket, from_grid(sp.MINI["lock"]), 7, 7), "宝石孔：还没开",
        "手画", size=(22, 22))

    for i, name in enumerate(("pip", "pip_large")):
        add("kit", name, paint(blank(2 + i, 2 + i), {(x, y) for x in range(2 + i) for y in range(2 + i)},
                                "highlight 2"),
            "品质点：纯白、代码染，落在格子左下角", "手画", size=(2 + i, 2 + i))
    star_line = from_grid(sp.STAR_LINE)
    add("kit", "star_fill", paint(blank(8, 8), inside(star_line), "highlight 2"),
        "品质星的填充：纯白、代码染；取线稿围住的格", "派生", size=(8, 8))
    add("kit", "star_line", star_line, "品质星的线稿：叠在填充上，不染", "手画", size=(8, 8))
    for name, rows in (("heart_empty", sp.HEART_EMPTY), ("heart_full", sp.HEART_FULL),
                       ("heart_entrust_empty", sp.ENTRUST_EMPTY), ("heart_entrust_full", sp.ENTRUST_FULL),
                       ("pip_enhance_empty", sp.ENHANCE_EMPTY), ("pip_enhance_full", sp.ENHANCE_FULL)):
        add("kit", name, from_grid(rows), "关系爱心／托付／强化点；每一对外轮廓一样", "手画", size=(8, 8))


def tab_base(fill: str, round_bottom: bool) -> Image.Image:
    """顶层页签的底，三态共用这一个外形：顶上两角各切三格（作者那张面板上的形），一圈 ember 1 描边。

    round_bottom 时底下两角照顶上那样切 —— 没选中、不可用的那几个像放在面板上的牌子；
    选中那一个底边方的，整行描边落在面板顶边那条线上，看着接进面板。
    """
    cut = {(0, 0), (1, 0), (0, 1)}
    corners = {(x, y) for x, y in cut} | {(19 - x, y) for x, y in cut}
    if round_bottom:
        corners |= {(x, 19 - y) for x, y in corners}
    shape = {(x, y) for y in range(20) for x in range(20)} - corners
    im = paint(blank(20, 20), shape, fill)
    return paint(im, edge(shape), "ember 1")


def build_kit_tabs() -> None:
    for state, fill, round_bottom in (("normal", "soil 4", True), ("selected", "soil 8", False),
                                      ("disabled", "wood 2", True)):
        look = "底边方的，接进面板顶边" if not round_bottom else "底下两角也切掉，像放在面板上的牌子"
        add("kit", f"tab_{state}", tab_base(fill, round_bottom),
            f"顶层页签的底（个人界面、学生列表……那一排）；最下面一行压在面板顶边上，{look}；图标放 (2,2)",
            "切（按模板重画）", size=(20, 20))

    person = tpl("interface-person1.png")
    # 没选中的那几个比模板深一档：模板那档 soil 4 配浅字只到 3.7:1，换 soil 3 之后是 5.5:1
    for state, x, mapping in (("normal", 113, {"soil 4": "soil 3"}), ("selected", 139, {})):
        sub = flood_clear(crop(person, x, 62, 25, 17), {"ember 2"})
        add("kit", f"tabtext_{state}", recolor(shrink9(sub, 2, 2, 2, 0, sy=8), mapping),
            "子页签（属性、状态、天赋……）；字由字体画，选中的深字、其余浅字；最下面一行接内容区顶边",
            "派生" if mapping else "切", nine=(2, 2, 2, 0), size=(8, 6))
        if state == "normal":
            add("kit", "tabtext_disabled", recolor(shrink9(sub, 2, 2, 2, 0, sy=8), {"soil 4": "wood 2"}),
                "子页签：不可用", "派生", nine=(2, 2, 2, 0), size=(8, 6))

    row = octagon(12, 16, 1)
    selected = paint(blank(12, 16), row, "ember 4")
    paint(selected, {(x, y) for x, y in edge(row) if y <= 1}, "ember 5")
    paint(selected, {(x, y) for x, y in edge(row) if y >= 14}, "ember 3")
    add("kit", "row_selected", selected, "列表里选中的那一行；字是深色", "手画", nine=(4, 0, 4, 0),
        size=(12, 16))
    add("kit", "row_focus", paint(blank(12, 16), edge(row), "ember 6"),
        "列表里焦点／悬停的那一行：只有一圈亮边，可以叠在选中行上", "手画", nine=(4, 0, 4, 0),
        size=(12, 16))


def split_knob(states: list[Image.Image], knob_names: set[str], horizontal: bool
               ) -> tuple[Image.Image, Image.Image]:
    """作者画了三张「钮在左、中、右」的滑条。每一列（或行）挑一张钮不在那里的拼出干净的槽，钮单独切出来。"""
    knob_colors = {rgba(n) for n in knob_names}
    w, h = states[0].size
    track = blank(w, h)
    for i in range(w if horizontal else h):
        for st in states:
            line = (st.crop((i, 0, i + 1, h)) if horizontal else st.crop((0, i, w, i + 1)))
            if not any(c in knob_colors for c in line.getdata()):
                track.paste(line, (i, 0) if horizontal else (0, i))
                break
        else:
            raise SystemExit("[FAIL] 滑条三张图里找不到钮不挡着的那一列，拼不出干净的槽")
    knob = blank(w, h)
    src, dst = states[0].load(), knob.load()
    for y in range(h):
        for x in range(w):
            if src[x, y] in knob_colors:
                dst[x, y] = src[x, y]
    return track, knob.crop(knob.getbbox())


def build_kit_sliders() -> None:
    sheet = tpl("bar3.png")
    soil = {"soil 1", "soil 4", "soil 5"}
    track, knob = split_knob([crop(sheet, 3, y, 26, 13) for y in (17, 34, 50)], soil, True)
    track = track.crop((0, 3, 26, 10))
    add("kit", "slider_track", shrink3h(track, 4, 4), "设置里的横滑条：槽", "切", nine=(4, 0, 4, 0))
    add("kit", "slider_knob", knob, "横滑条的钮", "切")
    track, knob = split_knob([crop(sheet, x, 18, 13, 28) for x in (49, 66, 82)], soil, False)
    track = track.crop((3, 0, 10, 28))
    add("kit", "scroll_track", shrink3v(track, 4, 4), "列表的竖滚动条：槽", "切", nine=(0, 4, 0, 4))
    add("kit", "scroll_knob", knob, "竖滚动条的钮", "切")
    add("kit", "scroll_slim_track", from_grid(sp.SCROLL_SLIM_TRACK),
        "文字区的细滚动条：槽；只在字超出那一块时出现，位置一直留着", "手画", nine=(0, 3, 0, 3), size=(6, 8))
    add("kit", "scroll_slim_knob", from_grid(sp.SCROLL_SLIM_KNOB),
        "细滚动条的钮：长短按看得见的那一段占全文多少拉", "手画", nine=(0, 3, 0, 3), size=(6, 8))


# ── kit/：字、指针、迷你记号 ─────────────────────────────────────────────────

def build_kit_glyphs() -> None:
    cells = blank(5 * len(sp.FONT_MINI_ORDER), 7)
    for i, ch in enumerate(sp.FONT_MINI_ORDER):
        glyph = from_grid([row.replace("#", "y") for row in sp.FONT_MINI[ch]])
        cell = blank(5, 7)
        cell.paste(glyph, (1, 1))
        cells.paste(outline(cell, "ember 1"), (i * 5, 0))
    add("kit", "font_mini", cells,
        f"迷你字：一格 5×7（字形 3×5 加一圈描边），顺序 {sp.FONT_MINI_ORDER}；格子角上的件数与键名",
        "手画", size=(5 * len(sp.FONT_MINI_ORDER), 7))

    digits = tpl("button2.png")
    strip = blank(10 * 10, 12)
    for n in range(10):
        region = crop(digits, (n % 4) * 32, 96 + (n // 4) * 32, 16, 16)
        glyph = region.crop(region.getbbox())
        if glyph.width > 10 or glyph.height != 12:
            raise SystemExit(f"[FAIL] 大数字 {n} 是 {glyph.size}，放不进 10×12 一格")
        strip.paste(glyph, (n * 10 + (10 - glyph.width) // 2, 0))
    add("kit", "font_digits", strip, "面板上的大数字（属性值、等级）：一格 10×12，顺序 0–9", "切",
        size=(100, 12))

    for name in ("cursor_arrow", "cursor_hand", "cursor_blocked"):
        image = from_grid(getattr(sp, name.upper()))
        hx, hy = sp.CURSOR_HOTSPOT.get(name) or tip(image)
        add("kit", name, image, f"鼠标指针，1× 原图，代码按整数倍放大；热点 ({hx},{hy})", "手画",
            size=(16, 16))

    for name, rows in sp.MINI.items():
        add("kit", f"mini_{name}", from_grid(rows), "迷你记号", "手画", size=(8, 8))
        if name in ("right", "up"):
            twin = "left" if name == "right" else "down"
            flipped = ImageOps.mirror(from_grid(rows)) if name == "right" else ImageOps.flip(from_grid(rows))
            add("kit", f"mini_{twin}", flipped, "迷你记号", "手画", size=(8, 8))


# ── hud/ ─────────────────────────────────────────────────────────────────────

# bar1.png 里四种填充各在哪一列起、多宽（第一行是空槽、第二行是满的）
BAR_SOURCES = {"hp": (3, 26), "mp": (35, 26), "sp": (67, 26), "xp": (99, 58)}
# 法力条比作者原图亮一档：原来那档转灰度后与生命条只差 8，三条战斗资源条要靠明度分得开
MP_LIGHTER = {"fruitblue 2": "fruitblue 3", "fruitblue 1": "fruitblue 2", "fruitblue 3": "fruitblue 4"}
# 扣减时那一截：四条共用，取一档比哪条都亮、色相又都不撞的冷白
LAG_FROM_XP = {"water 5": "ice 5", "water 4": "ice 4"}
# 经营侧那条竖着的精力条：模板是水色，在种地的游戏里会被读成「水壶里还剩多少水」；
# 精力是当日的体力预算，取种田游戏里精力条通用的那种绿
TUBE_GREEN = {"water 6": "cropgreen 3", "water 7": "cropgreen 5", "water 4": "cropgreen 2"}
# 读字的那几块底换成浅色，字用深色：模板那档 ember 3 是中间调，什么字色放上去都到不了 4.5:1
PAPER = {"ember 3": "soil 7"}
# 说明框的底提亮两档：模板那档 skin 3 上只有 outline 1 过 4.5:1，加成正负那两档都不到 3；
# skin 5 上那两档都过 6:1。说明框正是摆这些颜色的地方（物品词条、换装差值）
DETAIL_PAPER = {"skin 3": "skin 5"}


def bar_fill(key: str) -> Image.Image:
    """一条资源条的填充：左端那几列原样，中间挑一段首尾都是纯色的花纹（可以 Tile 铺），右端补一个斜切的头。"""
    sheet = tpl("bar1.png")
    x, w = BAR_SOURCES[key]
    layer = diff_layer(crop(sheet, x, 35, w, 10), crop(sheet, x, 19, w, 10)).crop((0, 2, w, 8))
    left = layer.getbbox()[0]
    colors = [c for c in layer.getdata() if c[3]]
    main = max(set(colors), key=colors.count)
    cap = layer.crop((left, 0, left + 4, 6))
    lo, hi = left + 4, layer.getbbox()[2] - 4

    def plain(col: int) -> bool:
        return all(layer.getpixel((col, y)) == main for y in range(6))

    tile = None
    for width in (12, 10, 8, 6):
        options = [s for s in range(lo, hi - width + 1) if plain(s) and plain(s + width - 1)]
        if options:
            best = max(options, key=lambda s: sum(
                1 for cx in range(s, s + width) for cy in range(6) if layer.getpixel((cx, cy)) != main))
            tile = layer.crop((best, 0, best + width, 6))
            break
    if tile is None:
        raise SystemExit(f"[FAIL] {key} 那条填充里找不到首尾都是纯色的一段，拼不出能铺的中段")
    head = blank(3, 6)
    for y, n in enumerate((3, 2, 2, 1, 1, 0)):
        for hx in range(n):
            head.putpixel((hx, y), main)
    out = blank(4 + tile.width + 3, 6)
    out.paste(cap, (0, 0))
    out.paste(tile, (4, 0))
    out.paste(head, (4 + tile.width, 0))
    return out


def build_hud() -> None:
    bar = crop(tpl("box3.png"), 0, 32, 232, 32)
    for k in range(8):
        paint(bar, slot_shape(7 + k * 28, 5), "ember 2")
    add("hud", "actionbar_frame", shrink3h(bar, 12, 12, sx=31),
        "技能栏与随身栏的底；格子从 (7,5) 起、每格间距 28；格数由代码给", "切", nine=(12, 0, 12, 0),
        size=(28, 32))

    add("hud", "bar_track", shrink3h(crop(tpl("bar1.png"), 99, 19, 58, 10), 5, 6, sx=25),
        "资源条的槽；填充放在 (2,2)，按槽里 ember 3 那片裁", "切", nine=(5, 0, 6, 0), size=(15, 10))
    fills = {
        "hp": (bar_fill("hp"), "生命条的填充", "切"),
        "sp": (bar_fill("sp"), "体力条的填充", "切"),
        "mp": (recolor(bar_fill("mp"), MP_LIGHTER), "法力条的填充（比原图亮一档）", "派生"),
        "xp": (bar_fill("xp"), "经验条的填充", "切"),
        "lag": (recolor(bar_fill("xp"), LAG_FROM_XP), "扣减时那一截（四条共用）", "派生"),
    }
    for key, (image, use, source) in fills.items():
        add("hud", f"bar_fill_{key}", image, f"{use}；中段用 Tile 铺、不拉伸，右端三列是斜切的头",
            source, nine=(4, 0, 3, 0))

    tubes = tpl("bar2.png")
    empty, full = crop(tubes, 50, 18, 13, 28), crop(tubes, 1, 18, 13, 28)
    water = {rgba(n) for n in TUBE_GREEN}
    liquid = blank(13, 28)
    for y in range(28):
        for x in range(13):
            if full.getpixel((x, y)) in water:
                liquid.putpixel((x, y), full.getpixel((x, y)))
    add("hud", "vigor_tube_track", empty, "经营侧那条竖着的精力条：空管", "切", size=(13, 28))
    add("hud", "vigor_tube_fill", recolor(liquid, TUBE_GREEN),
        "精力条的液面：叠在空管上，代码从下往上按比例露出（色改走绿）", "派生", size=(13, 28))

    for key, rows in sp.GAUGE_ICON.items():
        add("hud", f"gauge_icon_{key}", from_grid(rows), "资源条前面的标签", "手画", size=(8, 8))
    add("hud", "objective", from_grid(sp.OBJECTIVE), "任务目标那一行打头", "手画", size=(16, 16))
    add("hud", "screen_clear", from_grid(sp.SCREEN_CLEAR), "这一屏清干净了", "手画", size=(16, 16))
    add("hud", "down_mark", from_grid(sp.DOWN_MARK), "倒地：盖在小头像上", "手画", size=(16, 16))

    frame = octagon(20, 20, 1)
    out_edge, rim, _ = rings(frame, 2)
    # 里面那一圈与 HUD 细框挨着描边的那一圈同色，一整圈、不分左右亮暗
    portrait = paint(blank(20, 20), out_edge, "ember 1")
    paint(portrait, rim, "ember 4")
    add("hud", "portrait_frame", portrait, "队友那一块的头像框：中间 16×16 透明放小头像", "手画",
        size=(20, 20))


# ── world/ ───────────────────────────────────────────────────────────────────

def build_world() -> None:
    bar = tpl("box3.png")
    ring_empty, ring_full = crop(bar, 129, 96, 14, 16), crop(bar, 49, 96, 14, 16)
    add("world", "ring_cast", ring_empty, "读条底环：打断就没了的那一种", "切", size=(14, 16))
    add("world", "ring_cast_fill", diff_layer(ring_full, ring_empty),
        "读条那一圈：叠在底环上，代码按角度露出", "切", size=(14, 16))
    square = octagon(14, 16, 3)
    hole = octagon(6, 8, 1, 4, 4)
    base = paint(blank(14, 16), edge(square) | edge(hole), "ember 1")
    add("world", "ring_transfer", base, "转场读条底环：方的，与圆的那张分得开", "手画", size=(14, 16))
    add("world", "ring_transfer_fill", paint(blank(14, 16), square - edge(square) - hole, "ember 2"),
        "转场读条那一圈", "手画", size=(14, 16))
    add("world", "cell_usable", crop(bar, 15, 95, 18, 18),
        "当前操作格：够得到；比一格地大一圈，左上角对齐格子往外 1 像素", "切", size=(18, 18))
    add("world", "cell_blocked", from_grid(sp.CELL_BLOCKED), "当前操作格：够不到", "手画",
        size=(18, 18))

    bubble = tpl("box1.png")
    add("world", "bubble", shrink9(crop(bubble, 78, 27, 68, 36), 5, 5, 5, 5, sx=42),
        "气泡框（深底浅字）", "切", nine=(5, 5, 5, 5), size=(14, 14), text=True)
    add("world", "bubble_tail", crop(bubble, 91, 60, 11, 10),
        "气泡尾巴，模板原样、不染色；最上面一行压在气泡倒数第三行", "切", size=(11, 10))

    hint = from_grid([
        "..kkkkkkkk..",
        ".knnnnnnnnk.",
        "knLLLLLLLLnk",
    ] + ["knLLLLLLLLnk"] * 6 + [
        "knLLLLLLLLnk",
        ".knnnnnnnnk.",
        "..kkkkkkkk..",
    ])
    add("world", "hint_frame", hint, "引导提示框：浅底、圆角、没有铆钉，与气泡形状分得开", "手画",
        nine=(4, 4, 4, 4), size=(12, 12), text=True)
    add("world", "hint_icon", from_grid(sp.HINT_ICON), "引导提示左边那个记号", "手画", size=(16, 16))
    add("world", "immune", from_grid(sp.IMMUNE), "这一招对它没反应", "手画", size=(16, 16))


def build_world_markers() -> None:
    badge = from_grid(sp.DEVICE_BADGE)
    uses = {"nofuel": "缺料", "working": "在产", "done": "产完了"}
    for key, rows in sp.DEVICE_MARK.items():
        add("world", f"device_{key}", compose(badge, from_grid(rows)), f"设备头上的状态：{uses[key]}",
            "手画", size=(16, 16))

    border = edge({(x, y) for x in range(16) for y in range(16)})
    marks = {
        "ok": ("cropgreen 3", {p for p in border if min(p[0], 15 - p[0]) < 3 and min(p[1], 15 - p[1]) < 3},
               "cropgreen 4", "可摆"),
        "blocked": ("gemred 2", {(i, i) for i in range(16)} | {(i, 15 - i) for i in range(16)},
                    "gemred 2", "被拒"),
        "door": ("ember 5", grid_points(sp.PLACE_DOOR_ARROW), "ember 5", "门前那一格"),
        "occupied": ("ash 5", {(x, y) for x in range(1, 15) for y in range(1, 15) if (x + y) % 4 == 0},
                     "ash 5", "施工中已被占"),
    }
    for key, (line, extra, extra_color, use) in marks.items():
        im = paint(blank(16, 16), border, line)
        add("world", f"place_{key}", paint(im, extra, extra_color),
            f"建造预览，一格一张：{use}；中间透明", "手画", size=(16, 16))

    drop = blank(16, 16)
    for y, (x0, x1) in zip(range(11, 15), ((4, 11), (2, 13), (2, 13), (4, 11))):
        paint(drop, {(x, y) for x in range(x0, x1 + 1)}, "highlight 2")
    add("world", "drop_base", drop, "掉在地上那一件的底：纯白、代码按稀有度染", "手画", size=(16, 16))


# ── panel/、tab/ ─────────────────────────────────────────────────────────────

# interface-person2 那张属性页上的图标：左上角坐标（16×16 一格）
ATTR_ICONS = {
    "level": (112, 96), "str": (112, 128), "int": (112, 144), "agi": (113, 160), "con": (112, 176),
    "point": (176, 207),
}
ATTR_USE = {"level": "等级", "str": "力量", "int": "智力", "agi": "敏捷", "con": "体质", "point": "属性点"}
# 顶层页签从左到右：个人界面、学生列表、背包、任务列表、地图、关系列表、百科全书
TAB_IDS = ["character", "students", "backpack", "quests", "map", "relations", "codex"]
TAB_USE = dict(zip(TAB_IDS, ["个人界面", "学生列表", "背包", "任务列表", "地图", "关系列表", "百科全书"]))


def outline_color(im: Image.Image) -> tuple[int, int, int, int] | None:
    """图标那一圈描边的颜色：挨着透明的不透明像素里最多的那一色（只看窗口里面，不算窗口边）。"""
    px = im.load()
    w, h = im.size
    counts: dict[tuple[int, int, int, int], int] = {}
    for y in range(h):
        for x in range(w):
            if px[x, y][3] and any(0 <= x + dx < w and 0 <= y + dy < h and not px[x + dx, y + dy][3]
                                   for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                counts[px[x, y]] = counts.get(px[x, y], 0) + 1
    return max(counts, key=counts.get) if counts else None


def cut_icon(sheet: Image.Image, x: int, y: int, bg: set[str], label: str, size: int = 16,
             slack: int = 2) -> Image.Image:
    """从模板切一个 size×size 的图标，默认窗口左上角在 (x, y)；切到了图标就把窗口挪到刚好不切。

    模板里同一排的图标不一定对齐（背包那个页签图标比同排的低一像素），按固定坐标切会切掉一行。
    「切到了」的判法：窗口边上有一个不是描边色的图标像素，而它朝外那一格还是图标（不是底色）——
    描边没在窗口边上合上，图标还往外走。只认非描边色，是因为图标的描边常常贴着页签或面板的框线，
    那不算切到。往切到的那边挪一格再判，挪出 slack 格还切、或两头都切到，就停下报是哪一件。
    """
    under = {rgba(n) for n in bg} | {CLEAR}
    wx, wy = x, y
    while True:
        win = flood_clear(crop(sheet, wx, wy, size, size), bg)
        line = outline_color(win)
        sides = {
            "上": [((i, 0), (wx + i, wy - 1)) for i in range(size)],
            "下": [((i, size - 1), (wx + i, wy + size)) for i in range(size)],
            "左": [((0, j), (wx - 1, wy + j)) for j in range(size)],
            "右": [((size - 1, j), (wx + size, wy + j)) for j in range(size)],
        }
        cut = {side for side, cells in sides.items()
               if any(win.getpixel(p)[3] and win.getpixel(p) != line and sheet.getpixel(q) not in under
                      for p, q in cells)}
        if not cut:
            return win
        dx = ("右" in cut) - ("左" in cut)
        dy = ("下" in cut) - ("上" in cut)
        if {"上", "下"} <= cut or {"左", "右"} <= cut or abs(wx + dx - x) > slack or abs(wy + dy - y) > slack:
            raise SystemExit(f"[FAIL] {label}：{size}×{size} 的切图窗口装不下这个图标（{'、'.join(sorted(cut))}边被切到）")
        wx, wy = wx + dx, wy + dy


def build_panel() -> None:
    person = tpl("interface-person1.png")
    add("panel", "profile_frame", flood_clear(crop(person, 26, 58, 76, 76), {"ember 2"}),
        "个人界面左上的立绘框：64×64 立绘放在 (6,6)", "切", size=(76, 76))
    stats = tpl("interface-person2.png")
    for key, (x, y) in ATTR_ICONS.items():
        add("panel", f"attr_{key}", cut_icon(stats, x, y, {"ember 3"}, f"panel/attr_{key}"),
            f"属性页：{ATTR_USE[key]}", "切", size=(16, 16))
    for k, tab_id in enumerate(TAB_IDS):
        x0 = 35 + 22 * k
        fill = "soil 8" if k == 0 else "soil 4"
        icon = cut_icon(person, x0 + 2, 31, {fill}, f"tab/{tab_id}")
        add("tab", tab_id, icon, f"顶层页签图标：{TAB_USE[tab_id]}", "切", size=(16, 16))
        add("tab", f"{tab_id}_disabled", to_gray(icon), f"顶层页签图标：{TAB_USE[tab_id]}（不可用）",
            "派生", size=(16, 16))

    add("panel", "map_here", from_grid(sp.MAP_HERE), "地图页：当前在这张图", "手画", size=(8, 8))
    add("panel", "map_blocked", from_grid(sp.MAP_BLOCKED), "地图页：进不去的图", "手画", size=(8, 8))
    for key, rows, use in (("success", sp.RESULT_SUCCESS, "成功"), ("retreat", sp.RESULT_RETREAT, "撤退"),
                           ("fail", sp.RESULT_FAIL, "失败")):
        add("panel", f"result_{key}", from_grid(rows), f"出征结束那一屏：{use}", "手画", size=(32, 32))
    add("panel", "node_quest", from_grid(sp.NODE_QUEST), "主线页：这个节点派出一条任务", "手画",
        size=(16, 16))
    add("panel", "node_cutscene", from_grid(sp.NODE_CUTSCENE), "主线页：这个节点是一段演出", "手画",
        size=(16, 16))
    add("panel", "codex_unknown", from_grid(sp.CODEX_UNKNOWN), "图鉴里还没收录的那一条", "手画",
        size=(16, 16))
    add("panel", "coin", from_grid(sp.COIN), "钱（背包里那一栏）", "手画", size=(16, 16))


# ── menu/、glyph/ ────────────────────────────────────────────────────────────

def build_menu() -> None:
    sheet = tpl("box2.png")
    frame = crop(sheet, 83, 1, 78, 130)
    shape = {(x, y) for y in range(frame.height) for x in range(frame.width)
             if frame.getpixel((x, y))[3]}
    clean = paint(blank(*frame.size), shape, "ember 2")
    paint(clean, edge(shape), "ember 1")
    add("menu", "menu_frame", shrink9(clean, 11, 11, 11, 11),
        "主菜单那一列的外框；里面每一项左右各让 4、上下各让 2，项与项之间空 3 行", "切（按模板重画）",
        nine=(11, 11, 11, 11), size=(26, 26))

    # 取中间那一项：它的顶是「一道亮线、下面紧跟描边」，底是「描边、下面紧跟一道亮线」。
    # 第一项的顶和外框挨着、第三项的底边中段凹进去一截，所以不取它们。
    column = [frame.getpixel((37, y)) for y in range(frame.height)]
    lit, line = rgba("ember 4"), rgba("ember 1")
    top = next(y for y in range(41, frame.height - 1) if column[y] == lit and column[y + 1] == line)
    bottom = next(y for y in range(top + 2, frame.height) if column[y] == lit and column[y - 1] == line)
    item = flood_clear(crop(frame, 4, top, 70, bottom - top + 1), {"ember 2"})
    states = {
        "normal": {},
        "focus": {"ember 4": "ember 6", "wet 3": "wet 4"},
        "pressed": {"ember 4": "ember 3", "wet 3": "wet 2"},
        "disabled": {"ember 4": "wood 4", "wet 3": "wood 2"},
    }
    for state, mapping in states.items():
        add("menu", f"menu_item_{state}", shrink9(recolor(item, mapping), 6, 6, 6, 6),
            "主菜单的一项：载入游戏、新的游戏、设置；字由字体画", "切" if not mapping else "派生",
            nine=(6, 6, 6, 6), size=(16, 16), text=True)


def build_glyph() -> None:
    add("glyph", "keycap", from_grid(sp.KEYCAP), "键盘上任何一个键：框是这张，键名由字体写在里面",
        "手画", nine=(4, 0, 4, 0), size=(12, 16))
    for key, cells in sp.MOUSE_LIT.items():
        mouse = from_grid(sp.MOUSE_BODY)
        paint(mouse, {(x, y) for y, x in cells}, "ember 5")
        add("glyph", f"mouse_{key}", mouse, "鼠标键", "手画", size=(16, 16))
    digits = tpl("button2.png")
    for n in range(10):
        add("glyph", f"key_digit_{n}", crop(digits, (n % 4) * 32, (n // 4) * 32, 16, 16),
            "数字键（随身栏换格那一排）", "切", size=(16, 16))


# ── 校验、写盘、总览 ─────────────────────────────────────────────────────────

ALLOWED = set(PAL.values())
# 主菜单底图四周比一屏多画的那一圈出血：逻辑宽度会随窗口宽高比撑开一点，不留出血就露底
MENU_BG_BLEED = 16


def viewport() -> Pt:
    """逻辑分辨率，读 project.godot，不另存一份。"""
    text = (ROOT / "project.godot").read_text(encoding="utf-8")
    w = re.search(r"^window/size/viewport_width=(\d+)", text, re.MULTILINE)
    h = re.search(r"^window/size/viewport_height=(\d+)", text, re.MULTILINE)
    if not (w and h):
        raise SystemExit("[FAIL] project.godot 里找不到 viewport_width／viewport_height")
    return int(w.group(1)), int(h.group(1))


def author_owned() -> dict[str, Pt | None]:
    """放在同一批目录里、由作者直接画的那几张：生成器不碰，也不报成多余。值是画布尺寸，None 是不限。"""
    vw, vh = viewport()
    return {"menu/logo.png": None,
            "menu/menu_bg.png": (vw + 2 * MENU_BG_BLEED, vh + 2 * MENU_BG_BLEED)}


AUTHOR_OWNED = set(author_owned())


def validate(piece: Piece) -> list[str]:
    problems = []
    for r, g, b, a in piece.image.getdata():
        if a not in (0, 255):
            problems.append(f"有半透明像素（alpha={a}）")
            break
        if a and (r, g, b) not in ALLOWED:
            problems.append(f"有色板外的颜色 #{r:02X}{g:02X}{b:02X}")
            break
    if piece.image.getbbox() is None:
        problems.append("整张透明")
    if piece.nine:
        l, t, r, b = piece.nine
        w, h = piece.image.size
        if (l + r and l + r >= w) or (t + b and t + b >= h):
            problems.append(f"九宫格边距 {piece.nine} 占满了画布，中间没有能拉的那一格")
    return problems


# 生成器在自己写出的每张 PNG 里记的那条文字块。按像素算、不按文件字节算：画图软件另存时丢不丢这条
# 文字块、压缩率变没变，都不影响「像素是不是生成器写的那一版」这个判断。
FINGERPRINT_KEY = "ui_art"


def fingerprint(im: Image.Image) -> str:
    rgba_im = im.convert("RGBA")
    return hashlib.sha256(f"{rgba_im.width}x{rgba_im.height}:".encode() + rgba_im.tobytes()).hexdigest()[:16]


def save_owned(im: Image.Image, path: Path) -> None:
    info = PngInfo()
    info.add_text(FINGERPRINT_KEY, fingerprint(im))
    im.save(path, pnginfo=info)


def write(dry: bool = False) -> None:
    """一张一张定：像素与这次生成的一样就不动（没记指纹的补记上，叫认领）；像素不同时，记的指纹对得上
    就是生成器上回写的、换成新的，没有指纹或对不上就是作者手改过、跳过。dry 时只报会怎么定，不写。"""
    written, adopted, same, skipped = [], 0, 0, []
    produced = set()
    for p in PIECES:
        path = OUT / p.group / f"{p.name}.png"
        produced.add(path)
        new = fingerprint(p.image)
        if path.exists():
            with Image.open(path) as old:
                recorded = getattr(old, "text", {}).get(FINGERPRINT_KEY)
                actual = fingerprint(old)
            if actual == new:
                if recorded == new:
                    same += 1
                else:
                    if not dry:
                        save_owned(p.image, path)
                    adopted += 1
                continue
            if recorded != actual:
                skipped.append(path.relative_to(ROOT).as_posix())
                continue
        if not dry:
            path.parent.mkdir(parents=True, exist_ok=True)
            save_owned(p.image, path)
        written.append(path.relative_to(ROOT).as_posix())
    stale = sorted(f.relative_to(ROOT).as_posix() for f in OUT.rglob("*.png")
                   if f not in produced and f.relative_to(OUT).as_posix() not in AUTHOR_OWNED)
    head = "（预演，没写盘）要写" if dry else "写入"
    print(f"{head} {len(written)} 张，认领 {adopted} 张（像素没变、补记指纹），未变 {same} 张，"
          f"共 {len(PIECES)} 张。")
    for s in written:
        print(f"    {s}")
    if skipped:
        print(f"[跳过] {len(skipped)} 张作者手改过（没有指纹，或像素与记下的指纹对不上），生成器不碰；"
              "要换回生成器那一版，删掉那张再跑：")
        for s in skipped:
            print(f"    {s}")
    if stale:
        print(f"[多余] {len(stale)} 张不在清单里（改过名或删掉的件），要不要删由人决定：")
        for s in stale:
            print(f"    {s}")


def grid_of(path: Path) -> list[str]:
    """磁盘上一张图 → 字符格。作者改完一批、生成器跟上时用：誊出来贴进 ui_art_sprites.py。

    图例里没有字母的颜色、半透明像素都当场停 —— 前者先在 LEGEND 里加一个字母，后者本来就不许有。
    """
    letter = {rgba(name): ch for ch, name in sp.LEGEND.items()}
    names = {rgb: name for name, rgb in PAL.items()}
    im = Image.open(path).convert("RGBA")
    rows, missing = [], set()
    for y in range(im.height):
        row = ""
        for x in range(im.width):
            px = im.getpixel((x, y))
            if px[3] not in (0, 255):
                raise SystemExit(f"[FAIL] {path.name} 在 ({x},{y}) 有半透明像素")
            if px[3] == 0:
                row += "."
            elif px in letter:
                row += letter[px]
            else:
                missing.add(names.get(px[:3], f"色板外 #{px[0]:02X}{px[1]:02X}{px[2]:02X}"))
                row += "?"
        rows.append(row)
    if missing:
        raise SystemExit(f"[FAIL] {path.name} 里这几色在 LEGEND 里没有字母：{'、'.join(sorted(missing))}")
    return rows


def render9(im: Image.Image, nine: tuple[int, int, int, int], w: int, h: int,
            tile: bool = False) -> Image.Image:
    """照 Godot 的 NinePatchRect 把一张九宫格件撑到 w×h：角不动、边与中间拉伸（tile 时中间横向平铺）。

    只给总览图用，让人看得到件被撑开之后的样子；游戏里怎么撑由引擎做。
    """
    l, t, r, b = nine
    sw, sh = im.size
    src = [(0, l), (l, sw - r), (sw - r, sw)], [(0, t), (t, sh - b), (sh - b, sh)]
    dst = [(0, l), (l, w - r), (w - r, w)], [(0, t), (t, h - b), (h - b, h)]
    if l + r == 0:
        src, dst = ([(0, sw)], src[1]), ([(0, w)], dst[1])
    if t + b == 0:
        src, dst = (src[0], [(0, sh)]), (dst[0], [(0, h)])
    out = blank(w, h)
    for (sx0, sx1), (dx0, dx1) in zip(*[src[0], dst[0]]):
        for (sy0, sy1), (dy0, dy1) in zip(*[src[1], dst[1]]):
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0:
                continue
            part = im.crop((sx0, sy0, sx1, sy1))
            if tile and (sx0, sx1) == src[0][min(1, len(src[0]) - 1)] and len(src[0]) == 3:
                strip = blank(dx1 - dx0, part.height)
                for ox in range(0, dx1 - dx0, part.width):
                    strip.paste(part, (ox, 0))
                part = strip.resize((dx1 - dx0, dy1 - dy0), Image.NEAREST)
            else:
                part = part.resize((dx1 - dx0, dy1 - dy0), Image.NEAREST)
            out.paste(part, (dx0, dy0))
    return out


def preview(path: Path, scale: int = 3) -> None:
    """一张总览：每件放大 scale 倍，下面写名字，按组排；九宫格件另画一张撑开的样子。底色取 ash 2，只给人看。"""
    pad, label_h, max_w = 6, 12, 1600
    tiles = []
    for p in PIECES:
        shown = p.image
        if p.nine:
            l, t, r, b = p.nine
            w = max(p.image.width, l + r + 40) if l + r else p.image.width
            h = max(p.image.height, t + b + 20) if t + b else p.image.height
            shown = render9(p.image, p.nine, w, h, tile=p.name.startswith("bar_fill"))
        tiles.append((p, shown.resize((shown.width * scale, shown.height * scale), Image.NEAREST)))
    x = y = pad
    row_h = 0
    spots = []
    group = None
    for p, im in tiles:
        cell_w = max(im.width, 6 * len(p.name)) + pad
        if p.group != group or x + cell_w > max_w:
            x = pad
            y += row_h + (label_h * 2 if p.group != group else 0)
            row_h = 0
            if p.group != group:
                spots.append(("group", p.group, pad, y - label_h * 2 + 2))
            group = p.group
        spots.append(("tile", (p, im), x, y))
        x += cell_w
        row_h = max(row_h, im.height + label_h + pad)
    sheet = Image.new("RGBA", (max_w, y + row_h + pad), rgba("ash 2"))
    draw = ImageDraw.Draw(sheet)
    for kind, item, sx, sy in spots:
        if kind == "group":
            draw.text((sx, sy), f"[{item}]", fill=rgba("ember 5"))
            continue
        p, im = item
        sheet.alpha_composite(im, (sx, sy))
        draw.text((sx, sy + im.height + 1), p.name, fill=rgba("ash 7"))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)
    print(f"总览图写到 {path}")


def listing() -> None:
    line = ui_metric("LineHeight")
    for p in PIECES:
        nine = "" if p.nine is None else f"  九宫格 左{p.nine[0]} 上{p.nine[1]} 右{p.nine[2]} 下{p.nine[3]}"
        if p.text:
            nine += f"  放一行字至少高 {p.nine[1] + line + p.nine[3]}"
        print(f"{p.group}/{p.name}.png  {p.image.width}×{p.image.height}  [{p.source}]{nine}  {p.use}")
    for rel, size in author_owned().items():
        shown = "尺寸不限" if size is None else f"{size[0]}×{size[1]}"
        print(f"{rel}  {shown}  [作者画]  生成器不碰")


BUILDS = (build_kit_frames, build_kit_buttons, build_kit_slots, build_kit_tabs, build_kit_sliders,
          build_kit_glyphs, build_hud, build_world, build_world_markers, build_panel, build_menu,
          build_glyph)


def build_all() -> list[Piece]:
    """把清单建一遍（只在内存里，不写盘）。交图核对也从这里拿每一件该多大、九宫格边距多少。"""
    if not PIECES:
        for build in BUILDS:
            build()
    return PIECES


def main() -> int:
    ap = argparse.ArgumentParser(description="界面素材生成器")
    ap.add_argument("--dry-run", action="store_true", help="只生成与校验、报会怎么写，不写盘")
    ap.add_argument("--list", action="store_true", help="列出清单")
    ap.add_argument("--preview", type=Path, help="另写一张总览图到这个路径")
    ap.add_argument("--grid", nargs="+", metavar="组/名",
                    help="把磁盘上那几张誊成字符格打出来，例如 kit/cursor_arrow；作者改过的图跟进生成器时用")
    args = ap.parse_args()

    if args.grid:
        for rel in args.grid:
            path = OUT / f"{rel}.png"
            if not path.exists():
                raise SystemExit(f"[FAIL] 没有 {path.relative_to(ROOT).as_posix()}")
            rows = grid_of(path)
            print(f"# {rel}  {len(rows[0])}×{len(rows)}")
            for row in rows:
                print(f'    "{row}",')
        return 0

    build_all()
    failed = [(p, msg) for p in PIECES for msg in validate(p)]
    for p, msg in failed:
        print(f"[FAIL] {p.group}/{p.name}：{msg}")
    if failed:
        print(f"[FAIL] {len(failed)} 处不合格，一张都没写。")
        return 1
    if args.list:
        listing()
    if args.preview:
        preview(args.preview)
    # --list、--preview 是拿来看的，看的时候不写盘；要写就不带它们再跑一次
    write(dry=args.dry_run or args.list or args.preview is not None)
    print(f"[OK] {len(PIECES)} 张全部过了三项校验（硬 alpha、色板内、尺寸）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
