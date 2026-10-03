#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""代码仓命名守卫。

为什么存在：Godot 官方建议「文件夹与文件名用 snake_case（C# 脚本例外，按类名走
PascalCase），节点名用 PascalCase」，而理由不是好看 —— 导出后的 `.pck` 虚拟文件
系统**区分大小写**，Windows 与 macOS 的文件系统默认不区分。于是路径大小写写错
在编辑器里一切正常，只在导出后或 Linux 上才找不到文件，**而且不报编译错**。

**这正是「在 Godot 里看不出来」的那一类**（`ADR-0009` 留判据的唯一筛子），而且
静态扫一遍极便宜，所以它有资格当门禁。规则本体写在 `CONVENTIONS.md` 的
「文件与目录命名」一节，本脚本只是那一节的执行体 —— 改规则先改那一节。

五条判据，分两类。**形状三条**（名字长得对不对）：

1. 资源路径（`assets/`、`data/`、`scenes/` 与仓库根）里不许有大写字母，
   例外只有 `UPPERCASE_ALLOWED` 那几个。
2. 场景文件名是 snake_case（比第 1 条严：不许有中划线）。
3. C# 目录与 `.cs` 文件名是 PascalCase；`tools/` 下是小写。

**指向三条里最重要的那一条**（名字对了但引用对不上）：

4. 场景里 `[node name="…"]` 的节点名是 PascalCase。
5. 代码与配置里写死的 `res://` 路径、以及导出预设 `include_filter` 里不带通配符的
   条目，必须**按大小写精确**存在于磁盘上。

第 5 条是前四条的意义所在：名字全合规但引用方抄错一个字母，照样只在导出后炸。
`Path.exists()` 在 Windows 上不区分大小写，所以这里逐段 `scandir` 比对，不用它。

**判不到的**：代码里 `Name = "..."` 设的节点名（那是字符串字面量，不在场景文件里）、
两字母缩写全大写那条（`UIMetrics` 对、`UiMetrics` 错，静态分不出哪个标识符是缩写）。
两条都写在 `CONVENTIONS.md` 里当约定，评审时人工过。

用法（从代码仓根目录运行）：
    python tools/check_names.py     # 退出码 0 = 全部符合

接进门禁：`verify.py` 的 `step_names` 调本脚本作为子进程，认「覆盖量：」前缀判过。
没有自证入口（随 `ADR-0009` 一起取消），改了本脚本只能靠改的人复核 —— 最省事的办法
是临时把 `scenes/main.tscn` 改成 `Main.tscn`，确认它真的报出来，再改回去。
"""

from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parent.parent

# 作为引擎资源进 `res://` 的目录。它们吃大小写那个坑，所以不许有大写字母。
RESOURCE_DIRS = ("assets", "data", "scenes")
# C# 代码目录。官方那条例外只给 C# 文件，本仓把它扩到目录，理由见 CONVENTIONS.md。
CSHARP_DIRS = ("rules", "src", "tests")
# Python 入口目录：小写 snake_case，且带 `.gdignore`，不进资源扫描。
SNAKE_DIRS = ("tools",)

# 允许带大写字母的文件。**改这张表之前先读 CONVENTIONS.md 那一节的判据**：
# 它的大小写会不会被某个路径字符串写死？会，就别加。
UPPERCASE_ALLOWED = frozenset({
    "assets/fonts/LICENSE-OFL.txt",                    # 授权文件通行名
    "assets/downloaded/fists-of-fury/LICENSE.txt",     # 上游原名
    "assets/downloaded/samurai/License.txt",           # 上游原名（与上一行刻意不同名）
    "data/text/zh-CN.json",                            # 语言标签标准写法
})
# 仓库根上不是引擎资源、不进发行包的文件。
ROOT_UPPERCASE_ALLOWED = frozenset({
    "README.md", "ARCHITECTURE.md", "CONVENTIONS.md",
})
ROOT_UPPERCASE_ALLOWED_SUFFIXES = (".csproj", ".sln")

PASCAL_RE = re.compile(r"^[A-Z][A-Za-z0-9]*$")
SNAKE_FILE_RE = re.compile(r"^[a-z][a-z0-9_]*$")
LOWER_SEGMENT_RE = re.compile(r"^[a-z0-9][a-z0-9._-]*$")
NODE_NAME_RE = re.compile(r'^\[node\s+name="([^"]*)"', re.MULTILINE)
RES_PATH_RE = re.compile(r'res://([A-Za-z0-9_./{}-]+)')
INCLUDE_FILTER_RE = re.compile(r'^include_filter\s*=\s*"([^"]*)"', re.MULTILINE)

# 扫哪些文件找写死的 `res://`。`.import` 刻意不扫：它由导入器生成，里面的
# `source_file` 是引擎自己写的，不是人抄的 —— 人抄错的地方在代码与预设里。
RES_SOURCE_SUFFIXES = (".cs", ".tscn", ".tres", ".godot", ".cfg")


def git_managed_files() -> list[str] | None:
    """git 会管的文件：已跟踪 + 未被忽略的未跟踪。返回 None 表示问不出来。

    用 `git ls-files` 而不是自己遍历，是为了让 `.gitignore` 自动生效 —— 否则
    `bin/`、`obj/`、`.godot/`、`export/` 里的产物都会被拖进来，而那些名字不是我们写的。
    `-z` 分隔避免中文文件名被 `core.quotepath` 转成八进制（同 `check_eol.py`）。
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


def code_stem(name: str) -> str:
    """去掉 `.cs` 与它的 `.cs.uid` 附属物后缀，拿到该与类名一致的那一段。"""
    for suffix in (".cs.uid", ".cs"):
        if name.endswith(suffix):
            return name[: -len(suffix)]
    return name


def check_resource_paths(rel: str, fails: list[str]) -> None:
    """判据 1：资源路径不许有大写字母。"""
    if rel in UPPERCASE_ALLOWED or rel.removesuffix(".import") in UPPERCASE_ALLOWED:
        return
    bad = [seg for seg in rel.split("/") if any(c.isupper() for c in seg)]
    if bad:
        fails.append(
            f"[FAIL] {rel}：资源路径不许有大写字母（`{'`、`'.join(bad)}`）—— "
            f"导出后的 .pck 区分大小写，而 Windows 不区分，写错只在导出后炸；"
            f"确实必须带大写就加进 check_names.py 的 UPPERCASE_ALLOWED 并同步 "
            f"CONVENTIONS.md 那张表")


def check_scene_file_name(rel: str, fails: list[str]) -> None:
    """判据 2：场景文件名是 snake_case。"""
    stem = Path(rel).stem
    if not SNAKE_FILE_RE.match(stem):
        fails.append(
            f"[FAIL] {rel}：场景文件名要用小写 snake_case（如 `training_room.tscn`）")


def check_csharp_path(rel: str, fails: list[str]) -> None:
    """判据 3 的 C# 那一半：目录 PascalCase，`.cs` 文件名 PascalCase。"""
    parts = rel.split("/")
    for seg in parts[1:-1]:
        if not PASCAL_RE.match(seg):
            fails.append(
                f"[FAIL] {rel}：C# 目录名 `{seg}` 要用 PascalCase 并与命名空间一致")
            break
    name = parts[-1]
    if name.endswith((".cs", ".cs.uid")):
        stem = code_stem(name)
        if not PASCAL_RE.match(stem):
            fails.append(f"[FAIL] {rel}：C# 文件名要用 PascalCase 并与类名一致")


def check_snake_path(rel: str, fails: list[str]) -> None:
    """判据 3 的 `tools/` 那一半：小写。"""
    for seg in rel.split("/"):
        if any(c.isupper() for c in seg):
            fails.append(f"[FAIL] {rel}：`{seg}` 要用小写 snake_case")
            break


def check_root_file(rel: str, fails: list[str]) -> None:
    """判据 1 在仓库根上的那一半：根文件也进 `res://`，同样不许有大写。"""
    if rel in ROOT_UPPERCASE_ALLOWED or rel.endswith(ROOT_UPPERCASE_ALLOWED_SUFFIXES):
        return
    if any(c.isupper() for c in rel):
        fails.append(
            f"[FAIL] {rel}：仓库根也在 `res://` 下，文件名不许有大写字母")


def check_node_names(rel: str, fails: list[str]) -> int:
    """判据 4：场景里的节点名是 PascalCase。返回看过的节点数。"""
    try:
        text = (ROOT / rel).read_text(encoding="utf-8")
    except OSError as exc:
        fails.append(f"[FAIL] {rel}：读不出来（{exc}），节点名**没有被检查**")
        return 0
    names = NODE_NAME_RE.findall(text)
    if not names:
        fails.append(
            f"[FAIL] {rel}：一个 `[node name=...]` 都没解析到 —— "
            f"场景格式变了还是文件坏了？节点名这一条**没有执行**（不是通过）")
        return 0
    for name in names:
        if not PASCAL_RE.match(name):
            fails.append(
                f"[FAIL] {rel}：节点名 `{name}` 要用 PascalCase（内置节点就是这个形状）")
    return len(names)


def exists_exact(rel: str) -> bool:
    """按大小写精确判断 `res://<rel>` 是否存在。

    **不能用 `Path.exists()`**：Windows 与 macOS 的文件系统默认不区分大小写，
    它对 `scenes/Main.tscn` 会返回 True，而那个名字在 `.pck` 里并不存在 ——
    那正是本脚本要抓的失效形状。所以逐段 `scandir` 比对真实名字。
    """
    current = ROOT
    for seg in rel.split("/"):
        if not seg or seg == ".":
            continue
        try:
            entries = {e.name for e in os.scandir(current)}
        except OSError:
            return False
        if seg not in entries:
            return False
        current = current / seg
    return True


def check_res_references(rel: str, fails: list[str]) -> int:
    """判据 5：写死的 `res://` 路径必须按大小写精确存在。返回看过的引用数。"""
    try:
        text = (ROOT / rel).read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return 0
    seen = 0
    for target in dict.fromkeys(RES_PATH_RE.findall(text)):
        # 带 `{` 的是格式串（`skill-slot-{0}.png`），真实路径在运行时才成形。
        if "{" in target or "}" in target:
            continue
        seen += 1
        if not exists_exact(target):
            fails.append(
                f"[FAIL] {rel}：`res://{target}` 在磁盘上找不到（**按大小写精确**比对）"
                f" —— 大小写不符时编辑器照常工作，导出后才炸")
    return seen


def check_include_filter(fails: list[str]) -> int:
    """判据 5 的导出预设那一半：`include_filter` 里不带通配符的条目必须精确存在。

    为什么单独判它：非资源文件（`.json`、授权 `.txt`）靠这一行才进包，
    而这一行是**另一份**写死的大小写副本 —— 它和代码里的常量必须同时对。
    """
    preset = ROOT / "export_presets.cfg"
    if not preset.is_file():
        fails.append("[FAIL] 找不到 export_presets.cfg，导出预设那一条**没有执行**")
        return 0
    text = preset.read_text(encoding="utf-8")
    seen = 0
    for raw in INCLUDE_FILTER_RE.findall(text):
        for entry in (e.strip() for e in raw.split(",")):
            if not entry or "*" in entry or "?" in entry:
                continue
            seen += 1
            if not exists_exact(entry):
                fails.append(
                    f"[FAIL] export_presets.cfg：include_filter 里的 `{entry}` "
                    f"在磁盘上找不到（**按大小写精确**比对）—— 它不会进包，"
                    f"而导出不报错")
    return seen


def main() -> int:
    paths = git_managed_files()
    if paths is None:
        print("[FAIL] 问不出 git 的文件清单，这一轮命名守卫**没有执行**（不是通过）"
              "—— 确认装了 git 且在仓库内运行")
        print("EXIT=1")
        return 1

    fails: list[str] = []
    scanned = scenes = nodes = refs = 0

    for rel in paths:
        top = rel.split("/")[0]
        scanned += 1
        if top in RESOURCE_DIRS:
            check_resource_paths(rel, fails)
            if rel.endswith(".tscn"):
                scenes += 1
                check_scene_file_name(rel, fails)
                nodes += check_node_names(rel, fails)
        elif top in CSHARP_DIRS:
            check_csharp_path(rel, fails)
        elif top in SNAKE_DIRS:
            check_snake_path(rel, fails)
        elif "/" not in rel:
            check_root_file(rel, fails)
        if rel.endswith(RES_SOURCE_SUFFIXES):
            refs += check_res_references(rel, fails)

    refs += check_include_filter(fails)

    for line in fails:
        print(line)

    # 覆盖量为 0 一律判失败：清单解析坏了会让这个脚本静默变成空操作，
    # 而「0 个违反」与「0 个被检查」在输出上长得一样（踩坑记录 29 同一个形状）。
    if not scanned or not scenes or not nodes or not refs:
        print(f"[FAIL] 覆盖量不对：扫了 {scanned} 个路径、{scenes} 个场景、"
              f"{nodes} 个节点名、{refs} 处 res:// 引用 —— 有一项为 0，"
              f"说明清单或解析坏了，这一轮**没有真的检查**")
        print("EXIT=1")
        return 1

    print(f"覆盖量：{scanned} 个路径、{scenes} 个场景（{nodes} 个节点名）、"
          f"{refs} 处 res:// 引用按大小写精确比对"
          + (f"，{len(fails)} 项违反" if fails else "，全部符合"))
    if fails:
        print("EXIT=1")
        return 1
    print("[OK] 命名全部符合 CONVENTIONS.md 的「文件与目录命名」")
    print("EXIT=0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
