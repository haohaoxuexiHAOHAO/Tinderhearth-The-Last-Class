"""Run the GP-12 development scene, optionally with a graphical physics probe.

进仓精灵表的帧数在两处各有一份真相：登记表（`tools/asset-registry.json`，由
`tools/import_role_sheets.py` 从原件量出）与引擎实际切出来的帧数（`PlayerActor` 启动时
打 `[GP12] Sheet <名>=<帧数>`）。引擎读不到登记表（`tools/` 带 `.gdignore`，不进包），
所以两者相等由本入口在 Python 侧核 —— 见 `sheet_counts` 与 `valid_log`。

对不上的表现是「动画少一帧或多一帧」，既不报错也不崩，所以必须有人拿两个独立来源比一次。
"""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import time
from check_input_map import find_godot, ROOT


# GP-15 纵深轴的接线判据。规则层单测钉三轴分离本身，这四条钉「引擎那一段真的接上了」：
# 门面的移动向量 Y 变成纵深输入、符号没写反、纵深没漏进引擎持有的 X／Y、离地锁住落地解锁。
DEPTH = set('depth-front depth-back depth-air-lock depth-land-unlock depth-walk-visual'.split())

EXPECTED = DEPTH | set('floor move dash jump air-attack land dodge-exclusive heavy light-sprite startup-frame active-frame controller-replaced screenshot ceiling-hit ceiling-next-frame dodge-18-ticks dodge-ledge-fall dodge-ledge-collision ledge-landed frame-count foot-row body-height sprite-phase dodge-sprite tick-frame-1to1 focus-kept'.split()) | {
    f'{kind}-{step}-{phase}' for kind, count in (('light', 3), ('heavy', 2))
    for step in range(1, count + 1) for phase in ('startup', 'active', 'recovery')}

REGISTRY = ROOT / 'tools' / 'asset-registry.json'
# PlayerActor 本轮载入的表。hit/defense/death 已入仓但刻意不载（有图没规则），所以不在这里。
LOADED_SHEETS = ('idle', 'walk', 'run', 'jump', 'dodge', 'light', 'heavy')
SHEET_LINE = re.compile(r'^\[GP12\] Sheet (\w+)=(\d+)$', re.MULTILINE)


def registry_counts():
    """登记表里这几张表各几帧。读不出就抛 —— 核不了必须响，不许静默跳过。"""
    data = json.loads(REGISTRY.read_text(encoding='utf-8'))
    by_name = {Path(e['path']).stem: e for e in data['自绘素材']}
    missing = [name for name in LOADED_SHEETS if name not in by_name]
    if missing:
        raise SystemExit(f'登记表「自绘素材」缺 {missing}；先跑 python tools/import_role_sheets.py')
    return {name: by_name[name]['帧数'] for name in LOADED_SHEETS}


def sheet_counts(text):
    """引擎自己报的每张表帧数。"""
    return {name: int(count) for name, count in SHEET_LINE.findall(text)}


def valid_log(text, code, expect_counts=None):
    verdicts = re.findall(r'^\[GP12\] (PASS|FAIL) (\S+)$', text, re.MULTILINE)
    summary = re.findall(r'^\[GP12\] Summary (\d+)/(\d+)$', text, re.MULTILINE)
    marked = [line for line in text.splitlines() if line.startswith(('[GP12] PASS', '[GP12] FAIL', '[GP12] Summary'))]
    counts_ok = expect_counts is None or sheet_counts(text) == expect_counts
    return (len(marked) == len(verdicts) + len(summary) and code == 0 and len(verdicts) == len(EXPECTED)
            and {name for _, name in verdicts} == EXPECTED
            and all(v == 'PASS' for v, _ in verdicts) and counts_ok
            and summary == [(str(len(EXPECTED)), str(len(EXPECTED)))] and 'ERROR:' not in text)


def selfcheck():
    lines = [f'[GP12] PASS {name}' for name in sorted(EXPECTED)]
    summary = f'[GP12] Summary {len(EXPECTED)}/{len(EXPECTED)}'
    good = '\n'.join(lines + [summary])
    bad = ['\n'.join(lines[1:] + [lines[1], summary]), '\n'.join(lines[1:] + [summary]),
           good.replace('PASS', 'FAIL', 1), good + '\n' + summary,
           good.replace(summary, '[GP12] Summary 0/0'), good + '\nERROR: injected',
           good + '\n[GP12] PASS extra', '\n'.join(lines), good + '\n[GP12] PASS malformed extra']
    # 新增名称逐个注入「缺失」与「FAIL」两种形状：漏报一条判据和报了但失败，是两种不同的失效。
    for name in sorted(name for name in EXPECTED
                       if name in DEPTH or name in ('frame-count', 'foot-row', 'body-height',
                                                    'sprite-phase', 'dodge-sprite', 'startup-frame',
                                                    'tick-frame-1to1', 'focus-kept')):
        record = f'[GP12] PASS {name}'
        bad.extend((good.replace(record + '\n', ''), good.replace(record, f'[GP12] FAIL {name}')))
    assert valid_log(good, 0) and not valid_log(good, 1)
    assert all(not valid_log(text, 0) for text in bad)
    # 帧数比对也要自证：引擎少报一张表、报错一个帧数、一条都不报，三种都必须判失败。
    counts = registry_counts()
    sheets = '\n'.join(f'[GP12] Sheet {name}={n}' for name, n in counts.items())
    assert valid_log(sheets + '\n' + good, 0, counts)
    bad_counts = [good, sheets.replace(f'={counts["idle"]}', f'={counts["idle"] + 1}', 1) + '\n' + good,
                  '\n'.join(sheets.splitlines()[1:]) + '\n' + good]
    assert all(not valid_log(text, 0, counts) for text in bad_counts)
    total = len(bad) + len(bad_counts) + 3
    print(f'GP12 log guard selfcheck {total}/{total}; expected={len(EXPECTED)}; '
          f'sheet counts from registry={counts}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--probe', action='store_true')
    args = parser.parse_args()
    selfcheck()
    expect_counts = registry_counts()
    from PIL import Image
    # 打的是**主角实际用的那批**（自绘件）。原来打的是 samurai 四表，那是主角还在用下载件时的
    # 遗留；主角换成自绘表之后，打它等于在看一批与本场景无关的图（samurai 仍被 CameraHarness 用）。
    for sheet in LOADED_SHEETS:
        with Image.open(ROOT / 'assets' / 'self-drawn' / 'test-role' / f'{sheet}.png') as image:
            print(f'Sheet {sheet}: size={image.size}, frames={expect_counts[sheet]}, '
                  f'alpha bounds={image.getchannel("A").getbbox()}')
    exe = find_godot()
    if exe is None:
        raise SystemExit('Godot mono not found')
    folder = ROOT / 'logs' / 'player' / time.strftime('%Y%m%d-%H%M%S')
    folder.mkdir(parents=True)
    log = folder / 'engine.log'
    env = os.environ.copy()
    env['GP12_SHOT'] = str(folder / 'player.png')
    command = [str(exe), '--path', str(ROOT), '--log-file', str(log), 'res://scenes/PlayerDev.tscn']
    if args.probe:
        command += ['--', '--gp12-probe']
    result = subprocess.run(command, env=env, timeout=90 if args.probe else None)
    if not args.probe:
        return result.returncode
    text = log.read_text(encoding='utf-8')
    verdicts = re.findall(r'\[GP12\] (PASS|FAIL) (\S+)', text)
    good = valid_log(text, result.returncode, expect_counts)
    print(f'GP12 {sum(v == "PASS" for v, _ in verdicts)}/{len(verdicts)}; '
          f'sheet frames engine={sheet_counts(text)} registry={expect_counts}; '
          f'log={log}; EXIT={0 if good else 1}')
    return 0 if good else 1


if __name__ == '__main__':
    raise SystemExit(main())
