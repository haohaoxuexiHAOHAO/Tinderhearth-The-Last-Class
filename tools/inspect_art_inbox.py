#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""量一遍素材收件箱里的 PNG，给「能不能接进仓」一个有依据的判断。

**为什么要有它**：`temp/art-inbox/README.md` 写明的流程是「核对规格 → 登记 → 接进 → 清空」，
而「核对规格」此前是每次手工量一遍。手工量的问题不是慢，是**漏项不报错** —— 少量一次
半透明像素，那张图进仓之后守卫才发现，而那时它已经被别的东西引用了。

**像素判据不在这里重写一份**：半透明像素与放大件两条直接调 `check_assets.py` 里那两个函数
（`ENG-10` 的实现）。同一条规则有两份实现是漂移的开始 —— 生成器当初也是为这个改成调它的。
本工具只补收件箱这一步特有的两件事：**未知精灵表的格子尺寸**（登记之前还不知道帧宽是多少，
`check_assets.py` 是拿登记表当期望值的，这里没有登记表），以及**授权原文的位置**。

用法（从代码仓根目录运行）：
    python tools/inspect_art_inbox.py                     # 量默认收件箱
    python tools/inspect_art_inbox.py --path <某个目录>    # 量别处

输出约定与 `verify.py` 一致：末尾打覆盖量与一行 EXIT=；日志自己写 UTF-8 落到
logs/art/inbox-<时间戳>.log，不靠 shell 重定向（设计仓 reference/踩坑记录.md 第 27 条）。
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_assets                                     # noqa: E402  路径插好才导得到

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_INBOX = ROOT.parent / "temp" / "art-inbox"
LOG_DIR = ROOT / "logs" / "art"

# 候选格子尺寸。16 是正典的基础单位，32 是精灵格，8 是排版栅格；48／64 是外部素材常见的
# 「一格半」与「两格」尺寸，量出来是它们就说明要换算而不是直接用。
CANDIDATE_TILES = (8, 16, 32, 48, 64, 96)

# 收件箱分两桶（作者 2026-08-31 定的目录约定）。**授权因此是结构性的而不是逐个猜的**：
# 自绘件的授权是「本项目自有」，不需要许可证文件；下载件必须带许可证原文，缺了就不能进发行包。
SELF_BUCKET = "self-material"
OTHER_BUCKET = "other-material"

_LINES: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LINES.append(text)


def tile_guesses(width: int, height: int) -> list[int]:
    """能同时整除宽与高的候选格子尺寸，大的在前。"""
    return [t for t in sorted(CANDIDATE_TILES, reverse=True)
            if width % t == 0 and height % t == 0]


def bucket_of(rel: str) -> str:
    """这张图属于哪一桶。桶决定授权怎么判，不由包名或观感决定。"""
    head = rel.split("/", 1)[0]
    if head == SELF_BUCKET:
        return SELF_BUCKET
    return OTHER_BUCKET if head == OTHER_BUCKET else "（不在两个桶里）"


def describe(path: Path, root: Path) -> None:
    rel = path.relative_to(root).as_posix()
    im = check_assets.load_image(path)
    w, h = im.size

    # 半透明与放大件两条直接用 ENG-10 的实现。它们自己打 [FAIL]，本函数不重复判定。
    before = len(check_assets._FAILS)                    # noqa: SLF001  见模块注释
    check_assets.check_alpha(rel, im)
    check_assets.check_upscaled(rel, im)
    pixel_ok = len(check_assets._FAILS) == before        # noqa: SLF001

    colors = len({im.load()[x, y] for y in range(h) for x in range(w)})
    opaque = sum(1 for a in im.getchannel("A").getdata() if a == 255)
    guesses = tile_guesses(w, h)

    say(f"  {rel}")
    say(f"      桶 {bucket_of(rel)}｜画布 {w}×{h}｜色数 {colors}｜不透明像素 {opaque}／{w * h}"
        f"（{opaque * 100 // (w * h)}%）")
    say(f"      能整除的格子尺寸 {guesses if guesses else '（8／16／32／48／64／96 都除不尽）'}"
        f"｜像素判据 {'过' if pixel_ok else '**不过，见上面的 FAIL**'}")


def main() -> int:
    ap = argparse.ArgumentParser(description="量素材收件箱里的 PNG")
    ap.add_argument("--path", default=str(DEFAULT_INBOX), help="收件箱目录")
    args = ap.parse_args()

    inbox = Path(args.path).resolve()
    if not inbox.is_dir():
        say(f"[FAIL] 收件箱不在：{inbox}")
        print("EXIT=1")
        return 1

    say(f"收件箱 {inbox}")
    pngs = [p for p in sorted(inbox.rglob("*.png"))]
    licenses = [p for p in sorted(inbox.rglob("*")) if p.is_file()
                and "licen" in p.name.lower()]
    others = [p for p in sorted(inbox.rglob("*")) if p.is_file()
              and p.suffix.lower() not in (".png", ".md") and p not in licenses]

    by_bucket: dict[str, int] = {}
    for path in pngs:
        by_bucket[bucket_of(path.relative_to(inbox).as_posix())] = \
            by_bucket.get(bucket_of(path.relative_to(inbox).as_posix()), 0) + 1
    say(f"PNG {len(pngs)} 个 {by_bucket if by_bucket else ''}"
        f"｜许可证文件 {len(licenses)} 个｜其他文件 {len(others)} 个")
    say()

    for path in pngs:
        describe(path, inbox)
    say()

    # 授权按**桶**判，不按包名或观感 —— `ART-2` 栽过一次：Zpix 看着像开源，实读是单个商业产品
    # 且禁止拆分。自绘桶的授权是「本项目自有」，不需要许可证文件；下载桶必须带许可证原文。
    stray = sorted({bucket_of(p.relative_to(inbox).as_posix()) for p in pngs}
                   - {SELF_BUCKET, OTHER_BUCKET})
    if stray:
        say(f"[FAIL] 有 PNG 不在 {SELF_BUCKET}／{OTHER_BUCKET} 两个桶里 —— "
            f"桶决定授权怎么判，放错地方等于没有授权依据")

    other_packs = {p.relative_to(inbox).parts[1] for p in pngs
                   if bucket_of(p.relative_to(inbox).as_posix()) == OTHER_BUCKET
                   and len(p.relative_to(inbox).parts) > 2}
    licensed = {p.relative_to(inbox).parts[1] for p in licenses
                if len(p.relative_to(inbox).parts) > 2}
    naked = sorted(other_packs - licensed)
    if naked:
        say(f"[FAIL] 这些下载包没有许可证文件：{naked} —— 授权是法律事实，"
            f"判不出来就不能进发行包")
    elif other_packs:
        say(f"下载包 {len(other_packs)} 个，每个都带许可证原文。**必须逐字读**，不许按包名推断：")
        for path in licenses:
            say(f"  {path.relative_to(inbox).as_posix()}")

    self_count = by_bucket.get(SELF_BUCKET, 0)
    say(f"自绘 PNG {self_count} 个 —— 授权为「本项目自有」，"
        + ("不需要许可证文件" if self_count else "本桶现在是空的"))

    if others:
        say()
        say("这些不是 PNG 也不是许可证，**接仓时不要带进去**（工程文件、预览图之类）：")
        for path in others:
            say(f"  {path.relative_to(inbox).as_posix()}（{path.stat().st_size} 字节）")

    say()
    say(f"覆盖量：逐像素扫过 {len(pngs)} 个 PNG，每个查了 2 类"
        f"（半透明、放大件，实现来自 check_assets.py）加 3 项测量（色数、格子尺寸候选、桶）")
    if not pngs:
        say("[FAIL] 一个 PNG 都没扫到 —— 空转的检查也会「全绿」，所以这里必须失败")

    fails = len(check_assets._FAILS) + len(naked) + len(stray)   # noqa: SLF001
    say(f"结果：{fails} 条判据不成立"
        + ("（不改就不能进仓）" if fails else "（像素规格与授权归属都过，剩下的是用途判断）"))

    LOG_DIR.mkdir(parents=True, exist_ok=True)
    log = LOG_DIR / f"inbox-{time.strftime('%Y%m%d-%H%M%S')}.log"
    log.write_text("\n".join(_LINES) + "\n", encoding="utf-8", newline="\n")
    say(f"日志 {log.relative_to(ROOT).as_posix()}")
    print(f"EXIT={1 if fails or not pngs else 0}")
    return 1 if fails or not pngs else 0


if __name__ == "__main__":
    raise SystemExit(main())
