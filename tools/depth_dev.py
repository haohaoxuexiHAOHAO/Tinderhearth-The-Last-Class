"""ENG-15 graphical depth sorting and shadow entry with strict probe log guard."""
import argparse
import os
import re
import subprocess
import time
from check_input_map import find_godot, ROOT

# 排序那一半：覆盖量自报、静止时的方向、交换后反转，以及**去屏幕上数像素**的那条。
SORTING = set('sorted-count order-back-first order-swaps overlap-front-visible'.split())
# 纵深偏移与影子那一半。
SHADOW = set('depth-offset-signed shadow-foot-on-land shadow-follows-depth '
             'shadow-ground-fixed shadow-shrinks-in-air shadow-restored-on-land'.split())
# 前提判据：这一轮的测量条件成立吗（物理帧与渲染帧 1:1、窗口没失焦）。它们不测玩法 ——
# 失焦会让需要持续输入的判据以「玩法坏了」的形状失败，把它们单列才分得开「这一轮不能算」。
PRECONDITIONS = {'tick-frame-1to1', 'focus-kept'}
EXPECTED = SORTING | SHADOW | PRECONDITIONS | {'screenshot'}


def valid_log(text, code):
    verdicts = re.findall(r'^\[ENG15\] (PASS|FAIL) (\S+)$', text, re.MULTILINE)
    summary = re.findall(r'^\[ENG15\] Summary (\d+)/(\d+)$', text, re.MULTILINE)
    marked = [line for line in text.splitlines()
              if line.startswith(('[ENG15] PASS', '[ENG15] FAIL', '[ENG15] Summary'))]
    return (code == 0 and len(marked) == len(verdicts) + len(summary)
            and len(verdicts) == len(EXPECTED) and {name for _, name in verdicts} == EXPECTED
            and all(v == 'PASS' for v, _ in verdicts)
            and summary == [(str(len(EXPECTED)), str(len(EXPECTED)))] and 'ERROR:' not in text)


def selfcheck():
    """守卫自证：每条判据的「缺失」与「FAIL」两种形状都要被拦下。"""
    lines = [f'[ENG15] PASS {name}' for name in sorted(EXPECTED)]
    summary = f'[ENG15] Summary {len(EXPECTED)}/{len(EXPECTED)}'
    good = '\n'.join(lines + [summary])
    bad = ['\n'.join(lines[1:] + [lines[1], summary]), '\n'.join(lines[1:] + [summary]),
           good.replace('PASS', 'FAIL', 1), good + '\n' + summary,
           good.replace(summary, '[ENG15] Summary 0/0'), good + '\nERROR: injected',
           good + '\n[ENG15] PASS extra', '\n'.join(lines), good + '\n[ENG15] PASS malformed extra']
    for name in sorted(EXPECTED):
        record = f'[ENG15] PASS {name}'
        bad.extend((good.replace(record + '\n', ''), good.replace(record, f'[ENG15] FAIL {name}')))
    assert valid_log(good, 0) and not valid_log(good, 1)
    assert all(not valid_log(text, 0) for text in bad)
    print(f'ENG15 log guard selfcheck {len(bad) + 2}/{len(bad) + 2}; expected={len(EXPECTED)}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--probe', action='store_true')
    # 交互模式（不带 --probe）是给作者看的，没有判据、要等他关窗口。--smoke 跑固定帧数就退出，
    # 用来证明**那个模式本身启动得起来、没报错** —— 交互模式坏了的话，判据全绿也看不出来。
    parser.add_argument('--smoke', type=int, metavar='FRAMES',
                        help='run the interactive scene for N frames and check the log for errors')
    args = parser.parse_args()
    selfcheck()
    exe = find_godot()
    if exe is None:
        raise SystemExit('Godot mono not found')
    folder = ROOT / 'logs' / 'depth' / time.strftime('%Y%m%d-%H%M%S')
    folder.mkdir(parents=True)
    log = folder / 'engine.log'
    env = os.environ.copy()
    env['ENG15_SHOT'] = str(folder / 'depth.png')
    command = [str(exe), '--path', str(ROOT), '--log-file', str(log)]
    if args.smoke:
        command += ['--quit-after', str(args.smoke)]
    command += ['res://scenes/DepthDev.tscn', '--']
    if args.probe:
        command += ['--eng15-probe']
    result = subprocess.run(command, env=env, timeout=90 if args.probe or args.smoke else None)
    if args.smoke:
        text = log.read_text(encoding='utf-8')
        errors = [line for line in text.splitlines()
                  if 'ERROR:' in line or 'SCRIPT ERROR' in line or 'Cannot call method' in line]
        good = result.returncode == 0 and not errors
        print(f'ENG15 smoke frames={args.smoke} exit={result.returncode} '
              f'errors={len(errors)}; log={log}; EXIT={0 if good else 1}')
        for line in errors[:5]:
            print(f'  {line}')
        return 0 if good else 1
    if not args.probe:
        return result.returncode
    text = log.read_text(encoding='utf-8')
    good = valid_log(text, result.returncode)
    print(f'ENG15 log={log}; EXIT={0 if good else 1}')
    return 0 if good else 1


if __name__ == '__main__':
    raise SystemExit(main())
