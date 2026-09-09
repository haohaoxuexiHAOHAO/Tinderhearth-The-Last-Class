"""Run a repository Python check with disposable user directories off the system drive."""
from __future__ import annotations

import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parent.parent

# 需要 Godot 导出模板的入口。模板约 2 GB、装在真实 APPDATA 里，而本入口把 APPDATA 换成了
# 工作区 temp 下的空目录 —— 不复制过去，导出那一步就静默产不出东西，报出来的是「产物缺 exe
# 和 pck」，与真实原因（模板不在）差得很远。
#
# 为什么是一张显式清单而不是「一律复制」：绝大多数入口不导出，为它们复制 2 GB 是白花的。
# 为什么不怕漏加：漏了会让整轮验收在导出那一步失败，是响的不是哑的；而且 verify.py 的失败
# 信息里现在会直接点出「隔离出来的 APPDATA 里没有导出模板」（见 step_export）。
NEEDS_EXPORT_TEMPLATES = {"verify.py", "selfcheck_verify.py"}


def main() -> int:
    if len(sys.argv) < 2:
        raise SystemExit("Usage: python tools/run_local_check.py <tool.py> [arguments]")
    if sys.argv[1] == "--clean":
        target = (ROOT.parent / "temp" / sys.argv[2]).resolve()
        if target.parent != ROOT.parent / "temp" or not target.name.startswith("local-check-"):
            raise SystemExit("Refusing unrelated cleanup")
        shutil.rmtree(target)
        print(f"Removed: {target}")
        return 0
    tool = (ROOT / "tools" / sys.argv[1]).resolve()
    if tool.parent != ROOT / "tools" or not tool.is_file():
        raise SystemExit("Expected an existing tools/*.py entry")
    base = ROOT.parent / "temp"
    base.mkdir(exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix="local-check-", dir=base))
    env = os.environ.copy()
    for key in ("TEMP", "TMP", "APPDATA", "LOCALAPPDATA"):
        folder = work / key.lower()
        folder.mkdir()
        env[key] = str(folder)
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    try:
        templates = Path(os.environ["APPDATA"]) / "Godot" / "export_templates"
        if tool.name in NEEDS_EXPORT_TEMPLATES:
            if not templates.is_dir():
                raise SystemExit(f"{tool.name} needs export templates but {templates} is missing")
            shutil.copytree(templates, Path(env["APPDATA"]) / "Godot" / "export_templates")
        print(f"Isolated user directories: {work}", flush=True)
        return subprocess.run([sys.executable, str(tool), *sys.argv[2:]],
                              cwd=ROOT, env=env, check=False).returncode
    finally:
        subprocess.run(["dotnet", "build-server", "shutdown"], cwd=ROOT,
                       env=env, check=False, timeout=60)
        shutil.rmtree(work)
        print(f"Removed: {work}", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
