"""ENG-6 帧调优叠层（CombatDebugOverlay）图形入口与严格探针日志守卫。

判定框/受击框可视化与帧步进都只能靠画面与逐帧断言验，规则层单测覆盖不到。探针在
`CombatDebugDev` 里摆一主角一木桩，验两组：叠层画的框 ≙ 命中真正用的几何；帧步进真的冻结
战斗、单步只推一帧、且单步跑的是同一份结算（命中与硬直帧都对）。
"""
import argparse
import os
import re
import subprocess
import time
from check_input_map import find_godot, ROOT

# 可视化那一半：默认关、Active 帧判定框 ≙ 独立按 SpecFor 重算的框、非 Active 不画判定框、
# 受击框 ≙ 角色实际几何（18×32、贴脚底、居中）、受击框跟着纵深绘制偏移挪（GP-14 阶段 1 实机）。
VIZ = set('overlay-default-off hitbox-viz-hidden-inactive '
          'hitbox-viz-matches-active hurtbox-viz-matches '
          'hurtbox-viz-follows-depth'.split())
# 帧步进那一半：暂停冻住进行中的攻击、单步恰好推一帧且不自行连推、单步跑的是同一份结算
# （命中木桩、硬直帧数与规则层一致）——这条就是 `FR-19` 的「帧步进不改结算」的行为级证明。
FRAMESTEP = set('paused-freezes-combat step-advances-exactly-one '
                'step-runs-full-resolution'.split())
# 前提判据：这一轮的测量条件成立吗（物理帧与渲染帧 1:1、窗口没失焦）。它们不测玩法。
PRECONDITIONS = {'tick-frame-1to1', 'focus-kept'}
EXPECTED = VIZ | FRAMESTEP | PRECONDITIONS | {'screenshot'}


def valid_log(text, code):
    verdicts = re.findall(r'^\[ENG6\] (PASS|FAIL) (\S+)$', text, re.MULTILINE)
    summary = re.findall(r'^\[ENG6\] Summary (\d+)/(\d+)$', text, re.MULTILINE)
    marked = [line for line in text.splitlines()
              if line.startswith(('[ENG6] PASS', '[ENG6] FAIL', '[ENG6] Summary'))]
    return (code == 0 and len(marked) == len(verdicts) + len(summary)
            and len(verdicts) == len(EXPECTED) and {name for _, name in verdicts} == EXPECTED
            and all(v == 'PASS' for v, _ in verdicts)
            and summary == [(str(len(EXPECTED)), str(len(EXPECTED)))] and 'ERROR:' not in text)


def selfcheck():
    """守卫自证：每条判据的「缺失」与「FAIL」两种形状都要被拦下。"""
    lines = [f'[ENG6] PASS {name}' for name in sorted(EXPECTED)]
    summary = f'[ENG6] Summary {len(EXPECTED)}/{len(EXPECTED)}'
    good = '\n'.join(lines + [summary])
    bad = ['\n'.join(lines[1:] + [lines[1], summary]), '\n'.join(lines[1:] + [summary]),
           good.replace('PASS', 'FAIL', 1), good + '\n' + summary,
           good.replace(summary, '[ENG6] Summary 0/0'), good + '\nERROR: injected',
           good + '\n[ENG6] PASS extra', '\n'.join(lines), good + '\n[ENG6] PASS malformed extra']
    for name in sorted(EXPECTED):
        record = f'[ENG6] PASS {name}'
        bad.extend((good.replace(record + '\n', ''), good.replace(record, f'[ENG6] FAIL {name}')))
    assert valid_log(good, 0) and not valid_log(good, 1)
    assert all(not valid_log(text, 0) for text in bad)
    print(f'ENG6 log guard selfcheck {len(bad) + 2}/{len(bad) + 2}; expected={len(EXPECTED)}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--probe', action='store_true')
    # 交互模式（不带 --probe）给作者看叠层，要等他关窗口。--smoke 跑固定帧数就退出，用来证明
    # 那个模式本身启动得起来、没报错 —— 交互模式坏了的话判据全绿也看不出来。
    parser.add_argument('--smoke', type=int, metavar='FRAMES',
                        help='run the interactive scene for N frames and check the log for errors')
    args = parser.parse_args()
    selfcheck()
    exe = find_godot()
    if exe is None:
        raise SystemExit('Godot mono not found')
    folder = ROOT / 'logs' / 'combat-debug' / time.strftime('%Y%m%d-%H%M%S')
    folder.mkdir(parents=True)
    log = folder / 'engine.log'
    env = os.environ.copy()
    env['ENG6_SHOT'] = str(folder / 'combat-debug.png')
    command = [str(exe), '--path', str(ROOT), '--log-file', str(log)]
    if args.smoke:
        command += ['--quit-after', str(args.smoke)]
    command += ['res://scenes/CombatDebugDev.tscn', '--']
    if args.probe:
        command += ['--eng6-probe']
    result = subprocess.run(command, env=env, timeout=90 if args.probe or args.smoke else None)
    if args.smoke:
        text = log.read_text(encoding='utf-8')
        errors = [line for line in text.splitlines()
                  if 'ERROR:' in line or 'SCRIPT ERROR' in line or 'Cannot call method' in line]
        good = result.returncode == 0 and not errors
        print(f'ENG6 smoke frames={args.smoke} exit={result.returncode} '
              f'errors={len(errors)}; log={log}; EXIT={0 if good else 1}')
        for line in errors[:5]:
            print(f'  {line}')
        return 0 if good else 1
    if not args.probe:
        return result.returncode
    text = log.read_text(encoding='utf-8')
    good = valid_log(text, result.returncode)
    print(f'ENG6 log={log}; EXIT={0 if good else 1}')
    return 0 if good else 1


if __name__ == '__main__':
    raise SystemExit(main())
