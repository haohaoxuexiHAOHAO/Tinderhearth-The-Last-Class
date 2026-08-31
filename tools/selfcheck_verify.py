#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""`tools/verify.py` 的自证入口：造真实缺陷形状 → 确认拦得住 → 还原 → 复验。

为什么存在（WORKFLOW §6）：验收总入口是「唯一职责就是可信」的那类基础设施。它跑过一次
全绿**不能**证明它拦得住东西 —— 一个什么都不检的脚本也会全绿。所以每条判定都得用一个
真实缺陷形状撞一次，看它是不是真的报失败、报的位置对不对。

每条用例的缺陷形状都不是编的，取自设计仓 `reference/踩坑记录.md` 里已经踩过的：

    条数对不上      第 29 条：摘要说通过，可有一个测试压根没跑
    包内泄漏        第 33 条：导出正常退出，子工程源码被打进发行包
    解析器跑偏      同 33 的反面：解析失败若返回空清单，就会被读成「0 条泄漏」
    落点不对        两仓各有一个 tools/，入口被搬错仓时必须当场拒绝

用法（从代码仓根目录运行）：

    python tools/selfcheck_verify.py            # 全部用例，约两分钟
    python tools/selfcheck_verify.py --list     # 只列用例与覆盖登记，不执行

输出约定与 verify.py 一致：固定 UTF-8，逐条 [OK]/[FAIL]，末尾打覆盖量、结果与 EXIT=，
同时把整份输出写到 logs/verify/selfcheck-<时间戳>.log（交互终端实测会截断长输出，
见踩坑记录 20，所以结论必须在文件里读得全）。

覆盖量的口径写死在 UNPROVEN_BRANCHES 旁边：**按函数**，不是按分支。没有用例的分支逐条
列出来带理由 —— 把「都覆盖了」说成「每条路径都撞过」是夸大。

所有注入都在 finally 里还原；临时文件放工作区根的 temp/，用完即删（WORKFLOW §5）。
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import time
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import verify  # noqa: E402  —— 同目录的被测对象，用它的常量与步骤清单，避免抄第二份

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = verify.ROOT
# 临时资源一律放工作区根的 temp/，不写 C 盘（WORKFLOW §5）。工作区根是两仓的父目录。
TEMP = ROOT.parent / "temp" / "selfcheck-verify"

# 自己写 UTF-8 日志，不靠 shell 重定向（WORKFLOW §5）。这条不是可选的：本脚本要跑十来轮
# 验收、输出很长，而交互终端实测会截断输出并给出无意义的退出码（踩坑记录 20）——
# 结论落在文件里才读得全。
_LOG_LINES: list[str] = []


def say(text: str = "") -> None:
    print(text, flush=True)
    _LOG_LINES.append(text)


def log_path() -> Path:
    return verify.LOG_ROOT / f"selfcheck-{time.strftime('%Y%m%d-%H%M%S')}.log"


def flush_log(path: Path) -> None:
    verify.ensure_logs_hidden_from_godot()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(_LOG_LINES) + "\n", encoding="utf-8", newline="\n")

# 覆盖登记：verify.py 的每个步骤与关键判定，各自由哪条用例撞过。
# 登记了却没有用例、且没写豁免理由的，直接判失败 —— 否则新增判定不自证会悄悄溜过去。
COVERAGE_EXEMPT: dict[str, str] = {}

# **覆盖量的口径必须说清**，否则「覆盖 6 项」会被读成「每条分支都撞过」，那是夸大。
# 下面登记的是**按函数**的覆盖：每个步骤至少有一条用例让它真的判失败过。函数内部还有
# 若干分支没有用例，逐条写在这里连理由一起 —— 说不清的空白比已知的空白危险。
UNPROVEN_BRANCHES = {
    "step_build · 认不出输出形状": "要伪造一个 dotnet build 的假输出才能触发；"
                                   "伪造出来的形状不是真实缺陷形状，撞它证明不了什么",
    "step_export · 产物缺失或空壳": "需要让 Godot 退出码 0 却不写文件，本机造不出来。"
                                    "退出码非 0 的失败路径由「跑产物」那条间接覆盖",
    "step_test · 认不出运行器摘要": "同「认不出输出形状」，要伪造 xunit 的假输出",
}


@dataclass
class Case:
    name: str
    covers: str                 # 撞的是 verify.py 的哪一部分
    shape: str                  # 缺陷形状出自哪
    inject: Callable[[], None]
    restore: Callable[[], None]
    args: list[str]             # 跑 verify.py 时的参数
    expect_in_output: str       # 失败信息里必须出现的字样
    expect_exit: int = 1


# ── 注入与还原的小工具 ────────────────────────────────────────────────
def _write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def _drop(path: Path) -> None:
    path.unlink(missing_ok=True)


PHANTOM_TEST = ROOT / "tests" / "Foundation" / "ZzSelfcheckPhantomTests.cs"
FAILING_TEST = ROOT / "tests" / "Foundation" / "ZzSelfcheckFailingTests.cs"
UNCOUNTABLE_TEST = ROOT / "tests" / "Foundation" / "ZzSelfcheckUncountableTests.cs"
BROKEN_RULE = ROOT / "rules" / "ZzSelfcheckBroken.cs"
RULES_GDIGNORE = ROOT / "rules" / ".gdignore"
RULES_GDIGNORE_OFF = ROOT / "rules" / ".gdignore.selfcheck-off"
GAME_CONFIG = ROOT / "data" / "config" / "game.json"
_config_backup: str | None = None


def inject_phantom_test() -> None:
    """源码里有 11 个测试，编译进去的只有 10 个 —— 踩坑记录 29 的形状。

    `#if` 一个永不定义的符号：`[Fact]` 那一行确实在源码里（静态扫描数得到），但编译器
    整块跳过，运行器只会报 10 条。运行器自己会说「通过」，全靠条数比对拦下来。
    """
    _write(PHANTOM_TEST, """// 自证用的临时文件，selfcheck_verify.py 跑完会删掉。
#if ZZ_SELFCHECK_NEVER_DEFINED
using Xunit;

namespace Tinderhearth.Rules.Tests.Foundation;

public class ZzSelfcheckPhantomTests
{
    [Fact]
    public void 这条永远不会被编译进去所以运行器数不到它()
    {
        Assert.True(true);
    }
}
#endif
""")


def inject_failing_test() -> None:
    """一条真失败的测试。与上一条分开，是为了证明「条数对得上」和「有失败」是两条独立判定。"""
    _write(FAILING_TEST, """// 自证用的临时文件，selfcheck_verify.py 跑完会删掉。
using Xunit;

namespace Tinderhearth.Rules.Tests.Foundation;

public class ZzSelfcheckFailingTests
{
    [Fact]
    public void 这条故意失败用来证明失败真的会被报出来()
    {
        Assert.Equal(1, 2);
    }
}
""")


def inject_uncountable_test() -> None:
    """用 `[MemberData]` 写一条测试：静态数不出条数时必须**明说数不出来**，不许少算。

    少算的后果正是踩坑记录 29 那件事：期望值偏小，于是「有测试没跑」也能对得上。
    """
    _write(UNCOUNTABLE_TEST, """// 自证用的临时文件，selfcheck_verify.py 跑完会删掉。
using Xunit;

namespace Tinderhearth.Rules.Tests.Foundation;

public class ZzSelfcheckUncountableTests
{
    public static TheoryData<int> 数据 => new(1, 2, 3);

    [Theory]
    [MemberData(nameof(数据))]
    public void 条数只有运行时才知道(int n)
    {
        Assert.True(n > 0);
    }
}
""")


def inject_broken_rule() -> None:
    """一处编译错误。证明构建那一步不是只看退出码就签字。"""
    _write(BROKEN_RULE, """// 自证用的临时文件，selfcheck_verify.py 跑完会删掉。
namespace Tinderhearth.Rules;

public class ZzSelfcheckBroken
{
    public void 故意语法错误( { }
}
""")


def inject_missing_gdignore() -> None:
    """把 rules/.gdignore 挪走 —— 踩坑记录 33 那次泄漏就是这个形状。"""
    if RULES_GDIGNORE.exists():
        RULES_GDIGNORE.rename(RULES_GDIGNORE_OFF)


def restore_gdignore() -> None:
    """把 .gdignore 放回去，**并清掉引擎顺手生成的资源元数据**。

    第一版漏了后半句，代价当场看得见：`.gdignore` 一挪走，Godot 就把 `rules/**/*.cs` 当
    CSharpScript 资源扫了一遍，生成 8 个 `.cs.uid` 留在仓库里。它们不会被打进包，所以
    「还原后复验」照样全绿 —— 也就是说自证工具自己造的垃圾，靠它自己的复验发现不了。
    自证工具把仓库留在脏状态是不可接受的，所以还原要连引擎的副产物一起收。
    """
    if RULES_GDIGNORE_OFF.exists():
        RULES_GDIGNORE_OFF.rename(RULES_GDIGNORE)
    # 只清 .gdignore 覆盖的那两个子工程；src/ 下的 .cs.uid 是引擎层的正常产物，不能碰。
    for sub in ("rules", "tests"):
        base = ROOT / sub
        if not base.is_dir():
            continue
        for residue in list(base.rglob("*.cs.uid")) + list(base.rglob("*.cs.import")):
            residue.unlink(missing_ok=True)


EXPORT_PRESETS = ROOT / "export_presets.cfg"
_presets_backup: str | None = None


def inject_font_without_license() -> None:
    """把许可证从导出预设的 include_filter 里去掉 —— 包里有字体却没有许可证（`ART-3`）。

    这是真实缺陷形状而不是编的：字体进仓那天要是没人想起改 `include_filter`，包里就是这个样子，
    而**它不报错** —— 只会变成上架后的法律问题（OFL 第 2 条要求每份拷贝都带许可证与版权声明）。
    """
    global _presets_backup
    _presets_backup = EXPORT_PRESETS.read_text(encoding="utf-8")
    text = _presets_backup
    for need in verify.BUNDLED_FILES:
        text = text.replace(f",{need}", "").replace(f"{need},", "")
    EXPORT_PRESETS.write_text(text, encoding="utf-8", newline="\n")


def restore_presets() -> None:
    global _presets_backup
    if _presets_backup is not None:
        EXPORT_PRESETS.write_text(_presets_backup, encoding="utf-8", newline="\n")
        _presets_backup = None


def inject_broken_config() -> None:
    """把外置配置写成坏 JSON。产物照样导得出来，但一跑就炸 —— 冒烟那一步该拦住它。"""
    global _config_backup
    _config_backup = GAME_CONFIG.read_text(encoding="utf-8")
    GAME_CONFIG.write_text("{ 这不是 JSON", encoding="utf-8", newline="\n")


def restore_config() -> None:
    global _config_backup
    if _config_backup is not None:
        GAME_CONFIG.write_text(_config_backup, encoding="utf-8", newline="\n")
        _config_backup = None


def _noop() -> None:
    pass


# ── 素材守卫的注入（`ENG-10`）──────────────────────────────────────────
# 这几条用真实缺陷形状：半透明像素是画图软件默认开抗锯齿就会出现的，漏登记是往
# assets/ 里直接拖一个文件就会发生的。两者都不报错，只静默积累。
SEMI_TARGET = ROOT / "assets" / "placeholder" / "ui" / "panel.png"
UNREGISTERED = ROOT / "assets" / "placeholder" / "ui" / "dragged-in.png"
_semi_backup: bytes | None = None


def inject_semi_transparent() -> None:
    """把一个像素的 alpha 改成 128 —— 抗锯齿留下的那种脏边。"""
    global _semi_backup
    from PIL import Image
    _semi_backup = SEMI_TARGET.read_bytes()
    im = Image.open(SEMI_TARGET).convert("RGBA")
    px = im.load()
    r, g, b, _ = px[3, 3]
    px[3, 3] = (r, g, b, 128)
    im.save(SEMI_TARGET)


def restore_semi_transparent() -> None:
    global _semi_backup
    if _semi_backup is not None:
        SEMI_TARGET.write_bytes(_semi_backup)
        _semi_backup = None


def inject_unregistered_asset() -> None:
    """往 assets/ 里丢一个没进登记表的文件。"""
    shutil.copy(SEMI_TARGET, UNREGISTERED)


def restore_unregistered_asset() -> None:
    UNREGISTERED.unlink(missing_ok=True)
    # 引擎可能已经顺手给它生成了导入元数据，一起清掉，否则下一轮会报「登记表里有但文件不在」
    Path(str(UNREGISTERED) + ".import").unlink(missing_ok=True)


REGISTRY = ROOT / "tools" / "asset-registry.json"
IMPORT_TARGET = ROOT / "assets" / "placeholder" / "ui" / "panel.png.import"
_registry_backup: bytes | None = None
_import_backup: bytes | None = None


def inject_wrong_frame_count() -> None:
    """把登记表里的帧数改错 —— 精灵表切帧算错，游戏里表现为动画错位。"""
    global _registry_backup
    import json
    _registry_backup = REGISTRY.read_bytes()
    data = json.loads(REGISTRY.read_text(encoding="utf-8"))
    for entry in data.get("下载素材", []):
        if entry.get("帧数", 1) > 1:
            entry["帧数"] += 1
            break
    REGISTRY.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n",
                        encoding="utf-8", newline="\n")


def restore_registry() -> None:
    global _registry_backup
    if _registry_backup is not None:
        REGISTRY.write_bytes(_registry_backup)
        _registry_backup = None


def inject_vram_compression() -> None:
    """把一张纹理的导入参数改回「用在 3D 就转 VRAM 压缩」—— 像素图被有损压缩的入口。"""
    global _import_backup
    _import_backup = IMPORT_TARGET.read_bytes()
    text = IMPORT_TARGET.read_text(encoding="utf-8")
    IMPORT_TARGET.write_text(text.replace("detect_3d/compress_to=0",
                                          "detect_3d/compress_to=1"),
                             encoding="utf-8", newline="\n")


def restore_import() -> None:
    global _import_backup
    if _import_backup is not None:
        IMPORT_TARGET.write_bytes(_import_backup)
        _import_backup = None


def case_upscaled_asset() -> tuple[bool, str]:
    """造一张真正放大来的图：16×16 内容按最近邻放到 32×32，每个 2×2 块必然同色。"""
    TEMP.mkdir(parents=True, exist_ok=True)
    from PIL import Image
    import check_assets
    small = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    sp = small.load()
    for i in range(16):
        sp[i, i] = (200, 190, 172, 255)          # 一条对角线，保证不是纯色
    big = small.resize((32, 32), Image.NEAREST)
    fake = TEMP / "upscaled.png"
    big.save(fake)
    check_assets._FAILS.clear()
    check_assets.check_upscaled("upscaled.png", Image.open(fake).convert("RGBA"))
    caught = bool(check_assets._FAILS)
    check_assets._FAILS.clear()
    fake.unlink(missing_ok=True)
    return caught, "认出放大件" if caught else "**没认出来**：2×2 判据失效了"


def case_solid_not_upscaled() -> tuple[bool, str]:
    """纯色图不能被判成放大件 —— 否则守卫会拦下自己生成的占位件，然后被绕过。"""
    TEMP.mkdir(parents=True, exist_ok=True)
    from PIL import Image
    import check_assets
    solid = TEMP / "solid.png"
    Image.new("RGBA", (16, 16), (110, 100, 92, 255)).save(solid)
    check_assets._FAILS.clear()
    check_assets.check_upscaled("solid.png", Image.open(solid).convert("RGBA"))
    misjudged = bool(check_assets._FAILS)
    check_assets._FAILS.clear()
    solid.unlink(missing_ok=True)
    return not misjudged, "纯色图未被误判" if not misjudged else "**误判了**：会拦下自己的占位件"


# ── texture_filter 覆盖守卫（`ENG-13`）─────────────────────────────────
# 直接撞 check_assets.check_texture_filter：真包里没有覆盖（基线干净），也不该为自证往
# 场景或代码里塞一个。两条形状 —— 拦得住真覆盖、不误判继承与比较。
def case_texture_filter_override() -> tuple[bool, str]:
    """`.tscn` 里 `texture_filter=2`（Linear）与 `.cs` 里 `.TextureFilter=` 赋值都要被拦（`ENG-13`）。"""
    import check_assets
    check_assets._FAILS.clear()
    check_assets.check_texture_filter("fake.tscn", "[node name=\"X\"]\ntexture_filter = 2\n")
    tscn_caught = bool(check_assets._FAILS)
    check_assets._FAILS.clear()
    check_assets.check_texture_filter("fake.cs", "sprite.TextureFilter = TextureFilterEnum.Linear;")
    cs_caught = bool(check_assets._FAILS)
    check_assets._FAILS.clear()
    ok = tscn_caught and cs_caught
    return ok, (".tscn 与 .cs 覆盖都拦下" if ok
                else f"**漏了**：tscn_caught={tscn_caught}、cs_caught={cs_caught}")


def case_texture_filter_inherit_ok() -> tuple[bool, str]:
    """继承（0）与比较（`== ParentNode`）不能被误判成覆盖 —— 否则 `CameraProbe` 的核会被自己拦下。"""
    import check_assets
    check_assets._FAILS.clear()
    check_assets.check_texture_filter("fake.tscn", "[node name=\"X\"]\ntexture_filter = 0\n")
    check_assets.check_texture_filter(
        "fake.cs", "if (camera.TextureFilter == TextureFilterEnum.ParentNode) { }")
    misjudged = list(check_assets._FAILS)
    check_assets._FAILS.clear()
    return (not misjudged), ("继承与比较未被误判" if not misjudged
                             else f"**误判了**：{misjudged}")


# ── 不必跑 verify.py 全流程的用例（直接撞解析器与落点）────────────────
def case_bad_pck_format() -> tuple[bool, str]:
    """把包格式版本改成没见过的值：必须报错，不许猜着解。"""
    TEMP.mkdir(parents=True, exist_ok=True)
    target = TEMP / "bad-format.pck"
    data = bytearray(verify.EXPORT_PCK.read_bytes())
    data[4:8] = (99).to_bytes(4, "little")
    target.write_bytes(data)
    code, out = _run([sys.executable, str(ROOT / "tools" / "verify.py"),
                      "--manifest", str(target)], ROOT)
    ok = code != 0 and "包格式版本是 99" in out
    return ok, f"退出码 {code}；" + _first_problem(out)


def case_truncated_pck() -> tuple[bool, str]:
    """把包截断：目录读不全时必须抛错，**绝不能**返回空清单被读成「0 条泄漏」。"""
    TEMP.mkdir(parents=True, exist_ok=True)
    target = TEMP / "truncated.pck"
    data = verify.EXPORT_PCK.read_bytes()
    target.write_bytes(data[: len(data) // 2])
    code, out = _run([sys.executable, str(ROOT / "tools" / "verify.py"),
                      "--manifest", str(target)], ROOT)
    ok = code != 0 and "读不出包内清单" in out and "泄漏 0 条" not in out
    return ok, f"退出码 {code}；" + _first_problem(out)


def case_wrong_godot() -> tuple[bool, str]:
    """环境变量指向一个不存在的 Godot：必须在跑导出之前就说清，而不是到导出才炸。"""
    env = dict(os.environ)
    env[verify.GODOT_ENV] = str(TEMP / "没有这个文件.exe")
    done = subprocess.run(
        [sys.executable, str(ROOT / "tools" / "verify.py"), "--upto", "export"],
        cwd=str(ROOT), stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        timeout=1800, env=env,
    )
    out, _ = verify.decode_output(done.stdout or b"")
    ok = done.returncode != 0 and "那里没有文件" in out
    return ok, f"退出码 {done.returncode}；" + _first_problem(out)


def case_wrong_root() -> tuple[bool, str]:
    """把入口搬到一个不是代码仓的目录：必须当场拒绝，而不是去验一个空目录。

    这正是「两仓各有一个 tools/ 会不会混」那个担心的可执行形态。
    """
    fake = TEMP / "wrong-place"
    (fake / "tools").mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / "tools" / "verify.py", fake / "tools" / "verify.py")
    # verify.py 有同目录依赖（loglib），不一起拷贝的话它会先 ModuleNotFoundError 崩掉、
    # 根本走不到 check_root，这条用例就测不到它本该测的东西。新增 verify.py 的同目录
    # 依赖时，这里要跟着加。
    shutil.copy2(ROOT / "tools" / "loglib.py", fake / "tools" / "loglib.py")
    code, out = _run([sys.executable, str(fake / "tools" / "verify.py"), "--upto", "build"], fake)
    ok = code != 0 and "不像代码仓根目录" in out
    return ok, f"退出码 {code}；" + _first_problem(out)


# ── 发行素材守卫（`ENG-12`）──────────────────────────────────────────
# release 那条用真包撞：EXPORT_PCK 现在就含 28 个「可进发行包=false」的素材，一条判据两头证 ——
# release 拦得住、日常不误杀。未登记与「字体按字段而非路径」两条用合成清单直接撞审计函数：
# 真包里既没有未登记素材，也不该为了自证往仓库里塞一个。
def case_release_blocks_placeholders() -> tuple[bool, str]:
    """真发行包里就有占位件与下载件：--release 判失败，日常放行（`ENG-12`）。"""
    if not verify.EXPORT_PCK.is_file():
        return False, "没有 EXPORT_PCK 可撞"
    rel_clean, rel_msg, _ = verify.manifest_report(verify.EXPORT_PCK, release=True)
    day_clean, _, _ = verify.manifest_report(verify.EXPORT_PCK, release=False)
    ok = (not rel_clean) and day_clean
    why = "release 拦下、日常放行" if ok else f"release_clean={rel_clean}、day_clean={day_clean}"
    return ok, why + f"；release 首条：{rel_msg[0][:80] if rel_msg else '无'}"


def case_unregistered_in_pack() -> tuple[bool, str]:
    """包里出现登记表没有的 assets/ 素材 → 审计报未登记（漏登记等于绕过守卫，`ENG-12`）。"""
    reg = verify.load_asset_registry()
    ghost = verify.PackEntry("assets/placeholder/ui/zz-ghost.png.import", 0, 100, 0)
    audit = verify.audit_release_assets([ghost], reg)
    ok = audit.unregistered == ["placeholder/ui/zz-ghost.png"]
    return ok, f"未登记={audit.unregistered}" if ok else f"**没抓到**：{audit}"


def case_font_shippable_by_field() -> tuple[bool, str]:
    """字体在 assets/fonts/ 下，但按「可进发行包」字段判是可进包的 —— 按路径判会误杀（`ENG-12`）。"""
    reg = verify.load_asset_registry()
    e = verify.PackEntry(
        "assets/fonts/fusion-pixel-12px-proportional-zh_hans.ttf.import", 0, 100, 0)
    audit = verify.audit_release_assets([e], reg)
    ok = (audit.shippable == ["fonts/fusion-pixel-12px-proportional-zh_hans.ttf"]
          and not audit.replaceable and not audit.unregistered)
    return ok, "字体归可进包" if ok else f"**归类错**：{audit}"


DIRECT_CASES = (
    ("包格式没见过时报错而不是猜", "parse_pck", "踩坑记录 33 的反面：解析跑偏不能报干净",
     case_bad_pck_format),
    ("包被截断时报错而不是报 0 条泄漏", "parse_pck", "同上", case_truncated_pck),
    ("入口被搬到非代码仓时拒绝执行", "check_root", "两仓各有一个 tools/ 的混淆风险",
     case_wrong_root),
    ("Godot 定位不到时在导出之前就说清", "locate_godot", "错的工具链要在早期暴露",
     case_wrong_godot),
    ("整图放大件被认出来", "check_upscaled", "16×16 放大成 32×32：尺寸对得上但没有信息量",
     case_upscaled_asset),
    ("纯色图不被误判成放大件", "check_upscaled", "误判方向：纯色图天然满足每个 2×2 同色",
     case_solid_not_upscaled),
    ("场景与代码里的 texture_filter 覆盖被拦下", "check_texture_filter",
     "ENG-13：.tscn=2 与 .cs 赋值都要拦", case_texture_filter_override),
    ("继承与比较不被误判成 texture_filter 覆盖", "check_texture_filter",
     "ENG-13：误判方向，== ParentNode 是在核不是在改", case_texture_filter_inherit_ok),
    ("发行档拦占位件、日常档放行", "manifest_report", "ENG-12：真包含 28 个非自绘素材",
     case_release_blocks_placeholders),
    ("包里未登记素材被审计抓出", "audit_release_assets", "ENG-12：漏登记等于绕过守卫",
     case_unregistered_in_pack),
    ("字体按可进发行包字段而非路径放行", "audit_release_assets",
     "ENG-12：按路径判会误杀 assets/fonts/ 下的永久依赖", case_font_shippable_by_field),
)

CASES = (
    Case("构建有编译错误时判失败", "step_build", "构建步骤的失败方向",
         inject_broken_rule, lambda: _drop(BROKEN_RULE),
         ["--upto", "build"], "错误"),
    Case("源码里有测试没被编译进去时判失败", "step_test", "踩坑记录 29",
         inject_phantom_test, lambda: _drop(PHANTOM_TEST),
         ["--upto", "test"], "条数对不上"),
    Case("有测试真失败时判失败", "step_test", "测试步骤的失败方向",
         inject_failing_test, lambda: _drop(FAILING_TEST),
         ["--upto", "test"], "失败 1"),
    Case("条数静态数不出来时明说而不是少算", "expected_test_count", "踩坑记录 29 的变体",
         inject_uncountable_test, lambda: _drop(UNCOUNTABLE_TEST),
         ["--upto", "test"], "静态数不出用例条数"),
    Case("子工程缺 .gdignore 导致包内泄漏时判失败", "step_export", "踩坑记录 33",
         inject_missing_gdignore, restore_gdignore,
         ["--upto", "export"], "泄漏"),
    Case("包里有字体却没有许可证时判失败", "manifest_report",
         "ART-3：OFL 第 2 条要求每份拷贝都带许可证，而漏了不报错",
         inject_font_without_license, restore_presets,
         ["--upto", "export"], "OFL 第 2 条"),
    Case("素材有半透明像素时判失败", "step_assets", "像素绘制原则 §9 的绝对规则",
         inject_semi_transparent, restore_semi_transparent,
         ["--upto", "assets"], "半透明像素"),
    Case("素材漏登记时判失败", "step_assets", "漏登记等于绕过守卫",
         inject_unregistered_asset, restore_unregistered_asset,
         ["--upto", "assets"], "登记表里没有"),
    Case("精灵表帧数与登记不符时判失败", "step_assets", "切帧算错，动画会错位",
         inject_wrong_frame_count, restore_registry,
         ["--upto", "assets"], "帧"),
    Case("纹理被设成可转 VRAM 压缩时判失败", "step_assets", "像素图被有损压缩的入口",
         inject_vram_compression, restore_import,
         ["--upto", "assets"], "detect_3d"),
    Case("产物跑不起来时判失败", "step_smoke", "冒烟步骤的失败方向",
         inject_broken_config, restore_config,
         [], "跑产物"),
)


# ── 执行 ──────────────────────────────────────────────────────────────
def _run(cmd: list[str], cwd: Path) -> tuple[int, str]:
    """自己拿 bytes 再 decode，不过 shell 管道（踩坑记录 27）。"""
    done = subprocess.run(cmd, cwd=str(cwd), stdout=subprocess.PIPE,
                          stderr=subprocess.STDOUT, timeout=1800)
    text, _ = verify.decode_output(done.stdout or b"")
    return done.returncode, text


def _first_problem(out: str) -> str:
    for line in out.splitlines():
        if line.startswith("[FAIL]"):
            return line.strip()[:160]
    return "（输出里没有 [FAIL] 行）"


def run_case(case: Case) -> tuple[bool, str]:
    try:
        case.inject()
        code, out = _run([sys.executable, str(ROOT / "tools" / "verify.py"), *case.args], ROOT)
    finally:
        # 还原必须发生，哪怕上面抛了 —— 自证工具把仓库留在坏状态是不可接受的。
        case.restore()
    hit = case.expect_in_output in out
    ok = code == case.expect_exit and hit
    detail = f"退出码 {code}（期望 {case.expect_exit}）；"
    detail += "命中期望字样" if hit else f"未出现期望字样 {case.expect_in_output!r}"
    return ok, detail + "；" + _first_problem(out)


TRACKED_JUDGEMENTS = ("parse_pck", "check_root", "locate_godot", "expected_test_count",
                      "manifest_report", "audit_release_assets")


def coverage(names: list[str]) -> tuple[list[str], list[str]]:
    """自报覆盖量：verify.py 里每个步骤与关键判定，是否都有用例撞过。

    口径是**按函数**，不是按分支。没有用例的分支逐条记在 UNPROVEN_BRANCHES 里，
    末尾会一起打出来 —— 把「6 项都覆盖了」说成「每条路径都撞过」是夸大。
    """
    targets = [n for n in dir(verify) if n.startswith("step_")] + list(TRACKED_JUDGEMENTS)
    covered = sorted(set(names))
    missing = [t for t in sorted(targets) if t not in covered and t not in COVERAGE_EXEMPT]
    return covered, missing


def main() -> int:
    ap = argparse.ArgumentParser(description="verify.py 的自证（ENG-3）")
    ap.add_argument("--list", action="store_true", help="只列用例与覆盖登记，不执行")
    args = ap.parse_args()

    all_names = [c[1] for c in DIRECT_CASES] + [c.covers for c in CASES]
    covered, missing = coverage(all_names)

    if args.list:
        for name, covers, shape, _ in DIRECT_CASES:
            say(f"  {covers:<20} {name}  ← {shape}")
        for c in CASES:
            say(f"  {c.covers:<20} {c.name}  ← {c.shape}")
        say(f"\n覆盖量（口径：按函数）：{len(DIRECT_CASES) + len(CASES)} 条用例覆盖 "
            f"{len(covered)} 项；登记项里未覆盖 {missing or '无'}")
        say(f"已知未自证的分支 {len(UNPROVEN_BRANCHES)} 条：")
        for branch, why in UNPROVEN_BRANCHES.items():
            say(f"  - {branch}：{why}")
        return 0

    if not verify.EXPORT_PCK.is_file():
        say("[..]   先跑一次完整验收，好让解析器用例有真包可撞")
        code, _ = _run([sys.executable, str(ROOT / "tools" / "verify.py")], ROOT)
        if code != 0:
            say("[FAIL] 基线那一轮验收就没过，先修它再自证")
            path = log_path()
            say(f"日志 {path.relative_to(ROOT)}")
            flush_log(path)
            say("EXIT=1")
            return 1

    case_fails: list[str] = []      # 用例没按预期拦下
    meta_fails: list[str] = []      # 复验、清理、覆盖登记这类
    total = 0

    for name, covers, _shape, fn in DIRECT_CASES:
        total += 1
        try:
            ok, detail = fn()
        except Exception as exc:                          # noqa: BLE001
            ok, detail = False, f"用例本身抛了：{exc!r}"
        say(f"{'[OK]  ' if ok else '[FAIL]'} {covers} · {name} —— {detail}")
        if not ok:
            case_fails.append(f"{covers} · {name}")

    for case in CASES:
        total += 1
        try:
            ok, detail = run_case(case)
        except Exception as exc:                          # noqa: BLE001
            ok, detail = False, f"用例本身抛了：{exc!r}"
        say(f"{'[OK]  ' if ok else '[FAIL]'} {case.covers} · {case.name} —— {detail}")
        if not ok:
            case_fails.append(f"{case.covers} · {case.name}")

    # 还原后复验：注入全撤了，完整验收必须重新变绿。少了这一步，就无法区分
    # 「守卫拦得住」和「我把仓库改坏了所以什么都过不了」。
    say("[..]   还原后复验：重跑一次完整验收")
    code, out = _run([sys.executable, str(ROOT / "tools" / "verify.py")], ROOT)
    if code == 0 and "EXIT=0" in out:
        say("[OK]   还原后复验通过，仓库回到干净状态")
    else:
        say(f"[FAIL] 还原后复验没过（退出码 {code}）：{_first_problem(out)}")
        meta_fails.append("还原后复验")

    if TEMP.exists():
        shutil.rmtree(TEMP, ignore_errors=True)
    if TEMP.exists():
        say(f"[FAIL] 临时目录没删掉：{TEMP}")
        meta_fails.append("临时目录清理")
    else:
        say(f"[OK]   已清理临时目录 {TEMP}")

    say(f"\n覆盖量（口径：按函数，每项至少有一条用例让它真的判失败过）："
        f"{total} 条用例覆盖 {len(covered)} 项 —— {'、'.join(covered)}")
    if missing:
        say(f"[FAIL] 有判定既没有自证用例、也没写豁免理由：{missing}")
        meta_fails.append(f"未覆盖 {missing}")
    say(f"已知未自证的分支 {len(UNPROVEN_BRANCHES)} 条（各有理由，不是遗漏）：")
    for branch, why in UNPROVEN_BRANCHES.items():
        say(f"  - {branch}：{why}")

    fails = case_fails + meta_fails
    say(f"\n结果：{total - len(case_fails)}/{total} 条用例按预期拦下"
        f"／{len(fails)} 项必须修复")
    if fails:
        say("[FAIL] " + "；".join(fails))
    else:
        say("[OK] 登记的每项判定都用真实缺陷形状撞过，且还原后复验通过")
    # 日志最后写：交互终端实测会截断长输出（踩坑记录 20），结论得在文件里读得全。
    path = log_path()
    say(f"日志 {path.relative_to(ROOT)}")
    say(f"EXIT={1 if fails else 0}")
    flush_log(path)
    return 1 if fails else 0


if __name__ == "__main__":
    raise SystemExit(main())
