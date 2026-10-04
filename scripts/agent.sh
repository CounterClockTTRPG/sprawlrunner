#!/usr/bin/env bash
# Send commands to a game started with ./play.sh --agent and print its reply.
#   scripts/agent.sh "state"
#   scripts/agent.sh "drive fixer; key E; ui"
# Commands are separated by ';'. 'help' lists them. Waits up to 5 minutes.
set -uo pipefail
dir="$(cd "$(dirname "$0")/.." && pwd)/agent"
mkdir -p "$dir"
id="$(date +%s%N)"
printf '%s\n%s\n' "$id" "$*" > "$dir/cmd.tmp"
mv -f "$dir/cmd.tmp" "$dir/cmd.txt"
for _ in $(seq 1 1500); do
    if [ -f "$dir/out.txt" ] && [ "$(head -n 1 "$dir/out.txt")" = "$id" ]; then
        tail -n +2 "$dir/out.txt"
        exit 0
    fi
    sleep 0.2
done
echo "no reply in 5 minutes -- is the game running (./play.sh --agent)?"
exit 1
