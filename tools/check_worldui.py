#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""守世界空间 UI 的两条会静默退化的规则（`UI-9`）。

读条圆环、精英血条、伤害数字都画在**执行者身上**、随角色走、在侧视 2 倍缩放下不偏移不变形。
支撑这些的关键事实有两类，共同点是**失效都不报错**：

1. **元素挂在世界空间层（`UiLayer.WorldSpace`），不是屏幕空间的 Hud 层。** 世界空间层开了
   `FollowViewportEnabled`，子节点用世界坐标、自动跟相机变换与 2 倍缩放；一旦被改挂到 Hud 层，
   读条就画到界面角落、不再跟角色走 —— 而正典明确要求「读条画在执行者身上而不是界面角落」。
   这个改动一行报错都没有，画面只是「位置不对」。**静态核**扫 `src/UI/WorldSpaceUi.cs`，
   盯住挂载点是 `LayerOf(UiLayer.WorldSpace)`、且代码里不碰别的层。

2. **引擎侧真的挂对了层、跟得上目标、按状态显隐。** 规则层单测钉得住「血条只精英、伤害默认关、
   进度钳制」这些**判定**，钉不住「圆环的父节点真是那一层」「那层真开了 FollowViewport」
   「圆环位置真跟上了目标世界坐标」—— 那几件要有引擎在场。所以 `src/UI/WorldSpaceProbe.cs`
   启动时跑一遍脚本化自检、把每条判据打进日志，**行为核**读回来判：零 FAIL、自报条数与实际一致、
   判据标签集合与登记逐条对上。

**可以带 `--headless`（与 `check_hud.py`／`check_camera.py` 不同）。** 那两个要真窗口（撑开逻辑
宽度、截图量像素块），本条全是逻辑与节点关系检查、不截图。探针也因此排在启动探针链**最前面**、
任何相机出现之前跑 —— 那时世界空间层的画布变换是恒等，「圆环位置等于目标世界坐标」测起来干净。

纹理过滤覆盖不在这里核：那条是全仓规则，归 `ENG-13`（`tools/check_assets.py` 扫 `src/` 与
`rules/` 的 C#，本文件也在其内）。这里再抄一份就是第二份真相。

不用管道读中文输出（设计仓 reference/踩坑记录.md 第 27 条）：日志由引擎写 `--log-file`，
本脚本自己 decode。

用法（从代码仓根目录运行）：
    python tools/check_worldui.py             # 跑工程源码
    python tools/check_worldui.py --exported  # 跑 export/ 下的产物
    python tools/check_worldui.py --static    # 只跑静态核，不起引擎

改了本脚本就跑 `python tools/selfcheck_worldui.py` 自证。

输出约定与 `verify.py` 一致：逐条 [OK]／[FAIL]，末尾打覆盖量与一行 EXIT=。
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
import time
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

from loglib import prune_runs, KEEP_RUNS  # noqa: E402  同目录工具（跑完修剪旧日志）

ROOT = Path(__file__).resolve().parent.parent
LOG_DIR = ROOT / "logs" / "worldui"
GODOT_ROOT = Path(r"D:\godot\4-7")

# ── 静态核：挂载层不变式 ────────────────────────────────────────────────
# 世界空间 UI 的挂载点。挂到 Hud 就成了屏幕空间：读条画在界面角落而不是执行者身上（正典禁止），
# 随角色走与 2 倍缩放跟随都失效 —— 一行报错都没有。
WORLDUI_FILE = "src/UI/WorldSpaceUi.cs"
MOUNT_LAYER = "WorldSpace"

# `LayerOf(UiLayer.X)` 的挂载点，与代码里任何 `UiLayer.X` 引用。
MOUNT_RE = re.compile(r"LayerOf\(\s*UiLayer\.(\w+)\s*\)")
LAYER_RE = re.compile(r"\bUiLayer\.(\w+)\b")

# ── 行为核：探针判据的标签，逐条登记 ────────────────────────────────────
# 名字集合必须与登记的一模一样：漏一条说明少测了、多一条说明有人加了判据却没登记
# （失败方向没人核过），两种都不该悄悄溜过（同一手法见 check_hud.py 的 LAYOUT_CHECKS）。
WORLDUI_TAGS = {"挂载层", "缩放跟随", "跟随", "中断", "血条精英", "伤害数字", "字体"}

VERDICT_RE = re.compile(r"\[世界UI\] 判据 (PASS|FAIL) ｜ ([^｜]+) ｜ ([^｜]+) ｜ (.*)")
SELFCHECK_RE = re.compile(r"\[世界UI\] 自检 (\d+)/(\d+) 条通过")

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


# ── 静态核 ──────────────────────────────────────────────────────────────
def strip_comments_and_strings(text: str) -> str:
    """把注释与字符串挖掉，只留下真正会执行的代码。

    必须挖：`WorldSpaceUi.cs` 的文档注释里就写着 `<see cref="UiLayer.Hud"/>`（说明「挂在
    WorldSpace 那一层，不是 Hud」），字符串里有 `res://…` 素材路径。不挖的话「代码里只碰
    WorldSpace 层」会被注释里的 Hud 淹掉，于是永远 FAIL，然后我们学会忽略它 —— 那比没有守卫更坏。

    手写一个小状态机而不是拿正则硬凑：正则在「字符串里有 //」「注释里有引号」这两种形状上必错，
    而两种在本仓都真实存在（路径 `res://…` 就是第一种）。与 `check_hud.py` 同一份实现。
    """
    out: list[str] = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                i += 1
            i += 2
            continue
        if c == '"':
            i += 1
            while i < n and text[i] != '"':
                i += 2 if text[i] == "\\" else 1
            i += 1
            out.append('""')
            continue
        if c == "'":
            i += 1
            while i < n and text[i] != "'":
                i += 2 if text[i] == "\\" else 1
            i += 1
            out.append("''")
            continue
        out.append(c)
        i += 1
    return "".join(out)


def check_static() -> None:
    path = ROOT / WORLDUI_FILE
    if not path.is_file():
        fail(f"{WORLDUI_FILE} 不在 —— 静态核在空转，而空转的守卫也会「全绿」")
        return

    code = strip_comments_and_strings(path.read_text(encoding="utf-8"))
    mounts = sorted(set(MOUNT_RE.findall(code)))
    layers = set(LAYER_RE.findall(code))

    if not mounts:
        fail(f"{WORLDUI_FILE} 里找不到 LayerOf(UiLayer.X) 挂载点 —— 写法变了，静态核已经在空转")
    elif mounts != [MOUNT_LAYER]:
        fail(f"世界空间 UI 的挂载层是 {mounts}，必须是 {MOUNT_LAYER} —— 挂到 Hud 就成了屏幕空间："
             f"读条画在界面角落而不是执行者身上（正典禁止），随角色走与 2 倍缩放跟随都会失效，"
             f"且一行报错都没有")
    else:
        ok(f"世界空间 UI 挂在 UiLayer.{MOUNT_LAYER}（{WORLDUI_FILE} 里 LayerOf 挂载点都是它）")

    other = sorted(layers - {MOUNT_LAYER})
    if other:
        fail(f"{WORLDUI_FILE} 代码里还引用了别的 UiLayer：{other} —— 世界空间 UI 只该碰 "
             f"{MOUNT_LAYER} 那一层，碰到 Hud 等屏幕空间层就说明有元素画错了地方")
    else:
        ok(f"{WORLDUI_FILE} 代码里除 {MOUNT_LAYER} 外没有引用别的 UiLayer")


# ── 跑引擎 ──────────────────────────────────────────────────────────────
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


def run_engine(launcher: list[str], log: Path) -> str | None:
    """跑一次并把引擎日志读回来。**可以带 --headless** —— 全是逻辑与节点关系检查，不截图。

    探针排在启动探针链最前、任何相机之前，headless 下照样发 [世界UI] 判据；同一次启动里
    相机与 HUD 探针会各自跳过要窗口的那几步，与本守卫无关（只读 [世界UI]）。
    """
    log.parent.mkdir(parents=True, exist_ok=True)
    if log.exists():
        log.unlink()
    cmd = launcher + ["--headless", "--log-file", str(log), "--quit-after", "600"]
    try:
        subprocess.run(cmd, check=False, timeout=180,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except subprocess.TimeoutExpired:
        fail("引擎超时 180s 未退出")
        return None
    return log.read_text(encoding="utf-8", errors="replace") if log.is_file() else None


# ── 行为核 ──────────────────────────────────────────────────────────────
def check_verdicts(text: str) -> list[tuple[str, str, str, str]]:
    verdicts = VERDICT_RE.findall(text)
    if not verdicts:
        fail("启动日志里一条 [世界UI] 判据都没有 —— WorldSpaceProbe 没跑，或输出格式变了")
        return []

    failed = [(t.strip(), w.strip(), d.strip()) for v, t, w, d in verdicts if v == "FAIL"]
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
    return verdicts


def check_tags(verdicts: list[tuple[str, str, str, str]]) -> None:
    """判据的标签集合必须与登记的一模一样，且条数正好等于登记数（没有重复顶替）。"""
    tags = [t.strip() for _, t, _, _ in verdicts]
    got = set(tags)
    missing = sorted(WORLDUI_TAGS - got)
    extra = sorted(got - WORLDUI_TAGS)
    if missing or extra:
        fail(f"判据标签与登记不符 —— 少了 {missing or '无'}，多了 {extra or '无'}。"
             f"少了说明漏测（不报错），多了说明有人加了判据却没在这里登记（失败方向没人核过）")
    elif len(tags) != len(WORLDUI_TAGS):
        fail(f"判据有重复标签：共 {len(tags)} 条却只有 {len(got)} 个不同标签 —— "
             f"重复会让「名字集合对上」与「自报条数一致」互相打掩护")
    else:
        ok(f"{len(got)} 条判据的标签与登记逐条对上（{'、'.join(sorted(WORLDUI_TAGS))}）")


def analyse(text: str) -> None:
    verdicts = check_verdicts(text)
    if verdicts:
        check_tags(verdicts)


def main() -> int:
    ap = argparse.ArgumentParser(description="世界空间 UI 的挂载层与行为守卫（UI-9）")
    ap.add_argument("--exported", action="store_true", help="跑 export/ 下的产物而不是工程源码")
    ap.add_argument("--static", action="store_true", help="只跑静态核，不起引擎")
    args = ap.parse_args()

    check_static()
    say()

    stamp = time.strftime("%Y%m%d-%H%M%S")
    if args.static:
        say("范围：只跑了静态核（--static）—— **不能当一次验收**，行为核那几条未验证")
    else:
        if args.exported:
            exe = find_exported()
            if exe is None:
                fail("export/ 下没有产物 —— 先跑 python tools/verify.py")
                launcher = None
            else:
                launcher = [str(exe)]
                say(f"被测对象 产物 {exe.name}")
        else:
            godot = find_godot()
            if godot is None:
                fail(f"在 {GODOT_ROOT} 下找不到 mono 版 *_console.exe")
                launcher = None
            else:
                launcher = [str(godot), "--path", str(ROOT)]
                say(f"被测对象 工程源码（{godot.name}）")

        if launcher is not None:
            log = LOG_DIR / stamp / "engine.log"
            text = run_engine(launcher, log)
            if text is None:
                fail("没拿到引擎日志，行为核那几条未验证")
            else:
                say(f"引擎日志 {log.relative_to(ROOT).as_posix()}")
                analyse(text)

    say()
    say(f"覆盖量：{_CHECKED} 条判据，其中 {len(_FAILS)} 条失败")
    say("检查范围：静态核（世界空间 UI 挂在 UiLayer.WorldSpace、代码里不碰别的层）；"
        "行为核（启动自检零 FAIL、自报条数与实际一致、判据标签与登记逐条对上）")
    summary = LOG_DIR / stamp / "summary.log"
    summary.parent.mkdir(parents=True, exist_ok=True)
    summary.write_text("\n".join(_LINES) + "\n", encoding="utf-8", newline="\n")
    say(f"日志 {summary.relative_to(ROOT).as_posix()}")
    _pruned = prune_runs(LOG_DIR)
    if _pruned:
        say(f"[清理] logs/worldui 删掉 {len(_pruned)} 份旧日志，只留最近 {KEEP_RUNS} 次")
    print("EXIT=" + ("1" if _FAILS else "0"))
    return 1 if _FAILS else 0


if __name__ == "__main__":
    raise SystemExit(main())
