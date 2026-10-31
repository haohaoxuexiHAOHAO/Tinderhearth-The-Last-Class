"""色板的按需工具：加色带之前问两个问题，外加两件画画时要用的事。

**它不是守卫，不进 verify.py。** 既有色板里本来就有刻意接受的近邻（烧红的铜与炭火在暗端
就该挨着），做成守卫会一直报，而我们会学会忽略它。口径与那两个问题的顺序在设计仓
`production/像素绘制原则.md` 的「本项目的色带口径」。

两个问题：

  一、**这个色我取得到吗** —— 给几个目标色，报它们在色板里的最近邻与距离。
       距离大就说明色板这一片没有人，画那样东西时无色可取。
  二、**这条新色带撞不撞** —— 按色相生成一条候选，逐档报最近邻。
       撞上的后果是两份近似色并存，迟早各画一半素材，而没有任何东西会报错。
       改了一条**已有**色带的值之后用 `check`：它把那条自己排掉再比 —— `near` 拿整张表比，
       查表里已有的色只会报出它自己、距离 0。

⚠️ **距离档（已有／凑得出／真缺）是人定的判断，不是实测出来的阈值**，所以本脚本一律把
距离本身打出来。另有一条更要紧的：**拿真实照片的色当目标会判错** —— 真实宝石的彩度远高于
本项目的基调，追着它走会得出「全都缺」。判据是「这条色带读不读得出是什么材质」。

另外两件是画画时用的：**统计一批图的色彩分布**（参考图与自己画的素材都能跑，用来核
「大面材质的主色有没有堆在亮端」），与**从主表切一份小色板**（Aseprite 一次只用一个色板，
整张表色太多、找起来慢）。

还有一件是改色板时用的：**算半档**。半档是两边整数档的中点、不手调，所以整数档一改就要重算 ——
`half` 把那几行算好打出来；忘了重算的话，读色板的 `palettelib.read_steps` 会报出是哪一个。

用法（从代码仓根目录跑）：
    python tools/palette_probe.py                              列现有色带的区间与色相分布
    python tools/palette_probe.py near "#6E767E" "#3A3F45"     查这几个色取不取得到
    python tools/palette_probe.py check spirit                 改了一条色带的值之后，查它离别的色带多近
    python tools/palette_probe.py ramp 210 5 46 232 0.04 0.11  生成一条候选并查撞色
    python tools/palette_probe.py half gemred                  算一条色带每两档之间的半档，可直接贴进 .gpl
    python tools/palette_probe.py image temp/ref --crop 0.7    统计一批图的色彩分布
    python tools/palette_probe.py split 植被 土木                切一份小色板
    python tools/palette_probe.py split gemred --coarse        切一份只有整数档的（画一个基础单位的小件用）

本脚本没有自证入口。
"""

from __future__ import annotations

import argparse
import sys
from collections import Counter
from pathlib import Path

from palettelib import (NEAR, ROOT, chroma, distance, half_step, hue_deg, is_half,
                        luma, make_ramp, nearest, read_families, read_palette,
                        read_ramps, read_steps, verdict)

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
    """列现有色带：档数、明度区间、彩度峰值、色相，外加有没有半档。

    彩度峰值那一列是定上限时唯一要看的东西 —— 新色带该不该比它艳，得先知道它是多少。
    色相那一列答「借得到吗」的前半步：想画一件黄的东西，先看表里这一片色相上有哪几条。
    除了「半档」「含半档」那两列，各列都只按整数档量 —— 那是画小件也在用的那一套。
    """
    ramps = read_ramps()
    steps = read_steps()
    total = sum(len(v) for v in steps.values())
    halves = total - sum(len(v) for v in ramps.values())
    print(f"色板共 {total} 色（其中半档 {halves} 色）、{len(ramps)} 条色带\n")
    print(f"{'色带':14s} {'档':>3s} {'半档':>4s}  {'明度区间':>13s}  {'彩度峰值':>9s}  {'峰值档':>5s}  "
          f"{'相邻明度差':>10s}  {'含半档':>9s}  {'色相':>6s}  冷暖走向")
    for name, cols in ramps.items():
        lums = [luma(c) for c in cols]
        chromas = [chroma(c) for c in cols]
        peak = max(range(len(chromas)), key=lambda i: chromas[i])
        diffs = [lums[i + 1] - lums[i] for i in range(len(lums) - 1)]
        gap = f"{min(diffs):.0f}–{max(diffs):.0f}" if diffs else "—"
        nh = len(steps[name]) - len(cols)
        full = [luma(c) for _, c in steps[name]]
        fdiffs = [full[i + 1] - full[i] for i in range(len(full) - 1)]
        fgap = f"{min(fdiffs):.0f}–{max(fdiffs):.0f}" if nh else "—"
        h = ramp_hue(cols)
        hcol = "无彩" if h is None else f"{h:.0f}°"
        print(f"{name:14s} {len(cols):>3d} {(nh or '—')!s:>4s}  {lums[0]:>5.0f} → {lums[-1]:<5.0f}  "
              f"{chromas[peak]:>9.3f}  {peak + 1:>5d}  {gap:>10s}  {fgap:>9s}  {hcol:>6s}  {warmth(cols)}")
    print("\n半档是两边整数档的中点，画得比一个基础单位大时才用；「—」是那条没有半档 ——")
    print("  哪些有、为什么在设计仓 production/像素绘制原则.md 的「本项目的色带口径」。")
    print("\n冷暖那一列按每档的 R−B 定（正为暖、负为冷）。**一条色带中途翻冷暖是要注意的**：")
    print("  它对有些材质是对的（混凝土暗处冷、受光面暖），但钢不行 —— 暖色调的世界里钢只有用冷色")
    print("  才和金、铜分得开，翻暖之后会读成旧锡或水泥。")
    hue_spread(ramps)
    return 0


def ramp_hue(cols: list[tuple[int, int, int]]) -> float | None:
    """一条色带的代表色相，低彩度的档不参与平均；整条都发灰时返回 None。

    发灰的色算出来的色相是噪声（`winter` 各档会报出一串互不相干的角度），拿它排进
    色环会凭空多出几条「占了位」的色带。所以中性那几条宁可不给色相。
    """
    hues = [hue_deg(c) for c in cols if chroma(c) >= 0.12]
    if not hues:
        return None
    return sum(hues) / len(hues)


def hue_spread(ramps: dict[str, list[tuple[int, int, int]]]) -> None:
    """按色相绕色环排一遍，报每条到下一条的间距。

    这一段要有，是因为上面那张表一条一条看不出分布：条数相当的两片色相，挤在一处的那几条
    只换得到对比度、换不到色相，铺得开的那几条每条都读作另一种颜色 —— 差别全在色相间距上，
    **而加色带那两问答不出这件事**，它们只判一个具体色有没有被占。

    ⚠️ **色相角上的空隙不等于 RGB 空间里的空隙。** 邻居的色带够长时，它的亮档会漫进
    空隙里；反过来一组明度区间撞了，错开一截明度往往就不撞。所以这一段只告诉你「哪一片挤、
    哪一片空」，空的那片能不能填还得跑 `ramp` 查撞色，并且换几组明度区间。
    """
    rows = [(h, n, len(c)) for n, c in ramps.items() if (h := ramp_hue(c)) is not None]
    rows.sort()
    neutral = [n for n, c in ramps.items() if ramp_hue(c) is None]
    print(f"\n色相分布（{len(rows)} 条有彩色带绕色环排一圈）")
    print(f"{'色相':>6s}  {'色带':14s} {'档':>3s}  {'到下一条':>9s}")
    for i, (h, name, steps) in enumerate(rows):
        nxt = rows[(i + 1) % len(rows)][0]
        gap = (nxt - h) % 360
        print(f"{h:>5.0f}°  {name:14s} {steps:>3d}  {gap:>8.0f}°")
    widest = sorted(
        ((rows[(i + 1) % len(rows)][0] - h) % 360, name, rows[(i + 1) % len(rows)][1])
        for i, (h, name, _) in enumerate(rows)
    )[-3:]
    print("\n间距最大的三处：" + "、".join(
        f"{a} → {b} 之间 {g:.0f}°" for g, a, b in reversed(widest)))
    print(f"低彩度那几条不进色环（色相算出来是噪声）：{'、'.join(neutral)}")
    print("⚠️ **色相角上空着不等于那一片填得进去** —— 邻居色带的亮档会漫进来，")
    print("  要填先跑 `ramp <色相> …` 查撞色，并换几组明度区间：一组撞了不等于这个色相没地方。")
    print("  反过来「这一片挤」是这一段判得死的事。")


def warmth(cols: list[tuple[int, int, int]]) -> str:
    """把一条色带的冷暖走向压成一行，并点出它在哪一档翻了。

    这一列要有，是因为翻冷暖只看色块看不出来：`ash` 中段从冷翻暖，照它画的钢铁亮部
    会发米灰，而色板上那一排格子看着毫无异样。
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


def cmd_check(ramp_name: str) -> int:
    """一条已有色带逐档离色板里别的色带多近 —— 它自己那条除外。

    这一个要单独有，是因为 `near` 拿整张表比：查一个已经在表里的色，报出来的最近邻就是
    它自己、距离 0。**改了一条已有色带的值之后**要问的是「它现在离别人多近」，而那只有
    把它自己那条排掉才答得出来。
    """
    steps = read_steps()
    if ramp_name not in steps:
        raise SystemExit(f"[FAIL] 色板里没有「{ramp_name}」这条色带，有的是：{'、'.join(steps)}")
    others = {k: v for k, v in read_palette().items() if k.rsplit(" ", 1)[0] != ramp_name}
    print(f"「{ramp_name}」逐档的最近邻（它自己那条除外，半档也列）。"
          f"距离是 RGB 欧氏距离，档是人定的判断 —— 自己看数。\n")
    worst: tuple[float, str] | None = None
    for label, rgb in steps[ramp_name]:
        name, _, dist = nearest(rgb, others)
        print(f"  第 {label:>3s} 档 {show(rgb)}  明度 {luma(rgb):5.1f}  彩度 {chroma(rgb):.3f}  "
              f"色相 {hue_deg(rgb):5.1f}°  → {name:16s} {dist:5.1f}  {verdict(dist)}")
        if worst is None or dist < worst[0]:
            worst = (dist, f"第 {label} 档 vs {name}")
    assert worst is not None
    print(f"\n最近的一对：{worst[1]}，距离 {worst[0]:.1f}")
    if any(is_half(label) for label, _ in steps[ramp_name]):
        print("  半档离别的色带近也照留：它只在本条里用，不会变成「同一种东西两份近似色」"
              "（口径在像素绘制原则「本项目的色带口径」）。")
    return 0


def cmd_half(ramp_name: str) -> int:
    """算一条色带每两档之间的半档，打成可直接贴进 .gpl 的行，并报它离别的色带多近。

    半档是两边整数档的中点（理由在 `palettelib.half_step`），**不手调**：整数档一改就重跑这个，
    把那几行换掉。色板里已有的半档会标出对不对得上。

    这里刻意只用 `read_palette`、不用 `read_steps`：后者遇到算错的半档会停，而这个命令
    恰好是用来修它的。
    """
    entries = read_palette()
    cols: list[tuple[int, int, int]] = []
    while f"{ramp_name} {len(cols) + 1}" in entries:
        cols.append(entries[f"{ramp_name} {len(cols) + 1}"])
    if len(cols) < 2:
        raise SystemExit(f"[FAIL] 色板里没有「{ramp_name}」这条色带，或者它只有一档")
    others = {k: v for k, v in entries.items() if k.rsplit(" ", 1)[0] != ramp_name}
    print(f"「{ramp_name}」每两档之间的半档（两档中点）—— 每一行插在它两边那两档中间：\n")
    for i in range(len(cols) - 1):
        h = half_step(cols[i], cols[i + 1])
        label = f"{i + 1}.5"
        have = entries.get(f"{ramp_name} {label}")
        mark = "已在色板里" if have == h else ("色板里的值不对，换成这一行" if have else "色板里还没有")
        name, _, dist = nearest(h, others)
        print(f"{h[0]:3d} {h[1]:3d} {h[2]:3d}\t{ramp_name} {label}"
              f"   # {show(h)}  明度 {luma(h):5.1f}（离两边 {luma(h) - luma(cols[i]):4.1f}"
              f"／{luma(cols[i + 1]) - luma(h):4.1f}）  最近 {name} {dist:5.1f}  {mark}")
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
    print("  太近说明这一组明度与彩度上已经有人了 —— 先问「借得到吗」，别新开两份近似色。")
    # 这一行要有：撞了一组就判「这个色相满了」是会犯的错 —— 实测纯黄压在作物黄那段明度上撞、
    # 整条往亮处错开一截就不撞，而缺的恰好是亮的那一截。
    print("  ⚠️ 它判的只是这一组：同一个色相换一段明度区间往往就不撞，下结论前换几组再跑。")
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
        print(f"\n=== 现有色板（全部 {len(entries)} 色，每色等权）===")
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


def cmd_split(wanted: list[str], out: str, coarse: bool) -> int:
    """从主表切一份只含指定族或色带的小色板，供 Aseprite 画单个物件时载入。

    `coarse` 只留整数档：半档在 Aseprite 里夹在两档中间，画一个基础单位的小件时
    得自己分出哪几格是整数档，切掉它们就不用分了。

    **切出来的是派生物，不是第二份真相。** 改色一律改 `palette.gpl` 再重切一次 ——
    所以切出来的文件写在 `temp/` 下、**用完即删**，而且头部写明它是生成的、手改会被下次
    覆盖。手工拆成几份长期文件的做法刻意不走：那是 N 份真相，改一处漏一处不报错，
    而跨材质的那些关系（灰烬当不了钢铁、铜的暗端挨着炭火、宝石红要与果实红分得开）
    只有在一张表里才看得见。
    """
    entries = read_palette()
    read_steps()    # 只为它那几条校验：算错的半档不该被切进小表里带走
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
            if name.rsplit(" ", 1)[0] in set(keep) and not (coarse and is_half(name))]

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
        ("# 只有整数档（切的时候加了 --coarse），画一个基础单位的小件用。" if coarse else
         "# 整数档与半档（档号带 .5）都在、按明暗排；画一个基础单位的小件只用整数档。"),
        "#",
    ]
    for name, rgb in rows:
        lines.append(f"{rgb[0]:3d} {rgb[1]:3d} {rgb[2]:3d}\t{name}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"\n切出 {len(keep)} 条色带、{len(rows)} 色{'（只有整数档）' if coarse else ''} → {path}")
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
    p_split.add_argument("--coarse", action="store_true",
                         help="只切整数档、不要半档（画一个基础单位的小件用）")

    p_half = sub.add_parser("half", help="算一条色带每两档之间的半档（两档中点），可直接贴进 .gpl")
    p_half.add_argument("ramp", metavar="色带名")

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

    p_check = sub.add_parser("check", help="查一条已有色带逐档离别的色带多近（它自己那条除外）")
    p_check.add_argument("ramp", metavar="色带名")

    p_ramp = sub.add_parser("ramp", help="按色相生成一条候选色带并查撞色")
    p_ramp.add_argument("hue", type=float, help="色相角（度）")
    p_ramp.add_argument("steps", type=int, help="档数")
    p_ramp.add_argument("lum_lo", type=float, help="最暗档的目标明度")
    p_ramp.add_argument("lum_hi", type=float, help="最亮档的目标明度")
    p_ramp.add_argument("chroma_lo", type=float, help="亮端彩度（高光趋白，所以这个小）")
    p_ramp.add_argument("chroma_hi", type=float, help="暗端彩度")

    args = ap.parse_args()
    if args.cmd == "split":
        return cmd_split(args.wanted, args.out, args.coarse)
    if args.cmd == "half":
        return cmd_half(args.ramp)
    if args.cmd == "image":
        return cmd_image(args.target, args.top, args.crop, args.only)
    if args.cmd == "near":
        return cmd_near(args.colors)
    if args.cmd == "check":
        return cmd_check(args.ramp)
    if args.cmd == "ramp":
        return cmd_ramp(args.hue, args.steps, args.lum_lo, args.lum_hi,
                        args.chroma_lo, args.chroma_hi)
    return cmd_ramps()


if __name__ == "__main__":
    raise SystemExit(main())
