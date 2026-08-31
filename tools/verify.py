#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""代码仓验收总入口：构建 → 测试 → 导出 → 跑产物，四步串成一条命令。

为什么存在（`ENG-3`）：这四步此前是四条各自独立的命令，人工串有三种漏法 —— 漏跑一步、
跑在旧产物上、**看退出码就当过了**。后一种本项目已经撞过两次，都记在设计仓
`reference/踩坑记录.md`：

- 第 29 条：测试摘要打「Passed」，可其中一个测试压根没跑，`Total` 从 2 悄悄变成 1。
- 第 33 条：导出正常退出，却把 `rules/**/*.cs` 与 `obj/project.assets.json` 打进了发行包，
  资源包 182 KB 而该发行的只有 8.5 KB。**泄漏不会报错。**

所以本入口的重点不是省几次敲键盘，而是**每一步都另找一个量具核对产物**：

    构建    退出码 + 自己数错误行，认不出输出形状就判失败（不静默放过）
    测试    运行器报的条数 ≙ 从测试源码静态数出来的条数（两个独立来源必须相等）
    导出    先清空 export/ 再导 → 产物必须存在 → **解开 .pck 逐条看清单**查泄漏
    跑产物  真启动导出的 exe，从日志确认 C# 侧跑到了内容载入完成

门禁只调本入口。用法（从**代码仓根目录**运行）：

    python tools/verify.py               # 四步全跑，这是门禁用的形态
    python tools/verify.py --upto test   # 只跑到测试；前置步骤一定跟着跑，跑不出旧产物
    python tools/verify.py --manifest    # 不跑任何步骤，只把现有 .pck 的包内清单打出来
    python tools/verify.py --manifest <某个.pck>   # 看指定的包

改了本脚本就跑 `python tools/selfcheck_verify.py` 自证（WORKFLOW §6）：它逐条注入真实
缺陷形状、确认拦得住、还原后复验，并自报覆盖了哪几步。

输出约定（与设计仓 `tools/check_docs.py` 一致）：固定 UTF-8；标准输出**每步只有一行**，
逐步的完整日志落盘到 `logs/verify/<时间戳>/`，同目录写一份带起止时间戳的 `summary.md`；
末尾打覆盖量、结果与一行 `EXIT=`。日志由本脚本自己写 UTF-8，不靠 shell 重定向。

Godot 可执行文件的定位顺序：环境变量 `TINDERHEARTH_GODOT` → `PATH` → 已知安装根下按
`project.godot` 声明的引擎小版本搜 mono 版控制台 exe。找不到就判失败并给出补救命令。
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import struct
import subprocess
import sys
import time
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    # 被钩子或重定向调用时默认编码可能不是 UTF-8，打第一个中文就崩。
    sys.stdout.reconfigure(encoding="utf-8")

from loglib import prune_runs, KEEP_RUNS  # noqa: E402  同目录工具（跑完修剪旧日志）

ROOT = Path(__file__).resolve().parent.parent
LOG_ROOT = ROOT / "logs" / "verify"
EXPORT_DIR = ROOT / "export"
EXPORT_PRESET = "Windows Desktop"
EXPORT_EXE = EXPORT_DIR / "Tinderhearth-The-Last-Class.exe"
EXPORT_PCK = EXPORT_DIR / "Tinderhearth-The-Last-Class.pck"
TESTS_DIR = ROOT / "tests"
MAIN_SCAFFOLD = ROOT / "src" / "Main.cs"

# 落点自检要看到的文件。两仓各有一个 tools/，靠名字区分不够可靠 —— 让入口自己拒绝
# 在错的位置跑，才是能自动检出的执行体。
ROOT_MARKERS = (
    "project.godot",
    "Tinderhearth-The-Last-Class.sln",
    "export_presets.cfg",
    "tests",
)

# ── Godot 定位 ────────────────────────────────────────────────────────
GODOT_ENV = "TINDERHEARTH_GODOT"
# 本机安装根。setup_godot.py（设计仓）默认装到这里；换机器用环境变量覆盖，不改代码。
GODOT_SEARCH_ROOTS = (Path(r"D:\godot\4-7"),)
GODOT_PATH_NAMES = ("godot", "godot4", "Godot_v4.7.2-stable_mono_win64_console")

# ── 测试（踩坑记录 29）────────────────────────────────────────────────
# 期望条数不写死成常量，从测试源码数出来。死数字已经烂过一次：ADR-0007 里记的是 8 条，
# 现在实际 10 条 —— 写死就得每加一个测试改一次，忘了改就只能靠人判断「这次是该改还是
# 真少了」，而那正是第 29 条放过测试的那种判断。
TEST_ATTR_RE = re.compile(r"\[\s*(Fact|Theory|InlineData|MemberData|ClassData)\b")
BLOCK_COMMENT_RE = re.compile(r"/\*.*?\*/", re.DOTALL)

# ── 包内清单（踩坑记录 33）────────────────────────────────────────────
PCK_MAGIC = 0x43504447  # "GDPC"
# 只认实测过的包格式版本。Godot 4.7.2 导出的是 4，布局如 parse_pck() 注释所记。
# 换了版本就**报错**而不是猜着解 —— 猜错的解析会把「压根没看」伪装成「没有泄漏」，
# 那正是最坏的失效方向。
PCK_FORMAT_KNOWN = 4

# ── 随发行必须附带的东西（`ART-3`）────────────────────────────────────
# OFL 第 2 条要求**每份拷贝**都带许可证与版权声明。字体进了发行包，许可证就必须跟着进去，
# 而漏了不会报错 —— 只会变成上架后的法律问题。所以这里两头都判：
#   1. 许可证是**允许**出现的（否则会被下面「文档与脚本」那条当泄漏拦掉）；
#   2. 包里出现字体数据时，许可证**必须**在，否则判失败。
# ADR-0008 另钉了第 3 条：将来做了子集化或改字形，输出的字体名不得沿用原名 ——
# 那件事机器判不了（要读字体的 name 表并与「有没有改过」比对），只能人工验收，
# 已写在 ADR-0008 的「落地要求」与素材登记表的授权那一栏里。
FONT_DATA_RE = re.compile(r"\.(?:fontdata|ttf|otf)$", re.IGNORECASE)
# 允许随发行附带的非资源文件，**连理由一起登记**。不登记的话「该带的」与「漏出去的」分不开。
BUNDLED_FILES = {
    "assets/fonts/LICENSE-OFL.txt":
        "SIL OFL 1.1 第 2 条：字体的每份拷贝都必须带许可证与版权声明（ART-3）",
}

# 不该出现在发行包里的东西。命名说清是哪一类，报错才指得回踩坑记录那一条。
LEAK_RULES = (
    ("构建中间产物", re.compile(r"(^|/)(obj|bin)/", re.IGNORECASE)),
    ("依赖与还原清单", re.compile(r"\.(deps|assets|runtimeconfig)\.json$", re.IGNORECASE)),
    ("工程与解决方案文件", re.compile(r"\.(csproj|sln|user|props|targets)$", re.IGNORECASE)),
    ("NuGet 配置", re.compile(r"(^|/)(nuget\.config|packages\.lock\.json)$", re.IGNORECASE)),
    ("版本库元数据", re.compile(r"(^|/)\.git", re.IGNORECASE)),
    ("文档与脚本", re.compile(r"\.(md|py|log|txt|ps1|sh)$", re.IGNORECASE)),
)
# `.cs` 单独判，因为它有一种**合法**形态。实测（见 3-export-manifest.txt）：引擎层的
# `src/**/*.cs` 一定在包里，但预设的 `dotnet/include_scripts_content=false` 让它们只是
# 1 字节空占位 —— Godot 需要这些 CSharpScript 条目存在，内容并不发行。
# 所以判据是两条，都对着踩坑记录 33 记下的真实缺陷形状：
#   1. `src/` 以外的 `.cs`（`rules/`、`tests/`）出现 = 子工程被扫进来了，`.gdignore` 没了；
#   2. 任何 `.cs` 带内容 = `include_scripts_content` 被打开了，源码真的在发行包里。
CS_PLACEHOLDER_MAX_BYTES = 1
CS_ALLOWED_PREFIX = "src/"

# 包里必须有的东西。只查「没有它就一定不是个能跑的包」的那几条，不做内容清单登记
# （那要等素材登记表存在，归 `ENG-10`）。启动场景从 project.godot 读，不在这里写死 ——
# 脚手架被替换时不该还得回来改一次。
REQUIRED_PACK_ENTRIES = ("project.binary",)
REQUIRED_PACK_PREFIXES = ("data/",)

# ── 跑产物 ────────────────────────────────────────────────────────────
# 这几个标记钉在 src/Main.cs 那个临时脚手架上。脚手架被真正的启动流程替换时，这里要
# 一起改 —— 所以下面 verify_smoke_markers() 会先确认它们还在源码里，脱节就当场判失败，
# 而不是等到冒烟阶段报一个看不懂的「没找到标记」。
SMOKE_MARKERS = ("[启动] 引擎 ", "[启动] 名册容量 ", "：在册 ")
SMOKE_ERROR_MARKERS = ("ERROR:", "SCRIPT ERROR:", "USER ERROR:", "Unhandled exception")
SMOKE_FRAMES = 60  # --quit-after 的帧数；够 _Ready 跑完并把日志写出来

STEPS = ("assets", "build", "test", "export", "smoke")
STEP_TITLES = {
    "assets": "素材",
    "build": "构建",
    "test": "测试",
    "export": "导出",
    "smoke": "跑产物",
}


# ── 输出与日志 ────────────────────────────────────────────────────────
def ensure_logs_hidden_from_godot() -> None:
    """保证 `logs/` 有 `.gdignore`。

    为什么这条不能省：`logs/` 长在 `res://` 里面，Godot 的资源扫描会看见它。踩坑记录 33
    就是「`res://` 下的子目录被扫进发行包」，本入口自己往仓库里加目录，不能反倒成为
    下一次泄漏的来源。`logs/` 不入库（`.gitignore` 忽略），所以这个 `.gdignore` 只能现场
    补，不能靠提交 —— `selfcheck_verify.py` 写日志时也调它。
    """
    LOG_ROOT.parent.mkdir(parents=True, exist_ok=True)
    guard = LOG_ROOT.parent / ".gdignore"
    if not guard.exists():
        guard.write_text("", encoding="utf-8")


@dataclass
class StepResult:
    name: str
    ok: bool
    headline: str                       # 打屏那一行的正文
    seconds: float = 0.0
    skipped: bool = False
    details: list[str] = field(default_factory=list)   # 写进 summary.md 的要点
    log_names: list[str] = field(default_factory=list)


class Report:
    """打屏每步一行；详细日志逐步落盘；末尾写带起止时间戳的运行摘要。"""

    def __init__(self, log_dir: Path) -> None:
        self.log_dir = log_dir
        self.results: list[StepResult] = []
        self.notes: list[str] = []      # 覆盖量自报，不是问题但必须打出来
        self.fails: list[str] = []

    def say(self, text: str = "") -> None:
        print(text, flush=True)

    def note(self, text: str) -> None:
        self.notes.append(text)

    def fail(self, text: str) -> None:
        self.fails.append(text)

    def ensure_log_dir(self) -> None:
        self.log_dir.mkdir(parents=True, exist_ok=True)
        ensure_logs_hidden_from_godot()

    def write_log(self, name: str, text: str) -> str:
        """日志自己写 UTF-8，不经 shell 重定向（WORKFLOW §5）。"""
        self.ensure_log_dir()
        (self.log_dir / name).write_text(text, encoding="utf-8", newline="\n")
        return name

    def finish_step(self, result: StepResult) -> None:
        self.results.append(result)
        tag = "[SKIP]" if result.skipped else ("[OK]  " if result.ok else "[FAIL]")
        cost = "" if result.skipped else f"，{result.seconds:.1f}s"
        self.say(f"{tag} {STEP_TITLES[result.name]} {result.headline}{cost}")
        if not result.ok and not result.skipped:
            self.fail(f"{STEP_TITLES[result.name]}：{result.headline}")


# ── 子进程与解码 ──────────────────────────────────────────────────────
def decode_output(raw: bytes) -> tuple[str, str]:
    """拿 bytes 自己 decode，不走 shell 管道（踩坑记录 27）。

    .NET 与 Godot 重定向时都写 UTF-8，所以先按 UTF-8 严格解；解不动才退到本机代码页，
    并把**用了哪个编码**一起返回写进日志 —— 悄悄 replace 出一堆乱码比报错更坏。
    """
    for enc in ("utf-8", "cp936", "mbcs"):
        try:
            return raw.decode(enc), enc
        except (UnicodeDecodeError, LookupError):
            continue
    return raw.decode("utf-8", errors="replace"), "utf-8/replace（有解不出的字节）"


def run(cmd: list[str], cwd: Path, timeout: int) -> tuple[int, str, str]:
    """返回 (退出码, 合并后的输出, 用的编码)。超时与启动失败都折成非零退出码。"""
    env = dict(os.environ)
    # 钉住 CLI 语言：解析要稳定就得钉住它说哪国话，否则同一份脚本在不同机器上认不出
    # 同一句「Build succeeded」。这只影响日志语言，不影响构建结果。
    env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
    try:
        done = subprocess.run(
            cmd, cwd=str(cwd), stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=timeout, env=env,
        )
    except subprocess.TimeoutExpired as exc:
        text, enc = decode_output(exc.output or b"")
        return 124, f"{text}\n\n[verify.py] 超时：{timeout}s 未结束", enc
    except OSError as exc:
        return 126, f"[verify.py] 启动失败：{exc}", "n/a"
    text, enc = decode_output(done.stdout or b"")
    return done.returncode, text, enc


# ── 落点与工具链 ──────────────────────────────────────────────────────
def check_root(rep: Report) -> bool:
    missing = [m for m in ROOT_MARKERS if not (ROOT / m).exists()]
    if missing:
        rep.say(f"[FAIL] 落点 {ROOT} 缺 {missing}，这里不像代码仓根目录")
        rep.say("       本入口只验收代码仓；设计仓的检查是 python tools/check_docs.py")
        rep.fail(f"落点不对：{ROOT} 缺 {missing}")
        return False
    rep.say(f"[..]   落点 {ROOT}")
    return True


def engine_minor() -> str | None:
    """从 project.godot 取引擎小版本，不在脚本里再写死一个版本号。"""
    text = (ROOT / "project.godot").read_text(encoding="utf-8")
    m = re.search(r'config/features\s*=\s*PackedStringArray\(([^)]*)\)', text)
    if not m:
        return None
    versions = re.findall(r'"(\d+\.\d+)"', m.group(1))
    return versions[0] if versions else None


def godot_banner(exe: Path) -> str:
    """问它自己是什么版本。定位到之后必须核一遍，理由见 locate_godot()。"""
    code, out, _ = run([str(exe), "--version"], ROOT, timeout=120)
    if code != 0:
        return f"（--version 退出码 {code}）"
    lines = [ln.strip() for ln in out.splitlines() if ln.strip()]
    return lines[-1] if lines else "（无输出）"


def locate_godot() -> tuple[Path | None, str]:
    """找到本机的 Godot，并**核实它是对的那个**。返回 (路径, 说明或失败原因)。

    为什么定位到还要核一遍：`PATH` 上的 `godot` 很可能是标准版（非 .NET）或另一个小版本。
    标准版编不了 C#，届时会在导出阶段报一句与真实原因无关的错。让它自己报版本号，当场
    对不上就当场说清 —— 这比省一次 1 秒的调用值。
    """
    minor = engine_minor()
    if not minor:
        return None, "project.godot 里读不出 config/features 的引擎版本，无法核对 Godot"

    candidates: list[Path] = []
    override = os.environ.get(GODOT_ENV)
    if override:
        p = Path(override)
        if not p.is_file():
            return None, f"环境变量 {GODOT_ENV} 指向 {p}，那里没有文件"
        candidates.append(p)
    else:
        candidates += [Path(f) for name in GODOT_PATH_NAMES if (f := shutil.which(name))]
        for root in GODOT_SEARCH_ROOTS:
            if root.is_dir():
                candidates += sorted(root.rglob(f"Godot_v{minor}*mono*_console.exe"))

    rejected: list[str] = []
    for exe in candidates:
        banner = godot_banner(exe)
        if banner.startswith(minor) and "mono" in banner:
            return exe, banner
        rejected.append(f"{exe.name} → {banner}")

    why = f"没有能用的 Godot：需要 {minor}.x 的 mono（.NET）版"
    if rejected:
        why += f"，试过但对不上的有 {rejected}"
    else:
        why += "，本机一个候选都没找到"
    why += (f"。要么设环境变量 {GODOT_ENV} 指到它，"
            f"要么在设计仓跑 python tools/setup_godot.py --version {minor}.x")
    return None, why


# ── 步骤 0：素材（`ENG-10`。它排在构建之前，见 step_assets 的注释）────
BUILD_OK_RE = re.compile(r"Build succeeded|生成成功")
BUILD_BAD_RE = re.compile(r"Build FAILED|生成失败")
BUILD_COUNT_RE = re.compile(r"^\s*(\d+)\s+(Warning|Error)\(s\)\s*$", re.MULTILINE)
DIAG_RE = re.compile(r"\b(error|warning) [A-Z]{2}\d{4}\b")


def step_assets(rep: Report) -> StepResult:
    """素材守卫（`ENG-10`）：半透明像素、放大件、登记表比对、纹理导入参数。

    为什么排在最前面：它最快（纯 Python，没有编译与引擎启动），而且**坏素材不该有机会被
    打进包** —— 放在导出之后才查，等于每次都先花十几秒造一个已知有问题的产物。

    判定不只看退出码：认不出 `check_assets.py` 的输出形状同样拒绝判过（WORKFLOW §7）。
    """
    started = time.perf_counter()
    code, out, enc = run([sys.executable, str(ROOT / "tools" / "check_assets.py")],
                         ROOT, timeout=600)
    rep.write_log("0-assets.log", f"# 编码 {enc}\n# 退出码 {code}\n\n{out}")
    cost = time.perf_counter() - started

    gauge = next((ln for ln in out.splitlines() if ln.startswith("覆盖量：")), "")
    if code != 0:
        first = next((ln for ln in out.splitlines() if ln.startswith("[FAIL]")), "详见日志")
        return StepResult("assets", False, f"失败：{first.removeprefix('[FAIL] ')}",
                          cost, log_names=["0-assets.log"],
                          details=[gauge] if gauge else [])
    if not gauge:
        return StepResult("assets", False, "认不出 check_assets.py 的输出形状，拒绝判过",
                          cost, log_names=["0-assets.log"])
    return StepResult("assets", True, gauge.removeprefix("覆盖量："), cost,
                      log_names=["0-assets.log"],
                      details=[f"命令 tools/check_assets.py（输出编码 {enc}）"])


# ── 步骤 1：构建 ──────────────────────────────────────────────────────
def step_build(rep: Report) -> StepResult:
    started = time.perf_counter()
    code, out, enc = run(["dotnet", "build"], ROOT, timeout=900)
    rep.write_log("1-build.log", f"# 编码 {enc}\n# 退出码 {code}\n\n{out}")
    cost = time.perf_counter() - started

    counts = {kind: int(n) for n, kind in BUILD_COUNT_RE.findall(out)}
    shape_known = bool(BUILD_OK_RE.search(out) or BUILD_BAD_RE.search(out) or counts)
    # 优先用 MSBuild 自己报的数；它不打（终端记录器换了形态之类）才退回数诊断行。
    # 退路是**近似**的：MSBuild 会把同一条诊断在工程输出和末尾摘要里各打一次，所以可能
    # 翻倍。这不影响判定 —— 通过与否只看「有没有错误」，不看错误正好几条。
    diag = DIAG_RE.findall(out)
    errors = counts.get("Error", diag.count("error"))
    warnings = counts.get("Warning", diag.count("warning"))

    if not shape_known:
        # 认不出输出形状就不许放过。退出码 0 不等于成功（WORKFLOW §7），而看不懂的
        # 输出连「显式摘要」都没有，此时判过等于闭着眼签字。
        return StepResult("build", False, "认不出 dotnet build 的输出形状，拒绝判过",
                          cost, log_names=["1-build.log"])
    if code != 0 or errors or BUILD_BAD_RE.search(out):
        return StepResult("build", False,
                          f"失败：退出码 {code}，{errors} 个错误（详见 1-build.log）",
                          cost, log_names=["1-build.log"],
                          details=[f"错误 {errors} 条，警告 {warnings} 条"])
    headline = f"退出码 0，{errors} 错误 {warnings} 警告"
    return StepResult("build", True, headline, cost, log_names=["1-build.log"],
                      details=[f"命令 dotnet build（输出编码 {enc}）"])


# ── 步骤 2：测试 ──────────────────────────────────────────────────────
def expected_test_count() -> tuple[int, int, str | None]:
    """从测试源码静态数出用例条数，返回 (条数, 扫到的文件数, 出错原因)。

    这是测试那一步的**第二个量具**：运行器自己报的数字来自它自己的发现流程，而发现
    流程正是踩坑记录 29 里失灵的那一环，拿它证明自己等于没证明。
    """
    files = [p for p in sorted(TESTS_DIR.rglob("*.cs"))
             if not any(part in ("bin", "obj") for part in p.parts)]
    facts = theories = inline = 0
    blockers: list[str] = []
    for path in files:
        text = BLOCK_COMMENT_RE.sub("", path.read_text(encoding="utf-8"))
        for line in text.splitlines():
            code_part = line.split("//", 1)[0]
            for kind in TEST_ATTR_RE.findall(code_part):
                if kind == "Fact":
                    facts += 1
                elif kind == "Theory":
                    theories += 1
                elif kind == "InlineData":
                    inline += 1
                else:
                    blockers.append(f"{path.relative_to(ROOT).as_posix()} 用了 [{kind}]")
    if blockers:
        # 静态数不出条数就明说数不出来，不静默少算 —— 少算会让「有测试没跑」照样通过。
        return 0, len(files), f"静态数不出用例条数：{blockers[:3]}；请改用 [InlineData] 或在此显式登记"
    if theories and not inline:
        return 0, len(files), f"有 {theories} 个 [Theory] 却一行 [InlineData] 都没有，数不出条数"
    return facts + inline, len(files), None


TEST_FIELD_RES = {
    "total": re.compile(r"Total:\s*(\d+)"),
    "errors": re.compile(r"Errors:\s*(\d+)"),
    "failed": re.compile(r"Failed:\s*(\d+)"),
    "skipped": re.compile(r"Skipped:\s*(\d+)"),
    "not_run": re.compile(r"Not\s*Run:\s*(\d+)"),
}


def step_test(rep: Report) -> StepResult:
    started = time.perf_counter()
    expected, scanned, why = expected_test_count()
    rep.note(f"测试覆盖量：静态扫描 {scanned} 个测试源文件，数出 {expected} 条用例")
    if why:
        return StepResult("test", False, why, time.perf_counter() - started)

    # --no-build：跑刚构建出来的那个程序集，而不是让 dotnet 再决定要不要重编。
    # 前置步骤一定跑过（--upto 会把前置带上），所以这里不会撞上「还没构建」。
    code, out, enc = run(["dotnet", "run", "--project", "tests", "--no-build"],
                         ROOT, timeout=900)
    rep.write_log("2-test.log", f"# 编码 {enc}\n# 退出码 {code}\n# 期望条数 {expected}\n\n{out}")
    cost = time.perf_counter() - started

    got = {k: (int(m.group(1)) if (m := r.search(out)) else None)
           for k, r in TEST_FIELD_RES.items()}
    if got["total"] is None:
        return StepResult("test", False,
                          "认不出运行器的摘要（没有 Total: 字段），拒绝判过",
                          cost, log_names=["2-test.log"])

    parts = [f"报告 {got['total']} 条／期望 {expected} 条"]
    for key, label in (("failed", "失败"), ("errors", "错误"), ("skipped", "跳过"),
                       ("not_run", "未跑")):
        if got[key] is not None:
            parts.append(f"{label} {got[key]}")
    headline = "，".join(parts)

    bad: list[str] = []
    if got["total"] != expected:
        bad.append(f"条数对不上：报告 {got['total']}，源码里数出 {expected}")
    for key, label in (("failed", "失败"), ("errors", "错误"), ("not_run", "未跑")):
        if got[key]:
            bad.append(f"{label} {got[key]} 条")
    if code != 0:
        bad.append(f"退出码 {code}")
    if bad:
        return StepResult("test", False, f"{headline} —— {'；'.join(bad)}",
                          cost, log_names=["2-test.log"], details=bad)
    return StepResult("test", True, headline, cost, log_names=["2-test.log"],
                      details=[f"命令 dotnet run --project tests --no-build（输出编码 {enc}）",
                               "条数由测试源码静态数出，与运行器报告互相独立"])


# ── 包内清单 ──────────────────────────────────────────────────────────
class PckError(RuntimeError):
    pass


@dataclass
class PackEntry:
    path: str
    offset: int
    size: int
    flags: int


def parse_pck(path: Path) -> tuple[list[PackEntry], dict[str, int]]:
    """解开 .pck 读包内清单。

    为什么要自己解而不是看导出日志：日志的详细程度跟引擎版本和开关有关，而清单是**产物
    本身**。踩坑记录 33 那次泄漏没有任何报错，唯一能证明包干净的东西就是包里到底有什么。

    2026-08-30 用 Godot 4.7.2 的导出产物实测出的布局（格式版本 4）：

        u32  magic = "GDPC"
        u32  包格式版本（本机是 4）
        u32  引擎 major / u32 minor / u32 patch
        u32  pack_flags（实测 2 = 偏移相对 file_base）
        u64  file_base（实测 112，正好是文件头长度，也就是数据区起点）
        u64  **目录表的绝对偏移**（实测 7504，目录在文件末尾而不是紧跟文件头）
        零填充到 112 字节，数据区，最后是目录表：
        u32  条数，随后每条：u32 路径字节数（按 4 字节对齐补零）、路径、
             u64 偏移（相对 file_base）、u64 大小、16 字节 md5、u32 flags

    两处与常见说法不一样，写在这里免得下一个人再花一遍时间：**路径不带 `res://` 前缀**
    （存的是 `data/config/game.json` 这种），**目录表在文件尾**。

    解析失败一律抛异常，绝不返回空清单 —— 空清单会被下游读成「0 条泄漏」，那正是最坏
    的失效方式：守卫报告干净，其实压根没看。
    """
    data = path.read_bytes()
    pos = 0

    def u32() -> int:
        nonlocal pos
        if pos + 4 > len(data):
            raise PckError(f"读 u32 越界（偏移 {pos}／共 {len(data)} 字节）")
        v = struct.unpack_from("<I", data, pos)[0]
        pos += 4
        return v

    def u64() -> int:
        nonlocal pos
        if pos + 8 > len(data):
            raise PckError(f"读 u64 越界（偏移 {pos}／共 {len(data)} 字节）")
        v = struct.unpack_from("<Q", data, pos)[0]
        pos += 8
        return v

    def raw(n: int) -> bytes:
        nonlocal pos
        if pos + n > len(data):
            raise PckError(f"读 {n} 字节越界（偏移 {pos}／共 {len(data)} 字节）")
        b = data[pos:pos + n]
        pos += n
        return b

    magic = u32()
    if magic != PCK_MAGIC:
        raise PckError(f"文件头不是 GDPC（读到 {magic:#010x}），这不是独立的 .pck")
    fmt = u32()
    if fmt != PCK_FORMAT_KNOWN:
        raise PckError(
            f"包格式版本是 {fmt}，本脚本只实测过 {PCK_FORMAT_KNOWN}。"
            f"请先用 temp/ 下的一次性诊断确认新布局再改 parse_pck()，不要让它猜着解"
        )
    ver = (u32(), u32(), u32())
    pack_flags = u32()
    file_base = u64()
    dir_offset = u64()
    if not 0 < dir_offset < len(data):
        raise PckError(f"目录表偏移 {dir_offset} 落在 {len(data)} 字节的包外")

    pos = dir_offset
    count = u32()
    if count <= 0:
        raise PckError(f"包内条数为 {count}，不可能")

    entries: list[PackEntry] = []
    for i in range(count):
        n = u32()
        if not 0 < n < 4096 or n % 4:
            raise PckError(f"第 {i} 条的路径字节数 {n} 不合理（应为 4 的倍数），解析已跑偏")
        entry_path = raw(n).split(b"\x00", 1)[0].decode("utf-8", errors="replace")
        offset = file_base + u64()
        size = u64()
        raw(16)             # md5
        flags = u32()
        # 路径存的是不带 res:// 的仓内相对路径。这几条是「解析没跑偏」的证据：
        # 绝对路径、盘符、上跳与替换字符都说明读到的不是路径。
        if (not entry_path or entry_path.startswith(("/", "\\")) or ".." in entry_path
                or ":" in entry_path or "\ufffd" in entry_path):
            raise PckError(f"第 {i} 条路径 {entry_path!r} 不像仓内相对路径，解析已跑偏")
        if offset + size > len(data):
            raise PckError(f"第 {i} 条 {entry_path} 的数据段越出包尾，解析已跑偏")
        entries.append(PackEntry(entry_path, offset, size, flags))

    meta = {"format": fmt, "major": ver[0], "minor": ver[1], "patch": ver[2],
            "flags": pack_flags, "count": count, "bytes": len(data),
            "dir_offset": dir_offset, "tail": len(data) - pos}
    return entries, meta


def main_scene_entries() -> tuple[str, ...]:
    """启动场景在包里的两种可能落点。导出会把 `.tscn` 换成 `.tscn.remap` + 二进制 `.scn`。"""
    text = (ROOT / "project.godot").read_text(encoding="utf-8")
    m = re.search(r'run/main_scene\s*=\s*"res://([^"]+)"', text)
    if not m:
        return ()
    scene = m.group(1)
    return (scene, f"{scene}.remap")


def classify_leak(entry: PackEntry) -> str | None:
    """这条该不该在发行包里。返回泄漏类别，或 None 表示合法。"""
    if entry.path in BUNDLED_FILES:
        return None             # 登记过的随发行附带文件，见 BUNDLED_FILES
    for label, rx in LEAK_RULES:
        if rx.search(entry.path):
            return label
    if entry.path.lower().endswith(".cs"):
        if not entry.path.startswith(CS_ALLOWED_PREFIX):
            return f"{CS_ALLOWED_PREFIX} 以外的 C# 源码（子工程缺 .gdignore）"
        if entry.size > CS_PLACEHOLDER_MAX_BYTES:
            return f"带内容的 C# 源码（{entry.size} 字节，include_scripts_content 被打开了）"
    return None


def manifest_report(pck: Path) -> tuple[bool, list[str], list[str]]:
    """返回 (是否干净, 打屏用的要点, 落盘用的整份清单)。"""
    entries, meta = parse_pck(pck)
    lines = [
        f"# 包内清单 {pck.name}",
        f"# 格式版本 {meta['format']}，引擎 {meta['major']}.{meta['minor']}.{meta['patch']}，"
        f"pack_flags {meta['flags']}",
        f"# 条数 {meta['count']}，包体 {meta['bytes']} 字节，"
        f"目录表偏移 {meta['dir_offset']}，目录后剩余 {meta['tail']} 字节",
        "",
    ]
    leaks: list[str] = []
    for e in sorted(entries, key=lambda x: x.path):
        hit = classify_leak(e)
        if hit:
            leaks.append(f"{e.path}（{hit}）")
        lines.append(f"{e.size:>10}  {e.path}" + (f"  ← 泄漏：{hit}" if hit else ""))

    have = {e.path for e in entries}
    missing = [p for p in REQUIRED_PACK_ENTRIES if p not in have]
    missing += [f"{p}*" for p in REQUIRED_PACK_PREFIXES
                if not any(x.startswith(p) for x in have)]
    scenes = main_scene_entries()
    if not scenes:
        missing.append("（project.godot 里读不出 run/main_scene）")
    elif not any(s in have for s in scenes):
        missing.append(f"启动场景 {scenes[0]}（或它的 .remap）")

    # `ART-3`：包里有字体数据就必须有许可证。判据两边都取自**产物本身**，
    # 不看源码也不看导出预设 —— 预设改坏了正是要拦的情形之一。
    fonts = [e for e in entries if FONT_DATA_RE.search(e.path)]
    font_bytes = sum(e.size for e in fonts)
    unlicensed: list[str] = []
    for need, why in BUNDLED_FILES.items():
        if fonts and need not in have:
            unlicensed.append(f"{need}（{why}）")
    lines += ["", f"# 字体数据 {len(fonts)} 条／{font_bytes} 字节："
                  f"{[e.path for e in fonts]}",
              f"# 随发行附带 {[p for p in BUNDLED_FILES if p in have]}"]

    lines += ["", f"# 泄漏 {len(leaks)} 条", f"# 必需条目缺失 {len(missing)} 条"]
    if missing:
        lines.append(f"# 缺：{missing}")
    if unlicensed:
        lines.append(f"# 缺随发行附带文件：{unlicensed}")

    notes = [f"包内 {meta['count']} 条／{meta['bytes'] / 1024:.1f} KB",
             f"泄漏 {len(leaks)} 条", f"必需条目缺 {len(missing)} 条",
             f"字体数据 {font_bytes / 1024:.0f} KB，许可证 "
             + ("在" if not unlicensed else "**不在**")]
    problems = []
    if leaks:
        problems.append(f"泄漏 {len(leaks)} 条：{leaks[:3]}（踩坑记录 33，多半是缺 .gdignore）")
    if missing:
        problems.append(f"包里缺必需条目 {missing}")
    if unlicensed:
        problems.append(f"包里有 {len(fonts)} 条字体数据却缺 {unlicensed} —— "
                        f"OFL 第 2 条要求每份拷贝都带许可证与版权声明（ART-3）。"
                        f"补法：把它加进 export_presets.cfg 的 include_filter")
    return not problems, (problems or notes), lines


# ── 步骤 3：导出 ──────────────────────────────────────────────────────
def clean_export_dir() -> list[str]:
    """导出前清空 export/，好让「文件存在」直接等于「本轮生成」。

    不靠比 mtime：那要求脚本自己算「多新才算新」，而清空之后存在性本身就是证据。
    只删 export/ 里的内容，它已被 .gitignore 忽略，且每轮导出都会重建。
    """
    removed: list[str] = []
    if EXPORT_DIR.resolve().parent != ROOT or EXPORT_DIR.name != "export":
        raise RuntimeError(f"拒绝清理 {EXPORT_DIR}：它不是代码仓根下的 export/")
    if not EXPORT_DIR.is_dir():
        return removed
    for child in sorted(EXPORT_DIR.iterdir()):
        if child.is_dir():
            shutil.rmtree(child)
        else:
            child.unlink()
        removed.append(child.name)
    return removed


def step_export(rep: Report, godot: Path) -> StepResult:
    started = time.perf_counter()
    removed = clean_export_dir()
    code, out, enc = run(
        [str(godot), "--headless", "--path", str(ROOT),
         "--export-release", EXPORT_PRESET, str(EXPORT_EXE)],
        ROOT, timeout=900,
    )
    rep.write_log("3-export.log",
                  f"# 编码 {enc}\n# 退出码 {code}\n# 导出前清掉 {len(removed)} 项：{removed}\n\n{out}")
    cost = time.perf_counter() - started

    produced = [p.name for p in sorted(EXPORT_DIR.iterdir())] if EXPORT_DIR.is_dir() else []
    missing_files = [p.name for p in (EXPORT_EXE, EXPORT_PCK) if not p.is_file()]
    if missing_files:
        # 这一步的退出码本来就不可信，所以先信文件系统。
        return StepResult("export", False,
                          f"退出码 {code}，但产物缺 {missing_files}（本轮 export/ 只有 {produced}）",
                          cost, log_names=["3-export.log"])
    if EXPORT_EXE.stat().st_size == 0 or EXPORT_PCK.stat().st_size == 0:
        return StepResult("export", False, "产物存在但是 0 字节的空壳",
                          cost, log_names=["3-export.log"])

    try:
        clean, notes, lines = manifest_report(EXPORT_PCK)
    except (PckError, OSError) as exc:
        return StepResult("export", False, f"读不出包内清单：{exc}",
                          cost, log_names=["3-export.log"])
    rep.write_log("3-export-manifest.txt", "\n".join(lines) + "\n")
    logs = ["3-export.log", "3-export-manifest.txt"]

    exe_mb = EXPORT_EXE.stat().st_size / (1024 * 1024)
    headline = "，".join(notes) + f"，产物 {len(produced)} 项、exe {exe_mb:.1f} MB"
    if code != 0:
        clean = False
        headline = f"退出码 {code}；{headline}"
    return StepResult("export", clean, headline, cost, log_names=logs,
                      details=[f"清掉上轮 {len(removed)} 项：{removed}", *notes])


# ── 步骤 4：跑产物 ────────────────────────────────────────────────────
def verify_smoke_markers() -> str | None:
    """标记还在脚手架源码里吗。脱节就当场说清，别等冒烟阶段报一句看不懂的没找到。"""
    if not MAIN_SCAFFOLD.is_file():
        return f"{MAIN_SCAFFOLD.relative_to(ROOT).as_posix()} 不在了，冒烟标记无从校对"
    text = MAIN_SCAFFOLD.read_text(encoding="utf-8")
    gone = [m for m in SMOKE_MARKERS if m not in text]
    if gone:
        return (f"冒烟标记 {gone} 已不在 src/Main.cs 里 —— 启动流程改过了，"
                f"请同步改 verify.py 的 SMOKE_MARKERS")
    return None


def step_smoke(rep: Report) -> StepResult:
    started = time.perf_counter()
    if (why := verify_smoke_markers()):
        return StepResult("smoke", False, why, time.perf_counter() - started)
    if not EXPORT_EXE.is_file():
        return StepResult("smoke", False, f"没有 {EXPORT_EXE.name} 可跑",
                          time.perf_counter() - started)

    # 导出的 release 包不带控制台包装器（预设 debug/export_console_wrapper=1 只给 debug），
    # 而 GUI 子系统的 exe 在本机不往管道写东西，所以用引擎自己的 --log-file 落盘再读。
    #
    # 跑产物会让它建自己的 user:// 目录（mods 与 saves）。**本入口刻意不删它** —— 那将来
    # 是玩家真正的存档位置，一个验收工具去删玩家存档是个很难收场的坑。它不是临时资源，
    # 是产物正常启动的一部分；路径由产物自己打进 4-smoke-godot.log，需要时在那里看。
    game_log = rep.log_dir / "4-smoke-godot.log"
    rep.ensure_log_dir()
    code, out, enc = run(
        [str(EXPORT_EXE), "--headless", "--quit-after", str(SMOKE_FRAMES),
         "--log-file", str(game_log)],
        EXPORT_DIR, timeout=300,
    )
    cost = time.perf_counter() - started
    piped = out.strip()
    game_text = game_log.read_text(encoding="utf-8", errors="replace") if game_log.is_file() else ""
    rep.write_log("4-smoke.log",
                  f"# 编码 {enc}\n# 退出码 {code}\n# 管道输出 {len(piped)} 字符\n"
                  f"# 引擎日志 {game_log.name}，{len(game_text)} 字符\n\n"
                  f"── 管道 ──\n{piped}\n\n── 引擎日志 ──\n{game_text}")
    logs = ["4-smoke.log"] + ([game_log.name] if game_text else [])

    observed = game_text or piped
    if not observed:
        return StepResult("smoke", False,
                          f"退出码 {code}，但产物一个字都没输出 —— 证明不了它真跑起来了",
                          cost, log_names=logs)

    missing = [m for m in SMOKE_MARKERS if m not in observed]
    errors = [ln.strip() for ln in observed.splitlines()
              if any(k in ln for k in SMOKE_ERROR_MARKERS)]
    boot_lines = sum(1 for ln in observed.splitlines() if "[启动]" in ln)
    headline = f"退出码 {code}，[启动] {boot_lines} 行，错误 {len(errors)} 条"

    bad: list[str] = []
    if code != 0:
        bad.append(f"退出码 {code}")
    if missing:
        bad.append(f"没跑到的阶段：{missing}")
    if errors:
        bad.append(f"日志里有 {len(errors)} 条错误，例如 {errors[0][:80]}")
    if bad:
        return StepResult("smoke", False, f"{headline} —— {'；'.join(bad)}",
                          cost, log_names=logs, details=bad)
    return StepResult("smoke", True, headline, cost, log_names=logs,
                      details=[f"启动阶段标记 {len(SMOKE_MARKERS)} 个全部出现：{list(SMOKE_MARKERS)}",
                               "观察来源：引擎 --log-file 落盘的日志（release 包无控制台包装器）"])


# ── 运行摘要 ──────────────────────────────────────────────────────────
def write_summary(rep: Report, started: datetime, ended: datetime,
                  scope: list[str], godot: Path | None) -> Path:
    full = scope == list(STEPS)
    lines = [
        "# 验收运行摘要",
        "",
        f"- 起：{started:%Y-%m-%d %H:%M:%S}",
        f"- 止：{ended:%Y-%m-%d %H:%M:%S}（{(ended - started).total_seconds():.1f}s）",
        f"- 落点：`{ROOT}`",
        f"- Godot：`{godot}`" if godot else "- Godot：未定位",
        f"- 范围：{' → '.join(scope)}"
        + ("（完整）" if full else "（**不完整，不能当一次验收**）"),
        f"- 结果：{sum(1 for r in rep.results if r.ok and not r.skipped)}/"
        f"{len([r for r in rep.results if not r.skipped])} 步通过／"
        f"{len(rep.fails)} 项必须修复",
        "",
        "## 逐步",
        "",
    ]
    for r in rep.results:
        tag = "SKIP" if r.skipped else ("OK" if r.ok else "FAIL")
        lines.append(f"### {STEP_TITLES[r.name]}（{tag}，{r.seconds:.1f}s）")
        lines.append("")
        lines.append(r.headline)
        lines.append("")
        for d in r.details:
            lines.append(f"- {d}")
        if r.log_names:
            lines.append(f"- 日志：{'、'.join(f'`{n}`' for n in r.log_names)}")
        lines.append("")
    if rep.notes:
        lines += ["## 覆盖量", ""] + [f"- {n}" for n in rep.notes] + [""]
    if rep.fails:
        lines += ["## 必须修复", ""] + [f"- {f}" for f in rep.fails] + [""]

    rep.ensure_log_dir()
    path = rep.log_dir / "summary.md"
    path.write_text("\n".join(lines), encoding="utf-8", newline="\n")

    # 一行历史，方便回看某天跑过几次、结果如何。
    LOG_ROOT.mkdir(parents=True, exist_ok=True)
    with (LOG_ROOT / "runs.log").open("a", encoding="utf-8", newline="\n") as f:
        f.write(f"{started:%Y-%m-%d %H:%M:%S}  {'→'.join(scope)}  "
                f"{'PASS' if not rep.fails else 'FAIL'}  "
                f"{(ended - started).total_seconds():.1f}s  {rep.log_dir.name}\n")
    return path


# ── 入口 ──────────────────────────────────────────────────────────────
def do_manifest(target: str | None) -> int:
    """只看清单，不跑步骤。包在不在、干净不干净，随时能查一眼。"""
    pck = Path(target) if target else EXPORT_PCK
    if not pck.is_file():
        print(f"[FAIL] 没有 {pck}，先跑一次 python tools/verify.py")
        print("EXIT=1")
        return 1
    try:
        clean, notes, lines = manifest_report(pck)
    except (PckError, OSError) as exc:
        print(f"[FAIL] 读不出包内清单：{exc}")
        print("EXIT=1")
        return 1
    print("\n".join(lines))
    print(f"\n结果：{'／'.join(notes)}")
    print("[OK] 包内清单干净" if clean else "[FAIL] 包内清单有问题")
    print(f"EXIT={0 if clean else 1}")
    return 0 if clean else 1


def main() -> int:
    ap = argparse.ArgumentParser(description="代码仓验收总入口（ENG-3）")
    ap.add_argument("--upto", choices=STEPS, default=STEPS[-1],
                    help="跑到哪一步为止；前置步骤一定跟着跑（默认全跑）")
    ap.add_argument("--manifest", nargs="?", const="", metavar="PCK",
                    help="不跑步骤，只打包内清单；不给路径就看 export/ 下那个")
    args = ap.parse_args()

    if args.manifest is not None:
        return do_manifest(args.manifest or None)

    started = datetime.now()
    rep = Report(LOG_ROOT / started.strftime("%Y%m%d-%H%M%S"))
    scope = list(STEPS[:STEPS.index(args.upto) + 1])
    rep.say(f"[..]   验收 {' → '.join(STEP_TITLES[s] for s in scope)}"
            + ("" if scope == list(STEPS) else "（不完整范围）"))

    godot: Path | None = None
    if not check_root(rep):
        ended = datetime.now()
        write_summary(rep, started, ended, scope, None)
        print("EXIT=1")
        return 1
    if "export" in scope:
        godot, why = locate_godot()
        if godot is None:
            rep.say(f"[FAIL] {why}")
            rep.fail(why)
            write_summary(rep, started, datetime.now(), scope, None)
            print("EXIT=1")
            return 1
        rep.say(f"[..]   Godot {godot.name} → {why}")

    stopped = False
    for name in scope:
        if stopped:
            rep.finish_step(StepResult(name, False, "前一步失败，未执行", skipped=True))
            continue
        if name == "assets":
            result = step_assets(rep)
        elif name == "build":
            result = step_build(rep)
        elif name == "test":
            result = step_test(rep)
        elif name == "export":
            assert godot is not None
            result = step_export(rep, godot)
        else:
            result = step_smoke(rep)
        rep.finish_step(result)
        # 失败即停：后面每一步都依赖前一步的产物，硬跑下去只会产出误导性的失败。
        stopped = not result.ok

    ended = datetime.now()
    summary = write_summary(rep, started, ended, scope, godot)
    for n in rep.notes:
        rep.say(n)
    passed = sum(1 for r in rep.results if r.ok and not r.skipped)
    ran = len([r for r in rep.results if not r.skipped])
    rep.say(f"\n结果：{passed}/{ran} 步通过（范围 {len(scope)} 步"
            f"{'，完整' if scope == list(STEPS) else '，不完整'}）"
            f"／{len(rep.fails)} 项必须修复／{(ended - started).total_seconds():.1f}s")
    rep.say(f"摘要 {summary.relative_to(ROOT)}")
    _pruned = prune_runs(LOG_ROOT)
    if _pruned:
        rep.say(f"[清理] logs/verify 删掉 {len(_pruned)} 份旧运行，只留最近 {KEEP_RUNS} 次")

    if rep.fails:
        rep.say("[FAIL] 有必须修复项")
        print("EXIT=1")
        return 1
    if scope != list(STEPS):
        # 局部跑成功就是成功，判失败会让 --upto 没法用于调试。防误用靠两处标注：
        # 这行提醒、摘要里的「不完整，不能当一次验收」，以及 runs.log 记下的范围。
        rep.say("[WARN] 范围不完整，这不能当一次验收 —— 门禁必须不带参数跑")
        print("EXIT=0")
        return 0
    rep.say(f"[OK] {len(STEPS)} 步全过")
    print("EXIT=0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
