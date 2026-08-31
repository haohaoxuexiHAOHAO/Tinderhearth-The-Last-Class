#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""按启动日志实测相机五项行为（`UI-5`）。

要判定的事有五类，共同点是**失效都不报错**：

1. **只有一份相机实现。** 正典点名「行为不定下来，两种视角会各写一套」，而各写一套不会有任何
   报错 —— 只表现为「侧视手感和俯视不太一样」。所以这里有两道静态判据：引擎层派生
   `Camera2D` 的类型只许有一个且必须 `sealed`（子类化是「各写一套」最自然的形态），规则层带
   `Camera` 字样的类型只许是登记在册的那几个。
2. **两种视角跑的是同一批判据。** 启动自检把每条判据在两种视角下各打一遍，这里核**两边的判据
   名字集合完全相同**。某个行为只在一种视角下成立时，另一边会缺条 —— 那是「各写一套」的行为投影。
3. **缩放与视野对得上正典。** 期望值写在本脚本里并注明正典出处，**不从代码里解** ——
   让代码自己报期望等于自己证明自己（`UI-7` 栽过：用正则解 C# 取期望值，静默少解 12 条而守卫
   照样全绿。少算比算错更坏，它把「没核」伪装成「通过」）。
4. **可建造区尺寸真的来自配置。** 判据不是「打印了配置里的值」而是**装进相机的那个值**与本脚本
   自己解 `data/config/game.json` 得到的值一致 —— 前者只证明配置读到了，代码里另写死一份照样
   通过（PRD 的 `FR-24`，是否扩大已记 `GP-8`）。
5. **像素块到底什么时候会被切开。** 这条是**实测**：引擎侧铺一张 1px 棋盘格，取三次截图并量
   最小连续同色跑长 —— 整数相机位置（基线，必须恰好等于视口缩放 × 相机缩放）、分数相机位置
   （实测**不变**，2026-08-31）、非整数总缩放（必须变小，这是反证）。
   立项时以为分数相机位置会切开像素块，实测不是：整数总缩放 S 下，每个世界像素的屏幕跨度
   `[kS+d, (k+1)S+d)` 恰好包住 S 个像素中心，与偏移 d 无关。真会切开的是**总缩放不是整数**，
   而那正是代码挡住的东西（缩放只收正整数）。反证放在这里是因为量具若量不出失败，
   它量出的「成功」也不算证据。

不用管道读中文输出（设计仓 reference/踩坑记录.md 第 27 条）：日志由引擎写 `--log-file`，
本脚本自己 decode。**不能用 `--headless`** —— 第 5 类要真截图。

用法（从代码仓根目录运行）：
    python tools/check_camera.py             # 跑工程源码
    python tools/check_camera.py --exported  # 跑 export/ 下的产物

输出约定与 `verify.py` 一致：逐条 [OK]／[FAIL]，末尾打覆盖量与一行 EXIT=。
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import time
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

from loglib import prune_runs, KEEP_RUNS  # noqa: E402  同目录工具（跑完修剪旧日志）

ROOT = Path(__file__).resolve().parent.parent
LOG_DIR = ROOT / "logs" / "camera"
GODOT_ROOT = Path(r"D:\godot\4-7")

SRC_DIR = ROOT / "src"
RULES_DIR = ROOT / "rules"
SCENES_DIR = ROOT / "scenes"
PROJECT_GODOT = ROOT / "project.godot"
GAME_JSON = ROOT / "data" / "config" / "game.json"

# 跑实测用的窗口。720p 给出整数视口缩放 ×2，是四档里最快起来的一档（缩放链路本身归
# check_scaling.py，这里只需要一个已知的整数缩放）。
WINDOW = (1280, 720)

# ── 期望值：来自设计仓正典，不从代码里解 ────────────────────────────────
# canon/gameplay/玩法定位.md 「像素基准」：侧视关卡把相机拉近到 2 倍整数缩放。
CANON_ZOOM = {"TopDown": 1, "SideView": 2}
# UI-3 定的逻辑分辨率 640×360，除以各自缩放就是有效视野。
CANON_VIEW = {"TopDown": (640, 360), "SideView": (320, 180)}
# canon/gameplay/玩法定位.md 「像素基准」：基础单位 16px，一格建筑就是它。
CELL_PX = 16
# 视角只有两种（正典：战斗与出征一律侧视、基地与城区一律俯视，无例外）。
CANON_VIEWS = ("TopDown", "SideView")

# 规则层允许存在的、名字里带 Camera 的类型。多出一个就是多出一份相机实现。
RULES_CAMERA_TYPES = {"CameraRig", "CameraCutscene", "CameraFeel", "CameraView"}

# [相机] 视角 SideView ｜ 缩放 2 ｜ 视野 320x180 ｜ 死区 24x16 ｜ 实现 Tinderhearth.Rules.Ui.CameraRig
VIEW_RE = re.compile(
    r"\[相机\] 视角 (\w+) ｜ 缩放 (\d+) ｜ 视野 (\d+)x(\d+) ｜ 死区 (\d+)x(\d+) ｜ 实现 (\S+)")
# [相机] 判据 PASS ｜ SideView ｜ 死区内移动镜头不动、出了死区才跟 ｜ 实际 ...
VERDICT_RE = re.compile(r"\[相机\] 判据 (PASS|FAIL) ｜ ([^｜]+) ｜ ([^｜]+) ｜ (.*)")
# [相机] 自检 24/24 条通过
SELFCHECK_RE = re.compile(r"\[相机\] 自检 (\d+)/(\d+) 条通过")
# [相机] 装进相机的可建造区 640x480px
INSTALLED_RE = re.compile(r"\[相机\] 装进相机的可建造区 (\d+)x(\d+)px")
# [相机] 量 整数位置 ｜ 相机位置 0.0000,0.0000 ｜ 节点缩放 2 ｜ 取回图 1280x720 ｜ 视口缩放 x2
#         ｜ 期望跑长 4 ｜ 最小同色跑长 4 ｜ 统计段数 9999 ｜ 与基线 基线
MEASURE_RE = re.compile(
    r"\[相机\] 量 (?P<tag>\S+) ｜ 相机位置 (?P<x>-?[\d.]+),(?P<y>-?[\d.]+)"
    r" ｜ 节点缩放 (?P<zoom>[\d.]+) ｜ 取回图 (?P<w>\d+)x(?P<h>\d+) ｜ 视口缩放 x(?P<scale>\d+)"
    r" ｜ 期望跑长 (?P<expect>-?\d+) ｜ 最小同色跑长 (?P<run>-?\d+) ｜ 统计段数 (?P<samples>\d+)"
    r" ｜ 与基线 (?P<diff>.*)")

# 引擎层派生 Camera2D 的类型声明。
CAMERA2D_DECL_RE = re.compile(r"^\s*(?P<mods>[\w\s]*?)\bclass\s+(?P<name>\w+)\s*:\s*Camera2D\b",
                              re.MULTILINE)
# 规则层带 Camera 字样的类型声明（class／record／struct／enum 都算）。
RULES_TYPE_RE = re.compile(
    r"\b(?:class|record|struct|enum)\s+(?P<name>\w*Camera\w*)\b")
# 通过实例调用的可建造区安装点。只匹配带点的调用，避开方法声明本身。
BUILDABLE_CALL_RE = re.compile(r"\.\s*(?:Set|Use)Buildable\w*\((?P<args>[^)]*)\)", re.DOTALL)
# 统计段数的下限。少于这个数说明棋盘格没铺上或被界面盖住了 —— 那时「最小跑长」是空转出来的。
MIN_SAMPLES = 2000

# 三次截图的标签，与 src/World/CameraProbe.cs 里的常量一致。
TAG_INTEGER = "整数位置"
TAG_FRACTIONAL = "分数位置"
TAG_BROKEN_ZOOM = "非整数总缩放"

# [相机] 量 跳过 ｜ 显示后端 headless 没有渲染设备……
SKIPPED_RE = re.compile(r"\[相机\] 量 跳过")

_LINES: list[str] = []
_FAILS: list[str] = []
_CHECKED = 0


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


def ok(text: str) -> None:
    global _CHECKED
    _CHECKED += 1
    say(f"[OK] {text}")


def fail(text: str) -> None:
    global _CHECKED
    _CHECKED += 1
    say(f"[FAIL] {text}")
    _FAILS.append(text)


def find_godot() -> Path | None:
    """取 mono 版带控制台的 exe —— 本工程是 C#，非 mono 版跑不了。"""
    if not GODOT_ROOT.is_dir():
        return None
    cands = [p for p in GODOT_ROOT.rglob("*_console.exe") if "mono" in p.name]
    return sorted(cands)[0] if cands else None


def find_exported() -> Path | None:
    export = ROOT / "export"
    exes = sorted(export.glob("*.exe")) if export.is_dir() else []
    return exes[0] if exes else None


def cs_files(root: Path) -> list[Path]:
    return [p for p in sorted(root.rglob("*.cs"))
            if not any(part in ("bin", "obj") for part in p.parts)]


# ── 一、只有一份相机实现 ────────────────────────────────────────────────
def check_single_camera_implementation() -> None:
    """引擎层派生 `Camera2D` 的类型只许有一个，且必须 sealed。"""
    found: list[tuple[str, str, str]] = []      # (文件, 类名, 修饰符)
    scanned = cs_files(SRC_DIR)
    for path in scanned:
        text = path.read_text(encoding="utf-8")
        for m in CAMERA2D_DECL_RE.finditer(text):
            found.append((path.relative_to(ROOT).as_posix(),
                          m.group("name"), m.group("mods").strip()))

    if not scanned:
        fail("src/ 下一个 .cs 都没扫到 —— 这条检查在空转")
        return

    if len(found) != 1:
        fail(f"扫过 src/ 下 {len(scanned)} 个 .cs，派生 Camera2D 的类型有 {len(found)} 个："
             f"{[f'{f}:{n}' for f, n, _ in found]} —— 正典要求两种视角共用同一份实现，"
             f"必须恰好一个")
        return

    path, name, mods = found[0]
    ok(f"扫过 src/ 下 {len(scanned)} 个 .cs，派生 Camera2D 的类型只有 {name}（{path}）")
    if "sealed" not in mods:
        fail(f"{name} 不是 sealed（修饰符「{mods}」）—— 派两个子类正是「两种视角各写一套」"
             f"最自然的形态，封起来让那条路在编译期就走不通")
    else:
        ok(f"{name} 是 sealed，不能按视角派子类")


def check_no_extra_camera_types_in_rules() -> None:
    """规则层带 Camera 字样的类型只许是登记在册的那几个。"""
    found: dict[str, str] = {}
    scanned = cs_files(RULES_DIR)
    for path in scanned:
        for m in RULES_TYPE_RE.finditer(path.read_text(encoding="utf-8")):
            found[m.group("name")] = path.relative_to(ROOT).as_posix()

    if not scanned:
        fail("rules/ 下一个 .cs 都没扫到 —— 这条检查在空转")
        return
    if not found:
        fail("rules/ 下一个带 Camera 字样的类型都没扫到 —— 相机状态机不在规则层，"
             "或本脚本的数法过时了")
        return

    extra = sorted(set(found) - RULES_CAMERA_TYPES)
    if extra:
        fail(f"规则层多出这些相机类型：{[f'{n}({found[n]})' for n in extra]} —— "
             f"登记在册的只有 {sorted(RULES_CAMERA_TYPES)}，多一份就是多一套实现")
    else:
        ok(f"扫过 rules/ 下 {len(scanned)} 个 .cs，相机类型正是登记的 {sorted(found)}")


def check_no_camera_node_in_scenes() -> None:
    """场景里摆的 `Camera2D` 必须挂着那份实现的脚本，否则等于绕过它另开一台。"""
    scenes = sorted(SCENES_DIR.rglob("*.tscn")) if SCENES_DIR.is_dir() else []
    if not scenes:
        fail("scenes/ 下一个 .tscn 都没扫到 —— 这条检查在空转")
        return

    naked: list[str] = []
    total = 0
    for path in scenes:
        text = path.read_text(encoding="utf-8")
        for block in re.findall(r'\[node [^\]]*type="Camera2D"[^\]]*\](.*?)(?=\n\[|\Z)',
                                text, re.DOTALL):
            total += 1
            if "GameCamera" not in block:
                naked.append(path.relative_to(ROOT).as_posix())

    if naked:
        fail(f"这些场景里摆了不挂 GameCamera 脚本的 Camera2D：{naked} —— "
             f"绕过那份实现另开一台相机，行为不受任何判据管")
    else:
        ok(f"扫过 {len(scenes)} 个 .tscn，其中 Camera2D 节点 {total} 个，没有绕过 GameCamera 的")


def check_no_hardcoded_buildable_size() -> None:
    """可建造区的安装点不许出现整数字面量尺寸（0 起点除外）。"""
    offenders: list[str] = []
    scanned = cs_files(SRC_DIR) + cs_files(RULES_DIR)
    calls = 0
    for path in scanned:
        text = path.read_text(encoding="utf-8")
        for m in BUILDABLE_CALL_RE.finditer(text):
            calls += 1
            args = m.group("args")
            literals = [tok for tok in re.findall(r"\b\d+\b", args) if tok != "0"]
            if literals:
                line = text[: m.start()].count("\n") + 1
                offenders.append(f"{path.relative_to(ROOT).as_posix()}:{line} 字面量 {literals}")

    if calls == 0:
        fail("src/ 与 rules/ 里一处可建造区安装点都没扫到 —— 这条检查在空转")
    elif offenders:
        fail(f"这些地方把可建造区尺寸写成了字面量：{offenders} —— PRD 的 FR-24 要求从配置读，"
             f"是否扩大已记 GP-8")
    else:
        ok(f"扫过 {len(scanned)} 个 .cs、{calls} 处可建造区安装点，尺寸都不是字面量")


def check_no_pixel_snap() -> None:
    """项目不许打开 2D 像素吸附 —— 本作靠相机自己保证整数，靠吸附会让分数位移的反证失效。"""
    text = PROJECT_GODOT.read_text(encoding="utf-8")
    on = [m.group(0) for m in re.finditer(r"snap_2d_\w+_to_pixel\s*=\s*true", text)]
    if on:
        fail(f"project.godot 打开了 2D 像素吸附（{on}）—— 相机的整数保证会被它掩盖，"
             f"而分数位移的反证会静默变成「通过」")
    else:
        ok("project.godot 没打开 2D 像素吸附，相机的整数保证是自己的责任")


# ── 跑引擎 ──────────────────────────────────────────────────────────────
def run_engine(launcher: list[str], log: Path) -> str | None:
    """跑一次并把引擎日志读回来。**刻意不带 --headless** —— 像素对齐那条要真截图。"""
    log.parent.mkdir(parents=True, exist_ok=True)
    if log.exists():
        log.unlink()
    cmd = launcher + ["--resolution", f"{WINDOW[0]}x{WINDOW[1]}",
                      "--log-file", str(log), "--quit-after", "600"]
    try:
        subprocess.run(cmd, check=False, timeout=180,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except subprocess.TimeoutExpired:
        fail("引擎超时 180s 未退出")
        return None
    if not log.is_file():
        return None
    return log.read_text(encoding="utf-8", errors="replace")


# ── 二、两种视角跑的是同一批判据 ────────────────────────────────────────
def check_views_share_one_implementation(text: str) -> None:
    rows = {m.group(1): m for m in VIEW_RE.finditer(text)}
    missing = [v for v in CANON_VIEWS if v not in rows]
    if missing:
        fail(f"这些视角没打出「视角」那一行，无法核：{missing} —— 自检没跑到，或格式变了")
        return

    impls = {rows[v].group(7) for v in CANON_VIEWS}
    if len(impls) != 1:
        fail(f"两种视角报出的实现类型不同：{sorted(impls)} —— 正典要求共用同一份")
    else:
        ok(f"两种视角报出的实现是同一个类型：{impls.pop()}")

    verdicts: dict[str, set[str]] = {}
    for _, tag, what, _ in VERDICT_RE.findall(text):
        verdicts.setdefault(tag.strip(), set()).add(what.strip())

    per_view = {v: verdicts.get(v, set()) for v in CANON_VIEWS}
    if not all(per_view.values()):
        fail(f"某个视角一条判据都没有：{ {v: len(s) for v, s in per_view.items()} }")
        return

    only = {v: sorted(per_view[v] - per_view[other])
            for v, other in ((CANON_VIEWS[0], CANON_VIEWS[1]),
                             (CANON_VIEWS[1], CANON_VIEWS[0]))}
    if any(only.values()):
        fail(f"两种视角的判据名字集合不同 —— 只在一边成立的行为："
             f"{ {v: names for v, names in only.items() if names} }。"
             f"这正是「两种视角各写一套」的行为投影")
    else:
        ok(f"两种视角各跑了同一批 {len(per_view[CANON_VIEWS[0]])} 条判据，名字集合完全相同")


# ── 三、缩放与视野对得上正典 ────────────────────────────────────────────
def check_zoom_and_view_against_canon(text: str) -> None:
    rows = {m.group(1): m for m in VIEW_RE.finditer(text)}
    for view in CANON_VIEWS:
        m = rows.get(view)
        if m is None:
            fail(f"{view} 没打出「视角」那一行，缩放与视野无法核")
            continue

        zoom = int(m.group(2))
        got_view = (int(m.group(3)), int(m.group(4)))
        dead = (int(m.group(5)), int(m.group(6)))
        want_zoom = CANON_ZOOM[view]
        want_view = CANON_VIEW[view]

        if zoom != want_zoom:
            fail(f"{view} 缩放 x{zoom}，正典是 x{want_zoom}")
        elif got_view != want_view:
            fail(f"{view} 有效视野 {got_view[0]}x{got_view[1]}，"
                 f"按正典应是 {want_view[0]}x{want_view[1]}")
        else:
            ok(f"{view} 缩放 x{zoom}、有效视野 {got_view[0]}x{got_view[1]}，与正典一致"
               f"（死区 {dead[0]}x{dead[1]} 世界像素 = {dead[0] * zoom}x{dead[1] * zoom} 屏幕像素）")

    # 死区换算回屏幕像素必须两边相等 —— 这是「同一个数服务两种视角」的算式形态。
    screen = {v: (int(rows[v].group(5)) * int(rows[v].group(2)),
                  int(rows[v].group(6)) * int(rows[v].group(2)))
              for v in CANON_VIEWS if v in rows}
    if len(set(screen.values())) == 1:
        ok(f"死区换算回屏幕像素两种视角相同：{set(screen.values()).pop()}")
    else:
        fail(f"死区换算回屏幕像素两种视角不同：{screen} —— 取景会不一致")


# ── 四、可建造区尺寸真的来自配置 ────────────────────────────────────────
def expected_buildable_pixels() -> tuple[int, int] | str:
    """本脚本自己解配置，作为独立来源。"""
    try:
        data = json.loads(GAME_JSON.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as e:
        return f"读不动 {GAME_JSON.name}：{e}"
    try:
        return (int(data["buildableWidthCells"]) * CELL_PX,
                int(data["buildableHeightCells"]) * CELL_PX)
    except (KeyError, TypeError, ValueError) as e:
        return f"{GAME_JSON.name} 里缺可建造区字段或值不是整数：{e}"


def check_buildable_from_config(text: str) -> None:
    want = expected_buildable_pixels()
    if isinstance(want, str):
        fail(want)
        return

    installed = {(int(m.group(1)), int(m.group(2))) for m in INSTALLED_RE.finditer(text)}
    if not installed:
        fail("日志里没有「装进相机的可建造区」那一行 —— 自检没跑到，或格式变了")
        return
    if len(installed) != 1:
        fail(f"不同视角装进相机的可建造区不一致：{sorted(installed)}")
        return

    got = installed.pop()
    if got != want:
        fail(f"装进相机的可建造区 {got[0]}x{got[1]}px，"
             f"按 {GAME_JSON.name} 应是 {want[0]}x{want[1]}px —— "
             f"代码里另写死了一份，或读错了字段；两种情况都让改配置改不动它（PRD 的 FR-24）")
    else:
        ok(f"装进相机的可建造区 {got[0]}x{got[1]}px，与 {GAME_JSON.name} 解出的"
           f"{want[0] // CELL_PX}x{want[1] // CELL_PX} 格一致")


# ── 五、像素对齐实测 ────────────────────────────────────────────────────
def check_pixel_alignment(text: str) -> None:
    """三次截图：基线、分数位置、非整数总缩放。第三次是反证 —— 量具要能量出失败。"""
    if SKIPPED_RE.search(text):
        fail("引擎侧跳过了像素对齐实测（没有渲染设备）—— 本守卫刻意不带 --headless 就是为了这一项，"
             "跳过说明启动参数被改过或显示后端起不来")
        return

    rows = {m.group("tag"): m for m in MEASURE_RE.finditer(text)}
    for tag in (TAG_INTEGER, TAG_FRACTIONAL, TAG_BROKEN_ZOOM):
        if tag not in rows:
            fail(f"日志里没有「量 {tag}」那一行 —— 实测没跑到，或格式变了")
            return

    base = rows[TAG_INTEGER]
    frac = rows[TAG_FRACTIONAL]
    broken = rows[TAG_BROKEN_ZOOM]
    expect = int(base.group("expect"))
    scale = int(base.group("scale"))
    zoom = float(base.group("zoom"))

    if expect != scale * round(zoom):
        fail(f"期望跑长 {expect} 不等于视口缩放 x{scale} 乘相机缩放 x{zoom:g} —— "
             f"引擎侧算期望值的那一行有问题")
        return

    for tag in (TAG_INTEGER, TAG_FRACTIONAL, TAG_BROKEN_ZOOM):
        samples = int(rows[tag].group("samples"))
        if samples < MIN_SAMPLES:
            fail(f"{tag} 只统计到 {samples} 段棋盘格像素（下限 {MIN_SAMPLES}）—— "
                 f"图案没铺上或被界面盖住了，这个「最小跑长」是空转出来的")
            return

    # 前两次截图的相机位置必须真的一个整数一个分数。少了这一条，「引擎不受分数位置影响」与
    # 「偏移根本没生效」在日志里长得一模一样 —— 本轮第一版就栽在这里：相机自己的每帧同步在
    # 同一帧后面把位置抄回了整数，那次测的根本不是分数位置。
    def is_fractional(m: re.Match[str]) -> bool:
        return any(abs(float(m.group(axis)) - round(float(m.group(axis)))) > 1e-4
                   for axis in ("x", "y"))

    if is_fractional(base):
        fail(f"「{TAG_INTEGER}」那次的相机位置 {base.group('x')},{base.group('y')} 不是整数 —— "
             f"基线本身就没对齐，后面的比对没有意义")
        return
    if not is_fractional(frac):
        fail(f"「{TAG_FRACTIONAL}」那次的相机位置 {frac.group('x')},{frac.group('y')} 仍是整数"
             f" —— 偏移被谁抄回去了，这一轮测的不是分数位置")
        return
    ok(f"两次截图的相机位置一个整数（{base.group('x')}）一个分数（{frac.group('x')}），"
       f"分数位置真的进了渲染")

    if int(base.group("run")) != expect:
        fail(f"整数位置下最小同色跑长 {base.group('run')}，应恰好等于 {expect}"
             f"（x{scale} 视口 × x{zoom:g} 相机）—— 像素块没整块摊开，画面已经在糊")
    else:
        ok(f"整数位置下每个世界像素摊成 {expect}x{expect} 物理像素"
           f"（统计 {base.group('samples')} 段）")

    # **这条判的是「不变」，不是「变小」。** 实测结论（2026-08-31）：整数总缩放下相机的分数位置
    # 不改变任何物理像素 —— 每个世界像素的屏幕跨度 [kS+d, (k+1)S+d) 恰好包住 S 个像素中心，
    # 与偏移 d 无关。所以取整的理由不是清晰度而是运动可预期，见 UI-5 的实现笔记。
    if int(frac.group("run")) != expect:
        fail(f"分数位置下最小同色跑长 {frac.group('run')} 与整数位置的 {expect} 不同 —— "
             f"实测结论变了（原结论：整数总缩放下平移不影响采样）。先复核视口缩放是否仍是整数"
             f"（那条归 check_scaling.py），再更新 UI-5 的实现笔记")
    else:
        ok(f"分数位置下最小同色跑长仍是 {expect}，与基线 {frac.group('diff')}")

    # 反证：真会切开像素块的是非整数总缩放，而那正是代码挡住的东西。
    got_broken = int(broken.group("run"))
    if got_broken <= 0 or got_broken >= expect:
        fail(f"非整数总缩放（相机缩放 {broken.group('zoom')}）下最小同色跑长 {got_broken}，"
             f"没有小于 {expect} —— 反证不成立：量具量不出「切开」，那它量出的「没切开」"
             f"也不算证据。要么有人打开了像素吸附，要么这个缩放乘视口缩放又凑成了整数")
    else:
        ok(f"反证成立：相机缩放 {broken.group('zoom')} 让总缩放非整数后，最小同色跑长掉到 "
           f"{got_broken}（< {expect}）—— 所以缩放只收正整数，这条量具真的量得出切开")


def check_verdicts(text: str) -> None:
    verdicts = VERDICT_RE.findall(text)
    if not verdicts:
        fail("启动日志里一条 [相机] 判据都没有 —— CameraProbe 没跑，或输出格式变了")
        return

    failed = [(tag.strip(), what.strip(), detail.strip())
              for verdict, tag, what, detail in verdicts if verdict == "FAIL"]
    for tag, what, detail in failed:
        fail(f"实机判据不成立：{tag} 的「{what}」（{detail}）")
    if not failed:
        ok(f"启动自检 {len(verdicts)} 条实机判据全部成立")

    m = SELFCHECK_RE.search(text)
    if m is None:
        fail("自检没打出总数那一行 —— 步骤可能中途抛异常退出了")
    elif m.group(1) != m.group(2):
        fail(f"自检自报 {m.group(1)}/{m.group(2)} 条通过")
    elif int(m.group(2)) != len(verdicts):
        fail(f"自检自报 {m.group(2)} 条，日志里实际有 {len(verdicts)} 条 —— 两个数对不上")
    else:
        ok(f"自检自报条数与日志实际条数一致，都是 {len(verdicts)} 条")


def main() -> int:
    ap = argparse.ArgumentParser(description="相机五项行为实测（UI-5）")
    ap.add_argument("--exported", action="store_true", help="跑 export/ 下的产物而不是工程源码")
    args = ap.parse_args()

    if args.exported:
        exe = find_exported()
        if exe is None:
            say("[FAIL] export/ 下没有产物 —— 先跑 python tools/verify.py")
            print("EXIT=1")
            return 1
        launcher = [str(exe)]
        say(f"被测对象 产物 {exe.name}")
    else:
        godot = find_godot()
        if godot is None:
            say(f"[FAIL] 在 {GODOT_ROOT} 下找不到 mono 版 *_console.exe")
            print("EXIT=1")
            return 1
        launcher = [str(godot), "--path", str(ROOT)]
        say(f"被测对象 工程源码（{godot.name}）")

    want = expected_buildable_pixels()
    say(f"可建造区（本脚本自己解 {GAME_JSON.relative_to(ROOT).as_posix()}）："
        f"{want if isinstance(want, str) else f'{want[0]}x{want[1]}px'}")
    say()

    # 不依赖引擎的先跑：源码扫描比起引擎快得多，坏在这里就不必等引擎。
    check_single_camera_implementation()
    check_no_extra_camera_types_in_rules()
    check_no_camera_node_in_scenes()
    check_no_hardcoded_buildable_size()
    check_no_pixel_snap()
    say()

    stamp = time.strftime("%Y%m%d-%H%M%S")
    log = LOG_DIR / stamp / "engine.log"
    text = run_engine(launcher, log)
    if text is None:
        fail("没拿到引擎日志，实机那几条判定未验证")
    else:
        say(f"引擎日志 {log.relative_to(ROOT).as_posix()}")
        check_views_share_one_implementation(text)
        check_zoom_and_view_against_canon(text)
        check_buildable_from_config(text)
        check_pixel_alignment(text)
        check_verdicts(text)

    say()
    say(f"覆盖量：{_CHECKED} 条判据，其中 {len(_FAILS)} 条失败")
    say("检查范围：引擎层只有一个 sealed 的 Camera2D 派生类型、规则层相机类型登记比对、"
        "场景里没有绕过 GameCamera 的 Camera2D、可建造区尺寸不是字面量、项目未开像素吸附、"
        "两种视角判据名字集合全等、缩放与视野对正典、装进相机的可建造区对配置、"
        "整数与分数位置下的像素块实测（含反证）、启动自检零 FAIL 且自报条数与实际一致")
    summary = LOG_DIR / stamp / "summary.log"
    summary.parent.mkdir(parents=True, exist_ok=True)
    summary.write_text("\n".join(_LINES) + "\n", encoding="utf-8", newline="\n")
    say(f"日志 {summary.relative_to(ROOT).as_posix()}")
    _pruned = prune_runs(LOG_DIR)
    if _pruned:
        say(f"[清理] logs/camera 删掉 {len(_pruned)} 份旧日志，只留最近 {KEEP_RUNS} 次")
    print("EXIT=" + ("1" if _FAILS else "0"))
    return 1 if _FAILS else 0


if __name__ == "__main__":
    raise SystemExit(main())
