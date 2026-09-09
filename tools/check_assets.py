#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""素材守卫：假像素、错误缩放与槽位登记比对（`ENG-10`）。

只查**机器能判死**的四类，一条阈值类都不做：

1. **半透明像素**（alpha ∉ {0, 255}）。[像素绘制原则 §9] 把「透明度只使用完全透明或完全
   不透明」定为绝对规则，但一直没有执行体。
2. **整图放大件**（整图每个 n×n 块同色）。16×16 放大到 32×32 不增加任何信息量，却会骗过
   「尺寸对得上」这类检查。**报的是真实倍数与真实源尺寸**，不是「大概是 2 倍」—— 见
   <see cref="upscale_factor"/> 的注释，那条错误建议实际发生过。
3. **登记表与磁盘不一致**（多、少、尺寸不符、帧数除不尽）。
4. **纹理导入参数**（无损压缩、不生成 mipmap、关掉 detect_3d 转 VRAM 压缩）。这三项的失效
   方式都是静默的 —— 画面只是「有点花」或「有点糊」，不报错。
5. **单色帧**（整帧不透明像素只有一种颜色）。闪白由代码持有，动画只提供姿态；一帧只有一种
   颜色时它表达不出姿态，见 <see cref="check_solid_frames"/>（`ART-6`）。
6. **引擎常量与登记表的绑定**（帧框 44×32／地面行 30、判定框轻 15 重 21）。两处相等此前纯靠
   人记得同步，不同步全都不报错，见 <see cref="check_frame_geometry_binding"/> 与
   <see cref="check_hitbox_binding"/>（`ART-6`）。

**阈值类明确不做**：亮部比例、对比、色数、孤点。理由不是懒，而是[像素绘制原则 §11] 自己
写的那条 —— 那类警告永远需要人回到目标背景与题材去解释一次，把它和能判死的检查混在一个
工具里，代价是「你会学着忽略它的输出，连真失败一起忽略」。

为什么现在就要有它：这类缺陷**静默累积**。第 1 张素材时修是免费的，两百张之后就是一次
审计。`UI-1` 的 HUD 样板是第一批素材进仓的地方，所以时机是现在。

用法（从代码仓根目录运行）：
    python tools/check_assets.py            # 全查
    python tools/check_assets.py --list     # 只列登记表内容，不判定

依赖 Pillow（`tools/requirements.txt`）。**缺了必须报错退出，不许跳过检查** —— 手写解码器
只认 RGBA8，遇到调色板或 16 位文件会静默跳过，而本工具的全部价值就是不漏检。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
REGISTRY = ROOT / "tools" / "asset-registry.json"

REQUIRED_IMPORT_PARAMS = {
    "compress/mode": "0",                   # 无损。VRAM 压缩会毁掉像素图
    "mipmaps/generate": "false",            # 缩小级别对像素风没有意义
    "detect_3d/compress_to": "0",           # 关掉「用在 3D 就转 VRAM 压缩」
}

MAX_REPORTED_COORDS = 6                     # 报坐标够定位就行，不刷屏

# ── texture_filter 覆盖（`ENG-13`）────────────────────────────────────
# 像素清晰唯一依靠项目级 default_texture_filter=0（最近邻），而 CanvasItem.texture_filter
# 可逐节点覆盖并向下继承 —— 一次手滑（尤其误设 Linear）就静默毁掉整棵子树，不报错（`UI-4` 实测）。
# 规则：一律靠继承，不许局部覆盖。两个覆盖面各一条判据：
#   .tscn／.tres  →  texture_filter = N（N≠0，0=TEXTURE_FILTER_PARENT_NODE=继承）
#   .cs          →  .TextureFilter = 赋值（`(?!=)` 排除 == 比较；注释与 `TextureFilterEnum`
#                    枚举引用不带「.TextureFilter =」形状，不会误伤）
TEXTURE_FILTER_NAMES = {
    0: "Inherit", 1: "Nearest", 2: "Linear", 3: "NearestMipmap",
    4: "LinearMipmap", 5: "NearestMipmapAniso", 6: "LinearMipmapAniso",
}
TSCN_FILTER_RE = re.compile(r"^\s*texture_filter\s*=\s*(\d+)", re.MULTILINE)
CS_FILTER_ASSIGN_RE = re.compile(r"\.TextureFilter\s*=(?!=)")
SCAN_SKIP_DIRS = {".godot", "obj", "bin", "export", ".git"}

_LINES: list[str] = []
_FAILS: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


def fail(text: str) -> None:
    say(f"[FAIL] {text}")
    _FAILS.append(text)


def load_image(path: Path):
    """载入为 RGBA。**Pillow 缺失时直接退出**，不降级、不跳过。"""
    try:
        from PIL import Image
    except ModuleNotFoundError:
        print("[FAIL] 缺 Pillow —— 装：python -m pip install -r tools/requirements.txt")
        print("       不许跳过检查：会静默跳过的守卫比没有守卫更坏")
        print("EXIT=1")
        raise SystemExit(1)
    return Image.open(path).convert("RGBA")


def check_alpha(name: str, im, allow_soft_alpha: bool = False) -> None:
    """alpha 只允许 0 或 255。报出前几个坐标，够定位就行。

    `allow_soft_alpha=True` 只给**下载件**（`ART-5`）：它们注定要被自绘件替换，§9 守的是成品
    质量，开发期临时素材带柔和投影不影响发行。

    **判据是「是不是下载的」，不是「可不可进发行包」。** 2026-09-08 收窄：那两件事此前被当成
    同一件，因为当时「可进发行包=false」恰好只有下载件与脚本生成件。作者自绘的**测试木偶**
    进仓后这个等式就断了 —— 它是自绘件、但按用途登记成不得进包的占位件，照旧判法 §9 会对它
    静默放行，而那正是本条例外明确不覆盖的一类。现在口径取登记表的**分节**（下载素材／生成
    槽位／自绘素材），分节即出处，没法误设成另一种。
    """
    if allow_soft_alpha:
        return
    alpha = im.getchannel("A")
    values = set(alpha.getdata())
    bad = sorted(v for v in values if v not in (0, 255))
    if not bad:
        return
    w = im.width
    coords = [(i % w, i // w) for i, v in enumerate(alpha.getdata()) if v not in (0, 255)]
    shown = "、".join(f"({x},{y})" for x, y in coords[:MAX_REPORTED_COORDS])
    more = f" 等 {len(coords)} 处" if len(coords) > MAX_REPORTED_COORDS else ""
    fail(f"{name}：有半透明像素，alpha 出现 {bad[:8]}；位置 {shown}{more}")


def _blocks_uniform(px, w: int, h: int, n: int) -> bool:
    """整图每个 n×n 块（按 n 对齐）内部同色吗。一撞到不同色立刻返回，所以失败很便宜。"""
    for by in range(0, h, n):
        for bx in range(0, w, n):
            c = px[bx, by]
            for y in range(by, by + n):
                for x in range(bx, bx + n):
                    if px[x, y] != c:
                        return False
    return True


def upscale_factor(im) -> int:
    """这张图是几倍整数放大来的：最大的 n 使整图每个 n×n 块同色。返回 1 表示不是放大件。

    **为什么要给出真实倍数，而不是只回答「是不是 2 倍」。** 2026-09-08 实测：作者交来的自绘件
    是精确 10 倍放大件，而只测 2 倍的旧实现给出的建议是「改用 160×160 的源尺寸」—— 那个数
    错得离谱，真实源尺寸是 32×32。**守卫报错报得不对比不报更坏**：照它做会得到一张仍然放大了
    5 倍的图，而且这一次连守卫都不会再拦（32 的 5 倍是 160，160 的 2×2 块仍然同色…… 一路
    错下去）。顺带修掉旧实现的第二个洞：它遇到奇数宽高直接放过，于是 45×45 是 15×15 放大来的
    这种情况**一条都查不出来**。

    判据依赖一条性质：若整图每个 n×n 块同色，则对 n 的任一因子 a，每个 a×a 块也同色（a 块
    整个落在某个 n 块里）。所以「成立的倍数」对因子封闭，从 1 出发只往当前最优倍数的整数倍上
    试，就能升到最大值，不必把每个候选都全图扫一遍。

    纯色图天然满足任意倍数，那不是放大的证据，所以色数 < 2 直接返回 1 —— 占位素材因此一律
    带 1px 描边。
    """
    w, h = im.size
    px = im.load()
    if len({px[x, y] for y in range(h) for x in range(w)}) < 2:
        return 1
    best = 1
    for n in range(2, min(w, h) + 1):
        if w % n or h % n or n % best:
            continue
        if _blocks_uniform(px, w, h, n):
            best = n
    return best


def check_upscaled(name: str, im) -> None:
    """整图每个 n×n 块同色（n ≥ 2）⇒ 这张图是 n 倍放大来的。报**真实**倍数与真实源尺寸。"""
    n = upscale_factor(im)
    if n < 2:
        return
    w, h = im.size
    fail(f"{name}：整图每个 {n}×{n} 块同色 —— 这张图是 {n} 倍放大来的，"
         f"真实源尺寸 {w // n}×{h // n}，改用它"
         f"（角色逐帧件走 python tools/import_role_sheets.py 自动无损降采样）")


def check_solid_frames(name: str, im, entry: dict, allow_solid: bool = False) -> list[str]:
    """进仓件不许有**单色帧**：整帧不透明像素只有一种颜色（`ART-6`，2026-09-08 定）。

    这一条的口径是「闪白由代码持有，动画只提供受击姿态」。一帧的不透明像素只有一种颜色时它
    表达不出姿态 —— 它就是一张闪白。而闪白已经在代码侧（`CombatFeel.FlashFrames` 与受击方的
    闪白计时）做掉了，好处是能按轻重分别调时长、每个新敌人不必各画一套、能跟顿帧一起冻结、
    能做成可关的无障碍项。同一件事画进精灵表就有了两个真相，而画进去的那份**改不动**。

    **按形状判，不写死白色。** 作者这批的闪白帧是白的（`hit` 源第 3、4 帧，实测去掉地面行后
    只剩 (255,255,255,255) 一色、246 像素），下一批完全可能是红的或黑的；写死颜色只拦得住
    已经见过的那一次。报**真实帧号**，因为「这张表有问题」没法据以修图。

    单帧素材也走同一判据：一张纯色方块进仓同样是「没有形」，而占位生成器画的每张都带描边，
    所以基线本来就 ≥2 色 —— 这条不会误伤它们（`ENG-10` 实测）。

    **`allow_solid=True` 只给下载件**，判据取登记表的**分节**，与 §9 软 alpha 的例外
    （`ART-5`）同一条理由、同一个取法：下载件注定被自绘件替换，本条守的是我们自己画的东西的
    质量，对一定会被扔掉的第三方素材强制返工只增加收件摩擦。2026-09-08 实测这个例外**有真实
    用途**：`downloaded/samurai/hurt.png` 的第 1 帧就是整张白的 —— 也就是说「受击表里塞一张
    闪白」在下载素材里是常见做法，正好反证了闪白不能依赖美术给、必须由代码持有。

    返回**放行掉的单色帧**清单（`路径 第N帧 颜色`），供覆盖量自报 —— 例外必须看得见，
    否则「跳过了几条」就成了哑的。
    """
    w, h = im.size
    fw = entry.get("帧宽", w)
    if fw <= 0 or w % fw:
        return []                       # 帧宽本身不对，check_frames 会报，这里不重复
    px = im.load()
    waived: list[str] = []
    for index in range(w // fw):
        colors = {px[x, y] for y in range(h) for x in range(fw * index, fw * (index + 1))
                  if px[x, y][3] != 0}
        if len(colors) != 1:
            continue
        color = next(iter(colors))
        source = entry.get("源帧号", [])
        src = f"（源帧 {source[index]}）" if index < len(source) else ""
        if allow_solid:
            waived.append(f"{name} 第 {index + 1} 帧 {color}")
            continue
        fail(f"{name}：第 {index + 1} 帧{src}整帧只有一种不透明颜色 {color} —— "
             f"这是一张闪白不是姿态，闪白由代码持有（CombatFeel.FlashFrames），"
             f"不接仓；角色逐帧件走 python tools/import_role_sheets.py，它会自动排除")
    return waived


def check_frames(name: str, im, entry: dict) -> None:
    """尺寸与帧结构。没写帧宽的按单帧算。"""
    w, h = im.size
    if (w, h) != (entry["宽"], entry["高"]):
        fail(f"{name}：文件 {w}×{h} 与登记 {entry['宽']}×{entry['高']} 不符")
        return
    fw = entry.get("帧宽", entry["宽"])
    fh = entry.get("帧高", entry["高"])
    frames = entry.get("帧数", 1)
    if fh != h:
        fail(f"{name}：帧高登记 {fh}，图高 {h} —— 对不上")
    if fw <= 0 or w % fw:
        fail(f"{name}：图宽 {w} 除不尽帧宽 {fw} —— 精灵表切不出整数帧")
        return
    if w // fw != frames:
        fail(f"{name}：按帧宽 {fw} 算是 {w // fw} 帧，登记 {frames} 帧")


def check_fonts(entries: list[dict]) -> int:
    """核字体：文件在、内容与登记的 SHA256 一致、旁边有许可证。

    为什么核内容而不只核存在：字体是二进制，换成另一个版本或另一个字形版本（`zh_hant`／`ja`）
    在 git diff 里只有一行「二进制文件有差异」，而字形覆盖与度量会跟着变 —— 那正是
    [ADR-0008] 把版本钉死的理由。钉住内容才让「审计过」这句话指向一份确定的文件。

    **渲染参数不在这里核。** 那十项设在 `.ttf.import`、期望在 `rules/Ui/PixelFont.cs`、
    实际值由引擎自己报，守卫是 `tools/check_hud.py`。这里再抄一份就是第三份真相。
    """
    if not entries:
        fail("登记表里一条字体都没有 —— 界面文字会退回引擎默认字体，而那不报错")
        return 0

    checked = 0
    for entry in entries:
        rel = entry["path"]
        path = ASSETS / rel
        checked += 1
        if not path.is_file():
            fail(f"{rel}：登记表里有但文件不在（取法见 README「像素字体怎么进来的」）")
            continue

        data = path.read_bytes()
        if len(data) != entry["字节数"]:
            fail(f"{rel}：{len(data)} 字节与登记 {entry['字节数']} 不符")
            continue
        digest = hashlib.sha256(data).hexdigest()
        if digest != entry["sha256"]:
            fail(f"{rel}：sha256 {digest[:16]}… 与登记 {entry['sha256'][:16]}… 不符 —— "
                 f"换了版本或换了字形版本，覆盖与度量都会跟着变；"
                 f"重跑设计仓 python tools/audit_fonts.py 再更新 ADR-0008 与登记表")
            continue

        license_rel = entry["许可证文件"]
        if not (ASSETS / license_rel).is_file():
            fail(f"{license_rel}：不在 —— OFL 第 2 条要求每份拷贝都带许可证与版权声明")
            continue

        say(f"  {rel}｜{len(data)} 字节｜上游 {entry['上游版本']}｜字号 {entry['字号']}"
            f"｜许可证 {license_rel}"
            f"｜{'可进包' if entry.get('可进发行包') else '不得进包'}")
    return checked


def check_import(name: str, rel: str) -> bool:
    """纹理导入参数。没有 .import 说明还没导入过，**不算通过**。"""
    imp = ASSETS / (rel + ".import")
    if not imp.is_file():
        return False
    text = imp.read_text(encoding="utf-8")
    for key, want in REQUIRED_IMPORT_PARAMS.items():
        if f"{key}={want}" not in text:
            got = next((ln for ln in text.splitlines() if ln.startswith(f"{key}=")),
                       "（这一项根本不在）")
            fail(f"{name}：导入参数 {key} 应为 {want}，实际是 {got}")
    return True


def check_texture_filter(name: str, text: str) -> None:
    """扫一份文件里的 `texture_filter` 覆盖（`ENG-13`）。按后缀选规则。

    只报覆盖（赋值／非 Inherit），读取与比较不算 —— `CameraProbe` 那句
    `camera.TextureFilter == ParentNode` 是在**核**没有覆盖，不能被自己的守卫拦下。
    """
    if name.endswith((".tscn", ".tres")):
        for m in TSCN_FILTER_RE.finditer(text):
            val = int(m.group(1))
            if val != 0:
                filt = TEXTURE_FILTER_NAMES.get(val, str(val))
                fail(f"{name}：局部覆盖 texture_filter = {val}（{filt}）—— 像素清晰靠项目级"
                     f"最近邻的继承，不许逐节点覆盖（ENG-13）；删掉这行回到 Inherit(0)")
    elif name.endswith(".cs"):
        for m in CS_FILTER_ASSIGN_RE.finditer(text):
            line_no = text.count("\n", 0, m.start()) + 1
            fail(f"{name}:{line_no}：给 .TextureFilter 赋值 —— 不许覆盖，一律靠项目级"
                 f"最近邻继承（ENG-13）；读取或比较（== ParentNode）不在此列")


# ── 常量与登记表的绑定（`ART-6`）──────────────────────────────────────
# 为什么要有这一节：帧框（44×32、地面行 30）与判定框（轻 15、重 21）在引擎侧是**硬编码常量**，
# 在登记表里是**量出来的数**。两处相等纯靠人记得同步，而不同步的后果全都不报错：
#   帧框错 → 角色脚底离地或陷进地面，只能靠盯着看；
#   判定框错 → 打得到／打不到与画面不符，只能靠手感发现。
# 引擎侧读不了登记表（`tools/` 带 `.gdignore`，登记表不进包，导出后运行时拿不到它），
# 所以走守卫：把「不可能漂」变成「漂了当场拦下」。
PLAYER_ACTOR_CS = ROOT / "src" / "World" / "PlayerActor.cs"
COMBAT_FEEL_CS = ROOT / "rules" / "Combat" / "CombatFeel.cs"
SELF_DRAWN_SECTION = "自绘素材"
CS_CONST_RE = re.compile(r"const\s+int\s+(\w+)\s*=\s*(-?\d+)\s*;")
# 引擎常量名 → 登记表字段名。三条都是「帧框」，缺一条就对不上帧。
FRAME_GEOMETRY = (("FrameWidth", "帧宽"), ("FrameHeight", "帧高"), ("GroundRow", "帧内地面行"))
# 精灵表路径 → CombatFeel 常量前缀。判定框按轻重分开之后各有一份取值依据。
HITBOX_SHEETS = {"self-drawn/test-role/light.png": "Light",
                 "self-drawn/test-role/heavy.png": "Heavy"}


def cs_constants(path: Path) -> dict[str, int]:
    """把一份 C# 里的 `const int` 全读出来。读不到文件就报错，不当没这回事。"""
    if not path.is_file():
        fail(f"{path.relative_to(ROOT).as_posix()} 不在 —— 常量与登记表无从比对")
        return {}
    return {m.group(1): int(m.group(2))
            for m in CS_CONST_RE.finditer(path.read_text(encoding="utf-8"))}


def check_frame_geometry_binding(self_drawn: list[dict]) -> int:
    """引擎侧帧框常量 ≙ 登记表量出来的帧框。返回核过的判据条数。"""
    if not self_drawn:
        fail(f"登记表「{SELF_DRAWN_SECTION}」一节为空 —— 帧框绑定无从核，"
             f"而空转的守卫也会「全绿」")
        return 0
    boxes = {(e.get("帧宽"), e.get("帧高"), e.get("帧内锚点列"), e.get("帧内地面行"))
             for e in self_drawn}
    if len(boxes) != 1:
        fail(f"自绘素材各条的帧框不一致 {sorted(boxes)} —— 统一帧框是「切动作不跳」的前提，"
             f"重跑 python tools/import_role_sheets.py")
        return 0
    fw, fh, anchor, ground = next(iter(boxes))
    if None in (fw, fh, anchor, ground):
        fail(f"自绘素材缺帧框字段（帧宽/帧高/帧内锚点列/帧内地面行 = {fw}/{fh}/{anchor}/{ground}）"
             f"—— 重跑 python tools/import_role_sheets.py 重建这一节")
        return 0

    checked = 0
    consts = cs_constants(PLAYER_ACTOR_CS)
    want = {"帧宽": fw, "帧高": fh, "帧内地面行": ground}
    for const, field in FRAME_GEOMETRY:
        checked += 1
        got = consts.get(const)
        if got is None:
            fail(f"PlayerActor.cs 里找不到常量 {const} —— 帧框绑定断了，"
                 f"要么改回来，要么把这条判据一起改掉")
        elif got != want[field]:
            fail(f"PlayerActor.cs 的 {const}={got} 与登记表「{field}」={want[field]} 不符 —— "
                 f"素材重生成后帧框变了而常量没跟着改，表现是角色脚底离地／陷地，**不报错**")
    # 锚点必须在帧正中：引擎靠 FlipH 做朝向，锚点偏一列就会左右各偏一列。
    checked += 1
    if anchor != fw // 2:
        fail(f"登记表帧内锚点列 {anchor} 不等于帧宽的一半 {fw // 2} —— FlipH 会把角色甩偏")
    say(f"  帧框绑定：PlayerActor {fw}×{fh} 地面行 {ground} 锚点 {anchor} ≙ 登记表 "
        f"{len(self_drawn)} 条自绘素材（{checked} 条判据）")
    return checked


def check_hitbox_binding(self_drawn: list[dict]) -> int:
    """判定框常量 ≙ 登记表里 Active 帧的实测伸展。返回核过的判据条数。

    Active 窗口不在这里写死，从 `PlayerActor.cs` 的 `AttackActiveFirstFrame` 与
    `AttackActiveSpan` 读 —— 于是三处任意一处变了都会被拦下：改素材（登记表变）、
    改 Active 窗口（取的帧变）、改常量。
    """
    entries = {e["path"]: e for e in self_drawn if e.get("path") in HITBOX_SHEETS}
    missing = sorted(set(HITBOX_SHEETS) - set(entries))
    if missing:
        fail(f"登记表里缺攻击精灵表 {missing} —— 判定框的取值依据不在，"
             f"不许拿「找不到就跳过」当通过")
        return 0

    player = cs_constants(PLAYER_ACTOR_CS)
    feel = cs_constants(COMBAT_FEEL_CS)
    first = player.get("AttackActiveFirstFrame")
    span = player.get("AttackActiveSpan")
    if first is None or span is None:
        fail("PlayerActor.cs 里找不到 AttackActiveFirstFrame／AttackActiveSpan —— "
             "取不出 Active 帧，判定框的取值依据无从核")
        return 0

    checked = 0
    center_const = feel.get("HitboxCenterYWorldPx")
    for path, prefix in sorted(HITBOX_SHEETS.items()):
        entry = entries[path]
        reach = {r["帧"]: r for r in entry.get("逐帧伸展", [])}
        ground = entry.get("帧内地面行")
        if not isinstance(ground, int):
            # 缺字段就当场说清，别让下面的算式抛 TypeError —— 那会让整个守卫只剩一句
            # 「认不出输出形状」，指不回真实原因。
            fail(f"{path}：登记表缺「帧内地面行」（读到 {ground!r}）—— 判定框中心无从核，"
                 f"重跑 python tools/import_role_sheets.py")
            continue
        # 精灵帧是 0 基的，登记表的「帧」是 1 基的。
        active = [first + i + 1 for i in range(span)]
        rows = [r["行"] for f in active if (r := reach.get(f)) and r["行"]]
        if len(rows) != len(active):
            fail(f"{path}：Active 帧 {active} 里有帧量不出伸展（逐帧伸展 "
                 f"{[(f, reach.get(f, {}).get('右伸')) for f in active]}）—— "
                 f"判定框不能由「没有伸展」导出")
            continue
        width = max(reach[f]["右伸"] for f in active)
        top = min(r[0] for r in rows)
        bottom = max(r[1] for r in rows)
        height = bottom - top + 1
        center = ground - (top + bottom) / 2
        for const, expect in ((f"{prefix}HitboxWidthWorldPx", width),
                              (f"{prefix}HitboxHeightWorldPx", height)):
            checked += 1
            got = feel.get(const)
            if got is None:
                fail(f"CombatFeel.cs 里找不到常量 {const} —— 判定框绑定断了")
            elif got != expect:
                fail(f"CombatFeel.cs 的 {const}={got} 与 {path} 的 Active 帧 {active} 实测"
                     f"{expect} 不符 —— 画面上伸 {width}px 而判定框另一个数，"
                     f"表现是「打得不实」或「打空」，**不报错**")
        checked += 1
        if center_const is None:
            fail("CombatFeel.cs 里找不到 HitboxCenterYWorldPx")
        elif abs(center_const - center) > 0.5:
            fail(f"CombatFeel.cs 的 HitboxCenterYWorldPx={center_const} 与 {path} 的伸展区"
                 f"中心 {center}（脚底之上）差超过半个像素 —— 判定框和画面上那只拳不在同一高度")
        say(f"  判定框绑定：{prefix} Active 帧 {active} 实测右伸 {width}px、"
            f"行 {top}–{bottom}（高 {height}）、中心距脚底 {center}px "
            f"≙ CombatFeel（3 条判据）")
    return checked


def texture_filter_targets() -> list[Path]:
    """`ENG-13` 的扫描范围：场景资源全库 + `src/` 与 `rules/` 的 C#，跳过构建产物与缓存。

    C# 不扫 `tools/`（那是守卫代码本身、会提到属性名）也不扫 `tests/`（测试里多是比较）。
    """
    targets: list[Path] = list(ROOT.rglob("*.tscn")) + list(ROOT.rglob("*.tres"))
    for base in (ROOT / "src", ROOT / "rules"):
        if base.is_dir():
            targets += base.rglob("*.cs")
    return sorted({p for p in targets
                   if not any(part in SCAN_SKIP_DIRS for part in p.parts)})


def run_checks(list_only: bool = False) -> int:
    if not REGISTRY.is_file():
        fail(f"登记表不在：{REGISTRY.relative_to(ROOT)} —— 先跑 "
             f"python tools/gen_placeholders.py")
        return 1

    data = json.loads(REGISTRY.read_text(encoding="utf-8"))
    generated = data.get("生成槽位", [])
    downloaded = data.get("下载素材", [])
    self_drawn = data.get("自绘素材", [])
    fonts = data.get("字体", [])
    # 出处取**分节**，不取「可进发行包」字段：§9 的软 alpha 例外只对下载件（`ART-5`），
    # 而自绘的占位件也登记成不得进包，两者按字段判会混成一类，例外就漏到自绘件上去了。
    entries = {e["path"]: e for e in generated + downloaded + self_drawn}
    from_download = {e["path"] for e in downloaded}
    say(f"登记表：生成槽位 {len(generated)} 条、下载素材 {len(downloaded)} 条、"
        f"自绘素材 {len(self_drawn)} 条、字体 {len(fonts)} 条")

    if list_only:
        for e in generated + downloaded + self_drawn:
            say(f"  {e['path']:<44s} {e['宽']}×{e['高']} ×{e.get('帧数', 1)} 帧"
                f"｜{'待替换' if e.get('待替换') else '正式'}"
                f"｜{'可进包' if e.get('可进发行包') else '不得进包'}")
        for e in fonts:
            say(f"  {e['path']:<44s} {e['字节数']} 字节 字号 {e['字号']}"
                f"｜{'待替换' if e.get('待替换') else '正式'}"
                f"｜{'可进包' if e.get('可进发行包') else '不得进包'}")
        return 0

    on_disk = {p.relative_to(ASSETS).as_posix() for p in ASSETS.rglob("*.png")} \
        if ASSETS.is_dir() else set()
    for extra in sorted(on_disk - set(entries)):
        fail(f"{extra}：文件在但登记表里没有 —— 漏登记等于绕过守卫")
    for gone in sorted(set(entries) - on_disk):
        fail(f"{gone}：登记表里有但文件不在")

    scanned = 0
    no_import = []
    waived_solid: list[str] = []
    for rel in sorted(on_disk & set(entries)):
        im = load_image(ASSETS / rel)
        # 下载件放行软 alpha（ART-5）：它们注定要被自绘件替换，§9 守的是成品质量，不值得为
        # 临时素材增加收件摩擦。**自绘件与脚本生成件无论可不可进包都强制执行。**
        check_alpha(rel, im, allow_soft_alpha=rel in from_download)
        check_upscaled(rel, im)
        check_frames(rel, im, entries[rel])
        waived_solid += check_solid_frames(rel, im, entries[rel],
                                          allow_solid=rel in from_download)
        if not check_import(rel, rel):
            no_import.append(rel)
        scanned += 1

    say("\n常量与登记表的绑定（ART-6）：")
    binding = check_frame_geometry_binding(self_drawn) + check_hitbox_binding(self_drawn)

    say("\n字体：")
    font_count = check_fonts(fonts)

    # ENG-13：texture_filter 覆盖 —— 场景资源全库 + src/rules 的 C#。
    tf_files = texture_filter_targets()
    for p in tf_files:
        check_texture_filter(p.relative_to(ROOT).as_posix(), p.read_text(encoding="utf-8"))

    frame_total = sum(e.get("帧数", 1) for e in entries.values())
    say(f"\n覆盖量：登记 {len(entries)} 条（其中下载件 {len(from_download)} 条按 ART-5 放行软 "
        f"alpha，其余 {len(entries) - len(from_download)} 条强制 §9），磁盘 {len(on_disk)} 个 .png，"
        f"实际逐像素扫过 {scanned} 个、共 {frame_total} 帧；每个查了 5 类"
        f"（半透明、放大件、尺寸与帧数、单色帧、导入参数）；"
        f"另核字体 {font_count} 份（字节数、SHA256、旁边有许可证）；"
        f"另扫 {len(tf_files)} 份场景资源与 C# 的 texture_filter 覆盖（ENG-13）；"
        f"另核常量与登记表绑定 {binding} 条判据（帧框 4 条 + 判定框轻重各 3 条，ART-6）")
    if waived_solid:
        say(f"  单色帧按 ART-5 同一理由（下载件注定替换）放行 {len(waived_solid)} 帧："
            f"{'、'.join(waived_solid)} —— 自绘件与生成件一律不放行")
    if not tf_files:
        fail("texture_filter 扫描一个文件都没扫到 —— 空转的检查也会全绿，故判失败")
    if binding == 0:
        fail("常量与登记表一条都没核到 —— 空转的绑定守卫也会全绿，故判失败")
    if no_import:
        fail(f"{len(no_import)} 个素材没有 .import（还没导入过，导入参数无从核）："
             f"{'、'.join(no_import[:5])}")
    if scanned == 0:
        fail("一个素材都没扫到 —— 空转的检查也会「全绿」，所以这里必须失败")

    if _FAILS:
        say(f"[FAIL] 共 {len(_FAILS)} 条不成立")
        return 1
    say("[OK] 半透明像素 0、放大件 0、单色帧 0、登记表与磁盘一致、导入参数全对、"
        "字体内容与登记一致、无 texture_filter 覆盖、帧框与判定框常量与登记表一致")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="素材守卫（ENG-10）")
    ap.add_argument("--list", action="store_true", help="只列登记表内容，不判定")
    args = ap.parse_args()
    code = run_checks(args.list)
    print(f"EXIT={code}")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
