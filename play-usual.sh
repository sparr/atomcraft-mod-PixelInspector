#!/usr/bin/env bash
# Launch a play session with the mods this one is normally judged alongside.
#
#   ./play-usual.sh            # build and launch, detached, on your display
#   ./play-usual.sh --debug    # the same, with the mod's diagnostic overlay on
#   ./play-usual.sh --verify   # boot headless, confirm all four mods load, and exit
#
# A thin wrapper over ./play.sh. Everything it says about isolation holds: a hardlinked copy of
# the patched install, a throwaway prefix, your real Steam copy and worlds untouched. All
# arguments are passed through.
#
# What it adds, and why each is worth a wrapper rather than a note in a README:
#
#   - The companions. ActualResolution and IntegerZoom change the render target, the camera and
#     the UI scale, which are the three things this mod's panel is laid out against. Judging the
#     panel without them is judging it in a configuration nobody plays in.
#
#   - A kept log. play.sh runs in the foreground and writes its own output nowhere; this puts
#     stderr in a NEW timestamped file every launch. That matters because a Godot crash prints
#     its stack to stderr and nowhere else -- the game's own godot.log ends mid-sentence with no
#     hint of the cause. A crash went undiagnosable once because the previous launch's stderr had
#     been overwritten, so this never reuses a path.
#
#   - Detachment. setsid, so the session outlives the terminal it was started from.
#
#   - The game's PID, which is the one to kill. A Proton launch leaves three processes matching
#     the install path -- the python wrapper, steam.exe, and the game -- and killing the wrapper
#     leaves the game running. The one printed here is the game.
set -euo pipefail
cd "$(dirname "$0")"

# Built by their own projects; this does not build them, so a stale zip is a stale companion.
COMPANIONS=(
  ../ActualResolution/build/ActualResolution.zip
  ../IntegerZoom/build/IntegerZoom.zip
)

missing=()
for zip in "${COMPANIONS[@]}"; do
  [ -f "$zip" ] || missing+=("$zip")
done
if [ ${#missing[@]} -gt 0 ]; then
  printf '==> missing companion mod zip(s):\n' >&2
  printf '      %s\n' "${missing[@]}" >&2
  printf '    build each in its own project (./build.sh) and try again.\n' >&2
  exit 1
fi

LOG_DIR="${PLAY_LOG_DIR:-$HOME/.cache/atomcraft-pixelinspector-play/logs}"
mkdir -p "$LOG_DIR"
LOG="$LOG_DIR/play-$(date +%Y%m%d-%H%M%S).log"

# The game's own process, as distinct from the two Proton wrappers that share its command line.
#
# Deliberately NOT pgrep -f. That matches its argument as a REGULAR EXPRESSION, and the game's
# command line is the Wine spelling of the install path -- Z:\home\sparr\.cache\... -- which is
# nothing but backslash escapes to a regex. It matched nothing, ever, so this script sat out its
# whole two-minute wait on every launch and then reported a failure while the game ran happily.
#
# So: fixed-string matching on a ps snapshot, and the Wine spelling is used only as an anchor.
# All three processes carry the install path and "AtomCraft.exe"; only the game's own argv starts
# with the drive letter.
game_pid() {
  ps -eo pid=,args= \
    | grep -F 'AtomCraft.exe' \
    | grep -F 'atomcraft-pixelinspector-play' \
    | awk '$2 ~ /^Z:/ { print $1; exit }'
}

# --verify boots headless and exits on purpose, so there is no session to detach from and no PID
# to report. Run it in the foreground and hand back its verdict.
for arg in "$@"; do
  if [ "$arg" = "--verify" ]; then
    echo "==> verifying with ActualResolution and IntegerZoom"
    EXTRA_MODS="${COMPANIONS[*]}" exec nice -n 19 ./play.sh "$@"
  fi
done

EXTRA_MODS="${COMPANIONS[*]}" setsid nice -n 19 ./play.sh "$@" >"$LOG" 2>&1 </dev/null &

echo "==> launching with ActualResolution and IntegerZoom"
echo "    stderr: $LOG"

launcher=$!

for _ in $(seq 1 60); do
  sleep 2
  pid="$(game_pid || true)"
  [ -n "$pid" ] && break
  # The launcher giving up is the other way this ends, and waiting out the full two minutes for
  # a build error nobody is going to fix by waiting is no use to anyone.
  kill -0 "$launcher" 2>/dev/null || break
done

if [ -z "${pid:-}" ]; then
  echo "==> the game did not start; see $LOG" >&2
  exit 1
fi

echo "    game pid: $pid  (kill this one, not the proton wrapper)"
