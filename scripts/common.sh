#!/usr/bin/env bash
# Shared setup for every tool script.
#
# The Godot binary on this machine is a Windows executable driven from WSL, so
# the project has to be handed to it as a UNC path into the WSL filesystem, and
# its stdout arrives with CRLF line endings that every caller strips.

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Local overrides live in .env (gitignored). Anything set there wins over the
# defaults below, because of the ${VAR:-default} form.
if [ -f "$PROJECT_DIR/.env" ]; then
    set -a
    # shellcheck disable=SC1091
    source "$PROJECT_DIR/.env"
    set +a
fi

# Must be the .NET ("mono") build -- this project is C#, and the plain build
# cannot load a C# assembly at all.
GODOT_DIR="${GODOT_DIR:-/mnt/c/data/tools/Godot_v4.7.2-stable_mono_win64}"
GODOT="${GODOT:-$GODOT_DIR/Godot_v4.7.2-stable_mono_win64_console.exe}"

PROJECT_UNC="\\\\wsl.localhost\\${WSL_DISTRO_NAME:-Ubuntu}$(printf '%s' "$PROJECT_DIR" | tr '/' '\\')"
ASSEMBLY="$PROJECT_DIR/.godot/mono/temp/bin/Debug/Sprawlrunner.dll"

# Renderer. Forward+ compiles SdfgiDebugShaderRD at startup and the Intel UHD
# 620 Vulkan driver on this machine dies doing it -- the process vanishes inside
# a second with no error and no backtrace, before the first frame. Nothing here
# uses SDFGI; the mobile renderer is still Vulkan and simply drops that path.
# Set RENDER_METHOD=forward_plus once the GPU driver is new enough.
RENDER_METHOD="${RENDER_METHOD:-mobile}"
RENDER_ARGS=(--rendering-method "$RENDER_METHOD")

require_godot() {
    [ -x "$GODOT" ] && return 0
    cat >&2 <<MSG
Godot not found at:
  $GODOT

This project needs the Godot 4.7 .NET build. Point GODOT at it, either in .env
(copy .env.example) or on the command line:

  GODOT=/path/to/Godot_v4.7.2-stable_mono_win64_console.exe ./play.sh
MSG
    exit 127
}

# Compiling the C# assembly.
#
# Godot can do it -- `--headless --build-solutions --quit` -- but on this machine
# that takes a minute and a half and then does not exit: the assembly appears on
# disk and the process sits there until it is killed. So the assembly is built
# with the .NET SDK directly, which takes four seconds and returns a real exit
# code. Godot.NET.Sdk already puts the output exactly where Godot looks for it
# (.godot/mono/temp/bin/Debug), so nothing else has to know the difference.
#
# A Windows dotnet.exe driven from WSL needs the project as a UNC path, the same
# way the Godot binary does; a Linux-native dotnet takes the ordinary path.
DOTNET="${DOTNET:-}"
if [ -z "$DOTNET" ]; then
    if command -v dotnet >/dev/null 2>&1; then
        DOTNET="$(command -v dotnet)"
        CSPROJ_ARG="$PROJECT_DIR/Sprawlrunner.csproj"
    elif [ -x "/mnt/c/Program Files/dotnet/dotnet.exe" ]; then
        DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
        CSPROJ_ARG="$PROJECT_UNC\\Sprawlrunner.csproj"
    fi
else
    case "$DOTNET" in
        *.exe) CSPROJ_ARG="$PROJECT_UNC\\Sprawlrunner.csproj" ;;
        *)     CSPROJ_ARG="$PROJECT_DIR/Sprawlrunner.csproj" ;;
    esac
fi

require_dotnet() {
    [ -n "$DOTNET" ] && [ -x "$DOTNET" ] && return 0
    cat >&2 <<MSG
No .NET SDK found. This project is C# and needs one to compile.

Looked for 'dotnet' on PATH and for:
  /mnt/c/Program Files/dotnet/dotnet.exe

Set DOTNET to your SDK, or install one -- .NET 8 or newer will do, because the
assembly targets net8.0 and Godot rolls it forward.
MSG
    exit 127
}

# Only rebuilds when a source file is newer than the assembly. The stale case is
# worth guarding: Godot loads whatever assembly is on disk without complaint, so
# running a stale build looks exactly like a change that did not work.
build_if_stale() {
    if [ -f "$ASSEMBLY" ]; then
        local newer
        newer="$(find "$PROJECT_DIR/src" "$PROJECT_DIR/tests" \
            "$PROJECT_DIR/project.godot" "$PROJECT_DIR/Sprawlrunner.csproj" \
            -newer "$ASSEMBLY" -print -quit 2>/dev/null)"
        [ -z "$newer" ] && return 0
    fi
    require_dotnet
    echo "Sources changed -- building..." >&2
    local out status
    out="$("$DOTNET" build "$CSPROJ_ARG" -v q --nologo 2>&1 | tr -d '\r')"
    status=$?
    if [ "$status" -ne 0 ] || [ ! -f "$ASSEMBLY" ]; then
        printf '%s\n' "$out" | grep -E "error|Error" | sort -u >&2
        printf '%s\n' "$out" | tail -5 >&2
        echo "build failed" >&2
        exit 1
    fi
    # Warnings are worth seeing but not worth stopping for.
    printf '%s\n' "$out" | grep -E ": warning [A-Z]+[0-9]+" | sort -u >&2 || true
    return 0
}
