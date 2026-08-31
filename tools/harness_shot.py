#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""跑一次相机验收脚手架并存一张图（`UI-5` 的实机确认／`UI-8` 的排版迭代）。

**为什么要有它**：排 HUD 与判断脚手架有没有真画出东西，都需要「跑起来看一眼」落成一个能复核的
文件。此前这件事是拿一长串 PowerShell 管道拼出来的，两个后果：命令守卫每次都要拦一下（管道 +
python 正是它盯的形状），而且管道读中文输出会乱码（设计仓 reference/踩坑记录.md 第 27 条）。
多步逻辑写成 py 直接干活，这两个问题一起消失。

它做三件事：按需构建 → 带窗口跑一次工程并让 `CameraHarness` 存图 → 把结果与图的路径写进
UTF-8 日志。**不带 `--headless`** —— 没有渲染设备就取不到视口纹理。

**一次出两张**：俯视与侧视各一张。存图前脚手架会把 15 个剪影收拢到视野里 ——
`UI-8` 的「同屏 10–15 个敌人时 HUD 挡不挡人」那条验收要看得见那个场面，而散开的剪影在侧视
320×180 的视野里一次只看得见两三个。

用法（从代码仓根目录运行）：
    python tools/harness_shot.py                      # 构建后存两张 1280x720 的图
    python tools/harness_shot.py --no-build           # 跳过构建（改的只是数值时快一截）
    python tools/harness_shot.py --resolution 1920x1080
"""

from __future__ import annotations

import argparse
import subprocess
import sys
import time
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

from loglib import prune_runs, KEEP_RUNS  # noqa: E402  同目录工具（跑完修剪旧日志）

ROOT = Path(__file__).resolve().parent.parent
SHOT_DIR = ROOT / "logs" / "art"
GODOT_ROOT = Path(r"D:\godot\4-7")

# CameraHarness 认的用户参数与它打出来的那一行。
SHOT_ARG = "--harness-shot"
READY_MARK = "[脚手架] 相机验收场景就绪"
SHOT_MARK = "[脚手架] 存图 "

_LINES: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


def find_godot() -> Path | None:
    """取 mono 版带控制台的 exe —— 本工程是 C#，非 mono 版跑不了。"""
    if not GODOT_ROOT.is_dir():
        return None
    cands = [p for p in GODOT_ROOT.rglob("*_console.exe") if "mono" in p.name]
    return sorted(cands)[0] if cands else None


def build() -> bool:
    code = subprocess.run(["dotnet", "build", "--nologo", "-v", "q"],
                          cwd=ROOT, check=False,
                          stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode
    say(f"构建 {'通过' if code == 0 else f'失败（退出码 {code}）—— 跑 python tools/verify.py 看原因'}")
    return code == 0


def main() -> int:
    ap = argparse.ArgumentParser(description="跑一次相机验收脚手架并存图")
    ap.add_argument("--no-build", action="store_true", help="跳过构建")
    ap.add_argument("--resolution", default="1280x720", help="窗口尺寸，默认 1280x720（×2 缩放）")
    args = ap.parse_args()

    godot = find_godot()
    if godot is None:
        say(f"[FAIL] 在 {GODOT_ROOT} 下找不到 mono 版 *_console.exe")
        print("EXIT=1")
        return 1

    if not args.no_build and not build():
        print("EXIT=1")
        return 1

    stamp = time.strftime("%Y%m%d-%H%M%S")
    SHOT_DIR.mkdir(parents=True, exist_ok=True)
    before = {p.name for p in SHOT_DIR.glob("harness-*.png")}
    log = SHOT_DIR / f"shot-{stamp}.log"

    cmd = [str(godot), "--path", str(ROOT), "--resolution", args.resolution,
           "--log-file", str(log), "--quit-after", "900", "--", SHOT_ARG]
    say(f"跑 {godot.name}｜窗口 {args.resolution}｜引擎日志 {log.relative_to(ROOT).as_posix()}")
    try:
        subprocess.run(cmd, check=False, timeout=180,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except subprocess.TimeoutExpired:
        say("[FAIL] 引擎超时 180s 未退出")
        print("EXIT=1")
        return 1

    text = log.read_text(encoding="utf-8", errors="replace") if log.is_file() else ""
    if READY_MARK not in text:
        say(f"[FAIL] 日志里没有「{READY_MARK}」—— 脚手架没建起来。"
            f"headless 下它刻意不建，本脚本没带 --headless，所以更可能是启动就炸了")
    errors = [ln.strip() for ln in text.splitlines() if "ERROR" in ln or "SCRIPT ERROR" in ln]
    for line in errors[:5]:
        say(f"[FAIL] 引擎报错：{line}")

    fresh = sorted(p for p in SHOT_DIR.glob("harness-*.png") if p.name not in before)
    if not fresh:
        say("[FAIL] 没有新存出来的图 —— 存图那一步没跑到，或者存盘失败")
    else:
        for path in fresh:
            say(f"[OK] 图 {path.relative_to(ROOT).as_posix()}（{path.stat().st_size} 字节）")

    for line in (ln.strip() for ln in text.splitlines() if ln.startswith("[脚手架]")):
        say(f"     {line}")

    bad = bool(errors) or not fresh or READY_MARK not in text
    say(f"结果：{'有必须修复项' if bad else '存图成功，引擎无报错'}")
    (SHOT_DIR / f"shot-{stamp}-summary.log").write_text(
        "\n".join(_LINES) + "\n", encoding="utf-8", newline="\n")
    # 修剪旧存图与日志：一次运行落 ~4 个文件（两张图 + 两份日志），留最近几份足够复核。
    _pruned = prune_runs(SHOT_DIR)
    if _pruned:
        say(f"[清理] logs/art 删掉 {len(_pruned)} 份旧存图/日志，只留最近 {KEEP_RUNS} 份")
    print(f"EXIT={1 if bad else 0}")
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(main())
