#!/usr/bin/env python3
"""湿耕地那一套照干耕地那一套写出：形状一个像素不改，锈红（rust）每一档换成同一档的湿耕地（wet）。

为什么由代码写：色板里湿耕地那条色带就是这么定义的 —— 与 rust 一档对一档、形状相同只换色带（色板
assets/self-drawn/palette.gpl 里 wet 那几行的注释；设计仓 production/场景绘制约定.md 的「耕地要干与湿两套」）。
两张都手画，干湿两张迟早在几个像素上对不上，而那在 Godot 里只有浇水那一下才露出来。

读  assets/self-drawn/tiles/tilled.png      作者画的干耕地：十字排布，16 种邻格情形
写  assets/self-drawn/tiles/tilled-wet.png  湿耕地。别手改：改干那张，再跑一次本入口

只换 rust；别的色（十字四臂上标「没有邻格」的草、收边里的草屑）照抄。干那张里出现 wet、色板外的色
或半透明就停，并报出是哪一点 —— 那几样换不出一个说得清的湿版。

谁改过湿那张靠像素指纹认，与 tools/ui_art.py 同一个做法：写出的 PNG 里记一条按像素算的指纹。磁盘上那张
与它记的指纹对得上，就是本入口上回写的，可以换新；没有指纹或对不上，就是有人手改过，停下不写。

用法（从代码仓根目录跑）：
    python tools/tilled_wet.py

本脚本不进 verify.py，没有自证入口。写完照常跑一次 python tools/check_art.py。
"""

from __future__ import annotations

import hashlib
import sys
from collections import Counter

from PIL import Image
from PIL.PngImagePlugin import PngInfo

from palettelib import ROOT, read_palette

if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")

TILES = ROOT / "assets" / "self-drawn" / "tiles"
DRY = TILES / "tilled.png"
WET = TILES / "tilled-wet.png"
DRY_RAMP = "rust"
WET_RAMP = "wet"
# 写出的 PNG 里记指纹的那条文字块。按像素算，画图软件另存时丢不丢这条、压缩率变没变都不影响判断。
FINGERPRINT_KEY = "tilled_wet"


def fingerprint(im: Image.Image) -> str:
    rgba = im.convert("RGBA")
    return hashlib.sha256(f"{rgba.width}x{rgba.height}:".encode() + rgba.tobytes()).hexdigest()[:16]


def main() -> int:
    palette = read_palette()
    names = {rgb: name for name, rgb in palette.items()}
    if not DRY.exists():
        raise SystemExit(f"[FAIL] 找不到 {DRY.relative_to(ROOT).as_posix()}，没有干的就写不出湿的")

    dry = Image.open(DRY).convert("RGBA")
    wet = dry.copy()
    swapped: Counter[str] = Counter()
    kept: Counter[str] = Counter()
    for y in range(dry.height):
        for x in range(dry.width):
            r, g, b, a = dry.getpixel((x, y))
            if a == 0:
                continue
            if a != 255:
                raise SystemExit(f"[FAIL] {DRY.name} 的 ({x},{y}) 是半透明（alpha {a}），只许全透明或全不透明")
            name = names.get((r, g, b))
            if name is None:
                raise SystemExit(f"[FAIL] {DRY.name} 的 ({x},{y}) 是 {(r, g, b)}，不在色板里")
            ramp, step = name.rsplit(" ", 1)
            if ramp == WET_RAMP:
                raise SystemExit(f"[FAIL] {DRY.name} 的 ({x},{y}) 是 {name}：干那张里有湿耕地的色，"
                                 "换不出它在湿那张里该是哪一档")
            if ramp != DRY_RAMP:
                kept[name] += 1
                continue
            target = f"{WET_RAMP} {step}"
            if target not in palette:
                raise SystemExit(f"[FAIL] {DRY.name} 的 ({x},{y}) 是 {name}，色板里没有对应的 {target}")
            wet.putpixel((x, y), (*palette[target], 255))
            swapped[f"{name} → {target}"] += 1

    if not swapped:
        raise SystemExit(f"[FAIL] {DRY.name} 里一个 {DRY_RAMP} 都没有，写出来的湿那张与干那张一样")

    rel = WET.relative_to(ROOT).as_posix()
    new = fingerprint(wet)
    if WET.exists():
        with Image.open(WET) as old:
            recorded = getattr(old, "text", {}).get(FINGERPRINT_KEY)
            actual = fingerprint(old)
        if actual != new and recorded != actual:
            print(f"[FAIL] {rel} 有人手改过（没有指纹，或像素与记下的指纹对不上），不覆盖。"
                  "要改湿的就改干那张再跑；要换回照干那张写出的那一版，删掉它再跑")
            return 1
        if actual == new and recorded == new:
            print(f"[OK] {rel} 与照干那张写出的一样，没动")
            return 0

    info = PngInfo()
    info.add_text(FINGERPRINT_KEY, new)
    wet.save(WET, pnginfo=info)
    print(f"[写入] {rel}（{wet.width}×{wet.height}）")
    print("    换色：" + "，".join(f"{k}×{v}" for k, v in sorted(swapped.items())))
    if kept:
        print("    照抄：" + "，".join(f"{k}×{v}" for k, v in sorted(kept.items())))
    print("[OK] 湿那张照干那张写出；接着跑 python tools/check_art.py")
    return 0


if __name__ == "__main__":
    sys.exit(main())
