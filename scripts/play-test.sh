#!/usr/bin/env bash
# Input-driven playthrough: plays the real game in a window by injecting mouse
# and key events, checks the outcomes, writes shots/play_*.png.
#   scripts/play-test.sh [filter]   (mouse, moves, attack, tactical, editor, rivers)
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
build_if_stale
"$GODOT" "${RENDER_ARGS[@]}" --path "$PROJECT_UNC" --resolution 1600x900 -- --play "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
