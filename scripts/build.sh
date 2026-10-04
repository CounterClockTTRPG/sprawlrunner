#!/usr/bin/env bash
# Compile the C# assembly and stop. Prints nothing on success.
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
rm -f "$ASSEMBLY" 2>/dev/null
build_if_stale
echo "built: $ASSEMBLY"
