#!/usr/bin/env python3
"""QA: capture the opening at exact opening-clock times (HS.QA.OpeningScrub) → docs/qa/shots/<prefix>_NN_tSS_s.png

  opening_scrub.py <prefix> "<t1;t2;...>" [short]

Builds Assets/_Game/Scenes/QA_Opening.unity from the args, enters play mode, waits for Library/Agent/scrub.json.
"""
import json, os, subprocess, sys, time

ROOT = '/Users/sapnagoel/Documents/coding/Game'
AG = f'{ROOT}/Escort/Library/Agent'
U = ['python3', f'{ROOT}/tools/u.py']


def mtime(name):
    try:
        return os.path.getmtime(os.path.join(AG, name))
    except OSError:
        return 0


def wait(pred, timeout, step=0.5):
    t0 = time.time()
    while time.time() - t0 < timeout:
        if pred():
            return True
        time.sleep(step)
    return False


def main():
    prefix, times = sys.argv[1], sys.argv[2]
    short = len(sys.argv) > 3 and sys.argv[3] == 'short'
    with open(os.path.join(AG, 'scrub_args.json'), 'w') as f:
        json.dump({'prefix': prefix, 'times': times, 'short': '1' if short else '0'}, f)
    b0 = mtime('builder.json')
    subprocess.run(U + ['menu', 'Tools/HS/QA/Build Opening Scrub Scene'], capture_output=True, text=True)
    if not wait(lambda: mtime('builder.json') > b0, 60):
        print('scrub: scene build did not report'); sys.exit(2)
    s0 = mtime('scrub.json')
    subprocess.run(U + ['play'], capture_output=True, text=True)
    ok = wait(lambda: mtime('scrub.json') > s0, 600, 1.0)
    if not ok:
        print('scrub: timed out')
        subprocess.run(U + ['stop'], capture_output=True, text=True)
        sys.exit(2)
    with open(os.path.join(AG, 'scrub.json')) as f:
        files = json.load(f)['files']
    for name in files:
        print(f'{ROOT}/docs/qa/shots/{name}')
    time.sleep(2)  # the scrubber exits play mode itself


if __name__ == '__main__':
    main()
