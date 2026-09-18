#!/usr/bin/env bash
# Launch a real, playable Atomcraft with ONLY this mod loaded, for hands-on testing.
#
#   ./play.sh            # build the mod and launch the game on your display
#   ./play.sh --debug    # turn on the mod's diagnostic overlay for this play session
#   ./play.sh --verify   # boot headless under Xvfb, confirm the mod loaded, and exit
#
# What this does and does not touch:
#   - It runs against a hardlinked copy of the harness's already-patched game install, in a
#     throwaway Wine prefix. It never patches or launches your real Steam copy, and its saves
#     live in that private prefix, so your real worlds are untouched.
#   - The copy's Mods folder holds this mod and the PixelArt library it draws through, and
#     nothing else. The test harness is kept out: it suppresses the game's own automatic saves,
#     which is right for a test run and ruinous for a play session.
#   - --debug used to be the one exception to that, because the diagnostic overlay drew through
#     the harness and so had to live in the test mod. It draws through PixelArt now and ships
#     with the mod, so --debug is just a settings key and this script no longer loads the harness
#     for any reason. The autosaves stay.
#   - EXTRA_MODS="../Other/build/Other.zip" ./play.sh installs companions too, for when the
#     combination is what you want to judge.
#
# Prerequisite: a patched game copy must already exist (the harness makes one). If it does
# not, this stops and tells you.
set -euo pipefail
cd "$(dirname "$0")"

MOD_ID="PixelInspector"
MOD_NAME="PixelInspector"
# shellcheck disable=SC1091
. lib/harness.sh
require_harness

# shellcheck disable=SC1091
. "$HARNESS/lib/common.sh"
load_config
require_runner

VERIFY=0
DEBUG=0
for arg in "$@"; do
  case "$arg" in
    --verify) VERIFY=1 ;;
    --debug)  DEBUG=1 ;;
    *) echo "unknown argument: $arg (expected --verify or --debug)" >&2; exit 1 ;;
  esac
done

# --- the patched source install --------------------------------------------------------------
SRC="$INSTALL"                    # from the harness config: the patched game copy
BACKUP="$SRC/$DATA_DIR_NAME/Atomcraft.dll.backup"
if [ ! -f "$SRC/AtomCraft.exe" ] || [ ! -f "$BACKUP" ]; then
  cat >&2 <<MSG
No patched game copy at:
  $SRC
This script runs against the harness's patched copy so it never modifies your Steam install.
Create one first:
  ( cd $HARNESS && ./bootstrap.sh )
Once it exists, re-run this script.
MSG
  exit 1
fi

# --- build the mod ---------------------------------------------------------------------------
# --debug also needs the test mod, whose Initialize registers the Alt-held overlay. Stage the
# harness assembly first, because build.sh only compiles the test mod when it can already see
# that assembly. It comes out of the pinned zip -- the same bytes the loader will run -- rather
# than from a harness checkout that might be mid-edit.
require_pixelart

echo "==> building the $MOD_NAME mod"
./build.sh >/dev/null
ZIP="build/$MOD_ID.zip"
[ -f "$ZIP" ] || { echo "build did not produce $ZIP" >&2; exit 1; }

# --- an isolated play install: the patched copy, this mod the only mod ------------------------
PLAY_ROOT="${PLAY_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/atomcraft-$(printf '%s' "$MOD_ID" | tr '[:upper:]' '[:lower:]')-play}"
PLAY_INSTALL="$PLAY_ROOT/install"
PLAY_PREFIX="$PLAY_ROOT/prefix"

# Refresh the clone whenever the patched source has moved on, so a game update
# (re-bootstrapped upstream) is picked up. Hardlinked, so it costs almost nothing.
#
# Compared on .bootstrap.json rather than on a file mtime, and that is not fussiness: this used
# to test AtomCraft.exe, which is the stock Godot export template and is byte-identical across
# game builds. Its mtime therefore never changed, the clone was never refreshed, and a play
# session ran against a game three builds older than the one the mods were compiled against --
# silently, because the mods loaded and ran anyway. The bootstrap record is the one file that
# is written every time the source is provisioned, and it names the buildid.
SRC_STAMP="$SRC/.bootstrap.json"
PLAY_STAMP="$PLAY_INSTALL/.bootstrap.json"
if [ ! -f "$PLAY_INSTALL/AtomCraft.exe" ] || ! cmp -s "$SRC_STAMP" "$PLAY_STAMP"; then
  echo "==> cloning the patched game copy into $PLAY_INSTALL"
  rm -rf "$PLAY_INSTALL"
  mkdir -p "$PLAY_ROOT"
  cp -al "$SRC" "$PLAY_INSTALL" 2>/dev/null || cp -a "$SRC" "$PLAY_INSTALL"
fi

echo "==> installing $MOD_ID with PixelArt, which it draws through"
rm -f "$PLAY_INSTALL/Mods/"*.zip 2>/dev/null || true
mkdir -p "$PLAY_INSTALL/Mods"
cp "$ZIP" "$PLAY_INSTALL/Mods/$MOD_ID.zip"

# The drawing library. Not optional and not a companion: mod.json names it as a hard dependency,
# so without it the loader reports this mod in error and loads nothing of it.
cp "$PIXELART_ZIP" "$PLAY_INSTALL/Mods/PixelArt.zip"


for extra in ${EXTRA_MODS:-}; do
  [ -f "$extra" ] || { echo "no such mod zip: $extra" >&2; exit 1; }
  echo "==> also installing $(basename "$extra")"
  cp "$extra" "$PLAY_INSTALL/Mods/"
done

mkdir -p "$PLAY_PREFIX"

# launch_game reads these globals; point them at the isolated play copy.
INSTALL="$PLAY_INSTALL"
PREFIX="$PLAY_PREFIX"

# --- the diagnostic overlay ------------------------------------------------------------------
# It ships with the mod now and is read from the settings file, so turning it on is a matter of
# writing one key rather than of loading the harness. The file may not exist yet -- the mod
# writes it with the defaults on first run -- so this only edits one that is already there and
# otherwise leaves a note; a second ./play.sh --debug then finds it.
if [ "$DEBUG" = 1 ]; then
  SETTINGS=""
  if USER_DIR="$(user_dir 2>/dev/null)"; then
    SETTINGS="$USER_DIR/$MOD_ID.json"
  fi
  if [ -n "$SETTINGS" ] && [ -f "$SETTINGS" ]; then
    python3 - "$SETTINGS" <<'PY'
import json, sys
path = sys.argv[1]
with open(path) as f:
    settings = json.load(f)
settings["debugOverlay"] = True
with open(path, "w") as f:
    json.dump(settings, f, indent=4)
    f.write("\n")
print(f"==> debugOverlay=true in {path}")
PY
  else
    echo "==> no settings file yet at ${SETTINGS:-<prefix not created>}; the mod writes one with"
    echo "    the defaults on its first run. Quit, then ./play.sh --debug again to switch the"
    echo "    overlay on -- or edit debugOverlay yourself."
  fi
fi

# --- launch ---------------------------------------------------------------------------------
if [ "$VERIFY" = 1 ]; then
  # Boot headless-with-a-framebuffer just long enough to confirm the loader picked the mod
  # up, then quit. Uses a private Xvfb so nothing lands on your desktop.
  command -v Xvfb >/dev/null 2>&1 || { echo "Xvfb required for --verify" >&2; exit 1; }
  DISP=":$(( (RANDOM % 400) + 100 ))"
  Xvfb "$DISP" -screen 0 1920x1080x24 -nolisten tcp >/dev/null 2>&1 &
  XPID=$!
  trap 'kill "$XPID" 2>/dev/null || true' EXIT
  sleep 1

  LOG_DIR="$PLAY_ROOT/out"; mkdir -p "$LOG_DIR"
  echo "==> verify boot (Xvfb $DISP), quitting after a few seconds"
  set +e
  HEADFUL=1 DISPLAY="$DISP" timeout --foreground -k 5 120 \
    bash -c '. "'"$HARNESS"'/lib/common.sh"; load_config
             INSTALL="'"$PLAY_INSTALL"'"; PREFIX="'"$PLAY_PREFIX"'"; HEADFUL=1
             launch_game -s GodotMonoModLoader.gd --audio-driver Dummy --quit-after 900' \
    >"$LOG_DIR/verify.log" 2>&1
  set -e

  GLOG="$(HEADFUL=1 PREFIX="$PLAY_PREFIX" godot_log 2>/dev/null || true)"
  if [ -n "$GLOG" ] && grep -qa "\[$MOD_ID\] initialized" "$GLOG"; then
    echo "==> OK"
    grep -a "\[$MOD_ID\]" "$GLOG" | tail -3 | sed 's/^/    /'
    echo "    loaded mods: $(grep -aoE 'Loading Mod: [A-Za-z.]+' "$GLOG" | sed 's/Loading Mod: //' | sort -u | tr '\n' ' ')"
    exit 0
  fi
  echo "==> FAILED: no '[$MOD_ID] initialized' in the game log" >&2
  [ -n "$GLOG" ] && echo "    log: $GLOG" >&2
  exit 1
fi

echo "==> launching Atomcraft with $MOD_ID on display ${DISPLAY:-:0}"
echo "    install: $PLAY_INSTALL"
echo "    prefix:  $PLAY_PREFIX  (saves here are separate from your real game)"
echo "    settings: <prefix>/.../app_userdata/Atomcraft/$MOD_ID.json"
[ "$DEBUG" = 1 ] && echo "    debug:    debugOverlay=true; hold Alt to see the annotated block outlined"
export DISPLAY="${DISPLAY:-:0}"
HEADFUL=1 launch_game -s GodotMonoModLoader.gd
