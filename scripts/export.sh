#!/usr/bin/env bash
# Export a Windows build: Sprawlrunner.exe, its pack and the .NET data folder,
# plus the generated assets beside it (the .gdignore that keeps Godot's
# importer out of assets/ also keeps them out of the pack; Assets.Root finds
# them next to the executable).
#
#   bash scripts/export.sh [WINDOWS_DIR]     (default C:/temp/Sprawlrunner)
#
# Then it runs the self-check against the exported executable.
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
OUT_WIN="${1:-C:/temp/Sprawlrunner}"
OUT_WSL="$(wslpath "$OUT_WIN")"
mkdir -p "$OUT_WSL"
rm -rf "${OUT_WSL:?}"/*
"$GODOT" --headless --path "$PROJECT_UNC" --export-release "Windows Desktop" "$OUT_WIN/Sprawlrunner.exe" 2>&1 | tr -d '\r' | grep -E "ERROR|DONE" | grep -v "Storing"
cp -r "$PROJECT_DIR/assets" "$OUT_WSL/"
rm -f "$OUT_WSL/assets/.gdignore"
echo "exported to $OUT_WIN; checking the build..."
(cd "$OUT_WSL" && ./Sprawlrunner.exe --headless -- --smoke 2>&1 | tr -d '\r' | grep -E "FAIL|failed:|checks,")
