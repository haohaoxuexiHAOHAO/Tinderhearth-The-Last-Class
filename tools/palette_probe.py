"""加色带之前问色板两个问题的按需工具。

**它不是守卫，不进 verify.py。** 既有色板里本来就有刻意接受的近邻（烧红的铜与炭火在暗端
就该挨着），做成守卫会一直报，而我们会学会忽略它。口径与那两个问题的顺序在设计仓
`production/像素绘制原则.md` 的「本项目的色带口径」。

两个问题：

  一、**这个色我取得到吗** —— 给几个目标色，报它们在色板里的最近邻与距离。
       距离大就说明色板这一片没有人，画那样东西时无色可取。
  二、**这条新色带撞不撞** —— 按色相生成一条候选，逐档报最近邻。
       撞上的后果是两份近似色并存，迟早各画一半素材，而没有任何东西会报错。

⚠️ **距离档（已有／凑得出／真缺）是人定的判断，不是实测出来的阈值**，所以本脚本一律把
距离本身打出来。另有一条更要紧的：**拿真实照片的色当目标会判错** —— 真实宝石的彩度远高于
本项目的基调，追着它走会得出「全都缺」。判据是「这条色带读不读得出是什么材质」。

用法（从代码仓根目录跑）：
    python tools/palette_probe.py                              列现有色带与各自的区间
    python tools/palette_probe.py near "#6E767E" "#3A3F45"     查这几个色取不取得到
    python tools/palette_probe.py ramp 210 5 46 232 0.04 0.11  生成一条候选并查撞色

本脚本没有自证入口。
"""

from __future__ import annotations

import argparse
import sys

from palettelib import (NEAR, chroma, distance, hue_deg, luma, make_ramp,
                        nearest, read_palette, read_ramps, verdict)

if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")


def parse_hex(s: str) -> tuple[int, int, int]:
    t = s.strip().lstrip("#")
    if len(t) != 6:
        raise SystemExit(f"[FAIL] 「{s}」不是 #RRGGBB 的样子")
    try:
        return int(t[0:2], 16), int(t[2:4], 16), int(t[4:6], 16)
    except ValueError:
        raise SystemExit(f"[FAIL] 「{s}」里有不是十六进制的字符")


def show(rgb: tuple[int, int, int]) -> str:
    return f"#{rgb[0]:02X}{rgb[1]:02X}{rgb[2]:02X}"


def cmd_ramps() -> int:
    """列现有色带：档数、明度区间、彩度峰值、色相。

    彩度峰值那一列是定上限时唯一要看的东西 —— 新色带该不该比它艳，得先知道它是多少。
    """
    ramps = read_ramps()
    print(f"色板共 {sum(len(v) for v in ramps.values())} 色、{len(ramps)} 条色带\n")
    print(f"{'色带':14s} {'档':>3s}  {'明度区间':>13s}  {'彩度峰值':>9s}  {'峰值档':>5s}  "
          f"{'相邻明度差':>10s}  冷暖走向")
    for name, cols in ramps.items():
        lums = [luma(c) for c in cols]
        chromas = [chroma(c) for c in cols]
        peak = max(range(len(chromas)), key=lambda i: chromas[i])
        diffs = [lums[i + 1] - lums[i] for i in range(len(lums) - 1)]
        gap = f"{min(diffs):.0f}–{max(diffs):.0f}" if diffs else "—"
        print(f"{name:14s} {len(cols):>3d}  {lums[0]:>5.0f} → {lums[-1]:<5.0f}  "
              f"{chromas[peak]:>9.3f}  {peak + 1:>5d}  {gap:>10s}  {warmth(cols)}")
    print("\n冷暖那一列按每档的 R−B 定（正为暖、负为冷）。**一条色带中途翻冷暖是要注意的**：")
    print("  它对有些材质是对的（混凝土、面具暗处冷、受光面暖），但金属那一类不行 ——")
    print("  金属的高光反射的是天光，翻暖之后会读成旧锡或水泥。")
    return 0


def warmth(cols: list[tuple[int, int, int]]) -> str:
    """把一条色带的冷暖走向压成一行，并点出它在哪一档翻了。

    这一列是加进来的最晚一个，起因是实测 `ash` 的注释写着「金属」而它中段从冷翻暖 ——
    于是照它画的钢铁亮部发米灰，而色板上看不出这回事。
    """
    signs = ["暖" if c[0] > c[2] else ("冷" if c[2] > c[0] else "中") for c in cols]
    flips = [i for i in range(1, len(signs)) if signs[i] != signs[i - 1]]
    line = "".join(signs)
    if not flips:
        return line
    return f"{line}  ← 第 {'、'.join(str(i + 1) for i in flips)} 档翻了"


def cmd_near(colors: list[str]) -> int:
    entries = read_palette()
    print(f"色板共 {len(entries)} 色。距离是 RGB 欧氏距离，档是人定的判断 —— 自己看数。\n")
    for s in colors:
        rgb = parse_hex(s)
        name, near_rgb, dist = nearest(rgb, entries)
        print(f"{show(rgb)}  彩度 {chroma(rgb):.3f}  明度 {luma(rgb):5.1f}  色相 {hue_deg(rgb):5.1f}°")
        print(f"    最近是 {name}（{show(near_rgb)}），距离 {dist:5.1f}  → {verdict(dist)}")
    return 0


def cmd_ramp(hue: float, steps: int, lum_lo: float, lum_hi: float,
             chroma_lo: float, chroma_hi: float) -> int:
    entries = read_palette()
    cols = make_ramp(hue, steps, lum_lo, lum_hi, chroma_lo, chroma_hi)
    print(f"候选色带（色相 {hue}°，{steps} 档）—— 可直接贴进 .gpl，把 <名> 换掉：\n")
    for i, rgb in enumerate(cols, 1):
        print(f"{rgb[0]:3d} {rgb[1]:3d} {rgb[2]:3d}\t<名> {i}"
              f"   # {show(rgb)}  明度 {luma(rgb):5.1f}  彩度 {chroma(rgb):.3f}")
    lums = [luma(c) for c in cols]
    diffs = [lums[i + 1] - lums[i] for i in range(len(lums) - 1)]
    print(f"\n相邻明度差 {[round(d, 1) for d in diffs]}")
    print("  对照：材质色带在 30 上下，金属与晶体刻意更大（靠强高光与刻面读材质）\n")

    print("撞色检查：")
    worst: tuple[float, str] | None = None
    for i, rgb in enumerate(cols, 1):
        name, near_rgb, dist = nearest(rgb, entries)
        flag = "  <<< 太近，这一档色板里已经有了" if dist < NEAR else ""
        print(f"  第 {i} 档 → {name:16s} {dist:5.1f}{flag}")
        if worst is None or dist < worst[0]:
            worst = (dist, f"第 {i} 档 vs {name}")
    assert worst is not None
    print(f"\n最近的一对：{worst[1]}，距离 {worst[0]:.1f}")
    print("  太近说明这一片已经有人占了 —— 先问「借得到吗」，别新开两份近似色。")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="色板探针：加色带之前问两个问题")
    sub = ap.add_subparsers(dest="cmd")

    p_near = sub.add_parser("near", help="查这几个色在色板里取不取得到")
    p_near.add_argument("colors", nargs="+", metavar="#RRGGBB")

    p_ramp = sub.add_parser("ramp", help="按色相生成一条候选色带并查撞色")
    p_ramp.add_argument("hue", type=float, help="色相角（度）")
    p_ramp.add_argument("steps", type=int, help="档数")
    p_ramp.add_argument("lum_lo", type=float, help="最暗档的目标明度")
    p_ramp.add_argument("lum_hi", type=float, help="最亮档的目标明度")
    p_ramp.add_argument("chroma_lo", type=float, help="亮端彩度（高光趋白，所以这个小）")
    p_ramp.add_argument("chroma_hi", type=float, help="暗端彩度")

    args = ap.parse_args()
    if args.cmd == "near":
        return cmd_near(args.colors)
    if args.cmd == "ramp":
        return cmd_ramp(args.hue, args.steps, args.lum_lo, args.lum_hi,
                        args.chroma_lo, args.chroma_hi)
    return cmd_ramps()


if __name__ == "__main__":
    raise SystemExit(main())
