#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""`tools/check_hud.py` 的自证：造真实缺陷形状 → 确认拦得住 → 还原 → 复验。

为什么存在（WORKFLOW §6）：`check_hud.py` 跑过一次全绿**不能**证明它拦得住东西 ——
一个什么都不检的脚本也会全绿。所以每条判定都得用一个真实缺陷形状撞一次。

缺陷形状不是编的，取自本轮真的踩过或差点踩过的：

    根节点 0×0            2026-08-31 实测：对已在树里的节点调 `SetAnchorsPreset` 会改写偏移
                          保住 0×0，于是贴下边贴右边的块落到负坐标、画在屏幕外，一句报错都没有
    算式与实现漂移        技能块行距写 0 而算式按一个间距，块低了 4px（同日撞的）
    导入参数没生效        改了 `.ttf.import` 但没重新导入，抗锯齿仍是灰度 —— 12px 中文多一圈脏边
    界面里写死数字        「图省事写个 100 当 HP 上限」，代码照样跑
    界面里写绝对坐标      窄窗口上完全正常，只在宽窗口上错位
    headless 假过         没有真窗口时改窗口尺寸不会让拉伸重算，撑开那段量出来两次一样

分两类跑，因为代价差一个数量级：

- **解析型**：拿一份真实引擎日志做变异，直接调 `analyse()`。秒级，不起引擎。
- **注入型**：真改源码，跑 `check_hud.py`（静态核用 `--static`，行为核要构建 + 起引擎）。

用法（从代码仓根目录运行）：

    python tools/selfcheck_hud.py           # 全部用例
    python tools/selfcheck_hud.py --fast    # 只跑解析型与静态注入，跳过要起引擎的两条
    python tools/selfcheck_hud.py --list    # 只列用例与覆盖登记

所有注入都在 finally 里还原；临时资源放工作区根的 `temp/`，用完即删（WORKFLOW §5）。
"""

from __future__ import annotations

import argparse
import glob
import os
import re
import shutil
import subprocess
import sys
import time
from collections.abc import Callable
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_hud  # noqa: E402  —— 同目录的被测对象，用它的解析函数与常量，不抄第二份

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = check_hud.ROOT
TEMP = ROOT.parent / "temp" / "selfcheck-hud"

HUD_FILE = ROOT / "src" / "UI" / "LevelHud.cs"
LAYOUT_FILE = ROOT / "rules" / "Ui" / "HudLayout.cs"
FONT_SPEC_FILE = ROOT / "rules" / "Ui" / "PixelFont.cs"
LICENSE_FILE = ROOT / "assets" / "fonts" / "LICENSE-OFL.txt"

# 往界面代码里插一行的锚点。取一句必然存在、且插在它后面语法仍成立的代码。
INSERT_AFTER = "        MouseFilter = MouseFilterEnum.Ignore;"

_LINES: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


# ── 解析型：拿真实日志做变异 ────────────────────────────────────────────
def newest_engine_log() -> Path | None:
    logs = glob.glob(str(check_hud.LOG_DIR / "*" / "engine.log"))
    return Path(max(logs, key=os.path.getmtime)) if logs else None


def analyse_mutated(text: str) -> list[str]:
    """把变异后的日志喂给被测对象，收它报的失败。"""
    check_hud._LINES.clear()
    check_hud._FAILS.clear()
    check_hud._CHECKED = 0
    check_hud.analyse(text)
    return list(check_hud._FAILS)


def mutate_skipped(text: str) -> str:
    return text + "\n[HUD] 量 跳过 ｜ 显示后端 headless 改窗口尺寸不会让拉伸重算\n"


def mutate_drop_layout_verdict(text: str) -> str:
    """删掉排版那一组的一条判据 —— 漏测一条的形状，它不会让别的判据变红。"""
    out = []
    dropped = False
    for line in text.splitlines():
        if not dropped and "｜ 排版 ｜ 四块全部落在整数逻辑像素上" in line:
            dropped = True
            continue
        out.append(line)
    return "\n".join(out)


def mutate_extra_layout_verdict(text: str) -> str:
    """凭空加一条没登记的排版判据 —— 「加了判据却没登记」的形状，它的失败方向没人核过。"""
    return text + "\n[HUD] 判据 PASS ｜ 排版 ｜ 某条没人登记的新判据 ｜ 看起来通过了\n"


def mutate_drop_font_property(text: str) -> str:
    """删掉一条字体属性判据 —— 漏测一项的形状。它不会让别的判据变红。"""
    out = []
    dropped = False
    for line in text.splitlines():
        if not dropped and "｜ 字体 ｜ allow_system_fallback" in line:
            dropped = True
            continue
        out.append(line)
    return "\n".join(out)


def mutate_font_version(text: str) -> str:
    """字体换了个版本 —— 覆盖率与度量都会跟着变，而 diff 里只有一行「二进制文件有差异」。"""
    return text.replace(f"版本 {check_hud.FONT_VERSION}", "版本 2027.01.01")


def mutate_font_fail(text: str) -> str:
    """一项属性与 ADR 不符 —— 就是「改了 .ttf.import 又重新导入」之后的形状。"""
    return text.replace(
        "判据 PASS ｜ 字体 ｜ antialiasing 与 ADR-0008 一致 ｜ 期望 None，引擎报 None",
        "判据 FAIL ｜ 字体 ｜ antialiasing 与 ADR-0008 一致 ｜ 期望 None，引擎报 Gray")


def mutate_coverage(text: str) -> str:
    """占屏涨到一成以上 —— HUD 开始挤中间那块可读区。"""
    return re.sub(r"HUD 占屏 [\d.]+%", "HUD 占屏 13.40%", text)


def mutate_selfcheck_count(text: str) -> str:
    """自报条数比日志里实际的少一条 —— 少算比算错更坏，它把「没核」伪装成「通过」。

    刻意做成**自洽**的（分子分母相等），这样「全部通过」那条判据仍然满足，只有「自报条数与
    日志实际条数一致」那条能拦下来。改成 33/99 的话拦下它的是另一条判据，用例就没撞到目标。
    """
    def shrink(m: re.Match[str]) -> str:
        fewer = int(m.group(2)) - 1
        return f"[HUD] 自检 {fewer}/{fewer} 条通过"

    return re.sub(r"\[HUD\] 自检 (\d+)/(\d+) 条通过", shrink, text)


def mutate_no_verdicts(text: str) -> str:
    """一条判据都没有 —— 探针没跑或输出格式改了。空转必须判失败。"""
    return "\n".join(l for l in text.splitlines() if "[HUD]" not in l)


PARSE_CASES: list[tuple[str, str, Callable[[str], str], str]] = [
    ("撑开那段被跳过时判失败", "check_not_skipped", mutate_skipped, "跳过了撑开逻辑宽度"),
    ("排版判据漏一条时判失败", "check_layout_checks", mutate_drop_layout_verdict, "少了"),
    ("排版判据多一条没登记的时判失败", "check_layout_checks", mutate_extra_layout_verdict,
     "多了"),
    ("字体属性漏测一项时判失败", "check_font_properties", mutate_drop_font_property,
     "条判据，期望"),
    ("字体版本与 ADR 不符时判失败", "check_font_line", mutate_font_version, "ADR-0008 钉的是"),
    ("十项属性有一项对不上时判失败", "check_verdicts", mutate_font_fail, "antialiasing"),
    ("占屏超过上限时判失败", "report_layout", mutate_coverage, "超过上限"),
    ("自报条数与实际不符时判失败", "check_verdicts", mutate_selfcheck_count, "对不上"),
    ("一条判据都没有时判失败", "check_verdicts", mutate_no_verdicts, "一条 [HUD] 判据都没有"),
]


def case_comments_not_counted() -> tuple[bool, str]:
    """**误判方向**：注释与字符串里的数字不算违规。

    这条比看起来要紧：本项目的注释里写满了 640×360、12px、ADR-0008 这类数字。若它们被算进去，
    「界面代码里没有写死的数」会永远 FAIL，然后我们学会忽略它 —— 那比没有守卫更坏
    （同一条理由见 production/像素绘制原则.md §11）。
    """
    sample = '''
// 注释里有 640×360 与 12px
/* 块注释里有 4096 */
var path = "res://assets/ui/skill-slot-3.png";   // 字符串里有 3
var text = $"素材 {done}／{total}";
var keep = 2 + 42;
'''
    code = check_hud.strip_comments_and_strings(sample)
    found = sorted({m.group(0) for m in check_hud.NUMBER_RE.finditer(code)})
    want = ["2", "42"]
    ok = found == want
    return ok, f"挖掉注释与字符串后剩下的数字 {found}，期望 {want}"


def case_static_scope_empty() -> tuple[bool, str]:
    """静态核扫不到文件时必须判失败 —— 空转的检查也会「全绿」。"""
    original = check_hud.HUD_GLOB
    check_hud.HUD_GLOB = "src/UI/NoSuchHudFile*.cs"
    try:
        check_hud._LINES.clear()
        check_hud._FAILS.clear()
        check_hud.check_static()
        fails = list(check_hud._FAILS)
    finally:
        check_hud.HUD_GLOB = original
    hit = any("空转" in f for f in fails)
    return hit, f"报了 {len(fails)} 条失败：{fails[:1]}"


# ── 注入型：真改源码 ────────────────────────────────────────────────────
_backups: dict[Path, str] = {}


def stash(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    _backups[path] = text
    return text


def restore_all() -> None:
    for path, text in list(_backups.items()):
        path.write_text(text, encoding="utf-8", newline="\n")
        del _backups[path]


def inject_into_hud(line: str) -> None:
    text = stash(HUD_FILE)
    if INSERT_AFTER not in text:
        raise RuntimeError(f"{HUD_FILE.name} 里找不到插入锚点「{INSERT_AFTER.strip()}」")
    HUD_FILE.write_text(text.replace(INSERT_AFTER, INSERT_AFTER + "\n" + line, 1),
                        encoding="utf-8", newline="\n")


def inject_absolute_position() -> None:
    inject_into_hud("        Position = new Vector2(120, 300);")


def inject_hardcoded_number() -> None:
    inject_into_hud("        var 上限 = 100;   // 图省事写死的玩法数值")


def inject_texture_filter() -> None:
    inject_into_hud("        TextureFilter = TextureFilterEnum.Linear;")


def inject_license_gone() -> None:
    TEMP.mkdir(parents=True, exist_ok=True)
    shutil.move(str(LICENSE_FILE), str(TEMP / LICENSE_FILE.name))


def restore_license() -> None:
    kept = TEMP / LICENSE_FILE.name
    if kept.is_file():
        shutil.move(str(kept), str(LICENSE_FILE))


def inject_layout_drift() -> None:
    """把**贴下边那一块**的内容高改掉 4px —— 算式与实现漂移，块会整体偏出安全边距。

    这就是 2026-08-31 真撞过的形状：技能块的容器行距写 0 而尺寸算式按一个间距，块低了 4px，
    存图里几乎看不出来，是这条判据抓出来的。

    **必须挑贴下边或贴右边的块。** 第一版挑了队友块（贴右上），结果这条用例通不过 ——
    贴上边的块偏移与尺寸无关（`offset_top` 就是安全边距），块只会往下长，位置一点不变，
    于是预测与实际照样对得上。贴下边的块反过来：`offset_top = −(高 + 边距)`，
    而 `set_offsets_preset` 拿的是**内容最小尺寸**、实际尺寸拿的是「内容最小与
    `CustomMinimumSize` 取大」，两者一分叉位置立刻错开。
    """
    text = stash(LAYOUT_FILE)
    old = "            Height: SkillCellHeight),"
    if old not in text:
        raise RuntimeError("HudLayout.cs 里找不到技能块的高度算式")
    LAYOUT_FILE.write_text(
        text.replace(old, "            Height: SkillCellHeight + UiMetrics.ItemGap),"),
        encoding="utf-8", newline="\n")


def inject_top_anchored_drift() -> None:
    """把**贴上边那一块**的内容高改掉 4px —— 上一条用例的盲区，专门撞病根级判据。

    这条存在的理由是上一条用例暴露出来的：贴上边的块偏移与尺寸无关，块只往下多长几像素、
    位置一点不变，于是「实际矩形与预测逐块一致」照样通过 —— 漂移看不出来。补了一条
    「算式与容器要的最小尺寸逐块相等」之后，任何一块的漂移都逃不掉，与贴哪个角无关。
    """
    text = stash(LAYOUT_FILE)
    old = "            Height: PortraitSize + GaugeBarHeight),"
    if old not in text:
        raise RuntimeError("HudLayout.cs 里找不到队友块的高度算式")
    LAYOUT_FILE.write_text(
        text.replace(old, "            Height: PortraitSize + GaugeBarHeight + UiMetrics.ItemGap),"),
        encoding="utf-8", newline="\n")


def inject_font_expectation_drift() -> None:
    """把一项字体期望值改掉 —— 与「改了 .ttf.import 又重新导入」产生同一个形状。"""
    text = stash(FONT_SPEC_FILE)
    old = "public const bool AllowSystemFallback = false;"
    if old not in text:
        raise RuntimeError("PixelFont.cs 里找不到系统回退那一项")
    FONT_SPEC_FILE.write_text(
        text.replace(old, "public const bool AllowSystemFallback = true;"),
        encoding="utf-8", newline="\n")


STATIC_CASES: list[tuple[str, str, Callable[[], None], Callable[[], None], str]] = [
    ("界面里写绝对坐标时判失败", "check_static", inject_absolute_position, restore_all,
     "位置类 API"),
    ("界面里写死数字时判失败", "check_static", inject_hardcoded_number, restore_all,
     "数字字面量"),
    ("界面覆盖纹理过滤时判失败", "check_static", inject_texture_filter, restore_all,
     "TextureFilter"),
    ("字体旁的许可证不在时判失败", "check_license_present", inject_license_gone,
     restore_license, "OFL 第 2 条"),
]

ENGINE_CASES: list[tuple[str, str, Callable[[], None], str]] = [
    ("贴下边那块的算式漂移时判失败", "check_verdicts", inject_layout_drift, "逐块一致"),
    ("贴上边那块的算式漂移时也判失败", "check_verdicts", inject_top_anchored_drift,
     "容器要的最小尺寸"),
    ("字体期望值与引擎实际不符时判失败", "check_verdicts", inject_font_expectation_drift,
     "allow_system_fallback"),
]

# 覆盖登记：`check_hud.py` 里每个判定函数，各自由哪条用例撞过。
# **口径是按函数**，不是按分支。没有用例的分支逐条写在下面连理由一起。
UNPROVEN_BRANCHES = {
    "run_engine · 引擎超时": "要造一个跑满 240s 不退出的引擎，代价与收益不成比例；"
                             "超时那条路与「没拿到日志」共用同一个失败出口，后者被覆盖了",
    "check_static · 演示数值文件不在": "把 HudDemoModel.cs 挪走会让整个工程编不过，"
                                       "于是撞到的是构建失败而不是本条判据；"
                                       "它的失败信息与「扫不到界面源码」同一形状，后者被覆盖了",
    "main · --exported 分支": "跑产物与跑工程源码走同一套解析，差别只在启动器；"
                              "产物那条由 verify.py 的跑产物那步间接覆盖",
}


def run_check(args: list[str]) -> tuple[int, str]:
    """自己拿 bytes 再 decode，不过 shell 管道（踩坑记录 27）。"""
    done = subprocess.run([sys.executable, str(ROOT / "tools" / "check_hud.py"), *args],
                          cwd=str(ROOT), stdout=subprocess.PIPE,
                          stderr=subprocess.STDOUT, timeout=900)
    raw = done.stdout or b""
    for enc in ("utf-8", "cp936"):
        try:
            return done.returncode, raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return done.returncode, raw.decode("utf-8", errors="replace")


def build() -> tuple[bool, str]:
    env = dict(os.environ)
    env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
    done = subprocess.run(["dotnet", "build", "--nologo", "-v", "q"], cwd=str(ROOT),
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          timeout=900, env=env, check=False)
    text = (done.stdout or b"").decode("utf-8", errors="replace")
    return done.returncode == 0, text


def first_fail(out: str) -> str:
    for line in out.splitlines():
        if line.startswith("[FAIL]"):
            return line.strip()[:200]
    return "（输出里没有 [FAIL] 行）"


def main() -> int:
    ap = argparse.ArgumentParser(description="check_hud.py 的自证（UI-8）")
    ap.add_argument("--fast", action="store_true", help="跳过要构建并起引擎的两条")
    ap.add_argument("--list", action="store_true", help="只列用例与覆盖登记")
    args = ap.parse_args()

    all_cases = ([(n, c) for n, c, *_ in PARSE_CASES]
                 + [("注释与字符串里的数字不算违规（误判方向）", "strip_comments_and_strings"),
                    ("静态核扫不到文件时判失败", "check_static")]
                 + [(n, c) for n, c, *_ in STATIC_CASES]
                 + [(n, c) for n, c, *_ in ENGINE_CASES])

    if args.list:
        for name, covers in all_cases:
            say(f"  {covers:<26} {name}")
        say(f"\n覆盖量（口径：按函数）：{len(all_cases)} 条用例覆盖 "
            f"{len({c for _, c in all_cases})} 项")
        say(f"已知未自证的分支 {len(UNPROVEN_BRANCHES)} 条：")
        for branch, why in UNPROVEN_BRANCHES.items():
            say(f"  - {branch}：{why}")
        return 0

    fails: list[str] = []
    total = 0

    # 先要一份真实引擎日志。没有就跑一次完整检查造一份 —— 变异必须基于真的东西，
    # 手写一份假日志只能证明「我的假日志能骗过解析器」。
    log = newest_engine_log()
    if log is None:
        say("[..]   先跑一次 check_hud.py 造一份真实引擎日志")
        code, _ = run_check([])
        if code != 0:
            say("[FAIL] 基线那一轮检查就没过，先修它再自证")
            say("EXIT=1")
            return 1
        log = newest_engine_log()
    if log is None:
        say("[FAIL] 拿不到引擎日志，解析型用例无从跑")
        say("EXIT=1")
        return 1
    say(f"[..]   变异基于真实日志 {log.relative_to(ROOT).as_posix()}")
    baseline = log.read_text(encoding="utf-8", errors="replace")

    # 先确认基线本身是干净的，否则「变异后报失败」证明不了任何事。
    total += 1
    clean = analyse_mutated(baseline)
    if clean:
        say(f"[FAIL] 基线日志本身就有 {len(clean)} 条失败：{clean[:1]} —— "
            f"变异用例的结论会失去意义")
        fails.append("基线日志不干净")
    else:
        say("[OK]   基线日志喂进去零失败，变异出来的失败才是守卫抓到的")

    for name, covers, mutate, want in PARSE_CASES:
        total += 1
        got = analyse_mutated(mutate(baseline))
        hit = any(want in f for f in got)
        say(f"{'[OK]  ' if hit else '[FAIL]'} {covers} · {name} —— "
            f"报了 {len(got)} 条失败，{'命中' if hit else '未命中'}期望字样 {want!r}"
            + (f"：{got[0][:120]}" if got else ""))
        if not hit:
            fails.append(f"{covers} · {name}")

    for name, covers, fn in (("注释与字符串里的数字不算违规（误判方向）",
                              "strip_comments_and_strings", case_comments_not_counted),
                             ("静态核扫不到文件时判失败", "check_static",
                              case_static_scope_empty)):
        total += 1
        try:
            hit, detail = fn()
        except Exception as exc:                          # noqa: BLE001
            hit, detail = False, f"用例本身抛了：{exc!r}"
        say(f"{'[OK]  ' if hit else '[FAIL]'} {covers} · {name} —— {detail}")
        if not hit:
            fails.append(f"{covers} · {name}")

    for name, covers, inject, restore, want in STATIC_CASES:
        total += 1
        try:
            inject()
            code, out = run_check(["--static"])
        finally:
            restore()
        hit = code != 0 and want in out
        say(f"{'[OK]  ' if hit else '[FAIL]'} {covers} · {name} —— 退出码 {code}，"
            f"{'命中' if want in out else '未命中'}期望字样 {want!r}；{first_fail(out)}")
        if not hit:
            fails.append(f"{covers} · {name}")

    if args.fast:
        say(f"[SKIP] 跳过 {len(ENGINE_CASES)} 条要构建并起引擎的用例（--fast）—— "
            f"**这不能当一次完整自证**")
    else:
        for name, covers, inject, want in ENGINE_CASES:
            total += 1
            try:
                inject()
                built, log_text = build()
                if not built:
                    hit, detail = False, f"注入后构建失败，用例无从判：{log_text[-200:]}"
                else:
                    code, out = run_check([])
                    hit = code != 0 and want in out
                    detail = (f"退出码 {code}，"
                              f"{'命中' if want in out else '未命中'}期望字样 {want!r}；"
                              f"{first_fail(out)}")
            finally:
                restore_all()
                build()
            say(f"{'[OK]  ' if hit else '[FAIL]'} {covers} · {name} —— {detail}")
            if not hit:
                fails.append(f"{covers} · {name}")

    # 还原后复验：注入全撤了，完整检查必须重新变绿。少了这一步就无法区分
    # 「守卫拦得住」和「我把仓库改坏了所以什么都过不了」。
    say("[..]   还原后复验：重跑一次完整 check_hud.py")
    code, out = run_check([])
    if code == 0 and "EXIT=0" in out:
        say("[OK]   还原后复验通过，仓库回到干净状态")
    else:
        say(f"[FAIL] 还原后复验没过（退出码 {code}）：{first_fail(out)}")
        fails.append("还原后复验")

    if TEMP.exists():
        shutil.rmtree(TEMP, ignore_errors=True)
    if TEMP.exists():
        say(f"[FAIL] 临时目录没删掉：{TEMP}")
        fails.append("临时目录清理")
    else:
        say(f"[OK]   已清理临时目录 {TEMP}")

    covered = sorted({c for _, c in all_cases})
    say()
    say(f"覆盖量（口径：按函数，每项至少有一条用例让它真的判失败过）："
        f"{total} 条用例覆盖 {len(covered)} 项 —— {'、'.join(covered)}")
    say(f"已知未自证的分支 {len(UNPROVEN_BRANCHES)} 条（各有理由，不是遗漏）：")
    for branch, why in UNPROVEN_BRANCHES.items():
        say(f"  - {branch}：{why}")

    say(f"\n结果：{total - len(fails)}/{total} 条按预期拦下／{len(fails)} 项必须修复")
    if fails:
        say("[FAIL] " + "；".join(fails))
    else:
        say("[OK] 登记的每项判定都用真实缺陷形状撞过，且还原后复验通过")

    out_log = check_hud.LOG_DIR / f"selfcheck-{time.strftime('%Y%m%d-%H%M%S')}.log"
    out_log.parent.mkdir(parents=True, exist_ok=True)
    out_log.write_text("\n".join(_LINES) + "\n", encoding="utf-8", newline="\n")
    say(f"日志 {out_log.relative_to(ROOT).as_posix()}")
    say(f"EXIT={1 if fails else 0}")
    return 1 if fails else 0


if __name__ == "__main__":
    raise SystemExit(main())
