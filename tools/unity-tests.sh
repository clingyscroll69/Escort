#!/bin/zsh
# Headless Unity for the checkout this script lives in (works in any git worktree; the project must not be open in
# another editor). Results and logs go to Escort/Logs/headless/ (git-ignored).
#
#   tools/unity-tests.sh EditMode [filter]      run EditMode tests (filter = NUnit name regex, e.g. "TutorialData")
#   tools/unity-tests.sh PlayMode [filter]      run PlayMode tests
#   tools/unity-tests.sh method <Class.Method>  run an editor method (-executeMethod ... -quit)
#
# Prints passed/failed/total, each failure's name and message, and any C# compile errors; exits non-zero on failure.
# UNITY=/path/to/Unity overrides the editor (default: the Hub install matching ProjectVersion.txt).
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/Escort"
VER=$(sed -n 's/^m_EditorVersion: //p' "$PROJ/ProjectSettings/ProjectVersion.txt")
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity}"
OUT="$PROJ/Logs/headless"
mkdir -p "$OUT"

mode="${1:-}"
arg="${2:-}"
stamp=$(date +%H%M%S)
case "$mode" in
  EditMode|PlayMode)
    name="${mode}_${stamp}"
    args=(-runTests -testPlatform "$mode" -testResults "$OUT/$name.xml")
    [[ -n "$arg" ]] && args+=(-testFilter "$arg")
    ;;
  method)
    [[ -z "$arg" ]] && { echo "usage: $0 method <Class.Method>"; exit 2; }
    name="method_${stamp}"
    args=(-executeMethod "$arg" -quit)
    ;;
  *)
    echo "usage: $0 EditMode|PlayMode [filter] | method <Class.Method>"; exit 2 ;;
esac

start=$(date +%s)
"$UNITY" -batchmode -projectPath "$PROJ" -logFile "$OUT/$name.log" "${args[@]}"
code=$?
secs=$(( $(date +%s) - start ))

errors=$(grep -E "error CS[0-9]+" "$OUT/$name.log" | sort -u)
if [[ -n "$errors" ]]; then
  echo "COMPILE ERRORS:"
  echo "$errors" | head -40
fi

if [[ "$mode" == "method" ]]; then
  grep -E "^(Exception|NullReferenceException|.*Exception:)" "$OUT/$name.log" | head -20
  echo "[unity-tests] method $arg exit=$code (${secs}s) log=$OUT/$name.log"
  exit $code
fi

if [[ ! -f "$OUT/$name.xml" ]]; then
  echo "[unity-tests] no results (exit=$code, ${secs}s) — see $OUT/$name.log"
  tail -30 "$OUT/$name.log"
  exit 1
fi

python3 - "$OUT/$name.xml" "$secs" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
a = root.attrib
print(f"[unity-tests] {a.get('result')}: passed {a.get('passed')} failed {a.get('failed')} skipped {a.get('skipped')} total {a.get('total')} ({sys.argv[2]}s)")
for tc in root.iter('test-case'):
    if tc.attrib.get('result') == 'Failed':
        msg = tc.find('failure/message')
        text = (msg.text or '').strip().replace('\n', ' ')[:600] if msg is not None else ''
        print(f"  FAIL {tc.attrib.get('fullname')}: {text}")
sys.exit(0 if a.get('result', '').startswith('Passed') else 1)
PY
