#!/usr/bin/env python3
"""Agent helper around the Unity MCP bridge (tools/unity-mcp.mjs) + Library/Agent status files.

  u.py call <tool> [json]        call an MCP tool (retries while Unity reloads)
  u.py menu <Menu/Path>          execute a menu item
  u.py refresh                   AssetDatabase refresh, wait for compile + domain reload, print compile result
  u.py logs [error|warning|info|all] [limit]
  u.py capture <name> [json-extra]   render a camera to docs/qa/shots/<name>.png
  u.py play | stop | status
  u.py tests <EditMode|PlayMode> [filter]
"""
import json, os, subprocess, sys, time

ROOT = '/Users/sapnagoel/Documents/coding/Game'
MCP = ['node', f'{ROOT}/tools/unity-mcp.mjs']
AG = f'{ROOT}/Escort/Library/Agent'


def mcp(*args, retries=40, quiet=False):
    last = ''
    for i in range(retries):
        p = subprocess.run(MCP + list(args), capture_output=True, text=True)
        out = (p.stdout or '') + (p.stderr or '')
        transient = p.returncode != 0 and any(s in out for s in (
            'not connected', 'ECONNREFUSED', 'Connection', 'connect', 'timed out', 'Timeout', 'closed', 'socket'))
        if p.returncode == 0 and 'ERROR:' not in out[:20]:
            return out
        last = out
        if not transient and p.returncode != 0 and i > 2:
            break
        time.sleep(2)
    if not quiet:
        print(last)
    return last


def read(name):
    try:
        with open(os.path.join(AG, name)) as f:
            return json.load(f)
    except Exception:
        return None


def mtime(name):
    try:
        return os.path.getmtime(os.path.join(AG, name))
    except Exception:
        return 0


def wait_until(pred, timeout, step=1.0):
    t0 = time.time()
    while time.time() - t0 < timeout:
        if pred():
            return True
        time.sleep(step)
    return False


def refresh(timeout=600):
    c0, d0, r0 = mtime('compile.json'), mtime('domain.json'), mtime('refresh.json')
    menu_path = 'Tools/Agent/Refresh' if os.path.exists(os.path.join(AG, 'domain.json')) else 'Assets/Refresh'
    out = mcp('call', 'execute_menu_item', json.dumps({'menuPath': menu_path}))
    if menu_path == 'Tools/Agent/Refresh':
        wait_until(lambda: mtime('refresh.json') > r0 and (read('refresh.json') or {}).get('state') == 'done', 120, 0.5)
        rj = read('refresh.json') or {}
        compiling = rj.get('compiling', False)
    else:
        compiling = True
    started = wait_until(lambda: mtime('compile.json') > c0, 20 if not compiling else 120, 0.5)
    if not started:
        print('refresh: no compilation triggered')
        return 0
    wait_until(lambda: (read('compile.json') or {}).get('state') == 'finished', timeout, 1)
    cj = read('compile.json') or {}
    errs = cj.get('errors', -1)
    print(f"compile: errors={errs} warnings={cj.get('warnings')}")
    for m in cj.get('messages', [])[:60]:
        f = m['file'].split('Assets/')[-1]
        print(f"  {m['type']}: {f}:{m['line']} {m['msg'][:300]}")
    if errs == 0:
        wait_until(lambda: mtime('domain.json') > d0, 180, 1)
        # wait for MCP bridge to come back
        mcp('call', 'get_play_mode_status', quiet=True)
    return errs


def main():
    a = sys.argv[1:]
    if not a:
        print(__doc__); return
    cmd = a[0]
    if cmd == 'call':
        print(mcp('call', a[1], a[2] if len(a) > 2 else '{}'))
    elif cmd == 'menu':
        print(mcp('call', 'execute_menu_item', json.dumps({'menuPath': a[1]})))
    elif cmd == 'refresh':
        sys.exit(1 if refresh() != 0 else 0)
    elif cmd == 'logs':
        args = {'limit': int(a[2]) if len(a) > 2 else 30, 'includeStackTrace': False}
        if len(a) > 1 and a[1] != 'all':
            args['logType'] = a[1]
        out = mcp('call', 'get_console_logs', json.dumps(args))
        try:
            start = out.index('[')
            logs = json.loads(out[start:])
            for l in logs:
                msg = l['message']
                if 'McpUnityServerBatchModeTests' in msg:
                    continue
                print(f"[{l['type']}] {l['timestamp'][11:19]} {msg[:600]}")
        except Exception:
            print(out)
    elif cmd == 'capture':
        name = a[1]
        extra = json.loads(a[2]) if len(a) > 2 else {}
        args = {'name': name, **extra}
        with open(os.path.join(AG, 'capture_args.json'), 'w') as f:
            json.dump(args, f)
        c0 = mtime('capture.json')
        mcp('call', 'execute_menu_item', json.dumps({'menuPath': 'Tools/Agent/Capture'}))
        wait_until(lambda: mtime('capture.json') > c0, 30, 0.3)
        print(json.dumps(read('capture.json')))
    elif cmd in ('play', 'stop'):
        print(mcp('call', 'set_play_mode_status', json.dumps({'action': cmd})))
        time.sleep(3)
        print(mcp('call', 'get_play_mode_status'))
    elif cmd == 'status':
        print(mcp('call', 'get_play_mode_status'))
    elif cmd == 'tests':
        args = {'mode': a[1]}
        if len(a) > 2:
            args['filter'] = a[2]
        with open(os.path.join(AG, 'tests_args.json'), 'w') as f:
            json.dump(args, f)
        t0 = mtime('tests.json')
        for attempt in range(4):  # the trigger can be lost if it lands during a domain reload
            mcp('call', 'execute_menu_item', json.dumps({'menuPath': 'Tools/Agent/Run Tests'}))
            if wait_until(lambda: mtime('tests.json') > t0, 45, 1):
                break
        ok = wait_until(lambda: mtime('tests.json') > t0 and (read('tests.json') or {}).get('state') == 'finished', 1200, 1)
        d = read('tests.json') or {}
        if not ok:
            print('tests: timed out waiting for results', d)
            sys.exit(2)
        print(f"tests[{a[1]}]: {d.get('pass')} passed, {d.get('fail')} failed, {d.get('skip')} skipped")
        for r in d.get('results', []):
            if r['status'] == 'Failed':
                print(f"  FAIL {r['name']}: {r['message'][:600]}")
                for line in r['stack'].splitlines()[:4]:
                    print('      ' + line[:220])
                if r.get('output'):
                    print('      output: ' + r['output'][:600].replace(chr(10), ' | '))
        sys.exit(1 if d.get('fail') else 0)
    else:
        print(__doc__)


if __name__ == '__main__':
    main()
