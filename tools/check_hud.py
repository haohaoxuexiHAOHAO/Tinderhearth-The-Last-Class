#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""守关卡 HUD 的两条会静默退化的规则（`UI-8`）。

两条规则都是 `UI-8` 的验收标准，而两条的失效方式都不报错：

1. **没有绝对像素坐标，全部锚点与容器。** `aspect="expand"` 下逻辑宽度是变量（`UI-3` 实测
   3840×2130 的窗口得到 649×360），写死横向坐标的界面**在 640 宽的窗口上看起来完全正常**，
   只在宽窗口上错位。它同时是 `UI-2`（其他宽高比适配）的零成本前提。
2. **显示的数值全部由视图模型传入。** 界面里图省事写一个 100 当 HP 上限，代码照样跑、
   画面照样对，等 `design/数值模型.md` 真接进来才发现有两份互相矛盾的事实。

**一条规则两道核，缺一不可**：

- **静态核**扫界面源码：位置类 API、除 0 与 1 之外的数字字面量、纹理过滤覆盖。它证明得了
  「没写死」，证明不了「真的读了传进来那份」—— 界面完全可以拿视图模型当摆设、画一根固定长度的条。
- **行为核**读启动日志（`src/UI/HudProbe.cs` 打的判据）：块的实际屏幕矩形与规则层预测逐块一致、
  把窗口改成另一个宽高比后左锚点不动右锚点跟着走、四块全部落在整数逻辑像素上、
  灌三份不同的量看条长真的跟着变。

**不能用 `--headless`**：headless 下 `DisplayServer` 是 dummy 后端，改窗口尺寸不会让拉伸重算，
撑开那一段量出来的两次会一样 —— 判据会**假过**。所以引擎侧遇到 headless 会显式打「量 跳过」，
而本守卫**把「跳过」判成失败**。

不用管道读中文输出（设计仓 reference/踩坑记录.md 第 27 条）：日志由引擎写 `--log-file`，
本脚本自己 decode。

用法（从代码仓根目录运行）：
    python tools/check_hud.py             # 跑工程源码
    python tools/check_hud.py --exported  # 跑 export/ 下的产物
    python tools/check_hud.py --static    # 只跑静态核，不起引擎

改了本脚本就跑 `python tools/selfcheck_hud.py` 自证。

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
LOG_DIR = ROOT / "logs" / "hud"
GODOT_ROOT = Path(r"D:\godot\4-7")

# 跑实测用的窗口。720p 给整数视口缩放 ×2；引擎侧会自己再改成 1298×720 撑出奇数逻辑宽度。
WINDOW = (1280, 720)

# ── 静态核的扫描范围 ────────────────────────────────────────────────────
# 按**名字模式**取而不是写死一份清单：将来新增 `src/UI/HudXxx.cs` 会自动进扫描范围，
# 而写死清单的话新文件默认逃过检查 —— 那种漏法不报错。
HUD_GLOB = "src/UI/*Hud*.cs"

# 豁免必须连理由一起登记。不登记的话「忘了扫」与「故意不扫」长得一模一样。
HUD_EXEMPT = {
    "src/UI/HudProbe.cs":
        "启动自检脚手架，不是界面本体。它必须有窗口尺寸这类数字才撑得开逻辑宽度，"
        "而它一行界面都不画。`UI-10` 的端到端测试会替换它",
}

# 演示数值住在界面之外。这个分工就是静态核扫得干净的前提 —— 它不在也判失败。
DEMO_FILE = "src/World/HudDemoModel.cs"

# 位置类 API。**注意 `CustomMinimumSize` 不会被 `\bSize\s*=` 命中**（前面是词字符，边界不成立），
# 那是有意的：定尺寸是容器的正常用法，定位置才是这里要禁的。
POSITION_RE = re.compile(
    r"\b(?:Global)?Position\s*=|\bSet(?:Global)?Position\s*\(|\bSize\s*=|\bSetSize\s*\("
    r"|\bSetDeferred\s*\(\s*\"position\"")
# 逻辑分辨率常量：拿它排版就等于假设宽度是定值，而它是下限不是定值。
RESOLUTION_RE = re.compile(r"\bUiMetrics\.Base(?:Width|Height)\b")
# 纹理过滤覆盖：一次手滑静默毁掉整棵子树的文字与素材（全仓守卫归 `ENG-13`，这里只守 HUD）。
FILTER_RE = re.compile(r"\bTextureFilter\b")
# 数字字面量。0 与 1 是结构量（第一个元素、间距为零、加一取编号），其余一律不许。
NUMBER_RE = re.compile(r"(?<![\w.])\d+(?:\.\d+)?[fFdDmMuUlL]?\b")
ALLOWED_NUMBERS = {"0", "1"}

# ── 行为核：期望值写在这里，不从代码里解 ────────────────────────────────
# 让代码自己报期望等于自己证明自己（`UI-7` 栽过：正则解 C# 取期望值，静默少解 12 条而守卫
# 照样全绿）。下面这些的出处是 ADR-0008 与 UI-8 的 issue，不是源码。
FONT_PROPERTY_COUNT = 10          # ADR-0008 的十项像素完美属性
FONT_METRIC_CHECKS = 1            # 外加一条汉字宽度度量
FONT_SIZE = 12                    # ADR-0008：字号 12px
LINE_HEIGHT = 16                  # ADR-0008：行高 16px
FONT_VERSION = "2026.08.11"       # ADR-0008：版本钉死
FONT_PATH = "res://assets/fonts/fusion-pixel-12px-proportional-zh_hans.ttf"
LICENSE_PATH = ROOT / "assets" / "fonts" / "LICENSE-OFL.txt"

# 排版那一组的判据名字，**逐条登记**。名字集合必须与登记的一模一样：漏一条说明少测了，
# 多一条说明有人加了判据却没在这里登记 —— 后者的失败方向没人核过，同样不该悄悄溜过。
LAYOUT_CHECKS = {
    "HUD 根节点铺满视口",
    "四块的实际矩形与规则层预测逐块一致",
    "四块的算式与容器要的最小尺寸逐块相等",
    "四块都不压角色可能出现的那块",
    "可读横带贯通两端且高于角色可读区",
    "逻辑宽度撑开后左锚点不动右锚点跟着走",
    "四块全部落在整数逻辑像素上",
}
LAYOUT_TAG = "排版"
# 判据分组，各至少出现一次。
PLAIN_TAGS = (LAYOUT_TAG, "字体", "文本", "视图模型", "修饰键", "收尾")
# HUD 占屏上限。UI-8 的立项理由就是它落在全游戏最挤的地方，超过一成就开始挤中间那块可读区。
MAX_COVERAGE = 0.10

VERDICT_RE = re.compile(r"\[HUD\] 判据 (PASS|FAIL) ｜ ([^｜]+) ｜ ([^｜]+) ｜ (.*)")
SELFCHECK_RE = re.compile(r"\[HUD\] 自检 (\d+)/(\d+) 条通过")
SKIPPED_RE = re.compile(r"\[HUD\] 量 跳过")
FONT_RE = re.compile(
    r"\[HUD\] 字体 (?P<path>\S+) ｜ 版本 (?P<version>\S+) ｜ 字号 (?P<size>\d+)"
    r" 行高 (?P<line>\d+) ｜ 全局回退字体已换 (?P<fallback>\w+) ｜ 回退字号 (?P<fbsize>\d+)")
BAND_RE = re.compile(r"横带 (\d+)x(\d+)（占屏高 ([\d.]+)%）｜HUD 占屏 ([\d.]+)%")

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

    必须挖：本项目的注释里写满了 640×360、12px、§9、ADR-0008 这类数字，字符串里有素材路径与
    `{0}` 占位。不挖的话「代码里没有写死的数」这条判据会被注释里的数字淹掉，于是永远 FAIL，
    然后我们学会忽略它 —— 那比没有守卫更坏。

    手写一个小状态机而不是拿正则硬凑：正则在「字符串里有 //」「注释里有引号」这两种形状上必错，
    而两种在本仓都真实存在（路径 `res://…` 就是第一种）。
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
            # 逐字扫到配对的引号，认转义。插值串里的洞（`{...}`）一起挖掉 —— 本仓的洞里
            # 只有标识符，挖掉不会漏掉数字字面量。
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


def hud_node_files() -> tuple[list[Path], list[str]]:
    """取要扫的界面源码，与被豁免的那几个。"""
    found = sorted(ROOT.glob(HUD_GLOB))
    keep: list[Path] = []
    skipped: list[str] = []
    for path in found:
        rel = path.relative_to(ROOT).as_posix()
        if rel in HUD_EXEMPT:
            skipped.append(f"{rel}（{HUD_EXEMPT[rel]}）")
        else:
            keep.append(path)
    return keep, skipped


def check_static() -> None:
    files, skipped = hud_node_files()
    if not files:
        fail(f"按 {HUD_GLOB} 一个界面源码都没扫到 —— 这两条检查在空转，"
             f"而空转的守卫也会「全绿」")
        return

    say(f"静态核范围：{[p.relative_to(ROOT).as_posix() for p in files]}")
    for note in skipped:
        say(f"           豁免 {note}")

    if not (ROOT / DEMO_FILE).is_file():
        fail(f"{DEMO_FILE} 不在 —— 演示数值必须住在界面之外，那个分工正是静态核扫得干净的前提")
    else:
        ok(f"演示数值住在界面之外：{DEMO_FILE}")

    positions: list[str] = []
    resolutions: list[str] = []
    filters: list[str] = []
    numbers: list[str] = []
    allowed_hits = 0

    for path in files:
        rel = path.relative_to(ROOT).as_posix()
        raw = path.read_text(encoding="utf-8")
        code = strip_comments_and_strings(raw)
        for lineno, line in enumerate(code.splitlines(), start=1):
            for rx, bucket in ((POSITION_RE, positions), (RESOLUTION_RE, resolutions),
                               (FILTER_RE, filters)):
                if (m := rx.search(line)):
                    bucket.append(f"{rel}:{lineno} 「{m.group(0).strip()}」")
            for m in NUMBER_RE.finditer(line):
                if m.group(0) in ALLOWED_NUMBERS:
                    allowed_hits += 1
                else:
                    numbers.append(f"{rel}:{lineno} 「{m.group(0)}」")

    if positions:
        fail(f"界面代码里有位置类 API：{positions[:6]} —— `aspect=\"expand\"` 下逻辑宽度是变量，"
             f"写死坐标的界面只在宽窗口上错位，窄窗口上看不出来")
    else:
        ok(f"扫过 {len(files)} 个界面源码，没有 Position／Size 赋值，位置全靠锚点预设")

    if resolutions:
        fail(f"界面代码里拿逻辑分辨率常量排版：{resolutions[:6]} —— 它是下限不是定值")
    else:
        ok("界面代码没拿 UiMetrics.BaseWidth／BaseHeight 排版")

    if filters:
        fail(f"界面代码碰了 TextureFilter：{filters[:6]} —— 项目级最近邻是 12px 中文清晰的"
             f"唯一依靠（`UI-4` 实测），逐节点覆盖会静默毁掉整棵子树（全仓守卫归 `ENG-13`）")
    else:
        ok("界面代码没有覆盖 TextureFilter")

    if numbers:
        fail(f"界面代码里有数字字面量（0 与 1 之外）：{numbers[:8]} —— "
             f"显示的量必须由视图模型传入，排版量必须从 HudLayout／UiMetrics 取")
    else:
        ok(f"界面代码里除 0 与 1 之外没有数字字面量（结构量命中 {allowed_hits} 处）")


def check_license_present() -> None:
    """字体旁边必须有许可证（OFL 第 2 条）。**进不进发行包**由 verify.py 解包时判（`ART-3`）。"""
    if not LICENSE_PATH.is_file():
        fail(f"{LICENSE_PATH.relative_to(ROOT).as_posix()} 不在 —— OFL 第 2 条要求每份拷贝"
             f"都带许可证与版权声明")
        return
    text = LICENSE_PATH.read_text(encoding="utf-8", errors="replace")
    marks = ["SIL OPEN FONT LICENSE Version 1.1", "Copyright", "Fusion Pixel Font"]
    missing = [m for m in marks if m not in text]
    if missing:
        fail(f"许可证原文里找不到 {missing} —— 换过文件了，去设计仓跑 "
             f"python tools/audit_fonts.py 重新核")
    else:
        ok(f"字体旁的许可证在，且含版权声明与 OFL 1.1 标题（{LICENSE_PATH.stat().st_size} 字节）")


# ── 跑引擎 ──────────────────────────────────────────────────────────────
def find_godot() -> Path | None:
    if not GODOT_ROOT.is_dir():
        return None
    cands = [p for p in GODOT_ROOT.rglob("*_console.exe") if "mono" in p.name]
    return sorted(cands)[0] if cands else None


def find_exported() -> Path | None:
    export = ROOT / "export"
    exes = sorted(export.glob("*.exe")) if export.is_dir() else []
    return exes[0] if exes else None


def run_engine(launcher: list[str], log: Path) -> str | None:
    """跑一次并把引擎日志读回来。**刻意不带 --headless** —— 撑开逻辑宽度要真窗口。"""
    log.parent.mkdir(parents=True, exist_ok=True)
    if log.exists():
        log.unlink()
    cmd = launcher + ["--resolution", f"{WINDOW[0]}x{WINDOW[1]}",
                      "--log-file", str(log), "--quit-after", "900"]
    try:
        subprocess.run(cmd, check=False, timeout=240,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except subprocess.TimeoutExpired:
        fail("引擎超时 240s 未退出")
        return None
    return log.read_text(encoding="utf-8", errors="replace") if log.is_file() else None


# ── 行为核 ──────────────────────────────────────────────────────────────
def check_not_skipped(text: str) -> None:
    if SKIPPED_RE.search(text):
        fail("引擎侧跳过了撑开逻辑宽度那一段（没有真窗口）—— 本守卫刻意不带 --headless "
             "就是为了这一段，跳过说明启动参数被改过或显示后端起不来")
    else:
        ok("撑开逻辑宽度那一段真跑了（没有出现「量 跳过」）")


def check_font_line(text: str) -> None:
    m = FONT_RE.search(text)
    if m is None:
        fail("日志里没有「[HUD] 字体」那一行 —— HudProbe 没跑，或格式变了")
        return

    problems: list[str] = []
    if m.group("path") != FONT_PATH:
        problems.append(f"字体路径 {m.group('path')}，期望 {FONT_PATH}")
    if m.group("version") != FONT_VERSION:
        problems.append(f"版本 {m.group('version')}，ADR-0008 钉的是 {FONT_VERSION}")
    if int(m.group("size")) != FONT_SIZE or int(m.group("line")) != LINE_HEIGHT:
        problems.append(f"字号／行高 {m.group('size')}／{m.group('line')}，"
                        f"ADR-0008 是 {FONT_SIZE}／{LINE_HEIGHT}")
    if m.group("fallback") != "True":
        problems.append("全局回退字体没换成像素字体 —— 忘挂主题的界面会退回引擎默认字体")
    if int(m.group("fbsize")) != FONT_SIZE:
        problems.append(f"回退字号 {m.group('fbsize')}，期望 {FONT_SIZE}")

    if problems:
        fail("；".join(problems))
    else:
        ok(f"像素字体是 ADR-0008 钉的那一份（{FONT_VERSION}），字号 {FONT_SIZE}／行高 "
           f"{LINE_HEIGHT}，且已装成全局回退字体")


def check_font_properties(text: str, verdicts: list[tuple[str, str, str, str]]) -> None:
    font = [v for v in verdicts if v[1].strip() == "字体"]
    want = FONT_PROPERTY_COUNT + FONT_METRIC_CHECKS
    if len(font) != want:
        fail(f"字体那一组有 {len(font)} 条判据，期望 {want} 条"
             f"（ADR-0008 的十项属性 + 一条度量）—— 漏测一项不会报错，所以条数要核")
    else:
        ok(f"字体那一组 {len(font)} 条判据齐全（十项属性 + 一条汉字宽度度量）")


def check_layout_checks(verdicts: list[tuple[str, str, str, str]]) -> None:
    """排版那一组的判据名字集合必须与登记的一模一样。"""
    got = {what.strip() for _, tag, what, _ in verdicts if tag.strip() == LAYOUT_TAG}
    missing = sorted(LAYOUT_CHECKS - got)
    extra = sorted(got - LAYOUT_CHECKS)
    if missing or extra:
        fail(f"排版那一组的判据与登记不符 —— 少了 {missing or '无'}，多了 {extra or '无'}。"
             f"少了说明漏测（不报错），多了说明有人加了判据却没登记（失败方向没人核过）")
    else:
        ok(f"排版那一组 {len(got)} 条判据与登记逐条对上")


def check_plain_tags(verdicts: list[tuple[str, str, str, str]]) -> None:
    seen = {v[1].strip() for v in verdicts}
    missing = [t for t in PLAIN_TAGS if t not in seen]
    if missing:
        fail(f"这几组判据一条都没出现：{missing} —— 自检中途抛异常退出了？")
    else:
        ok(f"{'、'.join(PLAIN_TAGS)} 六组判据都跑了（共 {len(verdicts)} 条）")


def report_layout(text: str) -> None:
    """把占屏与可读横带打出来。**这是量具，同时判占屏上限。**"""
    rows = BAND_RE.findall(text)
    if not rows:
        fail("日志里没有可读横带那一行 —— 排版数据无从核")
        return

    worst = 0.0
    for width, height, share, coverage in rows:
        worst = max(worst, float(coverage) / 100)
        say(f"     量 可读横带 {width}x{height}（占屏高 {share}%）｜HUD 占屏 {coverage}%")
    if worst > MAX_COVERAGE:
        fail(f"HUD 占屏最高 {worst:.2%}，超过上限 {MAX_COVERAGE:.0%} —— "
             f"侧视有效视野只有 320×180，中间那块要留给角色与敌人")
    else:
        ok(f"HUD 占屏最高 {worst:.2%}，在上限 {MAX_COVERAGE:.0%} 之内")


def check_verdicts(text: str) -> list[tuple[str, str, str, str]]:
    verdicts = VERDICT_RE.findall(text)
    if not verdicts:
        fail("启动日志里一条 [HUD] 判据都没有 —— HudProbe 没跑，或输出格式变了")
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


def analyse(text: str) -> None:
    check_not_skipped(text)
    check_font_line(text)
    verdicts = check_verdicts(text)
    if verdicts:
        check_font_properties(text, verdicts)
        check_layout_checks(verdicts)
        check_plain_tags(verdicts)
    report_layout(text)


def main() -> int:
    ap = argparse.ArgumentParser(description="关卡 HUD 的排版与数据来源守卫（UI-8）")
    ap.add_argument("--exported", action="store_true", help="跑 export/ 下的产物而不是工程源码")
    ap.add_argument("--static", action="store_true", help="只跑静态核，不起引擎")
    args = ap.parse_args()

    check_static()
    check_license_present()
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
    say("检查范围：静态核（界面源码无位置类 API、无逻辑分辨率常量、无 TextureFilter 覆盖、"
        "除 0 与 1 外无数字字面量、演示数值在界面之外、字体旁有 OFL 许可证）；"
        "行为核（撑开那段没被跳过、字体是 ADR-0008 钉的那份、十项属性判据条数齐全、"
        "排版那一组判据名字与登记逐条对上、六组判据都跑过、占屏在上限内、"
        "零 FAIL 且自报条数一致）")
    summary = LOG_DIR / stamp / "summary.log"
    summary.parent.mkdir(parents=True, exist_ok=True)
    summary.write_text("\n".join(_LINES) + "\n", encoding="utf-8", newline="\n")
    say(f"日志 {summary.relative_to(ROOT).as_posix()}")
    _pruned = prune_runs(LOG_DIR)
    if _pruned:
        say(f"[清理] logs/hud 删掉 {len(_pruned)} 份旧日志，只留最近 {KEEP_RUNS} 次")
    print("EXIT=" + ("1" if _FAILS else "0"))
    return 1 if _FAILS else 0


if __name__ == "__main__":
    raise SystemExit(main())
