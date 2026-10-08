"""世界侧色板的读取与度量，只此一份。

为什么单独成一个模块：读 `.gpl` 这件事原先只有 `check_dualgrid.py` 在做，而加色带时要问的
那两个问题（「这个色我取得到吗」「这条新色带撞不撞既有的」）又要读同一份表。两处各写一份解析
迟早漂移，而漂移不报错 —— 表现只是两个工具对同一个色板给出不同答案。

**色值的唯一来源是那个 `.gpl`，本模块不抄任何数字。**
"""

from __future__ import annotations

import colorsys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PALETTE = ROOT / "assets" / "self-drawn" / "palette.gpl"

# 两个距离档（RGB 欧氏距离）。**这是人定的判断，不是实测出来的阈值**，所以引用它的地方
# 要把距离本身也报出来，让人自己看，而不是只报一个「过」或「不过」。
NEAR = 15.0   # 以下：这个色色板里已经有了
OKAY = 30.0   # 以下：凑得出来，但没有一条是为它准备的


def read_palette() -> dict[str, tuple[int, int, int]]:
    """读世界侧色板，返回「色板项名 → RGB」，顺序与文件一致。

    原先 `check_dualgrid.py` 里硬写着四个地形的 RGB，而色板落地之后那就是第二份副本 ——
    改了色板而忘了改那里不报错，表现只是守卫拿旧色去比，把对的素材判成错的（实测撞过一次：
    重映射之后那一关整个失败）。

    顺带多一条判据：引用方只许写项名，所以拼错项名会在 `pick()` 里当场停。
    """
    if not PALETTE.exists():
        raise SystemExit(f"[FAIL] 找不到 {PALETTE.relative_to(ROOT)}，颜色那几关没法判")
    out: dict[str, tuple[int, int, int]] = {}
    for line in PALETTE.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith(("#", "GIMP", "Name", "Columns")):
            continue
        parts = line.split("\t")
        rgb = tuple(int(v) for v in parts[0].split())
        if len(rgb) != 3 or len(parts) < 2:
            raise SystemExit(f"[FAIL] 色板里这一行解析不出来：{line!r}")
        out[parts[1].strip()] = rgb
    if not out:
        raise SystemExit("[FAIL] 色板里一条色项都没解析到，这一关根本没有执行，不是通过")
    return out


def pick(entries: dict[str, tuple[int, int, int]], entry: str) -> tuple[int, int, int]:
    """按项名取色。项名不存在就停，并把有哪些项报出来。"""
    if entry not in entries:
        raise SystemExit(f"[FAIL] 色板里没有「{entry}」这一项，"
                         f"有的是：{'、'.join(sorted(entries))}")
    return entries[entry]


def read_ramps() -> dict[str, list[tuple[int, int, int]]]:
    """同一份色板，按色带分组返回：「色带名 → 按档号排好的 RGB 列表」。

    项名的格式是「<色带名> <档号>」，档号从 1 连续。不连续会在这里报出来 ——
    那通常是手改色板时漏了一行或者重复了一个档号。
    """
    ramps: dict[str, list[tuple[str, tuple[int, int, int]]]] = {}
    for name, rgb in read_palette().items():
        parts = name.rsplit(" ", 1)
        if len(parts) != 2 or not parts[1].isdigit():
            raise SystemExit(f"[FAIL] 色板项名「{name}」不是「<色带名> <档号>」的样子")
        ramps.setdefault(parts[0], []).append((parts[1], rgb))
    out: dict[str, list[tuple[int, int, int]]] = {}
    for ramp_name, items in ramps.items():
        idx = [int(i) for i, _ in items]
        if idx != list(range(1, len(idx) + 1)):
            raise SystemExit(f"[FAIL] 色带「{ramp_name}」的档号不是 1..N 连续：{idx}")
        out[ramp_name] = [rgb for _, rgb in items]
    return out


def chroma(rgb: tuple[int, int, int]) -> float:
    """绝对彩度 `(最大通道 − 最小通道) ÷ 255`。

    **刻意不用 HSV 的饱和度**：那个量对暗色没有意义（一个几乎全黑的色可以报出很高的饱和度），
    拿它当上限会放过一批实际发灰的颜色。这条栽过一次。口径在设计仓
    `production/像素绘制原则.md` 的「本项目的色带口径」。
    """
    return (max(rgb) - min(rgb)) / 255.0


def luma(rgb: tuple[int, int, int]) -> float:
    """Rec.601 明度。核夜色塌缩与色带均匀度用的是同一条。"""
    r, g, b = rgb
    return 0.299 * r + 0.587 * g + 0.114 * b


def hue_deg(rgb: tuple[int, int, int]) -> float:
    """色相角（度）。无彩色返回 0，调用方要自己看彩度决定这个数有没有意义。"""
    r, g, b = (v / 255 for v in rgb)
    return colorsys.rgb_to_hls(r, g, b)[0] * 360.0


def distance(a: tuple[int, int, int], b: tuple[int, int, int]) -> float:
    """RGB 欧氏距离。粗，但够答「这个色色板里有没有」。"""
    return sum((x - y) ** 2 for x, y in zip(a, b)) ** 0.5


def nearest(rgb: tuple[int, int, int],
            entries: dict[str, tuple[int, int, int]]
            ) -> tuple[str, tuple[int, int, int], float]:
    """色板里离给定色最近的那一项，返回「项名、它的 RGB、距离」。"""
    name = min(entries, key=lambda k: distance(entries[k], rgb))
    return name, entries[name], distance(entries[name], rgb)


def verdict(dist: float) -> str:
    """把距离翻成一句人话。阈值是人定的判断，所以调用方仍要把距离本身报出来。"""
    if dist < NEAR:
        return "已有"
    if dist < OKAY:
        return "凑得出"
    return "真缺"


def make_ramp(hue: float, steps: int, lum_lo: float, lum_hi: float,
              chroma_lo: float, chroma_hi: float) -> list[tuple[int, int, int]]:
    """按目标明度与彩度各自线性走一条色带，色相固定。

    `chroma_hi` 给**暗端**、`chroma_lo` 给亮端 —— 金属与晶体的高光都是镜面反射、趋白，
    反过来亮端会发荧光。搜索是暴力的（在 HLS 网格上找离目标最近的一点），因为
    Rec.601 明度与 HLS 的 L 不是一回事，解析解不好写而这里的规模根本不在乎。
    """
    if steps < 2:
        raise SystemExit("[FAIL] 一条色带至少两档")
    out: list[tuple[int, int, int]] = []
    for i in range(steps):
        t = i / (steps - 1)
        target_l = lum_lo + (lum_hi - lum_lo) * t
        target_c = chroma_hi + (chroma_lo - chroma_hi) * t
        best: tuple[float, tuple[int, int, int]] | None = None
        for s_i in range(161):
            for l_i in range(161):
                rf, gf, bf = colorsys.hls_to_rgb(hue / 360.0, l_i / 160, s_i / 160)
                cand = (round(rf * 255), round(gf * 255), round(bf * 255))
                err = (abs(luma(cand) - target_l) / 255.0 * 2
                       + abs(chroma(cand) - target_c))
                if best is None or err < best[0]:
                    best = (err, cand)
        assert best is not None
        out.append(best[1])
    return out
