#!/usr/bin/env bash
# Visual walkthrough: runs the real game with a window and writes shots/*.png.
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
build_if_stale
"$GODOT" "${RENDER_ARGS[@]}" --path "$PROJECT_UNC" --resolution 1600x900 -- --shots "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
