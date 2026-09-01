#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""`tools/check_worldui.py` 的自证：造真实缺陷形状 → 确认拦得住 → 还原 → 复验。

为什么存在（WORKFLOW §6）：`check_worldui.py` 跑过一次全绿**不能**证明它拦得住东西 ——
一个什么都不检的脚本也会全绿。所以每条判定都得用一个真实缺陷形状撞一次。

缺陷形状不是编的，取自本条真会踩的：

    挂载层改成 Hud        读条圆环从「画在执行者身上」变成「画在界面角落」，不跟角色走、
                          不随 2 倍缩放 —— 正典禁止，且一行报错都没有
    代码碰了别的层         世界空间 UI 摸了 Hud 等屏幕空间层，说明有元素画错了地方
    注释里提到 Hud（误判）  `WorldSpaceUi.cs` 的文档注释里就写着 `UiLayer.Hud`（说明「不是 Hud」）
                          —— 若被算进去，静态核会永远 FAIL，然后我们学会忽略它，比没有守卫更坏
    探针漏一条／多一条判据  漏一条不报错、多一条没人核过失败方向
    自报条数与实际不符      少算比算错更坏，它把「没核」伪装成「通过」
    一条判据都没有          探针没跑或输出格式改了，空转必须判失败

分两类跑，因为代价差一个数量级：

- **解析型**：拿一份真实引擎日志做变异，直接调 `analyse()`。秒级，不起引擎。
- **静态注入型**：真改 `WorldSpaceUi.cs` 的源码，跑 `check_worldui.py --static`。不起引擎。

用法（从代码仓根目录运行）：

    python tools/selfcheck_worldui.py           # 全部用例
    python tools/selfcheck_worldui.py --fast    # 只跑静态核用例，跳过要起引擎造基线的解析型
    python tools/selfcheck_worldui.py --list    # 只列用例与覆盖登记

所有注入都在 finally 里还原。
"""

from __future__ import annotations

import argparse
import glob
import os
import re
import subprocess
import sys
import time
from collections.abc import Callable
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_worldui  # noqa: E402  —— 同目录的被测对象，用它的解析函数与常量，不抄第二份

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = check_worldui.ROOT
WORLDUI_SRC = ROOT / "src" / "UI" / "WorldSpaceUi.cs"

# 注入锚点。取必然存在、且插在它后面文本仍成立的句子（--static 不构建，无需语法正确）。
MOUNT_CALL = "ui.LayerOf(UiLayer.WorldSpace)"
INSERT_AFTER = "        _ring = new CastRing();"

_LINES: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


# ── 解析型：拿真实日志做变异 ────────────────────────────────────────────
def newest_engine_log() -> Path | None:
    logs = glob.glob(str(check_worldui.LOG_DIR / "*" / "engine.log"))
    return Path(max(logs, key=os.path.getmtime)) if logs else None


def analyse_mutated(text: str) -> list[str]:
    """把变异后的日志喂给被测对象，收它报的失败。"""
    check_worldui._LINES.clear()
    check_worldui._FAILS.clear()
    check_worldui._CHECKED = 0
    check_worldui.analyse(text)
    return list(check_worldui._FAILS)


def mutate_no_verdicts(text: str) -> str:
    """一条判据都没有 —— 探针没跑或输出格式改了。空转必须判失败。"""
    return "\n".join(l for l in text.splitlines() if "[世界UI]" not in l)


def mutate_drop_tag(text: str) -> str:
    """删掉「中断」那一条判据 —— 漏测一条的形状，它不会让别的判据变红。"""
    out = []
    dropped = False
    for line in text.splitlines():
        if not dropped and "｜ 中断 ｜" in line and "判据" in line:
            dropped = True
            continue
        out.append(line)
    return "\n".join(out)


def mutate_extra_tag(text: str) -> str:
    """凭空加一条没登记的判据 —— 「加了判据却没登记」的形状，它的失败方向没人核过。"""
    return text + "\n[世界UI] 判据 PASS ｜ 幽灵标签 ｜ 没人登记的新判据 ｜ 看着像过了\n"


def mutate_verdict_fail(text: str) -> str:
    """把「跟随」那一条从 PASS 翻成 FAIL —— 探针实机判据不成立的形状。"""
    return text.replace("判据 PASS ｜ 跟随", "判据 FAIL ｜ 跟随", 1)


def mutate_selfcheck_count(text: str) -> str:
    """自报条数比日志里实际的少一条 —— 少算比算错更坏，它把「没核」伪装成「通过」。

    刻意做成**自洽**的（分子分母相等），这样「全部通过」那条判据仍满足，只有「自报条数与
    日志实际条数一致」那条能拦下来（同一手法见 selfcheck_hud.py）。
    """
    def shrink(m: re.Match[str]) -> str:
        fewer = int(m.group(2)) - 1
        return f"[世界UI] 自检 {fewer}/{fewer} 条通过"

    return re.sub(r"\[世界UI\] 自检 (\d+)/(\d+) 条通过", shrink, text)


PARSE_CASES: list[tuple[str, str, Callable[[str], str], str]] = [
    ("一条判据都没有时判失败", "check_verdicts", mutate_no_verdicts, "一条 [世界UI] 判据都没有"),
    ("判据漏一条时判失败", "check_tags", mutate_drop_tag, "少了"),
    ("判据多一条没登记的时判失败", "check_tags", mutate_extra_tag, "多了"),
    ("探针某条判据 FAIL 时判失败", "check_verdicts", mutate_verdict_fail, "实机判据不成立"),
    ("自报条数与实际不符时判失败", "check_verdicts", mutate_selfcheck_count, "对不上"),
]


# ── 直接型：不起引擎、不改源码，直接调函数 ──────────────────────────────
def case_comments_not_counted() -> tuple[bool, str]:
    """**误判方向**：注释与字符串里的 `UiLayer.X` 不算引用。

    这条比看起来要紧：`WorldSpaceUi.cs` 的文档注释里就写着「挂在 WorldSpace 那一层，不是
    `UiLayer.Hud`」，字符串里有 `res://…` 路径。若它们被算进去，「代码里只碰 WorldSpace」会永远
    FAIL，然后我们学会忽略它 —— 那比没有守卫更坏。
    """
    sample = '''
/// 挂在 <see cref="UiLayer.WorldSpace"/> 那一层，不是 <see cref="UiLayer.Hud"/>。
// 注释里提到 UiLayer.Panel 与 UiLayer.Manage 都不该算
var tex = "res://assets/placeholder/ui/cast-ring.png";   // 路径里有 //
_layer = ui.LayerOf(UiLayer.WorldSpace);
'''
    code = check_worldui.strip_comments_and_strings(sample)
    layers = sorted(set(check_worldui.LAYER_RE.findall(code)))
    mounts = sorted(set(check_worldui.MOUNT_RE.findall(code)))
    hit = layers == ["WorldSpace"] and mounts == ["WorldSpace"]
    return hit, (f"挖掉注释与字符串后剩下的层引用 {layers}、挂载点 {mounts}，"
                 f"期望都只有 ['WorldSpace']")


def case_static_scope_empty() -> tuple[bool, str]:
    """静态核扫不到文件时必须判失败 —— 空转的检查也会「全绿」。"""
    original = check_worldui.WORLDUI_FILE
    check_worldui.WORLDUI_FILE = "src/UI/NoSuchWorldUiFile.cs"
    try:
        check_worldui._LINES.clear()
        check_worldui._FAILS.clear()
        check_worldui.check_static()
        fails = list(check_worldui._FAILS)
    finally:
        check_worldui.WORLDUI_FILE = original
    hit = any("空转" in f for f in fails)
    return hit, f"报了 {len(fails)} 条失败：{fails[:1]}"


DIRECT_CASES: list[tuple[str, str, Callable[[], tuple[bool, str]]]] = [
    ("注释与字符串里的层引用不算（误判方向）", "strip_comments_and_strings",
     case_comments_not_counted),
    ("静态核扫不到文件时判失败", "check_static", case_static_scope_empty),
]


# ── 静态注入型：真改源码 ────────────────────────────────────────────────
_backups: dict[Path, str] = {}


def stash(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    _backups[path] = text
    return text


def restore_all() -> None:
    for path, text in list(_backups.items()):
        path.write_text(text, encoding="utf-8", newline="\n")
        del _backups[path]


def inject_mount_hud() -> None:
    """把挂载点从 WorldSpace 改成 Hud —— 读条会画到界面角落而不是执行者身上（正典禁止）。"""
    text = stash(WORLDUI_SRC)
    if MOUNT_CALL not in text:
        raise RuntimeError(f"{WORLDUI_SRC.name} 里找不到挂载点「{MOUNT_CALL}」")
    WORLDUI_SRC.write_text(text.replace(MOUNT_CALL, "ui.LayerOf(UiLayer.Hud)", 1),
                           encoding="utf-8", newline="\n")


def inject_stray_layer() -> None:
    """在代码里插一句碰别的层 —— 挂载点没变，专门撞「只该碰 WorldSpace」那条。"""
    text = stash(WORLDUI_SRC)
    if INSERT_AFTER not in text:
        raise RuntimeError(f"{WORLDUI_SRC.name} 里找不到插入锚点「{INSERT_AFTER.strip()}」")
    WORLDUI_SRC.write_text(
        text.replace(INSERT_AFTER, INSERT_AFTER + "\n        UiLayer stray = UiLayer.Panel;", 1),
        encoding="utf-8", newline="\n")


STATIC_CASES: list[tuple[str, str, Callable[[], None], str]] = [
    ("挂载层被改成 Hud 时判失败", "check_static", inject_mount_hud, "必须是"),
    ("代码引用别的 UiLayer 时判失败", "check_static", inject_stray_layer, "别的 UiLayer"),
]

# 覆盖登记：`check_worldui.py` 里每个判定函数，各自由哪条用例撞过。**口径是按函数**。
UNPROVEN_BRANCHES = {
    "run_engine · 引擎超时": "要造一个跑满 180s 不退出的引擎，代价与收益不成比例；"
                             "超时那条路与「没拿到日志」共用同一个失败出口，后者被覆盖了",
    "main · --exported 分支": "跑产物与跑工程源码走同一套解析，差别只在启动器；"
                              "check_worldui 不进 verify.py 默认门禁，产物那条留给发布前手动跑",
    "行为核 · 探针某条判据真的 FAIL": "让探针真报 FAIL 要把引擎侧改坏（如把血条挂到 Hud），"
                                       "那是在测探针而非测守卫；其日志形状由 verdict_fail 覆盖",
}


def run_check(args: list[str]) -> tuple[int, str]:
    """自己拿 bytes 再 decode，不过 shell 管道（踩坑记录 27）。"""
    done = subprocess.run([sys.executable, str(ROOT / "tools" / "check_worldui.py"), *args],
                          cwd=str(ROOT), stdout=subprocess.PIPE,
                          stderr=subprocess.STDOUT, timeout=300)
    raw = done.stdout or b""
    for enc in ("utf-8", "cp936"):
        try:
            return done.returncode, raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return done.returncode, raw.decode("utf-8", errors="replace")


def first_fail(out: str) -> str:
    for line in out.splitlines():
        if line.startswith("[FAIL]"):
            return line.strip()[:200]
    return "（输出里没有 [FAIL] 行）"


def main() -> int:
    ap = argparse.ArgumentParser(description="check_worldui.py 的自证（UI-9）")
    ap.add_argument("--fast", action="store_true",
                    help="跳过要起引擎造基线的解析型用例，只跑静态核用例")
    ap.add_argument("--list", action="store_true", help="只列用例与覆盖登记")
    args = ap.parse_args()

    all_cases = ([(n, c) for n, c, *_ in PARSE_CASES]
                 + [(n, c) for n, c, *_ in DIRECT_CASES]
                 + [(n, c) for n, c, *_ in STATIC_CASES])

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

    # ── 解析型：要一份真实引擎日志 ──────────────────────────────────────
    if args.fast:
        say("[SKIP] 跳过解析型用例（--fast）—— **这不能当一次完整自证**，行为核那几条没撞过")
    else:
        log = newest_engine_log()
        if log is None:
            say("[..]   先跑一次 check_worldui.py 造一份真实引擎日志")
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

        # 先确认基线本身干净，否则「变异后报失败」证明不了任何事。
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

    # ── 直接型：不起引擎、不改源码 ──────────────────────────────────────
    for name, covers, fn in DIRECT_CASES:
        total += 1
        try:
            hit, detail = fn()
        except Exception as exc:                          # noqa: BLE001
            hit, detail = False, f"用例本身抛了：{exc!r}"
        say(f"{'[OK]  ' if hit else '[FAIL]'} {covers} · {name} —— {detail}")
        if not hit:
            fails.append(f"{covers} · {name}")

    # ── 静态注入型：真改源码，跑 --static ───────────────────────────────
    for name, covers, inject, want in STATIC_CASES:
        total += 1
        try:
            inject()
            code, out = run_check(["--static"])
        finally:
            restore_all()
        hit = code != 0 and want in out
        say(f"{'[OK]  ' if hit else '[FAIL]'} {covers} · {name} —— 退出码 {code}，"
            f"{'命中' if want in out else '未命中'}期望字样 {want!r}；{first_fail(out)}")
        if not hit:
            fails.append(f"{covers} · {name}")

    # 还原后复验：注入全撤了，静态核必须重新变绿。少了这一步就无法区分「守卫拦得住」
    # 和「我把源码改坏了所以什么都过不了」。复验只跑静态核 —— 本自证只动过源码。
    say("[..]   还原后复验：重跑一次 check_worldui.py --static")
    code, out = run_check(["--static"])
    if code == 0:
        say("[OK]   还原后复验通过，源码回到干净状态")
    else:
        say(f"[FAIL] 还原后复验没过（退出码 {code}）：{first_fail(out)}")
        fails.append("还原后复验")

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

    out_log = check_worldui.LOG_DIR / f"selfcheck-{time.strftime('%Y%m%d-%H%M%S')}.log"
    out_log.parent.mkdir(parents=True, exist_ok=True)
    out_log.write_text("\n".join(_LINES) + "\n", encoding="utf-8", newline="\n")
    say(f"日志 {out_log.relative_to(ROOT).as_posix()}")
    say(f"EXIT={1 if fails else 0}")
    return 1 if fails else 0


if __name__ == "__main__":
    raise SystemExit(main())
