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
EXPECTED |= {f'run-{check}' for check in ('ready', 'frozen-sequence', 'first-resume',
                                         'full-speed', 'release-first', 'normal-motion', 'direction-release')}
# ART-6：判定框按轻/重分开之后的行为级判据 —— 同一距离轻击打空、重击打到。
# 再加**按段**分开的行为级判据（2026-09-11）：连段推到第 3 段，同一距离第 2 段直拳打空、第 3 段
# 踢腿打到；前提判据钉那个距离由常量导出、且真的落在「第 2 段够不到、第 3 段够得到」的区间里。
REACH_SPLIT = {'reach-split-shape', 'light-reach-miss', 'heavy-reach-hit', 'reach-split-drained',
               'segment-light2-miss', 'segment-kick-hit', 'probe-segment-reach-derived',
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
# GP-20：精灵角色的命中白闪。既有的 flash-pixel 判的是**木桩几何**（换 Polygon2D 颜色），精灵那条
# 链完全不同（modulate 乘白无效，要着色器），所以另立四条：命中前不是白的（反证，缺了它取样点落在
# 浅色处会假绿）、命中当帧开关到位、屏幕上真的白了、按真实帧自己灭（中间不推进战斗，于是它同时
# 证明白闪不被顿帧拉长）。
HIT_FLASH = {'sprite-flash-baseline', 'sprite-flash-armed', 'sprite-flash-pixel',
             'sprite-flash-expired'}
EXPECTED |= HIT_FLASH
# GP-20：命中声音。**机器判不了「好不好听」也判不了「扬声器真的响了」**（后者取决于本机有没有
# 音频设备，当判据会让门禁随环境变红），所以只判能判的：三类采样真的载到（顺带证明登记表路径与
# .import 在运行时解析得出来）、命中当帧播放计数真的加一、音高落在登记幅度内且确实抖出了不同值。
HIT_AUDIO = {'audio-clips-loaded', 'audio-hit-plays', 'audio-pitch-jitter', 'audio-miss-plays'}
EXPECTED |= HIT_AUDIO
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
    for name in sorted({n for n in EXPECTED if n.startswith(('jump-', 'attack-light-', 'attack-heavy-', 'run-'))} | REACH_SPLIT | DEPTH_TOLERANCE | HIT_FLASH | HIT_AUDIO | PRECONDITIONS):
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
