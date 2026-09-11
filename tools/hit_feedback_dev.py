"""GP-13 graphical hit feedback entry and strict probe log guard."""
import argparse
import os
import re
import subprocess
import time
from check_input_map import find_godot, ROOT

EXPECTED = {f'{kind}-{name}' for kind in ('light', 'heavy') for name in
            ('active-hit', 'fresh-stun', 'freeze', 'distance', 'expired', 'dedup', 'shake', 'inactive', 'input-no-replay')} | {'screenshot', 'flash-pixel', 'restored-pixel', 'held-input-survives'} | set('startup-gate pre-overlap exit-active reenter-dedup first-recovery active-empty last-active-entry next-recovery landing-cancel refresh-distance no-tail-slide wall-block'.split())


EXPECTED |= {f'{action}-{check}' for action in ('jump', 'attack-light', 'attack-heavy')
             for check in ('ready', 'frozen-sequence', 'first-resume', 'no-replay', 'fresh-press')}
EXPECTED |= {f'sprint-{check}' for check in ('ready', 'frozen-sequence', 'first-resume',
                                           'full-speed', 'release-first', 'normal-motion', 'direction-release')}
# ART-6：判定框按轻重分开之后的行为级判据 —— 同一距离轻击打空、重击打到。
REACH_SPLIT = {'reach-split-shape', 'light-reach-miss', 'heavy-reach-hit', 'reach-split-drained',
               # 前提判据：探针放木桩的那个距离**真的**落在轻击框内。伸展是从美术量出来的，
               # 美术把它缩到放置距离以内时，这条会当场说清原因，而不是让下游某条判据莫名失败。
               'probe-near-in-reach'}
EXPECTED |= REACH_SPLIT
# GP-16：纵深容差命中的行为级判据 —— 同一横向距离，错开一排打空、挪回同排打中、正好差一个容差
# 仍打中、击退不碰纵深。加上一条前提判据（探针用的两个纵深都从常量导出且真的落在带内）。
# 两条前提判据：距离从常量导出且真的落在带内；本阶段开头去重集合是干净的（脏了会让「纵深错开
# 打空」假绿 —— 返回 0 是被去重挡的，不是纵深挡的）。
DEPTH_TOLERANCE = {'probe-depth-rows-derived', 'depth-probe-fresh-swing', 'depth-off-row-miss',
                   'depth-realign-hits-same-swing', 'depth-tolerance-edge-hit',
                   'knockback-horizontal-only', 'depth-probe-restored'}
EXPECTED |= DEPTH_TOLERANCE
# 前提判据：这一轮的测量条件成立吗（物理帧与渲染帧 1:1、窗口没失焦）。它们不测玩法。
PRECONDITIONS = {'tick-frame-1to1', 'focus-kept'}
EXPECTED |= PRECONDITIONS


def valid_log(text, code):
    verdicts = re.findall(r'^\[GP13\] (PASS|FAIL) (\S+)$', text, re.MULTILINE)
    summary = re.findall(r'^\[GP13\] Summary (\d+)/(\d+)$', text, re.MULTILINE)
    marked = [line for line in text.splitlines() if line.startswith(('[GP13] PASS', '[GP13] FAIL', '[GP13] Summary'))]
    return (code == 0 and len(marked) == len(verdicts) + len(summary)
            and len(verdicts) == len(EXPECTED) and {name for _, name in verdicts} == EXPECTED
            and all(v == 'PASS' for v, _ in verdicts)
            and summary == [(str(len(EXPECTED)), str(len(EXPECTED)))] and 'ERROR:' not in text)


def selfcheck():
    lines = [f'[GP13] PASS {name}' for name in sorted(EXPECTED)]
    summary = f'[GP13] Summary {len(EXPECTED)}/{len(EXPECTED)}'
    good = '\n'.join(lines + [summary])
    bad = ['\n'.join(lines[1:] + [lines[1], summary]), '\n'.join(lines[1:] + [summary]),
           good.replace('PASS', 'FAIL', 1), good + '\n' + summary,
           good.replace(summary, '[GP13] Summary 0/0'), good + '\nERROR: injected',
           good + '\n[GP13] PASS extra', '\n'.join(lines), good + '\n[GP13] PASS malformed extra']
    for name in sorted({n for n in EXPECTED if n.startswith(('jump-', 'attack-light-', 'attack-heavy-', 'sprint-'))} | REACH_SPLIT | DEPTH_TOLERANCE | PRECONDITIONS):
        record = f'[GP13] PASS {name}'
        bad.extend((good.replace(record + '\n', ''), good.replace(record, f'[GP13] FAIL {name}')))
    assert valid_log(good, 0) and not valid_log(good, 1)
    assert all(not valid_log(text, 0) for text in bad)
    print(f'GP13 log guard selfcheck {len(bad) + 2}/{len(bad) + 2}; expected={len(EXPECTED)}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--probe', action='store_true')
    parser.add_argument('--no-shake', action='store_true')
    args = parser.parse_args()
    selfcheck()
    exe = find_godot()
    if exe is None:
        raise SystemExit('Godot mono not found')
    folder = ROOT / 'logs' / 'hit-feedback' / time.strftime('%Y%m%d-%H%M%S')
    folder.mkdir(parents=True)
    log = folder / 'engine.log'
    env = os.environ.copy()
    env['GP13_SHOT'] = str(folder / 'feedback.png')
    command = [str(exe), '--path', str(ROOT), '--log-file', str(log), 'res://scenes/HitFeedbackDev.tscn', '--']
    if args.probe:
        command += ['--gp13-probe']
    if args.no_shake:
        command += ['--no-shake']
    result = subprocess.run(command, env=env, timeout=90 if args.probe else None)
    if not args.probe:
        return result.returncode
    text = log.read_text(encoding='utf-8')
    good = valid_log(text, result.returncode)
    print(f'GP13 log={log}; EXIT={0 if good else 1}')
    return 0 if good else 1


if __name__ == '__main__':
    raise SystemExit(main())
