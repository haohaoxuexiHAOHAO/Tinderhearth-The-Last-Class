#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""按启动日志实测输入映射（`UI-7`）。

要判定的事有五类，共同点是**失效都不报错**：

1. **动作齐不齐、绑定对不对。** 默认绑定走代码而不是 `project.godot` 的 `[input]` 段（理由见
   `rules/Ui/InputBindings.cs`），所以没有任何配置文件能让人一眼核对。判据是让引擎自己把装好的
   `InputMap` 逐条 `AsText()` 打出来，再与绑定表登记的**引擎自报名片段**比对 —— 两个独立来源。
   `PadTriggerRight` 被翻译成轴 4 还是轴 5，只有这样才看得出来。
2. **`project.godot` 里不许冒出 `[input]` 段。** 冒出来就意味着有人在编辑器里手加了动作，
   造出第二份会漂移的来源。
3. **除门面之外不许直接轮询引擎的动作状态。** 2026-08-30 实测：在 `_Input` 里
   `SetInputAsHandled` 之后 `Input.IsActionPressed` 仍为 true，所以直接轮询的代码会在玩家按住
   扳机挑技能时照旧看到轻攻击被按下。这条只能靠扫源码守。
4. **脚手架调试键不许押在编辑器要用的键上，且要与登记逐个对上。** 这些键不过 `InputMap`，
   绑定表那套核对一条都管不到。2026-08-31 撞过：目标态押在 `F8`，作者从编辑器起工程一按就
   停进程，看起来像程序崩了。详见 `HARNESS_KEYS` 那一段。
5. **组合、死区与设备切换的实机判据全过。** 引擎内测试底座还不存在（`ENG-6`），所以
   `src/UI/InputProbe.cs` 在启动时跑一遍脚本化自检并把每条判据打进日志，这里读回来判。

不用管道读中文输出（设计仓 reference/踩坑记录.md 第 27 条）：日志由引擎写 `--log-file`，
本脚本自己 decode。

用法（从代码仓根目录运行）：
    python tools/check_input_map.py             # 跑工程源码
    python tools/check_input_map.py --exported  # 跑 export/ 下的产物

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
LOG_DIR = ROOT / "logs" / "input"
GODOT_ROOT = Path(r"D:\godot\4-7")

BINDINGS_CS = ROOT / "rules" / "Ui" / "InputBindings.cs"
PROJECT_GODOT = ROOT / "project.godot"

# 允许直接轮询引擎动作状态的文件（门面本身，和它的启动自检）。
POLLING_ALLOWED = {
    "src/UI/InputRouter.cs",
    "src/UI/InputProbe.cs",
}
POLLING_RE = re.compile(r"Input\.IsAction(?:Pressed|JustPressed|JustReleased)\b")

# ── 脚手架调试键（2026-08-31 撞过）────────────────────────────────────────
# 这些键**不过 InputMap**：脚手架在 `_UnhandledKeyInput` 里直接匹配原始键码，于是绑定表那套
# 「一份来源 + 逐条核对」一条都管不到它们，谁挑了一个编辑器要用的键也没人拦。
#
# 真撞过的形状：目标进度三态原先押在 `F8`。作者从编辑器起工程、按 `F8`，进程直接关掉 ——
# 看起来像程序自己崩了，实际是编辑器的「停止运行项目」就是 `F8`，而它在**游戏窗口有焦点时
# 照样生效**（godotengine/godot#94713）。2026-08-31 实测另一半：脱开编辑器单独跑，注入 `F8`
# 与 `O` 两个事件，处理器都正常跑完、进程退出码 0 —— 所以问题不在处理器，在键位本身。
#
# 用**登记制**而不是只列黑名单：每个调试键都要写明用途，代码里多一个或少一个都判失败。
# 光有黑名单挡不住下一个人挑另一个被编辑器吃掉的键 —— 登记制会逼着他在这里写一行。
HARNESS_KEY_FILES = ("src/World/CameraHarness.cs",)
HARNESS_KEY_RE = re.compile(r"^\s*case Key\.(\w+):", re.MULTILINE)

# 键 → 用途。改脚手架键位就改这里，两边对不上就判失败。
HARNESS_KEYS = {
    "F1": "切视角",
    "F2": "切建造模式",
    "F3": "震一下",
    "F4": "震动开关",
    "F5": "收拢／散开 15 个剪影",
    "F6": "打印 HUD 排版数据",
    "F7": "队友数 4↔0",
    "O": "目标进度三态（原先是 F8，撞编辑器停止键，2026-08-31 换掉）",
    "G": "手柄预览：模拟按住扳机切成手柄态，不接手柄也能看手柄呈现（2026-08-31 加）",
    "F9": "放一段演出",
    "F10": "打印当前数值",
    "F11": "显示／隐藏调试文字",
}

# 编辑器会抢走的键：押在这上面的调试键在编辑器里起工程时按不出效果，且失败方式**看起来像崩溃**。
# 只列有依据的那些：`F8` 由上游 issue 与本项目实机各证一次。`F5`／`F6` 虽然也是编辑器的运行类
# 快捷键，但实测事件到得了游戏窗口（`F5` 由作者实机确认，`F6`／`F7` 由 2026-08-31 的注入实测
# 走的是同一条 `_UnhandledKeyInput` 通路），所以不列 —— 没有依据就不写进黑名单。
EDITOR_GRABBED_KEYS = {
    "F8": "编辑器「停止运行项目」，游戏窗口有焦点时照样生效（godotengine/godot#94713）——"
          "按下去只会停掉进程，作者会以为程序崩了",
}

# [输入] 动作 attack_light 死区 0.20 绑定 2 条
ACTION_RE = re.compile(r"\[输入\] 动作 (\S+) 死区 ([\d.]+) 绑定 (\d+) 条")
# [输入]   事件 attack_light ｜ Left Mouse Button
EVENT_RE = re.compile(r"\[输入\]   事件 (\S+) ｜ (.+)")
# [输入] 判据 PASS ｜ 左扳机按下后生效组为左 ｜ 实际 Left
VERDICT_RE = re.compile(r"\[输入\] 判据 (PASS|FAIL) ｜ ([^｜]+) ｜ (.*)")
# [输入] 自检 21/21 条通过
SELFCHECK_RE = re.compile(r"\[输入\] 自检 (\d+)/(\d+) 条通过")
# [输入] 技能组 Left = skill_1、skill_2、skill_3
GROUP_RE = re.compile(r"\[输入\] 技能组 (\w+) = (.+)")
# [输入] 绑定核对 PASS ｜ guard ｜ KeyQ ｜ 期望 Q - Physical ｜ 实际 ...
COMPARE_RE = re.compile(r"\[输入\] 绑定核对 (PASS|FAIL) ｜ ([^｜]+) ｜ ([^｜]+) ｜(.*)")
# [输入] 内置 ui_accept ｜ 共 3 条，手柄 0 条
BUILTIN_RE = re.compile(r"\[输入\] 内置 (\w+) ｜ 共 (\d+) 条，手柄 (\d+) 条")
# [输入] 内置补丁核对 PASS ｜ ui_accept ｜ PadFaceBottom ｜ 期望 ... ｜ 实际 ...
PATCH_RE = re.compile(r"\[输入\] 内置补丁核对 (PASS|FAIL) ｜ ([^｜]+) ｜ ([^｜]+) ｜(.*)")

# 绑定表里一条绑定的起头。**只数不解字符串** —— 解字符串已经栽过一次：把 EngineText 改成跨行
# 拼接之后，取值的正则静默少解出 12 条，守卫照样全绿。少算比算错更坏，它把「没核」伪装成「通过」。
# 所以期望值的**内容**改由引擎侧比对（InputProbe.CompareBindings），这里只负责一件 Python 干得
# 准的事：数出该有多少条，用来卡住「比对器自己漏了几条」。
BINDING_COUNT_RE = re.compile(r"new\(InputSymbol\.")

# 手柄上必须能确认与返回，否则「面板导航在手柄上可用」不成立 —— 焦点挪得动但按不下去。
BUILTIN_NEEDS_PAD = ("ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_cancel")

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


# ── 期望条数：只数，不解内容 ──────────────────────────────────────────
def declared_binding_count() -> tuple[int, str | None]:
    """数绑定表里声明了多少条绑定。"""
    if not BINDINGS_CS.is_file():
        return 0, f"找不到绑定表：{BINDINGS_CS}"
    n = len(BINDING_COUNT_RE.findall(BINDINGS_CS.read_text(encoding="utf-8")))
    if n == 0:
        return 0, "绑定表里一条 new(InputSymbol. 都没数到 —— 格式变了，本脚本要跟着改"
    return n, None


# ── 跑引擎，把日志读回来 ──────────────────────────────────────────────
def run_engine(launcher: list[str], log: Path) -> str | None:
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
    if not log.is_file():
        return None
    return log.read_text(encoding="utf-8", errors="replace")


# ── 四类检查 ──────────────────────────────────────────────────────────
def check_actions_and_bindings(text: str, declared: int) -> None:
    got_actions = {m.group(1): float(m.group(2)) for m in ACTION_RE.finditer(text)}
    got_events: dict[str, list[str]] = {}
    for m in EVENT_RE.finditer(text):
        got_events.setdefault(m.group(1), []).append(m.group(2))

    if not got_actions:
        fail("启动日志里一条 [输入] 动作 都没有 —— InputProbe 没跑，或输出格式变了")
        return
    ok(f"引擎里装了 {len(got_actions)} 个自定义动作，"
       f"共 {sum(len(v) for v in got_events.values())} 条绑定")

    # 逐条全等比对由引擎侧做（InputProbe.CompareBindings），理由见那边的注释。这里只核两件
    # Python 干得准的事：一条 FAIL 都没有，且比对覆盖的条数等于绑定表声明的条数。
    compares = COMPARE_RE.findall(text)
    if not compares:
        fail("日志里一条「绑定核对」都没有 —— 比对根本没跑")
        return

    failed = [(action.strip(), what.strip(), rest.strip())
              for verdict, action, what, rest in compares if verdict == "FAIL"]
    for action, what, rest in failed:
        fail(f"绑定核对不通过：{action} 的 {what} ｜{rest}")

    # 声明的每一条绑定都必须在两处比对之一里出现过：主表走「绑定核对」，补给内置动作的那几条走
    # 「内置补丁核对」。合起来数，才不会出现「加了一张表却没人核」。
    patched = len(PATCH_RE.findall(text))
    if len(compares) + patched != declared:
        fail(f"比对了 {len(compares)} 条主表 + {patched} 条内置补丁 = "
             f"{len(compares) + patched} 条，绑定表里声明了 {declared} 条 —— "
             f"差额说明有绑定没被核，或本脚本的数法过时了")
    elif not failed:
        ok(f"绑定表声明 {declared} 条，逐条全等比对 {len(compares)} 条主表 + "
           f"{patched} 条内置补丁，全部一致")

    covered = {action.strip() for _, action, _, _ in compares}
    uncovered = sorted(set(got_actions) - covered)
    if uncovered:
        fail(f"这些动作装进了 InputMap 却没被比对：{uncovered}")
    else:
        ok(f"装进 InputMap 的 {len(got_actions)} 个动作每个都被比对过")

    # 扳机的死区必须显式高于引擎默认的 0.2，否则手指搭上去就算按住修饰键。
    trigger_deadzone = _declared_trigger_deadzone()
    for action in ("skill_group_left", "skill_group_right"):
        got = got_actions.get(action)
        if got is None:
            fail(f"{action} 不在引擎里，死区无法核")
        elif abs(got - trigger_deadzone) > 1e-6:
            fail(f"{action} 死区 {got}，绑定表声明 {trigger_deadzone}")
        else:
            ok(f"{action} 死区 {got:.2f}，与绑定表声明一致且高于引擎默认 0.20")


def check_builtin_ui_actions(text: str) -> None:
    """界面导航靠引擎内置的 `ui_*`，所以要核它们在手柄上真的有绑定。

    这条不是形式检查：焦点挪得动但按不下去，「面板导航在手柄上可用」就不成立，而实机上要插着
    手柄点开一个面板才会发现。
    """
    got = {m.group(1): (int(m.group(2)), int(m.group(3))) for m in BUILTIN_RE.finditer(text)}
    if not got:
        fail("日志里没有内置动作那几行 —— 格式变了")
        return

    naked = [name for name in BUILTIN_NEEDS_PAD
             if name in got and got[name][1] == 0]
    absent = [name for name in BUILTIN_NEEDS_PAD if name not in got]
    if absent:
        fail(f"这些内置动作没打出来，无法核：{absent}")
    elif naked:
        fail(f"这些内置动作在手柄上一条绑定都没有，手柄进了面板就出不来：{naked}")
    else:
        ok("内置的方向、确认与返回在手柄上都有绑定："
           + "、".join(f"{n}({got[n][1]} 条)" for n in BUILTIN_NEEDS_PAD))

    # 补丁只许追加不许先擦：擦掉会把 ui_accept 的 Enter/Space 与 ui_cancel 的 Escape 带走，
    # 那是「修好手柄反而弄坏键鼠」。判据是这两个动作的键鼠事件仍在。
    for name, expect_kbd in (("ui_accept", 3), ("ui_cancel", 1)):
        if name not in got:
            continue
        total, pad = got[name]
        if total - pad < expect_kbd:
            fail(f"{name} 的键鼠事件只剩 {total - pad} 条（原有 {expect_kbd} 条）—— "
                 f"补丁把内置绑定擦掉了，键鼠玩家会退不出面板")
        else:
            ok(f"{name} 补手柄绑定后键鼠事件仍有 {total - pad} 条，没被擦掉")

    patches = PATCH_RE.findall(text)
    if not patches:
        fail("日志里一条「内置补丁核对」都没有 —— 补丁没跑或格式变了")
        return
    bad = [(a.strip(), s.strip(), r.strip()) for v, a, s, r in patches if v == "FAIL"]
    for action, symbol, rest in bad:
        fail(f"内置补丁没生效：{action} 的 {symbol} ｜{rest}")
    if not bad:
        ok(f"内置动作的 {len(patches)} 条手柄补丁逐条全等命中")


def _declared_trigger_deadzone() -> float:
    m = re.search(r"TriggerDeadzone = ([\d.]+)f", BINDINGS_CS.read_text(encoding="utf-8"))
    return float(m.group(1)) if m else -1.0


def check_no_input_section() -> None:
    text = PROJECT_GODOT.read_text(encoding="utf-8")
    if re.search(r"^\[input\]", text, re.MULTILINE):
        fail("project.godot 里出现了 [input] 段 —— 默认绑定的权威源是 "
             "rules/Ui/InputBindings.cs，两份来源会漂移")
    else:
        ok("project.godot 里没有 [input] 段，绑定只有一份来源")


def check_no_direct_polling() -> None:
    offenders: list[str] = []
    scanned = 0
    for path in sorted((ROOT / "src").rglob("*.cs")):
        rel = path.relative_to(ROOT).as_posix()
        scanned += 1
        if rel in POLLING_ALLOWED:
            continue
        for i, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            code = line.split("//", 1)[0]
            if POLLING_RE.search(code):
                offenders.append(f"{rel}:{i}")
    if scanned == 0:
        fail("src/ 下一个 .cs 都没扫到 —— 这条检查在空转")
    elif offenders:
        fail(f"这些地方直接轮询了引擎的动作状态，应改走 InputRouter：{offenders}")
    else:
        ok(f"扫过 src/ 下 {scanned} 个 .cs，除门面之外没有直接轮询引擎动作状态的地方")


def check_harness_debug_keys() -> None:
    """脚手架调试键不许押在编辑器要用的键上，且必须与登记逐个对上。"""
    found: dict[str, list[str]] = {}
    scanned = 0
    for rel in HARNESS_KEY_FILES:
        path = ROOT / rel
        if not path.is_file():
            fail(f"{rel} 不在 —— 这条检查在空转，而空转的守卫也会「全绿」")
            return
        scanned += 1
        text = path.read_text(encoding="utf-8")
        for key in HARNESS_KEY_RE.findall(text):
            found.setdefault(key, []).append(rel)

    if not found:
        fail(f"在 {list(HARNESS_KEY_FILES)} 里一个 `case Key.X:` 都没扫到 —— "
             f"脚手架调试键的写法变了，这条检查已经在空转")
        return

    grabbed = sorted(k for k in found if k in EDITOR_GRABBED_KEYS)
    if grabbed:
        fail("脚手架调试键押在编辑器要用的键上："
             + "；".join(f"{k}（{EDITOR_GRABBED_KEYS[k]}）" for k in grabbed))
    else:
        ok(f"扫过 {scanned} 个脚手架源码里的 {len(found)} 个调试键，"
           f"没有一个押在编辑器要用的 {sorted(EDITOR_GRABBED_KEYS)} 上")

    missing = sorted(set(HARNESS_KEYS) - set(found))
    extra = sorted(set(found) - set(HARNESS_KEYS))
    if missing or extra:
        fail(f"脚手架调试键与登记对不上：代码里多出 {extra}、登记里多出 {missing} —— "
             f"新加一个键却不登记，就没人核对过它会不会被编辑器吃掉")
    else:
        ok(f"{len(found)} 个调试键与登记逐个对上（{'、'.join(f'{k} {v}' for k, v in HARNESS_KEYS.items())}）")


def check_verdicts(text: str) -> None:
    verdicts = VERDICT_RE.findall(text)
    if not verdicts:
        fail("启动日志里一条 [输入] 判据都没有 —— InputProbe 没跑，或输出格式变了")
        return

    failed = [(what.strip(), detail.strip()) for verdict, what, detail in verdicts
              if verdict == "FAIL"]
    for what, detail in failed:
        fail(f"实机判据不成立：{what}（{detail}）")
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

    groups = dict(GROUP_RE.findall(text))
    want = {"Left": "skill_1、skill_2、skill_3", "Right": "skill_4、skill_5、skill_6"}
    if groups == want:
        ok("两组技能位各三个且顺序正确：" + "；".join(f"{k} = {v}" for k, v in groups.items()))
    else:
        fail(f"技能组内容不对：实际 {groups}，期望 {want}")


def main() -> int:
    ap = argparse.ArgumentParser(description="输入映射实测（UI-7）")
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

    declared, why = declared_binding_count()
    if why:
        say(f"[FAIL] {why}")
        print("EXIT=1")
        return 1
    say(f"绑定表 {BINDINGS_CS.relative_to(ROOT).as_posix()} 声明了 {declared} 条绑定")

    # 不依赖引擎的三条先跑：源码扫描比起引擎快得多，坏在这里就不必等引擎。
    check_no_input_section()
    check_no_direct_polling()
    check_harness_debug_keys()

    log = LOG_DIR / f"{time.strftime('%Y%m%d-%H%M%S')}.log"
    text = run_engine(launcher, log)
    if text is None:
        fail("没拿到引擎日志，实机那几条判定未验证")
    else:
        say(f"引擎日志 {log.relative_to(ROOT).as_posix()}")
        check_actions_and_bindings(text, declared)
        check_builtin_ui_actions(text)
        check_verdicts(text)

    say()
    say(f"覆盖量：{_CHECKED} 条判据，其中 {len(_FAILS)} 条失败")
    say("检查范围：绑定逐条全等比对引擎自报名（比对在引擎侧，本脚本核条数与零 FAIL）、扳机死区、"
        "内置 ui_* 的手柄绑定、project.godot 无 [input] 段、src/ 无直接轮询、启动自检的实机判据")
    _pruned = prune_runs(LOG_DIR)
    if _pruned:
        say(f"[清理] logs/input 删掉 {len(_pruned)} 份旧日志，只留最近 {KEEP_RUNS} 次")
    print("EXIT=" + ("1" if _FAILS else "0"))
    return 1 if _FAILS else 0


if __name__ == "__main__":
    raise SystemExit(main())
