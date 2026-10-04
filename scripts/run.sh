#!/usr/bin/env bash
# Run the game windowed.
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
build_if_stale
"$GODOT" "${RENDER_ARGS[@]}" --path "$PROJECT_UNC" "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
