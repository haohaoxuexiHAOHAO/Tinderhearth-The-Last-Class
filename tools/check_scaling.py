#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""按四档窗口尺寸实测缩放链路（`UI-3`）。

要判定的事只有一句：**逻辑分辨率必须整数倍放大到屏幕**（[ADR-0008]），否则像素字会糊。
它的失败方式是静默的 —— 画面只是"有点糊"，不报错，可能几个月后才被发现。所以判据不能是
"我设了 scale_mode=integer"，而必须是"引擎实际用上的变换是整数"。

做法：用不同 `--resolution` 启动工程，让引擎自己把显示指标写进 `--log-file`，本脚本从日志
里读回来比对。**不用管道读中文输出**（设计仓 reference/踩坑记录.md 第 27 条），日志由引擎
写、由本脚本 decode。

四档窗口与期望倍数（16:9 主流三档 + 720p）：

    1280x720  → x2      2560x1440 → x4
    1920x1080 → x3      3840x2160 → x6

用法（从代码仓根目录运行）：
    python tools/check_scaling.py             # 跑工程源码（快，不需要先导出）
    python tools/check_scaling.py --exported  # 跑 export/ 下的产物（更接近发行状态）

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

ROOT = Path(__file__).resolve().parent.parent
LOG_DIR = ROOT / "logs" / "scaling"
GODOT_ROOT = Path(r"D:\godot\4-7")

# 窗口尺寸 → 期望的整数倍。逻辑分辨率 640x360 时这四档都除得尽。
CASES: list[tuple[int, int, int]] = [
    (1280, 720, 2),
    (1920, 1080, 3),
    (2560, 1440, 4),
    (3840, 2160, 6),
]

EXPECT_LOGICAL = (640, 360)

# [显示] 逻辑 640x360 窗口 1280x720 缩放 x2,2
LINE_RE = re.compile(
    r"\[显示\] 逻辑 (\d+)x(\d+) 窗口 (\d+)x(\d+) 缩放 x([\d.]+),([\d.]+)")

_LINES: list[str] = []
_FAILS: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


def fail(text: str) -> None:
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
    if not export.is_dir():
        return None
    exes = sorted(export.glob("*.exe"))
    return exes[0] if exes else None


def run_case(launcher: list[str], w: int, h: int, log: Path) -> str | None:
    """跑一次并把引擎日志读回来。拿不到日志就返回 None（判失败，不猜）。"""
    log.parent.mkdir(parents=True, exist_ok=True)
    if log.exists():
        log.unlink()
    cmd = launcher + ["--resolution", f"{w}x{h}", "--log-file", str(log), "--quit-after", "30"]
    try:
        subprocess.run(cmd, check=False, timeout=120,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except subprocess.TimeoutExpired:
        fail(f"{w}x{h}：超时 120s 未退出")
        return None
    if not log.is_file():
        return None
    return log.read_text(encoding="utf-8", errors="replace")


def main() -> int:
    ap = argparse.ArgumentParser(description="缩放链路实测（UI-3）")
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

    stamp = time.strftime("%Y%m%d-%H%M%S")
    checked = 0
    for w, h, want in CASES:
        text = run_case(launcher, w, h, LOG_DIR / stamp / f"{w}x{h}.log")
        if text is None:
            fail(f"{w}x{h}：没拿到引擎日志，本档判定未验证")
            continue
        m = LINE_RE.search(text)
        if m is None:
            fail(f"{w}x{h}：日志里没有 [显示] 那一行 —— Main 没跑到，或格式变了")
            continue
        checked += 1
        lw, lh = int(m.group(1)), int(m.group(2))
        aw, ah = int(m.group(3)), int(m.group(4))
        sx, sy = float(m.group(5)), float(m.group(6))
        clamped = (aw, ah) != (w, h)
        ok = True

        # 真正要守的不变量是「整数且两轴相等」。**不是**「逻辑尺寸恰好等于 640x360」——
        # aspect="expand" 把高度锁在 360、按窗口宽高比撑宽（2026-08-30 实测 3840x2130 的
        # 窗口得到逻辑 649x360），整数缩放再把除不尽的余量留成黑边。
        if abs(sx - round(sx)) > 1e-6 or abs(sy - round(sy)) > 1e-6:
            fail(f"{w}x{h}：缩放 x{sx},{sy} 不是整数 —— 像素会糊")
            ok = False
        if abs(sx - sy) > 1e-6:
            fail(f"{w}x{h}：两轴缩放不等（x{sx} 对 x{sy}）—— 画面会被拉变形")
            ok = False
        if lw < EXPECT_LOGICAL[0] or lh < EXPECT_LOGICAL[1]:
            fail(f"{w}x{h}：逻辑尺寸 {lw}x{lh} 小于基准 "
                 f"{EXPECT_LOGICAL[0]}x{EXPECT_LOGICAL[1]} —— 界面会被裁")
            ok = False
        if not clamped and abs(sx - want) > 1e-6:
            fail(f"{w}x{h}：实际缩放 x{sx}，期望 x{want}")
            ok = False
        if ok:
            note = "" if not clamped else f"（本机屏幕装不下，窗口实为 {aw}x{ah}，按它判定）"
            say(f"[OK]   {w}x{h} → 逻辑 {lw}x{lh}，缩放 x{sx:g} 整数两轴一致{note}")

    say(f"\n覆盖量：登记 {len(CASES)} 档窗口，实际读到日志并判定 {checked} 档")
    if checked == 0:
        fail("一档都没判到 —— 空转的检查也会「全绿」，所以这里必须失败")
    if _FAILS:
        say(f"[FAIL] 共 {len(_FAILS)} 条不成立")
    else:
        say("[OK] 四档窗口的缩放倍数都是整数且与期望一致")
    log_path = LOG_DIR / stamp / "summary.log"
    log_path.parent.mkdir(parents=True, exist_ok=True)
    log_path.write_text("\n".join(_LINES) + "\n", encoding="utf-8", newline="\n")
    say(f"日志 {log_path.relative_to(ROOT)}")
    print(f"EXIT={1 if _FAILS else 0}")
    return 1 if _FAILS else 0


if __name__ == "__main__":
    raise SystemExit(main())
