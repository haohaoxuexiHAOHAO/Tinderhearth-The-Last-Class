"""世界侧色板的读取与度量，只此一份。

为什么单独成一个模块：读 `.gpl` 这件事原先只有 `check_dualgrid.py` 在做，而加色带时要问的
那两个问题（「这个色我取得到吗」「这条新色带撞不撞既有的」）又要读同一份表。两处各写一份解析
迟早漂移，而漂移不报错 —— 表现只是两个工具对同一个色板给出不同答案。

**色值的唯一来源是那个 `.gpl`，本模块不抄任何数字。**
"""

from __future__ import annotations

import colorsys
import re
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


_STEP = re.compile(r"^(\d+)(\.5)?$")


def is_half(name: str) -> bool:
    """项名（`gemred 1.5`）或档号（`1.5`）是不是半档。"""
    return name.endswith(".5")


def half_step(a: tuple[int, int, int], b: tuple[int, int, int]) -> tuple[int, int, int]:
    """相邻两个整数档之间的那个半档：逐通道取中点，正好落在一半上的进一。

    取 RGB 中点、不换一个色彩空间去插：本模块的明度（Rec.601）是 RGB 的线性函数，所以中点的
    明度就是两档的平均（取整只差半个单位）；彩度（最大通道 − 最小通道）在两档通道大小顺序相同时
    也是平均，不同时会略低于平均。两档同色相时，这正是 `make_ramp` 按「明度、彩度各自线性走」
    会在两档中间给出的那一点。
    """
    r, g, bl = ((x + y + 1) // 2 for x, y in zip(a, b))
    return r, g, bl


def read_steps() -> dict[str, list[tuple[str, tuple[int, int, int]]]]:
    """同一份色板按色带分组，整数档与半档一起按明暗排：「色带名 → [(档号, RGB), …]」。

    档号是整数（1、2…，从 1 连续）或半档（1.5、2.5…）。半档给画得比一个基础单位大的东西用，
    口径在设计仓 `production/像素绘制原则.md` 的「本项目的色带口径」。手改色板会犯的下面这几种错
    在这里当场报出来，因为别处都不报：

    - 整数档不连续 —— 漏了一行或者重复了一个档号；
    - 半档两边少了一个整数档；
    - 文件里的先后与明暗顺序对不上 —— Aseprite 按文件顺序摆格子，排错了画的人会挑错档；
    - **半档不等于两边的中点** —— 改了整数档而忘了重算半档，表现只是那个半档不再夹在正中间。
    """
    ramps: dict[str, list[tuple[str, tuple[int, int, int]]]] = {}
    for name, rgb in read_palette().items():
        parts = name.rsplit(" ", 1)
        if len(parts) != 2 or not _STEP.match(parts[1]):
            raise SystemExit(f"[FAIL] 色板项名「{name}」不是「<色带名> <档号>」的样子"
                             "（档号是 1、2…，半档是 1.5、2.5…）")
        ramps.setdefault(parts[0], []).append((parts[1], rgb))
    for ramp_name, items in ramps.items():
        idx = [int(s) for s, _ in items if not is_half(s)]
        if idx != list(range(1, len(idx) + 1)):
            raise SystemExit(f"[FAIL] 色带「{ramp_name}」的整数档不是 1..N 连续：{idx}")
        order = [float(s) for s, _ in items]
        if order != sorted(order):
            raise SystemExit(f"[FAIL] 色带「{ramp_name}」的档在文件里的先后不对："
                             f"{[s for s, _ in items]} —— 半档要排在它两边那两档中间")
        whole = dict(zip(idx, (rgb for s, rgb in items if not is_half(s))))
        for s, rgb in items:
            if not is_half(s):
                continue
            k = int(s.split(".")[0])
            if k not in whole or k + 1 not in whole:
                raise SystemExit(f"[FAIL] 「{ramp_name} {s}」两边没有 {k} 与 {k + 1} 两档")
            want = half_step(whole[k], whole[k + 1])
            if rgb != want:
                raise SystemExit(
                    f"[FAIL] 「{ramp_name} {s}」应是 {k} 与 {k + 1} 两档的中点 "
                    f"{want[0]} {want[1]} {want[2]}，现在是 {rgb[0]} {rgb[1]} {rgb[2]}。"
                    f"整数档改了就重算：python tools/palette_probe.py half {ramp_name}")
    return ramps


def read_ramps() -> dict[str, list[tuple[int, int, int]]]:
    """同一份色板按色带分组：「色带名 → 按档号排好的 RGB 列表」，**只有整数档**。

    半档不在里面：各条的档数、明度区间、彩度峰值与色相都按整数档量，那是画一个基础单位的
    小件也在用的那一套。要连半档一起（查撞色、切大画幅用的小表）用 `read_steps`，校验也在那里。
    """
    return {r: [rgb for s, rgb in items if not is_half(s)] for r, items in read_steps().items()}


def read_families() -> list[tuple[str, list[str]]]:
    """读色板里的族划分，返回 [(族名, [色带名...])]，顺序与文件一致。

    族是文件里 `# ==== 名 ====` 那种分隔注释划出来的。它存在的理由是 Aseprite 不理
    `Columns`、按侧边栏宽度自己换行 —— 顺序是唯一还能安排的东西，所以同族的色挨着放。

    **族不等于「画一个物件要哪几条」**：画一张草土交界的图块要苔绿加土褐，那是两族。
    所以按族拆出来的分表不一定够用，`palette_probe.py split` 因此也收色带名。
    """
    families: list[tuple[str, list[str]]] = []
    current: str | None = None
    for line in PALETTE.read_text(encoding="utf-8").splitlines():
        m = re.match(r"^#\s*=+\s*(.+?)\s*=+\s*$", line)
        if m:
            current = m.group(1)
            families.append((current, []))
            continue
        if line.startswith(("#", "GIMP", "Name", "Columns")) or not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) < 2:
            continue
        ramp = parts[1].strip().rsplit(" ", 1)[0]
        if current is None:
            raise SystemExit(f"[FAIL] 色带「{ramp}」排在第一个族标记之前 —— "
                             "每条色带都要落在某一族里，不然 split 取不到它")
        if ramp not in families[-1][1]:
            families[-1][1].append(ramp)
    if not families:
        raise SystemExit("[FAIL] 色板里一个族标记都没有（形如 `# ==== 名 ====`）")
    return families


def chroma(rgb: tuple[int, int, int]) -> float:
    """绝对彩度 `(最大通道 − 最小通道) ÷ 255`。

    **刻意不用 HSV 的饱和度**：那个量对暗色没有意义（一个几乎全黑的色可以报出很高的饱和度），
    拿它当上限会放过一批实际发灰的颜色。口径在设计仓
    `production/像素绘制原则.md` 的「本项目的色带口径」。
    """
    return (max(rgb) - min(rgb)) / 255.0


def luma(rgb: tuple[int, int, int]) -> float:
    """Rec.601 明度。本模块与 palette_probe 里说到「明度」的地方都按这一条算。"""
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
