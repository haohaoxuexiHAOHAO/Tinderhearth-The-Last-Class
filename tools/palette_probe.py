"""色板的按需工具：加色带之前问两个问题，外加两件画画时要用的事。

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

另外两件是画画时用的：**统计一批图的色彩分布**（参考图与自己画的素材都能跑，用来核
「大面材质的主色有没有堆在亮端」），与**从主表切一份小色板**（Aseprite 一次只用一个色板，
整张表一百多色找起来慢）。

用法（从代码仓根目录跑）：
    python tools/palette_probe.py                              列现有色带与各自的区间
    python tools/palette_probe.py near "#6E767E" "#3A3F45"     查这几个色取不取得到
    python tools/palette_probe.py ramp 210 5 46 232 0.04 0.11  生成一条候选并查撞色
    python tools/palette_probe.py image temp/ref --crop 0.7    统计一批图的色彩分布
    python tools/palette_probe.py split 植被 土木                切一份小色板

本脚本没有自证入口。
"""

from __future__ import annotations

import argparse
import sys
from collections import Counter
from pathlib import Path

from palettelib import (NEAR, ROOT, chroma, distance, hue_deg, luma, make_ramp,
                        nearest, read_families, read_palette, read_ramps,
                        verdict)

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


def load_pixels(paths: list[Path], crop: float = 1.0
                ) -> list[tuple[int, tuple[int, int, int]]]:
    """读图，返回 [(像素数, RGB)]。

    用 PIL 的 getcolors 一次拿全部色与计数 —— 逐像素循环在几百万像素上太慢，
    而色数通常只有几万，在那个列表上算分布足够快。

    `crop` 按比例只留中间那一块。**这个参数是承重的**：截图外围是网页界面（深色主题的
    底色与文字），而那批几乎无彩的深灰会挤进主色前几名、把彩度分布整体拉灰。
    裁与不裁各跑一次、看分布差多少，是判断「这份数据干净不干净」的唯一办法。
    """
    try:
        from PIL import Image
    except ImportError:
        raise SystemExit("[FAIL] 需要 Pillow：pip install -r tools/requirements.txt")
    if not 0.1 <= crop <= 1.0:
        raise SystemExit("[FAIL] --crop 要在 0.1 到 1.0 之间")
    out: Counter[tuple[int, int, int]] = Counter()
    for p in paths:
        with Image.open(p) as im:
            rgb = im.convert("RGB")
            if crop < 1.0:
                w, h = rgb.width, rgb.height
                dw, dh = round(w * (1 - crop) / 2), round(h * (1 - crop) / 2)
                rgb = rgb.crop((dw, dh, w - dw, h - dh))
            colors = rgb.getcolors(1 << 24)
            if colors is None:
                raise SystemExit(f"[FAIL] {p.name} 的色数超过上限，读不动")
            px = rgb.width * rgb.height
            print(f"  {p.name:16s} {rgb.width}×{rgb.height}  {px:>9,d} 像素  "
                  f"{len(colors):>6,d} 个不同色")
            for count, c in colors:
                out[c] += count
    return [(n, c) for c, n in out.items()]


def histogram(label: str, buckets: list[tuple[str, int]], total: int) -> None:
    """打一张占比直方图。宽度固定 40 格，所以两张图能并排着读。"""
    print(f"  {label}")
    for name, n in buckets:
        pct = n / total * 100 if total else 0.0
        bar = "█" * round(pct / 2.5)
        print(f"    {name:>12s} {pct:5.1f}%  {bar}")


def cmd_image(target: str, top: int, crop: float, only: str) -> int:
    """统计一批参考图的色彩分布，并与现有色板按同一口径并排对比。

    ⚠️ **两件事要分清**：像素加权的分布答「这张图看起来什么感觉」，而色板答「有哪些色可用」。
    一张图里六成是地面，地面色的占比就高 —— 那不代表色板该给地面六成的档。所以本命令
    报两样：像素分布（看感觉）与主色清单（看色板缺什么）。

    截图里混着界面：纯黑背景与白色文字会把分布拉偏，所以那两类单独报、不进分布。
    """
    root = Path(target)
    paths = sorted(root.rglob("*.png")) if root.is_dir() else [root]
    if not paths:
        raise SystemExit(f"[FAIL] {target} 下找不到 png")

    print(f"读 {len(paths)} 张图"
          f"{f'，只取中间 {crop:.0%}' if crop < 1.0 else '（整张，含界面）'}：")
    data = load_pixels(paths, crop)

    total_all = sum(n for n, _ in data)
    ui_black = sum(n for n, c in data if luma(c) < 15)
    ui_white = sum(n for n, c in data if luma(c) > 240 and chroma(c) < 0.05)
    body = [(n, c) for n, c in data if not (luma(c) < 15 or (luma(c) > 240 and chroma(c) < 0.05))]
    total = sum(n for n, _ in body)
    print(f"\n共 {total_all:,d} 像素。**剔掉界面那两类再统计**："
          f"近黑 {ui_black / total_all * 100:.1f}%、无彩近白 {ui_white / total_all * 100:.1f}%")
    print(f"剩 {total:,d} 像素（{total / total_all * 100:.1f}%）进下面的分布。")
    print("⚠️ 近黑那一档里混着两样东西：截图的页面底色，与画面里真正的暗部（洞口、深水）。")
    print("   这个命令分不开它们 —— 要分开只能先把图裁到画面区域再跑。\n")

    print("=== 参考图 ===")
    cb = [("0.00–0.05", 0), ("0.05–0.10", 0), ("0.10–0.15", 0), ("0.15–0.20", 0),
          ("0.20–0.30", 0), ("0.30–0.40", 0), ("0.40 以上", 0)]
    edges = [0.05, 0.10, 0.15, 0.20, 0.30, 0.40]
    lb = [("0–40", 0), ("40–80", 0), ("80–120", 0), ("120–160", 0),
          ("160–200", 0), ("200 以上", 0)]
    ledges = [40, 80, 120, 160, 200]
    for n, c in body:
        ch, lu = chroma(c), luma(c)
        i = next((k for k, e in enumerate(edges) if ch < e), len(edges))
        cb[i] = (cb[i][0], cb[i][1] + n)
        j = next((k for k, e in enumerate(ledges) if lu < e), len(ledges))
        lb[j] = (lb[j][0], lb[j][1] + n)
    histogram("彩度分布（按像素加权）", cb, total)
    histogram("明度分布（按像素加权）", lb, total)

    entries = read_palette()
    if only:
        want = {s.strip() for s in only.split(",") if s.strip()}
        have = {k.rsplit(" ", 1)[0] for k in entries}
        missing = want - have
        if missing:
            raise SystemExit(f"[FAIL] 色板里没有这几条色带：{sorted(missing)}")
        entries = {k: v for k, v in entries.items() if k.rsplit(" ", 1)[0] in want}
        print(f"\n=== 现有色板的 {len(want)} 条指定色带（{len(entries)} 色）===")
        print("  ⚠️ **这个对比仍然不对等**：参考图按像素加权（地面占六成就算六成），")
        print("     色板按每色等权。只能看大势，不能拿两边的百分比直接相减。\n")
    else:
        print("\n=== 现有色板（全部 152 色，每色等权）===")
        print("  ⚠️ **这个对比不对等**，而且偏差的方向是「让色板看起来比实际更艳」——")
        print("     宝石与炭火那些小面积色在这里和地面色一样重，而画面上它们只占几个像素。")
        print("     要比大面材质就加 --only，把点睛与特效那几条排掉。\n")
    pcb = [(name, 0) for name, _ in cb]
    plb = [(name, 0) for name, _ in lb]
    for c in entries.values():
        ch, lu = chroma(c), luma(c)
        i = next((k for k, e in enumerate(edges) if ch < e), len(edges))
        pcb[i] = (pcb[i][0], pcb[i][1] + 1)
        j = next((k for k, e in enumerate(ledges) if lu < e), len(ledges))
        plb[j] = (plb[j][0], plb[j][1] + 1)
    histogram("彩度分布", pcb, len(entries))
    histogram("明度分布", plb, len(entries))

    print(f"\n=== 参考图的前 {top} 个主色（量化到每通道 32 级后按像素数排）===")
    print("  量化是为了把缩放与压缩产生的中间色并回去 —— 不并的话前几名全是同一个色的邻居。\n")
    q: Counter[tuple[int, int, int]] = Counter()
    rep: dict[tuple[int, int, int], list[int]] = {}
    for n, c in body:
        key = (c[0] >> 3, c[1] >> 3, c[2] >> 3)
        q[key] += n
        acc = rep.setdefault(key, [0, 0, 0, 0])
        acc[0] += c[0] * n
        acc[1] += c[1] * n
        acc[2] += c[2] * n
        acc[3] += n
    print(f"  {'占比':>6s}  {'代表色':>8s}  {'彩度':>5s} {'明度':>5s} {'色相':>6s}  离色板最近的那一项")
    for key, n in q.most_common(top):
        acc = rep[key]
        c = (round(acc[0] / acc[3]), round(acc[1] / acc[3]), round(acc[2] / acc[3]))
        name, _, dist = nearest(c, entries)
        print(f"  {n / total * 100:5.2f}%  {show(c):>8s}  {chroma(c):5.3f} {luma(c):5.1f} "
              f"{hue_deg(c):5.0f}°  {name:14s} {dist:5.1f} → {verdict(dist)}")
    return 0


def cmd_split(wanted: list[str], out: str) -> int:
    """从主表切一份只含指定族或色带的小色板，供 Aseprite 画单个物件时载入。

    **切出来的是派生物，不是第二份真相。** 改色一律改 `palette.gpl` 再重切一次 ——
    所以切出来的文件写在 `temp/` 下、**用完即删**，而且头部写明它是生成的、手改会被下次
    覆盖。手工拆成几份长期文件的做法刻意不走：那是 N 份真相，改一处漏一处不报错，
    而跨材质的那些关系（灰烬当不了钢铁、铜的暗端挨着炭火、宝石红要与果实红分得开）
    只有在一张表里才看得见。
    """
    entries = read_palette()
    families = read_families()
    all_ramps = [r for _, rs in families for r in rs]

    picked: list[str] = []
    for token in wanted:
        hit_fam = [(name, rs) for name, rs in families if token in name]
        if len(hit_fam) > 1:
            raise SystemExit(f"[FAIL] 「{token}」同时像这几族：{[n for n, _ in hit_fam]}，说得再具体些")
        if hit_fam:
            name, rs = hit_fam[0]
            print(f"  族「{name}」→ {'、'.join(rs)}")
            picked.extend(rs)
            continue
        if token in all_ramps:
            print(f"  色带「{token}」")
            picked.append(token)
            continue
        raise SystemExit(f"[FAIL] 「{token}」既不是族名也不是色带名。\n"
                         f"  族：{'、'.join(n for n, _ in families)}\n"
                         f"  色带：{'、'.join(all_ramps)}")

    # 去重但保持主表顺序 —— 切出来的小表里，色带之间的先后仍然照主表走
    keep = [r for r in all_ramps if r in set(picked)]
    rows = [(name, rgb) for name, rgb in entries.items()
            if name.rsplit(" ", 1)[0] in set(keep)]

    # 默认总是同一个文件名：临时件不该攒一堆，而且族名是中文、进文件名会在终端里乱码。
    # 要同时留几份（画草地一份、画水一份）就各给一个 --out。
    path = Path(out) if out else (ROOT.parent / "temp" / "palette-split.gpl")
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "GIMP Palette",
        f"Name: Tinderhearth {' + '.join(wanted)}",
        "Columns: 8",
        "#",
        "# ⚠️ 这个文件是 tools/palette_probe.py split 从 assets/self-drawn/palette.gpl 切出来的。",
        "# **不要手改**：改了不会回到主表，而下次切会把它覆盖掉。要改色就去改主表再切一次。",
        "# 它是临时件，画完即删 —— 色值的唯一来源始终是那张主表。",
        f"# 这一份含 {len(keep)} 条色带、{len(rows)} 色：{'、'.join(keep)}",
        "#",
    ]
    for name, rgb in rows:
        lines.append(f"{rgb[0]:3d} {rgb[1]:3d} {rgb[2]:3d}\t{name}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"\n切出 {len(keep)} 条色带、{len(rows)} 色 → {path}")
    print("  在 Aseprite 里：色板右上角菜单 → Load Palette → 选这个文件。")
    print("  ⚠️ 画完删掉它。色值改了要回主表改，然后重切。")
    print("  ⚠️ 再切一次会覆盖这一份 —— 要同时留几份就各给一个 --out。")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="色板探针：加色带之前问两个问题，外加切分表")
    sub = ap.add_subparsers(dest="cmd")

    p_split = sub.add_parser("split", help="从主表切一份小色板（按族名或色带名）")
    p_split.add_argument("wanted", nargs="+", metavar="族名或色带名")
    p_split.add_argument("--out", default="", help="输出路径（默认写到工作区根的 temp/ 下）")

    p_img = sub.add_parser("image", help="统计一批参考图的色彩分布，与现有色板并排对比")
    p_img.add_argument("target", help="图片文件或装着图片的目录")
    p_img.add_argument("--top", type=int, default=24, help="列前几个主色（默认 24）")
    p_img.add_argument("--crop", type=float, default=1.0,
                       help="只取中间这个比例的一块，用来排掉截图外围的界面（默认 1.0 不裁）")
    p_img.add_argument("--only", default="",
                       help="色板那一侧只比这几条色带（逗号分隔）。"
                            "比大面材质时用它把点睛与特效排掉，否则对比偏艳")

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
    if args.cmd == "split":
        return cmd_split(args.wanted, args.out)
    if args.cmd == "image":
        return cmd_image(args.target, args.top, args.crop, args.only)
    if args.cmd == "near":
        return cmd_near(args.colors)
    if args.cmd == "ramp":
        return cmd_ramp(args.hue, args.steps, args.lum_lo, args.lum_hi,
                        args.chroma_lo, args.chroma_hi)
    return cmd_ramps()


if __name__ == "__main__":
    raise SystemExit(main())
