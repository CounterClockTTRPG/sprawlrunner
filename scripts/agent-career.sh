#!/usr/bin/env bash
# A scripted agent career: N days of the city's UI the way a player meets it --
# creation (and skipping the intro), driving to every contact (scenes waiting at the door), calls that
# ring mid-drive, the inbox, the morning card, sleeping at home. It plays no
# runs (that takes a playtester); it is the career layer's smoke test through
# the real screens. Starts the game in agent mode if it isn't running.
#
#   bash scripts/agent-career.sh [DAYS]      (default 20)
#
# Writes agent/career-log.txt: every command and reply, then a summary.
set -uo pipefail
cd "$(dirname "$0")/.."
DAYS="${1:-20}"
LOG=agent/career-log.txt
mkdir -p agent
: > "$LOG"
PROBLEMS=0

a() {
    local out
    out="$(bash scripts/agent.sh "$1")"
    printf '> %s\n%s\n' "$1" "$out" >> "$LOG"
    case "$out" in
        *"unknown command"*|*xception*|*"no reply in"*|*"not in the city"*|*"no button matching"*) PROBLEMS=$((PROBLEMS + 1)); echo "  problem: $1 -> $(printf '%s' "$out" | head -1)" ;;
    esac
    printf '%s' "$out"
}

# Answer whatever dialogs are up (a call, a scene, mail, the morning card).
clear_dialogs() {
    for _ in 1 2 3 4 5 6; do
        case "$(a "answer")" in *"no dialog"*|*"nothing to press"*) return ;; esac
    done
}

# Drive to a place; a call that rings on the way is answered and the drive resumed.
go() {
    for _ in 1 2 3; do
        a "drive $1" > /dev/null
        if a "state" | grep -q "dialog"; then clear_dialogs; else return; fi
    done
}

if ! timeout 3 bash scripts/agent.sh "state" > /dev/null 2>&1; then
    echo "starting the game in agent mode..."
    ./play.sh --agent > agent/game.log 2>&1 &
    for _ in $(seq 1 120); do grep -q "agent: listening" agent/game.log 2>/dev/null && break; sleep 1; done
fi

echo "creating a career"
a "key Esc" > /dev/null
a "press New career" > /dev/null
# Over an old agent career: confirm starting over.
a "ui" | grep -q "Start over" && a "press Start over" > /dev/null
a "wait 300" > /dev/null
a "press Hit the streets" > /dev/null
# The intro: Esc skips it.
a "wait 800" > /dev/null
a "key Esc" > /dev/null
a "wait 1500" > /dev/null
clear_dialogs

PLACES="fixer doc arms rigger decker fence talismonger"
for day in $(seq 1 "$DAYS"); do
    echo "day $day"
    for p in $PLACES; do
        go "$p"
        a "key E" > /dev/null
        clear_dialogs
        a "key Esc" > /dev/null
        clear_dialogs
    done
    a "key P" > /dev/null
    a "press Inbox" > /dev/null
    a "key Esc" > /dev/null
    go home
    a "key E" > /dev/null
    clear_dialogs
    a "press Sleep until tomorrow" > /dev/null
    clear_dialogs
    a "key Esc" > /dev/null
    clear_dialogs
    a "state" | grep -m1 "^career:" | sed 's/^/  /'
done

{ echo; echo "== $DAYS days, $PROBLEMS problems"; } >> "$LOG"
echo "$DAYS days, $PROBLEMS problems (log: $LOG)"
[ "$PROBLEMS" -eq 0 ]
