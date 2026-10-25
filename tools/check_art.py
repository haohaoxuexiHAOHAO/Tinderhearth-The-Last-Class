#!/usr/bin/env python3
"""交图核对：作者画完素材交过来，核一遍代码比得出来的事实。好不好看不归它判，那是作者的事。

核这几样，每一样都是逐像素或逐个数比得出来的：
  色板    每个不透明像素都是 assets/self-drawn/palette.gpl 里的一档；不是的报出离它最近的那一档。
  透明度  alpha 只有 0 与 255。
  尺寸    界面件照生成器的清单（python tools/ui_art.py --list，作者自画的那几张也列在那里）；
          icons/ 下按档，building 是大图标、其余是小图标（rules/UI/UIMetrics.cs）。别处没有尺寸规矩，不核。
  几态    同一件的几种状态（_normal、_focus、_pressed、_disabled……）画布一样大。
  九宫格  清单里标了边距的件，沿拉伸方向那几行、几列每一格都一样；中段按 Tile 铺的不核横向。
  文件名  与 check_names.py 同一条规矩。
最后列出清单里有、磁盘上还没有的 —— 只列代码仓自己知道的那些，内容图标等内容数据引用它们再说。

不进 verify.py：它核的是交图那一刻的事。留它的理由是色板外的近似色在 Godot 里看不出来
（ADR-0009：判断一个入口该不该留，看它判的东西在 Godot 里看不看得出来）。

用法（从代码仓根目录跑）：
    python tools/check_art.py                          核工作区里新加、改过的 PNG（比 git HEAD）
    python tools/check_art.py assets/self-drawn/icons  只核这几个文件或目录
    python tools/check_art.py --all                    核 assets/self-drawn 下全部 PNG

本脚本不写任何文件，没有自证入口。
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

from PIL import Image

import check_names
import ui_art
from palettelib import ROOT, nearest, read_palette

if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")

ART = ROOT / "assets" / "self-drawn"
# 状态后缀：同一件的几张图按「名字去掉它」归成一组，比画布大小
STATES = ("normal", "focus", "pressed", "disabled", "selected", "locked", "open", "empty", "full", "on", "off")
Pt = tuple[int, int]


def changed_pngs() -> list[Path]:
    """工作区里相对 git HEAD 新加、改过、改名过来的 PNG（只看 assets/self-drawn 下）。"""
    out = subprocess.run(["git", "-c", "core.quotepath=false", "status", "--porcelain", "-z", "-uall",
                          "--", "assets/self-drawn"], cwd=ROOT, capture_output=True, text=True,
                         encoding="utf-8").stdout
    fields, paths = out.split("\0"), []
    i = 0
    while i < len(fields):
        entry = fields[i]
        i += 1
        if len(entry) < 4:
            continue
        status, rel = entry[:2], entry[3:]
        if status.startswith("R"):
            i += 1          # -z 下改名后面跟着旧名，跳过
        if "D" not in status and rel.lower().endswith(".png"):
            paths.append(ROOT / rel)
    return sorted(set(paths))


def expectations() -> tuple[dict[str, tuple[Pt | None, str]], dict[str, ui_art.Piece]]:
    """「相对 assets/self-drawn 的路径 → (该多大, 依据)」，外加清单里每一件（拿九宫格边距用）。

    尺寸只从它的家里读：界面件读生成器清单，作者自画那几张读 ui_art.author_owned()，
    图标档读 UIMetrics。None 是「不限」。
    """
    pieces = {f"ui/{p.group}/{p.name}.png": p for p in ui_art.build_all()}
    sizes: dict[str, tuple[Pt | None, str]] = {rel: (p.image.size, "生成器清单") for rel, p in pieces.items()}
    for rel, size in ui_art.author_owned().items():
        sizes[f"ui/{rel}"] = (size, "作者自画那几张")
    return sizes, pieces


def tier_size(rel: str) -> tuple[Pt | None, str]:
    """icons/ 下按档：建筑是大图标，其余（物品、技能、状态、季节、天气、小头像……）是小图标。"""
    if rel.startswith("icons/building/"):
        n = ui_art.ui_metric("IconLarge")
        return (n, n), "大图标"
    if rel.startswith("icons/"):
        n = ui_art.ui_metric("IconSmall")
        return (n, n), "小图标"
    return None, ""


def family(path: Path) -> tuple[Path, str]:
    """同一件的几态归到同一组：名字去掉状态后缀；没有后缀的就是那一组的常态（tab/backpack）。"""
    stem = path.stem
    for state in STATES:
        if stem.endswith(f"_{state}"):
            return path.parent, stem[: -len(state) - 1]
    return path.parent, stem


def nine_problems(im: Image.Image, nine: tuple[int, int, int, int], tiled: bool) -> list[str]:
    """沿拉伸方向必须一样：横着拉的那几列每一行一色，竖着拉的那几行每一列一色。"""
    l, t, r, b = nine
    w, h = im.size
    px = im.load()
    out = []
    if l + r and not tiled:
        rows = [y for y in range(h) if len({px[x, y] for x in range(l, w - r)}) > 1]
        if rows:
            out.append(f"九宫格横着拉的那几列（x {l}–{w - r - 1}）在第 {short(rows)} 行不是同一色")
    if t + b:
        cols = [x for x in range(w) if len({px[x, y] for y in range(t, h - b)}) > 1]
        if cols:
            out.append(f"九宫格竖着拉的那几行（y {t}–{h - b - 1}）在第 {short(cols)} 列不是同一色")
    return out


def short(nums: list[int], n: int = 4) -> str:
    text = "、".join(str(v) for v in nums[:n])
    return text + (f" 等 {len(nums)} 处" if len(nums) > n else "")


def pixel_problems(im: Image.Image, palette: dict[str, tuple[int, int, int]]) -> list[str]:
    allowed = set(palette.values())
    off, soft = [], []
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = im.getpixel((x, y))
            if a not in (0, 255):
                soft.append((x, y, a))
            if a and (r, g, b) not in allowed:
                off.append((x, y, (r, g, b)))
    out = []
    if off:
        examples = []
        for x, y, rgb in off[:3]:
            name, _, dist = nearest(rgb, palette)
            examples.append(f"({x},{y}) #{rgb[0]:02X}{rgb[1]:02X}{rgb[2]:02X} 最近的是 {name}（差 {dist:.0f}）")
        out.append(f"色板外 {len(off)} 个像素，如 " + "；".join(examples))
    if soft:
        out.append(f"半透明 {len(soft)} 个像素，如 " + "；".join(f"({x},{y}) alpha {a}" for x, y, a in soft[:3]))
    return out


def check_one(path: Path, sizes, pieces, palette) -> list[str]:
    rel_repo = path.relative_to(ROOT).as_posix()
    rel = path.relative_to(ART).as_posix()
    problems: list[str] = []
    names: list[str] = []
    check_names.check_resource_paths(rel_repo, names)
    if names or not check_names.LOWER_SEGMENT_RE.match(path.name):
        problems.append("文件名只许小写字母、数字、点、下划线与连字符（check_names.py 那条规矩）")
    with Image.open(path) as raw:
        im = raw.convert("RGBA")
    problems += pixel_problems(im, palette)
    want, basis = sizes.get(rel) or tier_size(rel)
    if want and im.size != want:
        problems.append(f"画布 {im.width}×{im.height}，应为 {want[0]}×{want[1]}（{basis}）")
    piece = pieces.get(rel)
    if piece and piece.nine and im.size == piece.image.size:
        problems += nine_problems(im, piece.nine, tiled="Tile" in piece.use)
    folder, stem = family(path)
    others = {}
    for sib in folder.glob("*.png"):
        if sib != path and family(sib) == (folder, stem):
            with Image.open(sib) as s:
                others[sib.name] = s.size
    odd = [f"{n} 是 {w}×{h}" for n, (w, h) in sorted(others.items()) if (w, h) != im.size]
    if odd:
        problems.append(f"同一件的其余几态画布不一样大：{'；'.join(odd)}")
    return problems


def main() -> int:
    ap = argparse.ArgumentParser(description="交图核对：色板、透明度、尺寸、几态、九宫格、文件名")
    ap.add_argument("targets", nargs="*", type=Path, help="要核的 PNG 或目录；不给就核工作区里改过的")
    ap.add_argument("--all", action="store_true", help="核 assets/self-drawn 下全部 PNG")
    args = ap.parse_args()

    if args.all:
        files = sorted(ART.rglob("*.png"))
    elif args.targets:
        files = []
        for t in args.targets:
            t = (ROOT / t) if not t.is_absolute() else t
            if not t.exists():
                raise SystemExit(f"[FAIL] 没有 {t}")
            files += sorted(t.rglob("*.png")) if t.is_dir() else [t]
    else:
        files = changed_pngs()
    outside = [f for f in files if ART not in f.resolve().parents]
    if outside:
        raise SystemExit(f"[FAIL] 只核 assets/self-drawn 下的素材，这几张不在：{', '.join(map(str, outside))}")

    sizes, pieces = expectations()
    palette = read_palette()
    bad = 0
    for path in files:
        problems = check_one(path, sizes, pieces, palette)
        rel = path.relative_to(ART).as_posix()
        if problems:
            bad += 1
            print(f"[FAIL] {rel}")
            for p in problems:
                print(f"       {p}")
        else:
            print(f"[OK]   {rel}")
    missing = [(rel, want) for rel, (want, _) in sizes.items() if not (ART / rel).exists()]
    for rel, want in missing:
        shown = "尺寸不限" if want is None else f"{want[0]}×{want[1]}"
        print(f"[缺]   {rel}（{shown}）")
    if not files:
        print("工作区里没有新加或改过的 PNG；要核全部加 --all，或者给文件、目录")
    print(f"结果：核了 {len(files)} 张，通过 {len(files) - bad} 张，有问题 {bad} 张；清单里还缺 {len(missing)} 张")
    print(f"EXIT={1 if bad else 0}")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
