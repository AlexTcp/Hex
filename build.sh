#!/usr/bin/env bash
# =============================================================================
# build.sh — build / import / test / run Hex via the Godot 4.6 mono (C#) exe.
#
# Wraps the Windows Godot mono build so you don't have to remember the long
# headless invocations. Works from Git Bash / MSYS2 (uses cygpath/wslpath to hand
# Windows-style paths to the Godot .exe). The C# build uses `dotnet`.
#
# Usage:
#   ./build.sh [command]
#
# Commands:
#   build    dotnet build Hex.csproj (fast C# type-check, no engine reload)
#   import   headless asset/class reimport (run after adding/renaming class_name)
#   test     dotnet build, then run the gdUnit4 C# suites headless
#   run      dotnet build, then launch the game window   [default]
#   all      build + import + test
#   help     show this help
#
# Config (override via environment):
#   GODOT    path to the Godot mono executable (forward slashes recommended).
#            Defaults to the console build on this machine; falls back to a
#            `godot`/`godot4` found on PATH.
# =============================================================================
set -euo pipefail

# --- Resolve the project directory (this script's folder) --------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Convert a POSIX/MSYS path to a Windows-style path the Godot .exe understands.
to_win() {
	if command -v cygpath >/dev/null 2>&1; then
		cygpath -m "$1"            # mixed: forward-slash Windows path (D:/Hex)
	elif command -v wslpath >/dev/null 2>&1; then
		wslpath -w "$1"
	else
		printf '%s\n' "$1"
	fi
}

PROJECT_WIN="$(to_win "$SCRIPT_DIR")"
CSPROJ="$SCRIPT_DIR/Hex.csproj"

# --- Locate the Godot mono executable ----------------------------------------
DEFAULT_GODOT="C:/Users/AlexT/Desktop/Godot_v4.6.2-stable_mono_win64/Godot_v4.6.2-stable_mono_win64_console.exe"
GODOT="${GODOT:-$DEFAULT_GODOT}"

resolve_godot() {
	if [[ -x "$GODOT" || -f "$GODOT" ]]; then
		return 0
	fi
	for c in godot godot4 godot-mono Godot; do
		if command -v "$c" >/dev/null 2>&1; then
			GODOT="$(command -v "$c")"
			return 0
		fi
	done
	echo "ERROR: Godot mono executable not found." >&2
	echo "  Tried: $GODOT" >&2
	echo "  Set the GODOT env var, e.g.:" >&2
	echo "    GODOT='C:/path/to/Godot_..._mono_win64_console.exe' ./build.sh run" >&2
	exit 1
}

# --- Actions ------------------------------------------------------------------
do_build() {
	echo ">> dotnet build $CSPROJ"
	dotnet build "$CSPROJ"
}

do_import() {
	resolve_godot
	echo ">> Godot headless import ($PROJECT_WIN)"
	"$GODOT" --headless --import --path "$PROJECT_WIN"
}

do_test() {
	do_build
	resolve_godot
	echo ">> Godot headless gdUnit4 suite"
	"$GODOT" --headless --path "$PROJECT_WIN" \
		-s -d res://addons/gdUnit4/bin/GdUnitCmdTool.gd \
		--ignoreHeadlessMode -a res://tests
}

do_run() {
	do_build
	resolve_godot
	echo ">> Launching Hex ($PROJECT_WIN)"
	"$GODOT" --path "$PROJECT_WIN"
}

# --- Dispatch -----------------------------------------------------------------
cmd="${1:-run}"
case "$cmd" in
	build) do_build ;;
	import) do_import ;;
	test) do_test ;;
	run) do_run ;;
	all) do_build; do_import; do_test ;;
	help|-h|--help) awk 'NR==1{next} /^#/{sub(/^# ?/,"");print;next} {exit}' "${BASH_SOURCE[0]}" ;;
	*)
		echo "Unknown command: $cmd" >&2
		echo "Run './build.sh help' for usage." >&2
		exit 2
		;;
esac