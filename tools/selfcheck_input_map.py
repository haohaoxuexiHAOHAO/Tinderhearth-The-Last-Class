#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""`check_input_map.py` 的自证：注入真实缺陷形状 → 确认拦得住 → 还原 → 复验。

**它跑过一次全绿不能证明它拦得住东西** —— 一个什么都不检的脚本也会全绿。所以每条检查都要有一个
用真实缺陷形状造出来的用例，并且自报覆盖量：登记了检查目标却没实际执行，就算失败
（[WORKFLOW §6]）。

缺陷形状不是编的，都对着 `UI-7` 实现过程中真出现过或差一步就会出现的错：

- 编辑器里手加动作造出第二份绑定来源（这条正是把默认值放代码里的代价，所以必须有守卫）。
- 玩法代码直接轮询引擎动作状态（实测 `SetInputAsHandled` 不清轮询状态，所以这会打出没打算打的攻击）。
- 符号翻译成错的引擎枚举（右扳机接到轴 4）—— 守卫存在的主要理由，光看代码看不出来。
- 绑定表登记的引擎自报名与实际不符。
- 补给内置动作的手柄绑定被摘掉（摘掉手柄就退不出面板）。
- **比对器自己漏了几条**（本轮真发生过：改成跨行拼接后，用正则取期望值的老做法静默少解 12 条，
  守卫照样全绿。少算比算错更坏）。
- 组合解算的取舍被反过来（改成后按的赢）。
- **脚手架调试键押在编辑器的停止键 `F8` 上**（本轮真撞过：作者一按，进程直接关掉，
  而失败方式看起来像程序自己崩了），以及加了新调试键却不登记。

用法（从代码仓根目录运行，约三分钟）：
    python tools/selfcheck_input_map.py
    python tools/selfcheck_input_map.py --list    # 只列用例与覆盖登记，不跑

输出固定 UTF-8，逐条 [OK]／[FAIL]，末尾打覆盖量与一行 EXIT=；同时写
logs/input/selfcheck-<时间戳>.log。临时注入的文件在 finally 里还原，收尾会复验一次全绿。
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parent.parent
GUARD = ROOT / "tools" / "check_input_map.py"
LOG_DIR = ROOT / "logs" / "input"

BINDINGS_CS = ROOT / "rules" / "Ui" / "InputBindings.cs"
MODIFIERS_CS = ROOT / "rules" / "Ui" / "SkillModifiers.cs"
INSTALLER_CS = ROOT / "src" / "UI" / "InputMapInstaller.cs"
PROBE_CS = ROOT / "src" / "UI" / "InputProbe.cs"
PROJECT_GODOT = ROOT / "project.godot"
POLLING_CS = ROOT / "src" / "UI" / "ZzSelfcheckPolling.cs"
HARNESS_CS = ROOT / "src" / "World" / "CameraHarness.cs"

# 守卫里登记在册的检查函数。**从源码枚举**，所以新增一个 check_ 却不自证会被当场拦住，
# 而不是等到某天发现守卫一直在空转。
CHECK_FN_RE = re.compile(r"^def (check_\w+)\(", re.MULTILINE)

# 没有用例的分支要写明理由，否则覆盖量自报会判失败。
UNPROVEN: dict[str, str] = {
    "绑定表里删掉一条绑定": "删掉后声明数与比对数同时减一，条数判据不动。这条形状由规则层测试"
                            "「每个动作在两个设备族上都有绑定或有写明理由的豁免」盯着，"
                            "不该在守卫这边重复一遍",
    "引擎起不来": "要伪造引擎缺失或崩溃才能触发。真发生时 verify.py 的构建与跑产物两步先炸，"
                  "不会走到本守卫",
}


@dataclass
class Edit:
    """一次文本替换或建文件。path 为 None 表示建新文件。"""

    path: Path
    old: str | None
    new: str


@dataclass
class Case:
    name: str
    covers: list[str]
    shape: str
    expect: str
    edits: list[Edit]
    needs_build: bool = True
    _snapshots: dict[Path, bytes | None] = field(default_factory=dict)


CASES: list[Case] = [
    Case(
        name="project.godot 里冒出 [input] 段",
        covers=["check_no_input_section"],
        shape="有人在编辑器的 Input Map 面板里手加一个动作，于是绑定有了第二份来源",
        expect="出现了 [input] 段",
        needs_build=False,
        edits=[Edit(PROJECT_GODOT, None, '\n[input]\n\nzz_selfcheck={\n'
                                         '"deadzone": 0.2,\n"events": []\n}\n')],
    ),
    Case(
        name="门面之外直接轮询引擎动作状态",
        covers=["check_no_direct_polling"],
        shape="玩法代码图省事写 Input.IsActionPressed，于是修饰键按住时照旧看到轻攻击被按下",
        expect="直接轮询了引擎的动作状态",
        needs_build=False,
        edits=[Edit(POLLING_CS, None,
                    "using Godot;\n\nnamespace Tinderhearth.UI;\n\n"
                    "// 自证用的临时文件，selfcheck_input_map.py 跑完会删。\n"
                    "public static class ZzSelfcheckPolling\n{\n"
                    "    public static bool Attacking() => Input.IsActionPressed(\"attack_light\");\n"
                    "}\n")],
    ),
    Case(
        name="符号被翻译成错的引擎枚举",
        covers=["check_actions_and_bindings"],
        shape="右扳机接到轴 4。这是守卫存在的主要理由 —— 光读代码看不出 4 与 5 反了",
        expect="绑定核对不通过",
        edits=[Edit(INSTALLER_CS,
                    "InputSymbol.PadTriggerRight => Axis(JoyAxis.TriggerRight, 1f),",
                    "InputSymbol.PadTriggerRight => Axis(JoyAxis.TriggerLeft, 1f),")],
    ),
    Case(
        name="绑定表登记的引擎自报名与实际不符",
        covers=["check_actions_and_bindings"],
        shape="改键位时只改了绑定，忘了改跟着它的自报名",
        expect="绑定核对不通过",
        edits=[Edit(BINDINGS_CS, 'new(InputSymbol.KeyQ, "Q - Physical")',
                    'new(InputSymbol.KeyQ, "Z - Physical")')],
    ),
    Case(
        name="补给内置动作的手柄绑定被摘掉",
        covers=["check_builtin_ui_actions"],
        shape="有人觉得那句补丁多余就删了，于是手柄能挪焦点却退不出面板",
        expect="手柄上一条绑定都没有",
        edits=[Edit(INSTALLER_CS, "        PatchBuiltinUiActions();",
                    "        // PatchBuiltinUiActions();")],
    ),
    Case(
        name="比对器自己漏核了几条绑定",
        covers=["check_actions_and_bindings"],
        shape="本轮真发生过：用正则解 C# 取期望值时静默少解 12 条，守卫照样全绿。"
              "少算把「没核」伪装成「通过」，比算错更坏",
        expect="有绑定没被核",
        edits=[Edit(PROBE_CS,
                    "        var actual = events.Select(e => e.AsText()).OrderBy(t => t).ToList();",
                    "        if (action.StartsWith(\"skill_\")) { return; }\n"
                    "        var actual = events.Select(e => e.AsText()).OrderBy(t => t).ToList();")],
    ),
    Case(
        name="脚手架调试键押回编辑器的停止键 F8",
        covers=["check_harness_debug_keys"],
        shape="2026-08-31 真撞过：目标态押在 F8，作者从编辑器起工程一按就停进程，"
              "看起来像程序自己崩了 —— 而代码侧一句报错都没有",
        expect="押在编辑器要用的键上",
        needs_build=False,
        edits=[Edit(HARNESS_CS, "            case Key.O:", "            case Key.F8:")],
    ),
    Case(
        name="新加一个没登记的脚手架调试键",
        covers=["check_harness_debug_keys"],
        shape="加键时只改脚手架不改登记，于是没人核对过这个键会不会被编辑器吃掉 ——"
              "光有黑名单挡不住下一个人挑另一个坏键",
        expect="与登记对不上",
        needs_build=False,
        edits=[Edit(HARNESS_CS, "            case Key.F9:",
                    "            case Key.F12:\n            case Key.F9:")],
    ),
    Case(
        name="修饰键的取舍被反过来（改成后按的赢）",
        covers=["check_verdicts"],
        shape="后按的赢会让玩家瞄着技能 1 却因误碰 RT 打出技能 4，白扣一次 MP 与冷却",
        expect="实机判据不成立",
        edits=[Edit(MODIFIERS_CS,
                    "public SkillGroup Active => _held.Count > 0 ? _held[0] : SkillGroup.None;",
                    "public SkillGroup Active => _held.Count > 0 ? _held[^1] : SkillGroup.None;")],
    ),
]

_LINES: list[str] = []
_FAILS: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


def flush_log() -> Path:
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    path = LOG_DIR / f"selfcheck-{time.strftime('%Y%m%d-%H%M%S')}.log"
    path.write_text("\n".join(_LINES) + "\n", encoding="utf-8")
    return path


def run(cmd: list[str], timeout: int) -> tuple[int, str]:
    """拿 bytes 自己 decode，不走 shell 管道（踩坑记录 27）。"""
    p = subprocess.run(cmd, cwd=ROOT, capture_output=True, timeout=timeout, check=False)
    raw = p.stdout + p.stderr
    try:
        return p.returncode, raw.decode("utf-8")
    except UnicodeDecodeError:
        return p.returncode, raw.decode("mbcs", errors="replace")


def build() -> tuple[bool, str]:
    code, out = run(["dotnet", "build", "--nologo", "-v", "q"], timeout=600)
    return code == 0, out


def run_guard() -> tuple[int, str]:
    return run([sys.executable, str(GUARD)], timeout=600)


def apply(case: Case) -> str | None:
    """打补丁，先把原样存下来。返回失败原因。"""
    for edit in case.edits:
        case._snapshots[edit.path] = (
            edit.path.read_bytes() if edit.path.is_file() else None)
        if edit.old is None:
            if case._snapshots[edit.path] is None:
                edit.path.write_text(edit.new, encoding="utf-8")
            else:
                # 追加到已有文件（project.godot 那条）
                with edit.path.open("a", encoding="utf-8") as f:
                    f.write(edit.new)
            continue

        text = edit.path.read_text(encoding="utf-8")
        if text.count(edit.old) != 1:
            return (f"要替换的那段在 {edit.path.name} 里出现 {text.count(edit.old)} 次，"
                    f"应为 1 次 —— 源码改过了，本自证要跟着改")
        edit.path.write_text(text.replace(edit.old, edit.new), encoding="utf-8")
    return None


def restore(case: Case) -> None:
    for path, blob in case._snapshots.items():
        if blob is None:
            path.unlink(missing_ok=True)
        else:
            path.write_bytes(blob)
    case._snapshots.clear()


def registered_checks() -> list[str]:
    return CHECK_FN_RE.findall(GUARD.read_text(encoding="utf-8"))


def coverage(ran: list[str]) -> None:
    say()
    checks = registered_checks()
    covered = {name for case in CASES for name in case.covers}
    say(f"覆盖量：守卫里登记 {len(checks)} 个检查函数，用例覆盖 {len(covered)} 个"
        f"（跑过 {len(ran)}/{len(CASES)} 条用例）")

    missing = [c for c in checks if c not in covered]
    if missing:
        _FAILS.append(f"这些检查没有自证用例：{missing}")
        say(f"[FAIL] 这些检查没有自证用例也没写豁免：{missing}")
    else:
        say("[OK] 每个登记的检查函数都有至少一条用例")

    say(f"没有用例的形状 {len(UNPROVEN)} 条，各有理由：")
    for what, why in UNPROVEN.items():
        say(f"       · {what} —— {why}")


def main() -> int:
    ap = argparse.ArgumentParser(description="check_input_map.py 的自证")
    ap.add_argument("--list", action="store_true", help="只列用例与覆盖登记，不跑")
    args = ap.parse_args()

    if args.list:
        for i, case in enumerate(CASES, 1):
            say(f"{i}. {case.name}")
            say(f"   覆盖 {case.covers}｜期望命中「{case.expect}」"
                f"｜{'要重建' if case.needs_build else '不必重建'}")
            say(f"   形状 {case.shape}")
        coverage([])
        print("EXIT=0")
        return 0

    say(f"自证对象 {GUARD.relative_to(ROOT).as_posix()}")
    say(f"登记的检查函数 {registered_checks()}")
    say()

    say("先确认起点是干净的（不然后面每条失败都可能是本来就坏的）")
    code, _ = run_guard()
    if code != 0:
        say("[FAIL] 起点就不是全绿，先修好再自证 —— 跑 python tools/check_input_map.py 看原因")
        say(f"日志 {flush_log().relative_to(ROOT).as_posix()}")
        print("EXIT=1")
        return 1
    say("[OK] 起点全绿")
    say()

    ran: list[str] = []
    for i, case in enumerate(CASES, 1):
        say(f"用例 {i}／{len(CASES)}　{case.name}")
        say(f"       形状 {case.shape}")
        try:
            why = apply(case)
            if why:
                say(f"[FAIL] 注入失败：{why}")
                _FAILS.append(f"{case.name}：{why}")
                continue

            if case.needs_build:
                built, out = build()
                if not built:
                    say("[FAIL] 注入后编不过，这条用例没能真正跑起来")
                    say("       " + out.strip().splitlines()[-1] if out.strip() else "")
                    _FAILS.append(f"{case.name}：注入后编译失败")
                    continue

            code, out = run_guard()
            ran.append(case.name)
            if code == 0:
                say(f"[FAIL] 守卫没拦住 —— 退出码 0，期望非零")
                _FAILS.append(f"{case.name}：守卫没拦住")
            elif case.expect not in out:
                say(f"[FAIL] 拦住了但报错没指向正确位置，日志里找不到「{case.expect}」")
                _FAILS.append(f"{case.name}：报错指向不对")
            else:
                hit = next(ln for ln in out.splitlines() if case.expect in ln)
                say(f"[OK] 拦住了，报错指向正确：{hit.strip()[:110]}")
        finally:
            restore(case)
        say()

    say("还原后复验")
    if case.needs_build or any(c.needs_build for c in CASES):
        built, out = build()
        if not built:
            say("[FAIL] 还原后编不过 —— 源码可能没还原干净")
            _FAILS.append("还原后编译失败")
    code, _ = run_guard()
    if code == 0:
        say("[OK] 还原后守卫恢复全绿")
    else:
        say("[FAIL] 还原后守卫仍不通过 —— 源码没还原干净，去看 git status")
        _FAILS.append("还原后守卫仍不通过")

    if POLLING_CS.exists():
        POLLING_CS.unlink()
        say("[..] 清掉了残留的临时注入文件 " + POLLING_CS.name)

    coverage(ran)
    say()
    say(f"结果：{len(ran)}／{len(CASES)} 条用例执行，{len(_FAILS)} 项失败")
    say(f"日志 {flush_log().relative_to(ROOT).as_posix()}")
    print("EXIT=" + ("1" if _FAILS else "0"))
    return 1 if _FAILS else 0


if __name__ == "__main__":
    raise SystemExit(main())
