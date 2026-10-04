#!/usr/bin/env bash
# SPRAWLRUNNER -- start the game.
#
#   ./play.sh                 play
#   ./play.sh --fullscreen    play full screen (F11 toggles in-game too)
#   ./play.sh --editor        open the project in the Godot editor
#   ./play.sh --build         compile the C# assembly and stop
#   ./play.sh --check         headless self-test of the simulation
#   ./play.sh --shots         visual walkthrough, writes shots/*.png
#   ./play.sh --agent         remote-controlled play for an agent (see scripts/agent.sh)
#
# Needs the Godot 4.7 .NET build. The default path suits this machine; override
# it in .env (copy .env.example) or with GODOT=... on the command line.
set -uo pipefail
cd "$(dirname "$0")"

case "${1:-}" in
    --editor)  shift; exec bash scripts/editor.sh "$@" ;;
    --build)   shift; exec bash scripts/build.sh "$@" ;;
    --check)   shift; exec bash scripts/smoke.sh "$@" ;;
    --shots)   shift; exec bash scripts/shots.sh "$@" ;;
    --agent)   shift; exec bash scripts/agent-run.sh "$@" ;;
    --help|-h) sed -n '3,13p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
esac

source scripts/common.sh
require_godot
build_if_stale

ARGS=()
while [ $# -gt 0 ]; do
    case "$1" in
        --fullscreen) ARGS+=(--fullscreen) ;;
        *)            ARGS+=("$1") ;;
    esac
    shift
done

exec "$GODOT" "${RENDER_ARGS[@]}" --path "$PROJECT_UNC" "${ARGS[@]}"
