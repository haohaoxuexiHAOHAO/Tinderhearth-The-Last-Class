#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""把收件箱里的逐帧角色 PNG 接成进仓精灵表（`ART-6`）。

**为什么要有这个入口而不是手工切图**：作者交来的一批是 91 张逐帧 PNG，精确 10 倍放大、每张
底部画了一条拉满整行的灰色地面参考线、线中央嵌一个品红锚点像素，而且十三个动作的画布尺寸有
四种（32×32／36×31／30×29／28×27）。
手工处理这三件事各有一种静默失效：降采样倍数猜错就永久损失像素、参考线留在图里就变成角色
脚下多一条灰边、按画布居中对齐会让不同动作的脚底差 1–4 个像素而**没有任何报错** —— 表现只是
「切换动作时角色轻微上下跳」，那是要盯着看才发现的那类缺陷。

四件事都做成可重跑的判定，做不到就报错退出，一件都不猜：

1. **降采样倍数自己探测。** 取最大的 n 使整图每个 n×n 块同色（实现是 `check_assets.upscale_factor`，
   `ENG-10` 那一份，不写第二遍）。块内不同色或倍数除不尽宽高，那就不是无损放大件 —— 直接
   报错退出，不四舍五入、不猜。同一动作各帧还必须还原到同一个源尺寸。
2. **地面参考线按形状认，线里嵌的锚点像素按颜色认。** 参考线的判据是「整行**除锚点像素外**
   只有一种不透明颜色、且连成一段、长度 ≥4、位于画布底部 20% 内」，再要求这一行在**同一动作
   各帧都成立**，取最靠下的那一行。线本身不认颜色：闪白帧整张图只有白色、参考线也是白的，
   只认 (116,116,116) 会漏掉它们、把线留在图里。颜色仍然要报出来供人核对。
   锚点像素反过来**只认颜色**（`ANCHOR_COLOR`，实测 #FF00FF）—— 它就一个像素，没有形状可认。
3. **脚底行取参考线，水平列取锚点像素。** 参考线那一行就是作者标的地面，所有动作的这一行落到
   统一帧框的同一行。水平列**直接取锚点像素的列** —— 作者已经在图里标出来了，不必再从线的形状
   里推。三条硬判据，任一条不成立就报错退出、一个字节都不写：一帧里锚点像素恰好出现一次；
   锚点落在参考线所在行；同一动作各帧的锚点坐标完全一致。
4. **帧框取能容纳最宽动作的偶数尺寸，锚点居中。** 宽度 `2 × max(左伸, 右伸+1)`，高度取容得下
   最高动作、且把地面行包含在内的最小偶数。锚点因此在帧的正中，`FlipH` 才不会把角色甩偏。
5. **单色帧不接仓。** 去掉地面行后整帧只有一种不透明颜色的，那是一张闪白不是一个姿态 ——
   闪白由代码持有（`CombatFeel.FlashFrames`），画进精灵表等于同一件事有两个真相。判据按
   **形状**不按颜色（作者这批是白的，下一批可能是红的），报出真实帧号与真实颜色，原件不动。
6. **攻击动作量「伸出静止起手姿之外」的那部分。** 那就是打出去的那只拳，判定框的宽高由它导出
   （`CombatFeel.LightHitboxWidthWorldPx` 等），`tools/check_assets.py` 拿登记表逐条核常量 ——
   于是「素材改了而判定框没跟着改」不再是静默失效。

用法（从代码仓根目录运行）：

    python tools/import_role_sheets.py            # 重建精灵表与登记表的「自绘素材」一节
    python tools/import_role_sheets.py --check    # 只核对不写盘：进仓件与现在重算的是否逐字节一致
    python tools/import_role_sheets.py --inbox <目录>

**重跑结果稳定**：同一份收件箱重跑两次，PNG 逐字节相同、登记表一字不差 —— 所以 `--check` 是
一条有意义的判据（进仓件被手工改过会当场报出来）。收件箱只读，本入口不写它、不删它。

输出约定与 `verify.py` 一致：末尾打覆盖量与一行 `EXIT=`；日志自己写 UTF-8 落到
`logs/art/import-role-sheets-<时间戳>.log`，不靠 shell 重定向（设计仓踩坑记录第 27 条）。

依赖 Pillow（`tools/requirements.txt`）。缺了报错退出，不降级 —— 会静默跳过的守卫比没有更坏。
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_assets                                  # noqa: E402  路径插好才导得到

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
REGISTRY = ROOT / "tools" / "asset-registry.json"
LOG_DIR = ROOT / "logs" / "art"
DEFAULT_INBOX = ROOT.parent / "temp" / "art-inbox" / "self-material" / "test-role"

# 进仓落点。与 downloaded/（下载件）、placeholder/（脚本生成件）并列，第三类是作者自绘。
OUT_DIR = "self-drawn/test-role"
REGISTRY_SECTION = "自绘素材"

# 收件箱目录 → 进仓表名 + 用途 + 是不是攻击。**表名就是 PlayerActor 里的动画名，这个映射只此一处。**
# 轻重攻击的目录名带 fist_ 前缀（作者按招式命名），代码侧按连段类型命名，两边靠这张表对上。
# 「是不是攻击」只决定要不要量伸展：判定框宽高从那份测量导出（见 measure_reach），
# 走路跑步没有「打出去的那只拳」，量它只会往登记表里塞没人用的数。
#
# 2026-09-09 作者整批替换：`hit` 拆成 `light_hit`／`heavy_hit`，`defense` 拆成
# `general_defense`／`precise_defense`，另新增 `imbalance`（失衡）。后五张与 `death` 一样
# **只入仓备用、不载入 PlayerActor** —— 受击、防御、失衡与死亡的玩法都还不存在，载进来只会
# 出现「有图没规则」的半成品状态。
ACTIONS: tuple[tuple[str, str, str, bool], ...] = (
    ("idle", "idle", "待机", False),
    ("walk", "walk", "行走（低速位移）", False),
    ("run", "run", "奔跑与冲刺", False),
    ("jump", "jump", "跳跃与滞空", False),
    ("dodge", "dodge", "闪避翻滚", False),
    ("fist_light_attack", "light", "轻拳，三段连段共用同一组帧（占位映射）", True),
    ("fist_heavy_attack", "heavy", "重拳，两段连段共用同一组帧（占位映射）", True),
    ("light_hit", "light_hit", "轻受击。本轮未接进玩法，入仓备用", False),
    ("heavy_hit", "heavy_hit", "重受击。本轮未接进玩法，入仓备用", False),
    ("general_defense", "general_defense", "普通防御。本轮未接进玩法，入仓备用", False),
    ("precise_defense", "precise_defense", "精准防御。本轮未接进玩法，入仓备用", False),
    # imbalance 这一条的备注是**待办不是描述**：作者说失衡与失衡恢复画在同一目录的 5 帧里，
    # 但没给帧号区间。所以这里**不擅自切分** —— 猜一个 1–3／4–5 出来，代码里看不出是猜的，
    # 将来接玩法时会照着这个猜的数写状态机。要等作者给区间，再拆成两条登记。
    ("imbalance", "imbalance", "失衡与失衡恢复合在一张表，帧号区间待作者确认。"
                               "本轮未接进玩法，入仓备用", False),
    ("death", "death", "死亡。本轮未接进玩法，入仓备用", False),
)

# 地面参考线的判据只有形状，颜色只用来报告与核对（闪白帧的线是白的，见模块注释第 2 条）。
LINE_MIN_RUN = 4                 # 少于这么长的一段不当线：孤立几个同色像素太容易撞上肢体
LINE_BOTTOM_FRACTION = 5         # 只在画布最底下 1/5 里找 —— 地面不会画在角色胸口
CANONICAL_LINE_COLOR = (116, 116, 116, 255)

# 作者嵌在参考线里的水平锚点像素。**这个颜色是量出来的**：2026-09-09 逐帧扫全部 13 个动作
# 共 91 帧，(255,0,255,255) 每帧恰好出现 1 次、全部落在参考线所在行、全部在第 14 列；画面用色
# 里没有第二处品红（其余稀有色都是 ≥2 像素的高光，见「实测锚点」一节的笔记）。
#
# 为什么锚点认颜色而参考线认形状：一个像素没有形状可认，只能靠一种画面绝不会用到的颜色标出来；
# 而线有形状（横贯一段、只有一种颜色），认形状才能兼容「整帧闪白时线也是白的」那种情况。
ANCHOR_COLOR = (255, 0, 255, 255)

_LINES: list[str] = []
_FAILS: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


def fail(text: str) -> None:
    say(f"[FAIL] {text}")
    _FAILS.append(text)


def frame_index(path: Path) -> int:
    """从文件名尾部取帧号。认不出就报错 —— 猜顺序等于猜动画。"""
    m = re.fullmatch(r"[A-Za-z_]+?(\d+)", path.stem)
    if m is None:
        raise SystemExit(f"[FAIL] 文件名认不出帧号：{path.name}（要求 <动作名><序号>.png）")
    return int(m.group(1))


def reduce_lossless(path: Path):
    """按探测出来的最大整数倍数无损降采样，返回 (缩完的图, 倍数)。

    倍数由 `check_assets.upscale_factor` 给：它的判据就是「整图每个 n×n 块同色」，
    而那正好等价于「按 n 倍最近邻降采样不丢任何信息」—— 所以这里不需要第二个证明。
    """
    from PIL import Image
    im = check_assets.load_image(path)
    factor = check_assets.upscale_factor(im)
    if factor == 1:
        return im, 1
    w, h = im.size
    return im.resize((w // factor, h // factor), Image.NEAREST), factor


def line_candidates(im) -> list[tuple[int, int, int, tuple[int, int, int, int]]]:
    """这张图里形状像地面参考线的行：(行, 左端, 右端, 线色)。判据见模块注释第 2 条。

    「只有一种不透明颜色」这一条**把锚点像素排除在外再数**。2026-09-09 实测的缺陷就在这里：
    作者按约定在拉满的参考线中央嵌了一个 `ANCHOR_COLOR` 像素，那一行于是有两种颜色，旧判据
    `len(colors) == 1` 把它判成脏行，十三个动作全部报「没有一行在所有帧里都像地面参考线」。
    锚点是**新增的素材约定**，不是素材缺陷 —— 判据要容纳它。
    """
    w, h = im.size
    px = im.load()
    lowest = h - max(2, h // LINE_BOTTOM_FRACTION)
    out = []
    for y in range(lowest, h):
        run = [x for x in range(w) if px[x, y][3] != 0]
        if len(run) < LINE_MIN_RUN or run[-1] - run[0] + 1 != len(run):
            continue
        colors = {px[x, y] for x in run} - {ANCHOR_COLOR}
        if len(colors) == 1:
            out.append((y, run[0], run[-1], next(iter(colors))))
    return out


def anchor_pixels(im) -> list[tuple[int, int]]:
    """这张图里全部 `ANCHOR_COLOR` 像素的坐标。数量由调用方判死，这里只如实数。"""
    w, h = im.size
    px = im.load()
    return [(x, y) for y in range(h) for x in range(w) if px[x, y] == ANCHOR_COLOR]


def content_bbox(im, skip_row: int) -> tuple[int, int, int, int]:
    """跳过地面行之后的不透明内容包围盒。"""
    w, h = im.size
    px = im.load()
    xs, ys = [], []
    for y in range(h):
        if y == skip_row:
            continue
        for x in range(w):
            if px[x, y][3] != 0:
                xs.append(x)
                ys.append(y)
    if not xs:
        raise SystemExit("[FAIL] 去掉地面行之后这一帧什么都不剩")
    return min(xs), min(ys), max(xs), max(ys)


def opaque_colors(im, skip_row: int) -> set[tuple[int, int, int, int]]:
    """去掉地面行之后，这一帧用到的全部不透明颜色。"""
    w, h = im.size
    px = im.load()
    return {px[x, y] for y in range(h) if y != skip_row for x in range(w) if px[x, y][3] != 0}


class Frame:
    """一帧的测量结果。所有数都是量出来的，没有一处写死。"""

    def __init__(self, path: Path, im, factor: int) -> None:
        self.path = path
        self.number = frame_index(path)     # 源帧号，排除单色帧后仍要报得出「原来是第几帧」
        self.image = im
        self.factor = factor
        self.size = im.size
        self.line_row = -1
        self.line_span = (0, 0)
        self.line_color = (0, 0, 0, 0)
        self.anchor = (-1, -1)              # 作者嵌的锚点像素坐标，源画布内
        self.bbox = (0, 0, 0, 0)
        self.colors: set[tuple[int, int, int, int]] = set()

    @property
    def opaque(self) -> int:
        return sum(1 for a in self.image.getchannel("A").getdata() if a != 0)

    def line_pixels(self) -> int:
        return self.line_span[1] - self.line_span[0] + 1


def measure_action(folder: Path, sheet: str) -> tuple[list[Frame], list[Frame]]:
    """量一个动作的全部帧，返回 (进仓的帧, 被排除的单色帧)。

    做四件事：降采样、逐帧找线候选、定这个动作的地面行、把**单色帧**挑出来不接仓。

    单色帧为什么不接仓（2026-09-08 定，见设计仓 `production/像素绘制原则.md`）：闪白由代码
    持有，动画只提供受击姿态。一帧的不透明像素只有一种颜色时它表达不出姿态 —— 它就是一张
    闪白，而闪白已经由 `TrainingDummy.FlashRemaining` 与 `CombatFeel.FlashFrames` 在代码侧
    做了，可按轻重分别调时长、能跟顿帧一起冻结、能做成可关的无障碍项。把它再画进精灵表等于
    同一件事有两个真相，且画进去的那份**改不动**。

    判据按**形状**不按颜色：作者的 `hit` 第 3、4 帧是白的，下一个人可能画成红的或黑的，
    写死白色只拦得住已经见过的那一次。报的是**真实帧号与真实颜色**，不是「有几帧有问题」。
    """
    files = sorted(folder.glob("*.png"), key=frame_index)
    if not files:
        fail(f"{folder.name}：一个 PNG 都没有")
        return [], []
    numbers = [frame_index(p) for p in files]
    if numbers != list(range(1, len(files) + 1)):
        fail(f"{folder.name}：帧号不是 1..{len(files)} 连续，实际 {numbers} —— 顺序不猜")
        return [], []

    frames: list[Frame] = []
    candidates: list[set[int]] = []
    anchor_bad = False
    for path in files:
        im, factor = reduce_lossless(path)
        frame = Frame(path, im, factor)
        # §9 在源头就要过：这批是自绘件，半透明像素一律不放行（例外只对下载件，`ART-5`）。
        before = len(check_assets._FAILS)                            # noqa: SLF001
        check_assets.check_alpha(f"{folder.name}/{path.name}", im)
        if len(check_assets._FAILS) != before:                       # noqa: SLF001
            _FAILS.append(f"{path.name} 半透明像素")
        # 硬判据一：一帧里锚点像素恰好一个。零个说明这帧漏标（水平列就没有依据），
        # 两个以上说明画面里另有品红（那时得换锚点色，而不是猜该用哪一个）。
        found = anchor_pixels(im)
        if len(found) != 1:
            anchor_bad = True
            fail(f"{folder.name}/{path.name}：锚点像素 {ANCHOR_COLOR} 出现 {len(found)} 次"
                 f"（期望恰好 1 次），坐标 {found[:8]} —— 水平锚点只认这个像素，"
                 f"零个就没有依据、多个就无从取舍")
        else:
            frame.anchor = found[0]
        cands = line_candidates(im)
        candidates.append({y for y, _, _, _ in cands})
        frame._cands = {y: (a, b, c) for y, a, b, c in cands}        # noqa: SLF001
        frames.append(frame)

    sizes = {f.size for f in frames}
    if len(sizes) != 1:
        fail(f"{folder.name}：各帧还原后的源尺寸不一致 {sorted(sizes)} —— 对不出统一帧框")
        return [], []
    if anchor_bad:
        return [], []

    # 硬判据三：同一动作各帧的锚点坐标必须完全一致。不一致就意味着「同一动作内位移恒定」
    # 这条前提不成立 —— 帧内构图会逐帧横跳，而那是要盯着看才发现的那类缺陷。
    spots = {f.anchor for f in frames}
    if len(spots) != 1:
        fail(f"{folder.name}：各帧锚点坐标不一致 "
             f"{[(f.path.name, f.anchor) for f in frames]} —— 同一动作内位移必须恒定，"
             f"请把锚点像素放到逐帧同一位置")
        return [], []
    anchor_x, anchor_y = next(iter(spots))

    common = set.intersection(*candidates)
    if not common:
        fail(f"{folder.name}：没有一行在所有帧里都像地面参考线 —— 认不出脚底行，"
             f"逐帧候选 {[sorted(c) for c in candidates]}")
        return [], []
    row = max(common)                       # 最靠下的那一行才是地面，上面的可能是躺平的肢体
    # 硬判据二：锚点必须落在参考线所在行。参考线给脚底行、锚点给水平列，两者是同一个锚点的
    # 两个分量；分居两行就说明其中一个标错了，而错了之后画面只是「脚底差一点」，不报错。
    if anchor_y != row:
        fail(f"{folder.name}：锚点像素在第 {anchor_y} 行，地面参考线在第 {row} 行 —— "
             f"锚点必须嵌在参考线里，否则脚底行与水平列各说一套")
        return [], []
    for frame in frames:
        a, b, color = frame._cands[row]                              # noqa: SLF001
        frame.line_row = row
        frame.line_span = (a, b)
        frame.line_color = color
        frame.bbox = content_bbox(frame.image, row)
        frame.colors = opaque_colors(frame.image, row)
        if frame.bbox[3] > row:
            fail(f"{folder.name}/{frame.path.name}：地面行 {row} 下面还有内容"
                 f"（内容到第 {frame.bbox[3]} 行）—— 脚底锚点不成立")

    solid = [f for f in frames if len(f.colors) == 1]
    keep = [f for f in frames if len(f.colors) > 1]
    for f in solid:
        say(f"  [排除] {folder.name}/{f.path.name}（源帧 {f.number}）：去掉地面行后只有一种"
            f"不透明颜色 {next(iter(f.colors))}、共 {f.opaque - f.line_pixels()} 像素 —— "
            f"这是一张闪白不是受击姿态，闪白由代码持有（CombatFeel.FlashFrames），不接仓")
    if not keep:
        fail(f"{folder.name}：全部 {len(frames)} 帧都是单色帧 —— 一帧姿态都没有，接不了")
        return [], solid
    return keep, solid


def build(inbox: Path) -> tuple[dict[str, list[Frame]], dict[str, list[Frame]],
                               dict[str, int], dict[str, int]]:
    """量全部动作，算出统一帧框。返回 (进仓帧, 排除的单色帧, 各动作水平锚点, 帧框参数)。"""
    actions: dict[str, list[Frame]] = {}
    dropped: dict[str, list[Frame]] = {}
    for folder_name, sheet, _, _ in ACTIONS:
        folder = inbox / folder_name
        if not folder.is_dir():
            fail(f"收件箱里没有 {folder_name}/ —— 登记表要求的动作缺一个")
            continue
        frames, solid = measure_action(folder, sheet)
        if solid:
            dropped[sheet] = solid
        if frames:
            actions[sheet] = frames

    extra = sorted(p.name for p in inbox.iterdir()
                   if p.is_dir() and p.name not in {a[0] for a in ACTIONS})
    if extra:
        fail(f"收件箱多出没登记的动作目录 {extra} —— 接仓映射只认 ACTIONS 那张表，"
             f"多的要么加进表要么说明为什么不接")

    # 水平锚点直接取作者标的锚点像素那一列。measure_action 已经判死「一帧恰好一个、落在参考线
    # 那一行、各帧完全一致」，所以这里不需要再统计、也没有取整规则可讨论。
    anchors: dict[str, int] = {sheet: frames[0].anchor[0] for sheet, frames in actions.items()}

    left = right = top = 0
    below = -10 ** 9
    for sheet, frames in actions.items():
        ax = anchors[sheet]
        for f in frames:
            x0, y0, x1, y1 = f.bbox
            left = max(left, ax - x0)
            right = max(right, x1 - ax)
            top = max(top, f.line_row - y0)
            below = max(below, y1 - f.line_row)
    width = 2 * max(left, right + 1)
    ground = top
    height = ground + max(below, 0) + 1
    if height % 2:
        height += 1
    box = {"width": width, "height": height, "ground": ground, "anchor": width // 2,
           "left": left, "right": right, "top": top}
    return actions, dropped, anchors, box


def compose(frames: list[Frame], anchor_x: int, box: dict[str, int]):
    """拼一张横向精灵表：逐帧去掉地面行（锚点像素也在那一行，一并去掉）、按锚点平移进统一帧框。

    去线与去锚点是同一次操作：硬判据二保证锚点嵌在参考线里，所以清掉整行就把它带走了。
    不靠推理收尾 —— 拼完再扫一遍成品，还剩一个 `ANCHOR_COLOR` 就报错，那才是执行体。
    """
    from PIL import Image
    w, h = box["width"], box["height"]
    sheet = Image.new("RGBA", (w * len(frames), h), (0, 0, 0, 0))
    for i, frame in enumerate(frames):
        src = frame.image.copy()
        sw, _ = src.size
        for x in range(sw):
            src.putpixel((x, frame.line_row), (0, 0, 0, 0))
        dx = box["anchor"] - anchor_x
        dy = box["ground"] - frame.line_row
        sheet.paste(src, (i * w + dx, dy))
        # 平移量算错就会静默裁掉手脚，所以数一遍不透明像素对不对得上。
        # 源的不透明像素里，整条参考线（含嵌在里面的那一个锚点像素）都被清掉了。
        want = frame.opaque - frame.line_pixels()
        cell = sheet.crop((i * w, 0, (i + 1) * w, h))
        got = sum(1 for a in cell.getchannel("A").getdata() if a != 0)
        if got != want:
            fail(f"{frame.path.name}：进帧框后不透明像素 {got} ≠ 源 {want} —— 平移把内容裁掉了")
    left = anchor_pixels(sheet)
    if left:
        fail(f"{frames[0].path.parent.name}：进仓件里还剩 {len(left)} 个锚点像素 "
             f"{ANCHOR_COLOR} @ {left[:8]} —— 去线机制没把它带走，那个品红点会画到玩家眼前")
    return sheet


def png_bytes(image) -> bytes:
    import io
    buf = io.BytesIO()
    image.save(buf, format="PNG")
    return buf.getvalue()


def measure_reach(sheet_img, count: int, box: dict[str, int]) -> tuple[int, list[dict]]:
    """量「伸出静止起手姿之外」的那部分：判定框宽高就是从这里导出的。

    返回 `(基线列, 逐帧伸展)`。基线取**这个动作自己的第 1 帧**（作者画的静止起手姿）的右
    边界；每帧只量超过这条基线的不透明像素 —— 那正好是打出去的那只拳，不含身体。

    为什么需要它：判定框此前轻重共用 28×28，而画面上轻拳与重拳伸出的距离本来就不一样
    （2026-09-09 这批实测轻 15px、重 21px）。「重击打得更远却和轻击同框」在代码里看不出
    问题、也不报错，只在手上表现为「重击不实」。要让这两个数指回事实，就得有一处**从图里
    量出来**的记录 —— 而且它要跟着素材走：作者换一批画，这两个数就该跟着变，不该靠人记得。
    `tools/check_assets.py` 再拿它去核
    `CombatFeel` 的常量。凭感觉写 26 或 30 都能跑，那才是要避免的。

    坐标一律用帧框内的列／行（就是引擎渲染看到的那套），锚点第 `box['anchor']` 列、
    地面行第 `box['ground']` 行，所以「右伸」可直接当世界像素用。
    """
    w, h = box["width"], box["height"]
    cells = [sheet_img.crop((i * w, 0, (i + 1) * w, h)).load() for i in range(count)]

    def right_edge(px) -> int:
        return max((x for y in range(h) for x in range(w) if px[x, y][3] != 0), default=-1)

    base = right_edge(cells[0])
    out: list[dict] = []
    for i, px in enumerate(cells):
        pts = [(x, y) for y in range(h) for x in range(w) if px[x, y][3] != 0 and x > base]
        if pts:
            out.append({"帧": i + 1, "右伸": max(x for x, _ in pts) - box["anchor"],
                        "行": [min(y for _, y in pts), max(y for _, y in pts)],
                        "像素": len(pts)})
        else:
            out.append({"帧": i + 1, "右伸": 0, "行": [], "像素": 0})
    return base, out


def registry_entry(sheet: str, purpose: str, frames: list[Frame], solid: list[Frame],
                   anchor_x: int, box: dict[str, int], reach: tuple[int, list[dict]] | None) -> dict:
    src_w, src_h = frames[0].size
    factors = sorted({f.factor for f in frames})
    colors = sorted({f.line_color for f in frames})
    boxes = [f.bbox for f in frames]
    tallest = max(f.line_row - f.bbox[1] for f in frames)
    widest = max(f.bbox[2] - f.bbox[0] + 1 for f in frames)
    entry = {
        "path": f"{OUT_DIR}/{sheet}.png",
        "用途": f"作者自绘测试木偶 · {purpose}",
        "宽": box["width"] * len(frames),
        "高": box["height"],
        "帧宽": box["width"],
        "帧高": box["height"],
        "帧数": len(frames),
        # 下面三个数是**机器读的**：引擎侧 PlayerActor 的 FrameWidth／FrameHeight／GroundRow
        # 与它们必须逐条相等，由 tools/check_assets.py 的 check_frame_geometry_binding 盯着。
        # 上面「帧内锚点」那句是给人读的同一件事，两处不许各说一套。
        "帧内锚点列": box["anchor"],
        "帧内地面行": box["ground"],
        "源画布": f"{src_w}×{src_h}，逐帧 PNG 放大 {factors if len(factors) > 1 else factors[0]} 倍，"
                  f"整图 n×n 块同色，按最近邻无损降采样",
        "地面参考线": f"源画布第 {frames[0].line_row} 行，色值 {colors if len(colors) > 1 else colors[0]}"
                      f"，接仓时整行去掉（作者嵌的锚点像素也在这一行，一并去掉）",
        "锚点像素": f"色值 {list(ANCHOR_COLOR)}，源画布 ({anchor_x},{frames[0].line_row})，"
                    f"逐帧同坐标、每帧恰好一个 —— 水平列由它给出，不由线的形状推",
        "帧内锚点": f"水平第 {box['anchor']} 列（源画布第 {anchor_x} 列），"
                    f"脚底地面行第 {box['ground']} 行，脚底像素落在第 {box['ground'] - 1} 行",
        "角色本体": f"最高 {tallest}px、最宽 {widest}px（去掉参考线后量的）",
        "源帧号": [f.number for f in frames],
        "逐帧包围盒": "；".join(f"{i + 1}:{b[0]},{b[1]}-{b[2]},{b[3]}" for i, b in enumerate(boxes)),
        "来源": f"作者自绘，收件箱 temp/art-inbox/self-material/test-role/"
                f"{frames[0].path.parent.name}/，由 tools/import_role_sheets.py 接入",
        "授权": "本项目自有（作者自绘）",
        "待替换": True,
        "可进发行包": False,
        "可进发行包依据": "作者自绘，但**是测试木偶不是正式角色美术** —— 用途就是把动画链路与"
                          "相位对齐验出来。「可进发行包 ≡ 是不是作者画的」这条在这里不够用："
                          "照它判会让一个明确的占位件拿到进包资格，`ENG-12` 的发行守卫就白设了。"
                          "所以按用途判 false，待替换成正式角色美术（`ART-6`）。"
                          "§9 半透明像素判定**不因此放行**：那条例外只对下载件（`ART-5`），"
                          "判据取登记表的分节而不是本字段。",
    }
    if solid:
        entry["排除的单色帧"] = {
            "源帧号": [f.number for f in solid],
            # 颜色写成 list 不写 tuple：JSON 没有元组，读回来是 list，写进去是 tuple 的话
            # `--check` 的比对会**永远不相等**（2026-09-08 实测踩到）。下面 write_registry
            # 的比对也改成走一遍 JSON，两处一起才让这类失效不可能再发生。
            "颜色": [list(sorted(f.colors)[0]) for f in solid],
            "理由": "去掉地面行后整帧只有一种不透明颜色 —— 那是一张闪白，不是受击姿态。"
                    "闪白由代码持有（CombatFeel.FlashFrames，可按轻重分别调、能跟顿帧一起"
                    "冻结、能做成可关的无障碍项），画进精灵表等于同一件事有两个真相，"
                    "而画进去的那份改不动。原件保留在收件箱不动，"
                    "**待作者把这几帧重画成真正的受击姿态**（`ART-6`）。",
        }
    if reach is not None:
        base, per_frame = reach
        entry["伸展基线"] = (
            f"第 1 帧（静止起手姿）右边界第 {base} 列，锚点右侧 {base - box['anchor']}px；"
            f"下面每帧只量超出这条边界的像素 —— 那就是打出去的那只拳，不含身体")
        entry["逐帧伸展"] = per_frame
    return entry


def write_registry(entries: list[dict], check_only: bool) -> bool:
    """只重写「自绘素材」一节，其余原样保留（与 gen_placeholders.py 同一分工）。

    比对**先走一遍 JSON** 再比。理由是登记表的真相是 JSON 里那份：Python 侧的 tuple、set
    这类东西写进去会变形，直接拿内存对象比就会「永远不相等」，于是 `--check` 变成一条永远
    失败的判据 —— 而永远失败的判据等于没有判据，人会学着忽略它。2026-09-08 实测踩到：
    单色帧的颜色是 tuple，读回来是 list。
    """
    payload = json.loads(REGISTRY.read_text(encoding="utf-8")) if REGISTRY.is_file() else {}
    payload.setdefault(
        "自绘素材维护",
        "由 python tools/import_role_sheets.py 重写这一节，手工别改 —— 它的每个数都是从"
        "收件箱原件量出来的，手改会让 --check 与磁盘对不上。")
    same = payload.get(REGISTRY_SECTION) == json.loads(json.dumps(entries, ensure_ascii=False))
    if check_only:
        if not same:
            fail("登记表的「自绘素材」一节与现在重算的不一致 —— 有人手改过，或收件箱变了")
        return same
    payload[REGISTRY_SECTION] = entries
    REGISTRY.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                        encoding="utf-8", newline="\n")
    return same


def main() -> int:
    ap = argparse.ArgumentParser(description="把收件箱逐帧 PNG 接成进仓精灵表（ART-6）")
    ap.add_argument("--inbox", default=str(DEFAULT_INBOX), help="逐帧 PNG 的收件箱目录（只读）")
    ap.add_argument("--check", action="store_true", help="只核对不写盘")
    args = ap.parse_args()

    inbox = Path(args.inbox).resolve()
    if not inbox.is_dir():
        say(f"[FAIL] 收件箱不在：{inbox}")
        print("EXIT=1")
        return 1

    say(f"收件箱 {inbox}（只读）")
    say(f"落点 assets/{OUT_DIR}/｜登记表一节「{REGISTRY_SECTION}」"
        f"｜模式 {'核对' if args.check else '重建'}")
    say()

    actions, dropped, anchors, box = build(inbox)
    if not actions or _FAILS:
        finish()
        return 1

    say(f"统一帧框 {box['width']}×{box['height']}：锚点第 {box['anchor']} 列，"
        f"地面行第 {box['ground']} 行（脚底像素第 {box['ground'] - 1} 行）")
    say(f"  由实测极值定出：锚点左伸 {box['left']}、右伸 {box['right']}、"
        f"地面行以上 {box['top']} —— 宽 = 2×max(左伸, 右伸+1)，高 = 容下最高动作的最小偶数")
    say()
    # 横向落位要报出来：脚底行与水平列现在都由作者标的锚点精确给出，对齐本身不再有取整误差。
    # 留着这一节是因为**它量的是另一件事** —— 各动作的身体相对锚点画在哪，那是源画的构图，
    # 对齐规则管不着。列范围差得多就说明作者把身体画偏了，只能让人看着判要不要回去改画。
    say("横向落位（首帧内容在帧框里的列范围，用来看切动作时角色会不会横跳）：")
    for sheet, frames in actions.items():
        dx = box["anchor"] - anchors[sheet]
        x0, _, x1, _ = frames[0].bbox
        say(f"  {sheet:8s} 锚点列 {anchors[sheet]:2d} 偏移 {dx:+d}｜首帧内容列 "
            f"{x0 + dx}–{x1 + dx}（宽 {x1 - x0 + 1}）")
    say()

    total_frames = 0
    dropped_frames = 0
    entries: list[dict] = []
    changed: list[str] = []
    identical: list[str] = []
    out_root = ASSETS / OUT_DIR
    for folder_name, sheet, purpose, is_attack in ACTIONS:
        frames = actions.get(sheet)
        if not frames:
            continue
        solid = dropped.get(sheet, [])
        dropped_frames += len(solid)
        ax = anchors[sheet]
        sheet_img = compose(frames, ax, box)
        reach = measure_reach(sheet_img, len(frames), box) if is_attack else None
        data = png_bytes(sheet_img)
        target = out_root / f"{sheet}.png"
        same = target.is_file() and target.read_bytes() == data
        if args.check:
            if not same:
                fail(f"{target.relative_to(ASSETS).as_posix()}："
                     f"{'内容与现在重算的不一致' if target.is_file() else '文件不在'}")
        elif not same:
            out_root.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            changed.append(sheet)
        if same:
            identical.append(sheet)

        src_w, src_h = frames[0].size
        say(f"  {folder_name:18s} → {sheet + '.png':12s} {len(frames)} 帧"
            + (f"（源 {len(frames) + len(solid)} 帧，排除单色帧 "
               f"{[f.number for f in solid]}）" if solid else "")
            + f"｜源 {src_w}×{src_h} ×{sorted({f.factor for f in frames})[0]} 倍"
              f"｜地面行 {frames[0].line_row}｜锚点像素 ({ax},{frames[0].line_row}) 逐帧一致")
        for i, f in enumerate(frames):
            x0, y0, x1, y1 = f.bbox
            say(f"      帧 {i + 1:2d} {f.path.name:26s} 线 x{f.line_span[0]}–{f.line_span[1]}"
                f" 色{f.line_color} 锚点 {f.anchor}｜内容 ({x0},{y0})–({x1},{y1})"
                f"｜偏移 dx={box['anchor'] - ax} dy={box['ground'] - f.line_row}"
                f"｜高 {f.line_row - y0}px｜色数 {len(f.colors)}")
        if reach is not None:
            base, per_frame = reach
            hit = max(r["右伸"] for r in per_frame)
            say(f"      伸展基线列 {base}（锚点右侧 {base - box['anchor']}px）"
                f"｜逐帧右伸 {[r['右伸'] for r in per_frame]}｜最远 {hit}px"
                f" —— 判定框宽由此导出，见 CombatFeel 与 check_assets")
        total_frames += len(frames)
        entries.append(registry_entry(sheet, purpose, frames, solid, ax, box, reach))

    say()
    registry_same = write_registry(entries, args.check)
    say(f"登记表「{REGISTRY_SECTION}」{len(entries)} 条"
        f"｜{'与重算一致' if registry_same else ('已重写' if not args.check else '不一致')}")
    if changed:
        say(f"写入 {len(changed)} 张：{'、'.join(changed)}")
    if identical and not args.check:
        say(f"内容未变 {len(identical)} 张（重跑结果稳定）：{'、'.join(identical)}")

    reach_count = sum(1 for e in entries if "逐帧伸展" in e)
    say()
    say(f"覆盖量：{len(entries)} 个动作、源共 {total_frames + dropped_frames} 帧逐像素处理"
        f"（进仓 {total_frames} 帧、排除单色帧 {dropped_frames} 帧），每帧查了 8 项"
        f"（半透明像素、无损放大倍数、源尺寸一致、地面参考线形状、锚点像素恰好一个、"
        f"平移不裁内容、进仓件无锚点残留、单色帧）；每个动作另查 2 项"
        f"（锚点落在参考线所在行、各帧锚点坐标完全一致）；"
        f"另核帧框极值 4 个、量 {reach_count} 个攻击动作的逐帧伸展、写登记表 {len(entries)} 条")
    if total_frames == 0:
        fail("一帧都没处理 —— 空转的入口也会「全绿」，所以这里必须失败")
    if reach_count != sum(1 for a in ACTIONS if a[3]):
        fail(f"标了攻击的动作有 {sum(1 for a in ACTIONS if a[3])} 个，量出伸展的只有 "
             f"{reach_count} 个 —— 判定框的取值依据缺一份，不许静默少量")
    return finish()


def finish() -> int:
    say()
    say(f"结果：{len(_FAILS)} 条判据不成立"
        + ("（不改就不能接仓）" if _FAILS else "（帧框、锚点与像素规格都过）"))
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    log = LOG_DIR / f"import-role-sheets-{time.strftime('%Y%m%d-%H%M%S')}.log"
    log.write_text("\n".join(_LINES) + "\n", encoding="utf-8", newline="\n")
    print(f"日志 {log.relative_to(ROOT).as_posix()}")
    code = 1 if _FAILS else 0
    print(f"EXIT={code}")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
