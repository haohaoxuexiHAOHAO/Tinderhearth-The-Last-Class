#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""代码仓行尾守卫（ENG-11）。

为什么存在：`.gitattributes` 声明 `* text=auto eol=lf`，却没有任何机制在工作区里
发现违反 —— 有声明、无执行体。同一机制在代码仓比设计仓更危险：`.sh` 与 git 钩子带
`\\r` 时 Git Bash 报 `bad interpreter: /bin/sh^M` 直接不执行，文档准出检查静默失效
（踩坑记录 28）。

这里的逻辑与设计仓 `tools/check_docs.py` 的行尾部分完全等价，唯一的差别是 ROOT
指向代码仓。两份不合并成共享入口，是因为每份都自包含、能独立运行，合并反而要求
执行时知道自己在哪个仓，那是一个新的依赖。

两个方向都判：`eol=lf` 的文件里不许有 `\\r`；`eol=crlf` 的（`*.bat`／`*.cmd`）
里不许有裸 `\\n`。只守一半等于只执行了半份 `.gitattributes`。

二进制判定门槛与 git 自己一致：前若干字节内出现 NUL 就当二进制，跳过行尾检查。
没有这条的话 `* text=auto` 在 PNG 等二进制文件上也会返回 `lf`，会误报。

用法（从代码仓根目录运行）：
    python tools/check_eol.py        # 检查，退出码 0 = 全部符合
    python tools/check_eol.py --fix  # 把行尾改回 .gitattributes 声明的样子

接进门禁：`verify.py` 的 `step_eol` 调本脚本作为子进程，认「覆盖量：」前缀判过。
改了本脚本就跑 `python tools/selfcheck_verify.py` 自证（它会撞行尾步骤）。
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parent.parent

# 与设计仓 check_docs.py 保持一致：前 8000 字节内出现 NUL 就认为是二进制。
BINARY_SNIFF_BYTES = 8_000


def _git_managed_files() -> list[str] | None:
    """git 会管的文件：已跟踪 + 未被忽略的未跟踪。返回 None 表示问不出来。

    用 `git ls-files` 而不是自己遍历目录，是为了让 `.gitignore` 自动生效 ——
    否则 `__pycache__/`、`.vs/`、`obj/`、`bin/` 之类的产物都会被拖进行尾检查。
    `-z` 分隔避免中文文件名被 `core.quotepath` 转成八进制（踩坑记录见 check_docs.py）。
    """
    try:
        out = subprocess.run(
            ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
            cwd=ROOT, capture_output=True, check=True,
        ).stdout
    except (subprocess.CalledProcessError, FileNotFoundError, OSError):
        return None
    seen: set[str] = set()
    result: list[str] = []
    for raw in out.split(b"\0"):
        if not raw:
            continue
        name = raw.decode("utf-8", errors="replace")
        if name not in seen:
            seen.add(name)
            result.append(name)
    return result


def _git_eol_policy(paths: list[str]) -> dict[str, str] | None:
    """问 git 每个路径解析后的 `eol` 属性（`lf`／`crlf`／`unspecified`）。

    把 `.gitattributes` 当权威源的关键一步：规则解析交给 git，本脚本只负责
    比对实际字节。输出格式是 `<路径> NUL eol NUL <值> NUL` 三元组（已实测）。
    """
    if not paths:
        return {}
    payload = b"\0".join(p.encode("utf-8") for p in paths) + b"\0"
    try:
        out = subprocess.run(
            ["git", "check-attr", "-z", "--stdin", "eol"],
            cwd=ROOT, input=payload, capture_output=True, check=True,
        ).stdout
    except (subprocess.CalledProcessError, FileNotFoundError, OSError):
        return None
    fields = out.split(b"\0")
    policy: dict[str, str] = {}
    for i in range(0, len(fields) - 2, 3):
        name = fields[i].decode("utf-8", errors="replace")
        policy[name] = fields[i + 2].decode("utf-8", errors="replace")
    return policy


def _eol_targets() -> tuple[list[tuple[str, str, bytes]], int, int] | None:
    """收集有行尾要求的文本文件：[(相对路径, 期望行尾, 内容)]，外加两个跳过计数。

    返回 None 表示策略问不出来 —— 调用方必须判失败，不能当通过。
    """
    paths = _git_managed_files()
    if paths is None:
        return None
    policy = _git_eol_policy(paths)
    if policy is None:
        return None

    targets: list[tuple[str, str, bytes]] = []
    binary = unset = 0
    for rel in paths:
        want = policy.get(rel, "unspecified")
        if want not in ("lf", "crlf"):
            unset += 1
            continue
        try:
            with (ROOT / rel).open("rb") as fh:
                head = fh.read(BINARY_SNIFF_BYTES)
                if b"\0" in head:
                    binary += 1
                    continue
                data = head + fh.read()
        except OSError:
            continue
        targets.append((rel, want, data))
    return targets, binary, unset


def _count_eol_violations(want: str, data: bytes) -> tuple[int, int]:
    """按期望行尾数出违反处数：(不该有的 CRLF 或 LF 数, 不该有的单独 CR 数)。"""
    crlf = data.count(b"\r\n")
    lone_cr = data.count(b"\r") - crlf
    if want == "lf":
        return crlf, lone_cr
    return data.count(b"\n") - crlf, lone_cr


def main() -> int:
    ap = argparse.ArgumentParser(description="代码仓行尾守卫（ENG-11）")
    ap.add_argument("--fix", action="store_true",
                    help="把行尾改回 .gitattributes 声明的样子，不做其他检查")
    args = ap.parse_args()

    collected = _eol_targets()
    if collected is None:
        print("[FAIL] 问不出 git 的文件清单或 `eol` 属性，这一轮行尾守卫"
              "**没有执行**（不是通过）—— 确认装了 git 且在仓库内运行")
        print("EXIT=1")
        return 1
    targets, binary, unset = collected

    if args.fix:
        fixed = 0
        for rel, want, data in targets:
            bad_eol, lone_cr = _count_eol_violations(want, data)
            if not bad_eol and not lone_cr:
                continue
            normalized = data.replace(b"\r\n", b"\n").replace(b"\r", b"\n")
            if want == "crlf":
                normalized = normalized.replace(b"\n", b"\r\n")
            (ROOT / rel).write_bytes(normalized)
            print(f"[FIX] {rel}：{bad_eol + lone_cr} 处 → {want.upper()}")
            fixed += 1
        print(f"覆盖量：检查 {len(targets)} 个文本文件"
              f"（跳过 {binary} 个二进制、{unset} 个未声明 eol），改写 {fixed} 个")
        if fixed == 0:
            print("[OK] 行尾本来就是对的，没有改动任何文件")
        print("EXIT=0")
        return 0

    fails: list[str] = []
    for rel, want, data in targets:
        bad_eol, lone_cr = _count_eol_violations(want, data)
        if not bad_eol and not lone_cr:
            continue
        parts = []
        if bad_eol:
            parts.append(f"{bad_eol} 处 {'CRLF' if want == 'lf' else '单独的 LF'}")
        if lone_cr:
            parts.append(f"{lone_cr} 处单独的 CR")
        msg = (f"行尾应为 {want.upper()}（`.gitattributes` 声明 eol={want}），"
               f"实测有 {'、'.join(parts)}；"
               f"跑 `python tools/check_eol.py --fix` 改回来")
        fails.append(f"[FAIL] {rel}：{msg}")

    for line in fails:
        print(line)

    if not targets:
        print("[FAIL] 一个有行尾要求的文本文件都没检到，"
              "说明文件清单或 `eol` 属性解析坏了")
        print("EXIT=1")
        return 1

    print(f"覆盖量：检查 {len(targets)} 个文本文件"
          f"（跳过 {binary} 个二进制、{unset} 个未声明 eol）"
          + (f"，{len(fails)} 个有违反" if fails else "，全部符合"))
    if fails:
        print("EXIT=1")
        return 1
    print("[OK] 行尾全部符合 .gitattributes 声明")
    print("EXIT=0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
