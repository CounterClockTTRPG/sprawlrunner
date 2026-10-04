#!/usr/bin/env bash
# Headless self-check. Exits non-zero if any assertion fails.
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
build_if_stale
"$GODOT" --headless --path "$PROJECT_UNC" -- --smoke "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
