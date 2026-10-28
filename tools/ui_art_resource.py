#!/usr/bin/env python3
"""素材资源的三份产物，照生成器清单（tools/ui_art.py）写出：规则层那份清单、引擎层那个资源类型、作者在检查器里核的那一份资源。

为什么由清单生成、不手写：资源的字段就是设计仓 production/界面绘制约定.md 那几张表里的件，一张图一格。
手写一百多格，漏一格、拼错一个名字都不报错，表现只是那一件永远读不到。三份都从同一份清单写出，
清单加一件，三份一起跟。

写什么：
  rules/UI/UIArtManifest.cs         每一格该是什么：尺寸、九宫格边距、哪一态、用在哪。规则层，测试与载入核对读它。
  src/UI/UIArt.cs                   素材资源这个类型：一件一格，按 assets/self-drawn/ui/ 下的目录分组。
  assets/self-drawn/ui/ui_art.tres  按文件名填好的那一份。只在它不存在时写 —— 作者在检查器里换过的格子不覆盖；
                                    存在时只报它比清单少了哪几格、多了哪几格，由人在检查器里补。

用法（从代码仓根目录跑）：
    python tools/ui_art_resource.py           写那两个 .cs；资源不存在就写一份
    python tools/ui_art_resource.py --check   不写，只报那两个 .cs 与资源跟清单对不对得上，对不上退出码 1

清单与磁盘上的图对不对得上、能由排版单位推出的那几档对不对得上单位，由 tests/UI/UIArtTests.cs 判。
本脚本不进 verify.py，没有自证入口。
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import defaultdict
from pathlib import Path

import check_art
import ui_art
from palettelib import ROOT

if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")

MANIFEST = ROOT / "rules" / "UI" / "UIArtManifest.cs"
RESOURCE_CS = ROOT / "src" / "UI" / "UIArt.cs"
NINE_CS = ROOT / "src" / "UI" / "NinePiece.cs"
RESOURCE = ui_art.OUT / "ui_art.tres"
HEADER = "// 本文件由 tools/ui_art_resource.py 照生成器清单（tools/ui_art.py）写出，别手改：改清单之后重跑那个入口。"


def pascal(text: str) -> str:
    return "".join(part[:1].upper() + part[1:] for part in text.split("_"))


def prop(p: ui_art.Piece) -> str:
    """资源里那一格的属性名：目录加文件名，都转成 PascalCase。检查器按目录分组时把目录那一截去掉。"""
    return pascal(p.group) + pascal(p.name)


def state_of(name: str) -> str:
    """文件名末尾的状态后缀，与交图核对同一张表（check_art.STATES）。"""
    return next((s for s in check_art.STATES if name.endswith(f"_{s}")), "")


def ordered(pieces: list[ui_art.Piece]) -> list[ui_art.Piece]:
    """按目录归到一起（目录按清单里第一次出现的先后），同一目录里照清单顺序 —— 检查器里一个目录只出现一次。"""
    groups: dict[str, list[ui_art.Piece]] = {}
    for p in pieces:
        groups.setdefault(p.group, []).append(p)
    return [p for members in groups.values() for p in members]


def cs(text: str) -> str:
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


def xml(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def detail(p: ui_art.Piece, families: dict[tuple[str, str], list[str]], line_height: int) -> str:
    w, h = p.image.size
    parts = [f"{w}×{h}"]
    if p.nine is not None:
        l, t, r, b = p.nine
        parts.append(f"九宫格 左 {l} 上 {t} 右 {r} 下 {b}")
        if p.text:
            parts.append(f"放一行字至少高 {t + line_height + b}")
    states = families[family_key(p)]
    if len(states) > 1:
        parts.append("同一件的几态：" + "、".join(states))
    parts.append(f"来源：{p.source}")
    return "；".join(parts) + "。"


def family_key(p: ui_art.Piece) -> tuple[str, str]:
    parent, base = check_art.family(Path(p.group) / f"{p.name}.png")
    return parent.as_posix(), base


def manifest_source(pieces: list[ui_art.Piece]) -> str:
    out = [
        HEADER,
        "",
        "namespace Tinderhearth.Rules.UI;",
        "",
        "/// <summary>素材资源里每一格该是什么：生成器清单的一份照抄，一件一格。</summary>",
        "/// <remarks>",
        "/// 尺寸是生成器写出的那一张的尺寸。作者换图要保持原尺寸，载入时按它核（<see cref=\"UIArtCheck\"/>）；",
        "/// 能由排版单位推出的那几档对不对得上单位、清单与磁盘上的图对不对得上，由测试判（tests/UI/UIArtTests.cs）。",
        "/// </remarks>",
        "public static class UIArtManifest",
        "{",
        "    /// <summary>全部界面件。同一目录的排在一起，目录内照生成器清单的顺序。</summary>",
        "    public static readonly IReadOnlyList<UIArtPiece> Pieces =",
        "    [",
    ]
    for p in pieces:
        w, h = p.image.size
        nine = "null" if p.nine is None else "new NineMargins({}, {}, {}, {})".format(*p.nine)
        out.append(f"        new({cs(p.group)}, {cs(p.name)}, new PixelSize({w}, {h}), {nine}, "
                   f"{cs(state_of(p.name))}, {cs(p.use)}),")
    out += [
        "    ];",
        "",
        "    /// <summary>",
        "    /// 同一批目录里由作者直接画、生成器不碰的那几张。它们不进素材资源，用到它们的那一页落地时再接。",
        "    /// </summary>",
        "    public static readonly IReadOnlyList<string> AuthorOwned =",
        "    [",
    ]
    out += [f"        {cs('ui/' + rel)}," for rel in ui_art.author_owned()]
    out += ["    ];", "}", ""]
    return "\n".join(out)


def resource_source(pieces: list[ui_art.Piece]) -> str:
    families: dict[tuple[str, str], list[str]] = defaultdict(list)
    for p in pieces:
        families[family_key(p)].append(state_of(p.name) or p.name)
    line_height = ui_art.ui_metric("LineHeight")
    out = [
        HEADER,
        "",
        "using Godot;",
        "",
        "namespace Tinderhearth.UI;",
        "",
        "/// <summary>",
        "/// 素材资源：界面件一律从这一份取。一件一格，格名就是文件名，按 <c>assets/self-drawn/ui/</c> 下的目录分组。",
        "/// </summary>",
        "/// <remarks>",
        "/// 九宫格件那一格是一个 <see cref=\"NinePiece\"/>：图与它四边的边距放在一起。每一格该多大、用在哪，在",
        "/// <see cref=\"Tinderhearth.Rules.UI.UIArtManifest\"/>；载入时缺哪几格、哪张尺寸不对，由",
        "/// <see cref=\"UIArtGuard\"/> 一次全报。按文件名填好的那一份在 <c>assets/self-drawn/ui/ui_art.tres</c>，",
        "/// 作者在检查器里换图，换上去的那张要保持原尺寸。",
        "/// </remarks>",
        "[GlobalClass]",
        "public partial class UIArt : Resource",
        "{",
    ]
    group = None
    for p in pieces:
        out.append(f"    /// <summary>{xml(p.use)}</summary>")
        out.append(f"    /// <remarks>{xml(detail(p, families, line_height))}</remarks>")
        if p.group != group:
            out.append(f"    [ExportGroup({cs(p.group)}, {cs(pascal(p.group))})]")
            group = p.group
        kind = "NinePiece" if p.nine is not None else "Texture2D"
        out += ["    [Export]", f"    public {kind}? {prop(p)} {{ get; set; }}", ""]
    out += [
        "    /// <summary>每一格填了什么，给载入核对用：分组、名字、那张图、九宫格那一格（不是九宫格件的是 <c>null</c>）。</summary>",
        "    internal IEnumerable<(string Group, string Name, Texture2D? Texture, NinePiece? Nine)> Cells()",
        "    {",
    ]
    for p in pieces:
        name = prop(p)
        if p.nine is not None:
            out.append(f"        yield return ({cs(p.group)}, {cs(p.name)}, {name}?.Texture, {name});")
        else:
            out.append(f"        yield return ({cs(p.group)}, {cs(p.name)}, {name}, null);")
    out += ["    }", "}", ""]
    return "\n".join(out)


def uid_line(path: Path) -> str | None:
    """贴图的 uid 在它的 .import 里，脚本的在旁边那个 .uid 里；还没有（Godot 没扫过）就只按路径引用。"""
    if path.suffix == ".png":
        imported = path.with_name(path.name + ".import")
        if imported.exists():
            found = re.search(r'^uid="(uid://[^"]+)"', imported.read_text(encoding="utf-8"), re.MULTILINE)
            return found.group(1) if found else None
        return None
    sidecar = path.with_name(path.name + ".uid")
    return sidecar.read_text(encoding="utf-8").strip() if sidecar.exists() else None


def ext(kind: str, path: Path, ident: str) -> str:
    uid = uid_line(path)
    res = "res://" + path.relative_to(ROOT).as_posix()
    return f'[ext_resource type="{kind}"' + (f' uid="{uid}"' if uid else "") + f' path="{res}" id="{ident}"]'


def resource_text(pieces: list[ui_art.Piece]) -> str:
    out = ['[gd_resource type="Resource" script_class="UIArt" format=3]', "",
           ext("Script", RESOURCE_CS, "ui_art"), ext("Script", NINE_CS, "nine_piece")]
    out += [ext("Texture2D", ui_art.OUT / p.group / f"{p.name}.png", f"{p.group}_{p.name}") for p in pieces]
    out.append("")
    for p in pieces:
        if p.nine is not None:
            l, t, r, b = p.nine
            out += [f'[sub_resource type="Resource" id="nine_{p.group}_{p.name}"]',
                    'script = ExtResource("nine_piece")',
                    f'Texture = ExtResource("{p.group}_{p.name}")',
                    f"Left = {l}", f"Top = {t}", f"Right = {r}", f"Bottom = {b}", ""]
    out += ["[resource]", 'script = ExtResource("ui_art")']
    for p in pieces:
        ref = f'SubResource("nine_{p.group}_{p.name}")' if p.nine is not None else f'ExtResource("{p.group}_{p.name}")'
        out.append(f"{prop(p)} = {ref}")
    out.append("")
    return "\n".join(out)


def resource_gaps(pieces: list[ui_art.Piece]) -> tuple[list[str], list[str]]:
    """已有那一份资源比清单少了哪几格、多了哪几格（按 [resource] 那一节的属性名比）。"""
    text = RESOURCE.read_text(encoding="utf-8")
    section = text.split("[resource]", 1)[1] if "[resource]" in text else ""
    have = set(re.findall(r"^(\w+) = ", section, re.MULTILINE)) - {"script"}
    want = {prop(p) for p in pieces}
    return sorted(want - have), sorted(have - want)


def main() -> int:
    ap = argparse.ArgumentParser(description="素材资源：照生成器清单写清单、资源类型与那一份资源")
    ap.add_argument("--check", action="store_true", help="不写，只报对不对得上")
    args = ap.parse_args()

    pieces = ordered(ui_art.build_all())
    names = [prop(p) for p in pieces]
    dupes = sorted({n for n in names if names.count(n) > 1})
    if dupes:
        print(f"[FAIL] 这几件转成属性名之后撞了：{'、'.join(dupes)}")
        return 1

    stale = 0
    for path, source in ((MANIFEST, manifest_source(pieces)), (RESOURCE_CS, resource_source(pieces))):
        rel = path.relative_to(ROOT).as_posix()
        current = path.read_text(encoding="utf-8") if path.exists() else None
        if current == source:
            print(f"[OK]   {rel} 与清单一致")
        elif args.check:
            stale += 1
            print(f"[FAIL] {rel} 与清单对不上，不带 --check 重跑一次")
        else:
            path.write_text(source, encoding="utf-8", newline="\n")
            print(f"[写入] {rel}")

    rel = RESOURCE.relative_to(ROOT).as_posix()
    if not RESOURCE.exists():
        if args.check:
            stale += 1
            print(f"[FAIL] {rel} 还不存在，不带 --check 跑一次")
        else:
            RESOURCE.write_text(resource_text(pieces), encoding="utf-8", newline="\n")
            print(f"[写入] {rel}：{len(pieces)} 格，按文件名填好")
    else:
        missing, extra = resource_gaps(pieces)
        for name in missing:
            print(f"[缺]   {rel} 里没有 {name} 这一格：在检查器里补上，或者确认没人改过它之后删掉这份重跑")
        for name in extra:
            print(f"[多余] {rel} 里多了 {name} 这一格，清单里已经没有它")
        stale += bool(missing or extra)
        if not (missing or extra):
            print(f"[OK]   {rel} 的格子与清单一致（只比格子在不在，不比填的是哪张图）")

    print(f"清单 {len(pieces)} 件；EXIT={1 if stale else 0}")
    return 1 if stale else 0


if __name__ == "__main__":
    sys.exit(main())
