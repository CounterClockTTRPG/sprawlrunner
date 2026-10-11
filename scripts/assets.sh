#!/usr/bin/env bash
# SPRAWLRUNNER -- generate the pictures and the spoken lines, via ../assetgen.
#
#   bash scripts/assets.sh               generate anything missing
#   bash scripts/assets.sh --list        say what is missing, generate nothing
#   bash scripts/assets.sh --force NAME  regenerate one, even if it exists
#
# Reads assets/art/manifest.txt and assets/voice/manifest.txt and writes
# assets/art/<name>.webp, assets/voice/<name>.ogg and assets/video/<name>.ogv. Everything about talking
# to a model -- the Replicate token, the style prompt, the voice characters,
# the cost history -- lives in assetgen's `sprawlrunner` project; this script
# only knows names, prompts and the sizes the game draws at.
#
# It skips what exists so a re-run is free, and asks the library before it asks
# Replicate so a run that died half way does not pay twice. The results are
# kept in the repository, so building, playing and checking the game never need
# a token or the network -- and the game draws its own art for anything missing.
set -uo pipefail
cd "$(dirname "$0")/.."

ASSETGEN="${ASSETGEN:-../assetgen}"
export ASSETGEN_PROJECT="${ASSETGEN_PROJECT:-sprawlrunner}"
CLI="$ASSETGEN/.venv/bin/assetgen"
PY="$ASSETGEN/.venv/bin/python"

# Committed sizes, from where each picture appears: a portrait is a 340x300
# panel at a contact and 120 px on the PDA; a banner spans the 700 px briefing;
# the title art fills a 1600 px window.
PORTRAIT_PIXELS="${PORTRAIT_PIXELS:-512}"
BANNER_PIXELS="${BANNER_PIXELS:-1024}"
TITLE_PIXELS="${TITLE_PIXELS:-1600}"

if [ ! -x "$CLI" ]; then
    cat >&2 <<MSG
No assetgen at $ASSETGEN.

The pictures and voices are generated there -- it holds the Replicate token,
the project's style and characters, and the cost history. Point ASSETGEN at
it, or see its README. The files already in assets/ are enough to play.
MSG
    exit 1
fi
command -v ffmpeg >/dev/null || { echo "ffmpeg is needed to turn speech into Ogg Vorbis" >&2; exit 1; }

mode="generate"; only=""
case "${1:-}" in
    --list)  mode="list" ;;
    --force) mode="force"; only="${2:?--force needs a name}" ;;
    "")      ;;
    *)       echo "unknown option: $1" >&2; exit 2 ;;
esac

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
MISSING=0; FAILED=0; COUNT=0

# Whether this entry should be (re)made now; prints the --list line itself.
wanted() {
    local name="$1" target="$2"
    if [ "$mode" = "force" ]; then [ "$name" = "$only" ]; return; fi
    [ -f "$target" ] && return 1
    if [ "$mode" = "list" ]; then echo "  missing  $name"; MISSING=$((MISSING + 1)); return 1; fi
    return 0
}

# Ask the library first: only generate when assetgen has no asset by this name.
# Forcing one that exists re-runs its stored request as a new version of the
# same asset (`regen`), so the name keeps meaning one thing in the library.
generate() {
    local name="$1"; shift
    local shown
    if shown="$("$CLI" show "$name" 2>/dev/null)"; then
        if printf '%s\n' "$shown" | grep -q '^  error '; then
            # A run that failed (a refused prompt, a model hiccup) leaves an
            # asset with no versions; drop it and ask again.
            echo y | "$CLI" rm "$name" >/dev/null
            "$CLI" new --name "$name" "$@" >/dev/null
            return
        fi
        if [ "$mode" = "force" ]; then "$CLI" regen "$name" >/dev/null; return; fi
        echo "    in the library -- fetching"
        return 0
    fi
    "$CLI" new --name "$name" "$@" >/dev/null
}

# A picture painted over another one (`1:1 < hire-wisp`): image-to-image on
# flux-dev, so a crew member at home or in love keeps the face of their portrait;
# the strength keeps the face and takes the scene, clothes and mood from the prompt.
FROM_STRENGTH="${FROM_STRENGTH:-0.8}"

picture() {
    local name="$1" aspect from="" prompt="$3" target="assets/art/$1.webp" px="$PORTRAIT_PIXELS"
    aspect="$(echo "${2%%<*}" | xargs)"
    case "$2" in *'<'*) from="$(echo "${2#*<}" | xargs)" ;; esac
    [ "$aspect" = "1:1" ] || px="$BANNER_PIXELS"
    case "$name" in title-*|intro-*|ending-*) px="$TITLE_PIXELS" ;; esac
    wanted "$name" "$target" || return 0
    local extra=()
    if [ -n "$from" ]; then
        echo "  $name (from $from)"
        # The library keeps the full-size original; the committed webp will do if it hasn't.
        # By exact name: assetgen matches id prefixes first, and `hire-ox` prefixes `hire-ox-home`.
        local id
        id="$("$CLI" ls --kind image --json | "$PY" -c "import json, sys; print(next((a['id'] for a in json.load(sys.stdin) if a['name'] == sys.argv[1]), ''))" "$from")"
        if [ -z "$id" ] || ! "$CLI" get "$id" --out "$TMP/$from.png" >/dev/null 2>&1; then
            [ -f "assets/art/$from.webp" ] || { echo "    FAILED: no $from to start from" >&2; FAILED=$((FAILED + 1)); return 0; }
            "$PY" -c "from PIL import Image; Image.open('assets/art/$from.webp').convert('RGB').save('$TMP/$from.png')"
        fi
        extra=(--model flux-dev --image "$TMP/$from.png" --param "prompt_strength=$FROM_STRENGTH")
    else
        echo "  $name"
    fi
    if ! generate "$name" --kind image "${extra[@]}" --param "aspect_ratio=$aspect" --text "$prompt"; then
        echo "    FAILED to generate" >&2; FAILED=$((FAILED + 1)); return 0
    fi
    if ! "$CLI" get "$name" --out "$TMP/$name.png" >/dev/null || ! "$PY" scripts/fit.py "$TMP/$name.png" "$target" "$px"; then
        echo "    FAILED to fetch or fit" >&2; FAILED=$((FAILED + 1)); return 0
    fi
    echo "    ${px}px  $(du -h "$target" | cut -f1)"
}

line() {
    local name="$1" voice="$2" text="$3" target="assets/voice/$1.ogg"
    wanted "$name" "$target" || return 0
    echo "  $name ($voice)"
    if ! generate "$name" --kind speech --voice "$voice" --text "$text"; then
        echo "    FAILED to generate" >&2; FAILED=$((FAILED + 1)); return 0
    fi
    # Mono Vorbis at 32 kHz: Godot decodes Ogg from bytes at runtime, and a
    # greeting is a few kilobytes rather than the model's ~70 KB of MP3.
    if ! "$CLI" get "$name" --out "$TMP/$name.audio" >/dev/null \
        || ! ffmpeg -nostdin -loglevel error -y -i "$TMP/$name.audio" -ac 1 -ar 32000 -c:a libvorbis -q:a 4 "$target"; then
        echo "    FAILED to fetch or convert" >&2; FAILED=$((FAILED + 1)); return 0
    fi
    echo "    $(du -h "$target" | cut -f1)"
}

# A clip: image-to-video from one of the pictures, then Ogg Theora for Godot.
clip() {
    local name="$1" frame="$2" prompt="$3" target="assets/video/$1.ogv"
    wanted "$name" "$target" || return 0
    echo "  $name (from $frame)"
    if [ ! -f "assets/art/$frame.webp" ]; then echo "    FAILED: no assets/art/$frame.webp to start from" >&2; FAILED=$((FAILED + 1)); return 0; fi
    "$PY" -c "from PIL import Image; Image.open('assets/art/$frame.webp').convert('RGB').save('$TMP/$frame.png')"
    if ! generate "$name" --kind video --image "$TMP/$frame.png" --param duration=5 --param resolution=720p --param aspect_ratio=16:9 --text "$prompt"; then
        echo "    FAILED to generate" >&2; FAILED=$((FAILED + 1)); return 0
    fi
    if ! "$CLI" get "$name" --out "$TMP/$name.mp4" >/dev/null \
        || ! ffmpeg -nostdin -loglevel error -y -i "$TMP/$name.mp4" -an -vf scale=1280:720 -c:v libtheora -q:v 7 "$target"; then
        echo "    FAILED to fetch or convert" >&2; FAILED=$((FAILED + 1)); return 0
    fi
    echo "    $(du -h "$target" | cut -f1)"
}

# Reads `a | b | rest` lines, skipping blanks and comments, and calls $1 with them.
each() {
    local fn="$1" manifest="$2" raw a b c
    echo "assets: $manifest"
    while IFS= read -r raw || [ -n "$raw" ]; do
        case "$raw" in ''|'#'*) continue ;; esac
        a="$(echo "$raw" | cut -d'|' -f1 | xargs)"
        b="$(echo "$raw" | cut -d'|' -f2 | xargs)"
        c="$(echo "$raw" | cut -d'|' -f3- | sed 's/^ *//; s/ *$//')"
        if [ -z "$a" ] || [ -z "$b" ] || [ -z "$c" ]; then echo "  malformed line: $raw" >&2; exit 1; fi
        COUNT=$((COUNT + 1))
        "$fn" "$a" "$b" "$c" </dev/null   # nothing in there may eat the manifest
    done < "$manifest"
}

each picture assets/art/manifest.txt
each line assets/voice/manifest.txt
each clip assets/video/manifest.txt

if [ "$mode" = "list" ]; then echo "assets: $COUNT in the manifests, $MISSING missing"; exit 0; fi
if [ "$FAILED" -gt 0 ]; then echo "assets: $FAILED failed" >&2; exit 1; fi
echo "assets: $COUNT in the manifests, all present"
