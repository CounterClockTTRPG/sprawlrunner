#!/usr/bin/env bash
# Run the game windowed under remote control: it reads agent/cmd.txt and
# answers in agent/out.txt. Drive it with scripts/agent.sh. Uses its own
# career file (user://agent_career.json), never the player's.
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
build_if_stale
mkdir -p "$PROJECT_DIR/agent"
"$GODOT" "${RENDER_ARGS[@]}" --path "$PROJECT_UNC" --resolution 1600x900 -- --agent "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
