#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""日志目录修剪：每类只保留最近 N 次运行，避免 `logs/` 越堆越多。

为什么存在：`logs/` 在 `.gitignore` 里，是每次跑验收或守卫写的**运行证据** —— 可再生，但工具
**只写不清**，跑得越多堆得越多（2026-08-31 实测堆到 913 文件 6.2 MB）。有意义的结论本来就落进
了 issue 的验证表，旧日志留着没价值。所以每个写日志的工具在写完后调 `prune_runs` 修剪自己那一类。

**「一次运行」＝类目录下的一个直接子项**：时间戳子目录（如 `logs/verify/<时间戳>/`）与时间戳文件
（如 `logs/input/<时间戳>.log`）都算一项，按修改时间留最新 N 项，其余删掉。两种布局同一套逻辑。

用法：
    from loglib import prune_runs
    prune_runs(LOG_DIR)                 # 用默认保留数
    prune_runs(LOG_DIR, keep=3)

也可当入口手动清全部标准类目：
    python tools/loglib.py              # 每类修剪到默认保留数
    python tools/loglib.py --keep 3
"""

from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path

# 每类默认保留的运行数。留几次够对比最近两三轮，又不至于堆积。
KEEP_RUNS = 5

# 标准日志类目，供手动入口一次清全部。各工具自己那类由工具调用时传入。
ROOT = Path(__file__).resolve().parent.parent
LOG_ROOT = ROOT / "logs"


def prune_runs(category_dir: Path, keep: int = KEEP_RUNS) -> list[str]:
    """把 `category_dir` 下的运行修剪到最近 `keep` 项，返回被删掉的项名。

    **绝不抛异常打断调用方** —— 它是收尾清理，失败了也不该让一次成功的验收变成失败。删不掉的项
    记进返回值即可（调用方要报就报一句）。目录不存在或 `keep` 覆盖不到时返回空列表。
    """
    keep = max(keep, 1)     # 至少留 1 项：把最新一次也删掉没有意义
    if not category_dir.is_dir():
        return []

    try:
        children = sorted(
            category_dir.iterdir(),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )
    except OSError:
        return []

    removed: list[str] = []
    for child in children[keep:]:
        try:
            if child.is_dir():
                shutil.rmtree(child)
            else:
                child.unlink()
            removed.append(child.name)
        except OSError:
            # 删不掉（占用、权限）就跳过，不影响调用方。
            continue
    return removed


def main() -> int:
    ap = argparse.ArgumentParser(description="把 logs/ 每类修剪到最近 N 次运行")
    ap.add_argument("--keep", type=int, default=KEEP_RUNS, help=f"每类保留几次（默认 {KEEP_RUNS}）")
    args = ap.parse_args()

    if not LOG_ROOT.is_dir():
        print(f"没有 {LOG_ROOT.relative_to(ROOT).as_posix()}，无需修剪")
        return 0

    total = 0
    for category in sorted(p for p in LOG_ROOT.iterdir() if p.is_dir()):
        removed = prune_runs(category, keep=args.keep)
        total += len(removed)
        print(f"{category.name}: 删 {len(removed)} 项，留最近 {args.keep} 次")
    print(f"共删 {total} 项")
    return 0


if __name__ == "__main__":
    sys.exit(main())
