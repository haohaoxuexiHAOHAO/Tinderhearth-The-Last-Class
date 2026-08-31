#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""`check_camera.py` 的自证：注入真实缺陷形状 → 确认拦得住 → 还原 → 复验。

**它跑过一次全绿不能证明它拦得住东西** —— 一个什么都不检的脚本也会全绿。所以每条检查都要有一个
用真实缺陷形状造出来的用例，并且自报覆盖量：登记了检查目标却没实际执行，就算失败
（[WORKFLOW §6]）。

缺陷形状不是编的，都对着 `UI-5` 的验收标准或实现过程中真出现过的错：

- **给侧视另写一台相机。** 正典点名的失效：「行为不定下来，两种视角会各写一套」。它不报错，
  只表现为「侧视手感和俯视不太一样」。
- **给相机去掉 `sealed`。** 派两个子类是「各写一套」最自然的形态。
- **规则层多出一份相机状态机。** 同一件事在规则层的形态。
- **某个行为只在一种视角下成立。** 「各写一套」的行为投影 —— 判据在一边跑了，另一边悄悄没跑。
- **场景里摆一台裸 `Camera2D`。** 绕过那份实现，行为不受任何判据管。
- **可建造区尺寸写成字面量**，以及**读错配置字段**。两者都让改配置改不动游戏（PRD 的 `FR-24`，
  是否扩大已记 `GP-8`）。
- **打开 2D 像素吸附。** 相机的整数保证会被它掩盖。
- **棋盘格没铺满就去量。** 本轮真差点栽在这里：反证第一版因为相机每帧把位置抄回整数而测了个
  整数位置，还以为引擎自己会吸附。空转的量具会「全绿」，所以统计段数必须有下限。
- **震动开关没关死。** 直接对着验收标准「关闭后重击不产生任何位移」。

用法（从代码仓根目录运行，约五分钟）：
    python tools/selfcheck_camera.py
    python tools/selfcheck_camera.py --list    # 只列用例与覆盖登记，不跑

输出固定 UTF-8，逐条 [OK]／[FAIL]，末尾打覆盖量与一行 EXIT=；同时写
logs/camera/selfcheck-<时间戳>.log。临时注入在 finally 里还原，收尾会复验一次全绿。
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
GUARD = ROOT / "tools" / "check_camera.py"
LOG_DIR = ROOT / "logs" / "camera"

RIG_CS = ROOT / "rules" / "Ui" / "CameraRig.cs"
CAMERA_CS = ROOT / "src" / "World" / "GameCamera.cs"
PROBE_CS = ROOT / "src" / "World" / "CameraProbe.cs"
PROJECT_GODOT = ROOT / "project.godot"
MAIN_TSCN = ROOT / "scenes" / "Main.tscn"
EXTRA_CAMERA_CS = ROOT / "src" / "World" / "ZzSelfcheckSideCamera.cs"
EXTRA_RIG_CS = ROOT / "rules" / "Ui" / "ZzSelfcheckCameraRig.cs"

# 守卫里登记在册的检查函数。**从源码枚举**，所以新增一个 check_ 却不自证会被当场拦住，
# 而不是等到某天发现守卫一直在空转。
CHECK_FN_RE = re.compile(r"^def (check_\w+)\(", re.MULTILINE)

# 没有用例的分支要写明理由，否则覆盖量自报会判失败。
UNPROVEN: dict[str, str] = {
    "配置里缺可建造区字段": "缺字段会在 GameConfig 构造时抛，游戏根本起不来，守卫这边只会看到"
                            "「没拿到引擎日志」。这条形状由规则层测试"
                            "「配置缺字段时当场抛而不是静默填零」盯着，不该在守卫这边重复一遍",
    "死区与震动的具体取值不合适": "手感值只能实机收敛（`UI-12`），守卫刻意不核「应该是几」。"
                                  "量纲与允许区间由规则层测试盯着（能被侧视缩放整除、落在算得出"
                                  "来的区间里），改值不该让守卫失败",
    "引擎起不来": "要伪造引擎缺失或崩溃才能触发。真发生时 verify.py 的构建与跑产物两步先炸，"
                  "不会走到本守卫",
    "视口缩放不是整数": "那是缩放链路的事，归 tools/check_scaling.py 与它的四档窗口用例。"
                        "本守卫只在整数缩放的前提下量像素块，前提破了应当由那边先判失败",
}


@dataclass
class Edit:
    """一次文本替换或建文件。old 为 None 表示建新文件或追加。"""

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
        name="给侧视另写一台相机",
        covers=["check_single_camera_implementation"],
        shape="正典点名的失效：「行为不定下来，两种视角会各写一套」。不报错，"
              "只表现为侧视手感和俯视不太一样",
        expect="派生 Camera2D 的类型有",
        edits=[Edit(EXTRA_CAMERA_CS, None,
                    "using Godot;\n\nnamespace Tinderhearth.World;\n\n"
                    "// 自证用的临时文件，selfcheck_camera.py 跑完会删。\n"
                    "public sealed partial class ZzSelfcheckSideCamera : Camera2D\n{\n"
                    "    public int Zoomed => 2;\n}\n")],
    ),
    Case(
        name="给相机去掉 sealed",
        covers=["check_single_camera_implementation"],
        shape="派两个子类是「两种视角各写一套」最自然的形态，封不住这条路就等于没封",
        expect="不是 sealed",
        edits=[Edit(CAMERA_CS, "public sealed partial class GameCamera : Camera2D",
                    "public partial class GameCamera : Camera2D")],
    ),
    Case(
        name="规则层多出一份相机状态机",
        covers=["check_no_extra_camera_types_in_rules"],
        shape="同一件事在规则层的形态：有人为侧视另建一套几何，两边的死区与钳制迟早分叉",
        expect="规则层多出这些相机类型",
        edits=[Edit(EXTRA_RIG_CS, None,
                    "namespace Tinderhearth.Rules.Ui;\n\n"
                    "// 自证用的临时文件，selfcheck_camera.py 跑完会删。\n"
                    "public sealed class ZzSelfcheckCameraRig\n{\n"
                    "    public int Zoom => 2;\n}\n")],
    ),
    Case(
        name="某个行为只在一种视角下成立",
        covers=["check_views_share_one_implementation"],
        shape="「各写一套」的行为投影：判据在侧视跑了，俯视那边悄悄没跑，"
              "于是俯视的震动开关根本没人验",
        expect="两种视角的判据名字集合不同",
        edits=[Edit(PROBE_CS,
                    '        Check(view, "关掉震动后重击不产生任何位移", '
                    'zeroed && !rig.IsShaking, $"恒零 {zeroed}");',
                    "        if (view == CameraView.SideView)\n        {\n"
                    '            Check(view, "关掉震动后重击不产生任何位移", '
                    'zeroed && !rig.IsShaking, $"恒零 {zeroed}");\n        }\n')],
    ),
    Case(
        name="场景里摆一台裸 Camera2D",
        covers=["check_no_camera_node_in_scenes"],
        shape="在编辑器里顺手拖一个 Camera2D 进场景。它绕过那份实现，行为不受任何判据管，"
              "而且会与真相机抢 current",
        expect="不挂 GameCamera 脚本的 Camera2D",
        needs_build=False,
        edits=[Edit(MAIN_TSCN, None,
                    '\n[node name="ZzSelfcheckCamera" type="Camera2D" parent="."]\n'
                    "zoom = Vector2(2, 2)\n")],
    ),
    Case(
        name="可建造区尺寸写成字面量",
        covers=["check_no_hardcoded_buildable_size"],
        shape="正典写着 40×30，于是有人直接写进代码。之后 GP-8 要扩大时改配置改不动游戏",
        expect="写成了字面量",
        edits=[Edit(PROBE_CS,
                    "            _config.BuildableWidthCells * UiMetrics.BaseUnit,\n"
                    "            _config.BuildableHeightCells * UiMetrics.BaseUnit);",
                    "            40 * UiMetrics.BaseUnit,\n"
                    "            30 * UiMetrics.BaseUnit);")],
    ),
    Case(
        name="读错配置字段_宽度当高度用",
        covers=["check_buildable_from_config"],
        shape="复制粘贴改漏一处。配置读到了、也没有字面量，但装进相机的是错的尺寸 ——"
              "这就是为什么判据必须是「装进相机的那个值」而不是「打印出来的配置值」",
        expect="按 game.json 应是",
        edits=[Edit(PROBE_CS,
                    "            _config.BuildableHeightCells * UiMetrics.BaseUnit);",
                    "            _config.BuildableWidthCells * UiMetrics.BaseUnit);")],
    ),
    Case(
        name="项目打开 2D 像素吸附",
        covers=["check_no_pixel_snap"],
        shape="有人为了「让画面稳一点」打开吸附。相机自己的整数保证被它掩盖，"
              "之后谁把取整删了都看不出来",
        expect="打开了 2D 像素吸附",
        needs_build=False,
        edits=[Edit(PROJECT_GODOT,
                    "textures/canvas_textures/default_texture_filter=0",
                    "textures/canvas_textures/default_texture_filter=0\n"
                    "2d/snap/snap_2d_transforms_to_pixel=true")],
    ),
    Case(
        name="棋盘格没铺满就去量",
        covers=["check_pixel_alignment"],
        shape="本轮真差点栽在这里：反证第一版测了个整数位置还以为引擎会吸附。"
              "空转的量具照样全绿，所以统计段数必须有下限",
        expect="图案没铺上或被界面盖住了",
        edits=[Edit(PROBE_CS, "    private const int PatternSize = 512;",
                    "    private const int PatternSize = 8;")],
    ),
    Case(
        name="震动开关没关死",
        covers=["check_verdicts"],
        shape="直接对着验收标准「关闭后重击不产生任何位移」。关了还晃对晕动敏感的玩家就是不可用，"
              "而它不报错",
        expect="实机判据不成立",
        edits=[Edit(RIG_CS,
                    "        if (!ShakeEnabled || _shakeElapsed >= _shakeSeconds "
                    "|| _shakeSeconds <= 0.0)",
                    "        if (_shakeElapsed >= _shakeSeconds || _shakeSeconds <= 0.0)")],
    ),
    Case(
        name="俯视也被改成 2 倍缩放",
        covers=["check_zoom_and_view_against_canon"],
        shape="有人觉得基地拉近一点好看。正典写明侧视 2 倍、俯视不缩放，"
              "改了之后基地的有效视野只剩一半而没有任何报错",
        expect="正典是 x1",
        edits=[Edit(RIG_CS,
                    ": View == CameraView.SideView ? UiMetrics.SideViewZoom : 1;",
                    ": View == CameraView.SideView ? UiMetrics.SideViewZoom : 2;")],
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
    path.write_text("\n".join(_LINES) + "\n", encoding="utf-8", newline="\n")
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


def cleanup_leftovers() -> None:
    for path in (EXTRA_CAMERA_CS, EXTRA_RIG_CS):
        if path.exists():
            path.unlink()
            say(f"[..] 清掉了残留的临时注入文件 {path.name}")


def main() -> int:
    ap = argparse.ArgumentParser(description="check_camera.py 的自证")
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
        say("[FAIL] 起点就不是全绿，先修好再自证 —— 跑 python tools/check_camera.py 看原因")
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
                    tail = out.strip().splitlines()[-1] if out.strip() else ""
                    say("[FAIL] 注入后编不过，这条用例没能真正跑起来")
                    say(f"       {tail}")
                    _FAILS.append(f"{case.name}：注入后编译失败")
                    continue

            code, out = run_guard()
            ran.append(case.name)
            if code == 0:
                say("[FAIL] 守卫没拦住 —— 退出码 0，期望非零")
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
    built, _ = build()
    if not built:
        say("[FAIL] 还原后编不过 —— 源码可能没还原干净")
        _FAILS.append("还原后编译失败")
    code, _ = run_guard()
    if code == 0:
        say("[OK] 还原后守卫恢复全绿")
    else:
        say("[FAIL] 还原后守卫仍不通过 —— 源码没还原干净，去看 git status")
        _FAILS.append("还原后守卫仍不通过")

    cleanup_leftovers()
    coverage(ran)
    say()
    say(f"结果：{len(ran)}／{len(CASES)} 条用例执行，{len(_FAILS)} 项失败")
    say(f"日志 {flush_log().relative_to(ROOT).as_posix()}")
    print("EXIT=" + ("1" if _FAILS else "0"))
    return 1 if _FAILS else 0


if __name__ == "__main__":
    raise SystemExit(main())
