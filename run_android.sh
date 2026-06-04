#!/usr/bin/env bash
# Build and run Hex on a connected Android device via CLI.
#
# Prerequisites (one-time setup):
#   1. Godot 4.4 (or later) editor installed.
#   2. Android export template installed (Editor > Manage Export Templates).
#   3. Android SDK + JDK paths set in Godot Editor Settings > Export > Android.
#   4. An "Android" preset in export_presets.cfg (Project > Export > Add > Android,
#      set a unique package name and a debug keystore, then save).
#   5. adb on PATH (from Android Platform Tools).
#   6. Device connected via USB with USB debugging enabled, or over adb wireless.
#
# Overrides via environment:
#   GODOT=/path/to/godot       Path to Godot editor binary
#   ADB=/path/to/adb           Path to adb binary
#   PRESET="Android"           Export preset name in export_presets.cfg
#   BUILD_MODE=debug|release   Default: debug
#   PACKAGE=com.example.app    Override package name (otherwise parsed from preset)
#   DEVICE=<serial>            Target a specific adb device
#   LOGCAT=1                   Tail logcat after launch
#   VERBOSE=1                  Stream full Godot/gradle output to stdout
#                              (default: log to file, only print errors)

set -euo pipefail

PROJECT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)"
PRESET="${PRESET:-Android}"
BUILD_MODE="${BUILD_MODE:-debug}"
BUILD_DIR="$PROJECT_DIR/build/android"
APK="$BUILD_DIR/hex-$BUILD_MODE.apk"

# --- Resolve godot -----------------------------------------------------------
resolve_godot() {
    if [[ -n "${GODOT:-}" ]]; then echo "$GODOT"; return; fi
    # Prefer the .NET / Mono build first — required for projects with C# scripts.
    for c in godot-mono Godot-mono \
             "$HOME/DoT/Godot_v4.6.2-stable_mono_linux_x86_64/Godot_v4.6.2-stable_mono_linux.x86_64" \
             godot Godot \
             "$HOME/DoT/Godot_v4.6.2-stable_linux.x86_64" \
             "/opt/godot/godot" \
             "/usr/local/bin/godot"; do
        if command -v "$c" &>/dev/null; then command -v "$c"; return; fi
        if [[ -x "$c" ]]; then echo "$c"; return; fi
    done
    echo ""
}

resolve_adb() {
    if [[ -n "${ADB:-}" ]]; then echo "$ADB"; return; fi
    if command -v adb &>/dev/null; then command -v adb; return; fi
    for c in "$HOME/DoT/android-sdk/platform-tools/adb" \
             "$HOME/Android/Sdk/platform-tools/adb" \
             "/opt/android-sdk/platform-tools/adb"; do
        if [[ -x "$c" ]]; then echo "$c"; return; fi
    done
    echo ""
}

GODOT_BIN="$(resolve_godot)"
ADB_BIN="$(resolve_adb)"

if [[ -z "$GODOT_BIN" ]]; then
    echo "ERROR: Godot not found. Set GODOT=/path/to/godot" >&2; exit 1
fi
if [[ -z "$ADB_BIN" ]]; then
    echo "ERROR: adb not found. Set ADB=/path/to/adb or add platform-tools to PATH" >&2; exit 1
fi
if [[ ! -f "$PROJECT_DIR/export_presets.cfg" ]]; then
    echo "ERROR: export_presets.cfg missing. Open the project in Godot and create an" >&2
    echo "       '$PRESET' export preset (Project > Export > Add > Android)." >&2
    exit 1
fi

# --- Preflight sanity checks -------------------------------------------------
if [[ "${SKIP_PREFLIGHT:-0}" != "1" ]]; then
    PREFLIGHT="$PROJECT_DIR/scripts/tools/preflight.sh"
    if [[ -x "$PREFLIGHT" ]]; then
        GODOT_BIN="$GODOT_BIN" PROJECT_DIR="$PROJECT_DIR" "$PREFLIGHT" || {
            echo "ERROR: Preflight failed. Fix the issues above, or set SKIP_PREFLIGHT=1 to bypass." >&2
            exit 1
        }
    fi
fi

# --- Device check ------------------------------------------------------------
ADB_ARGS=()
if [[ -n "${DEVICE:-}" ]]; then ADB_ARGS=(-s "$DEVICE"); fi

DEVICE_COUNT=$("$ADB_BIN" devices | awk 'NR>1 && $2=="device"' | wc -l)
if [[ "$DEVICE_COUNT" -eq 0 ]]; then
    echo "ERROR: No Android device attached. Run 'adb devices' and authorize it." >&2
    exit 1
fi

# --- Export ------------------------------------------------------------------
mkdir -p "$BUILD_DIR"
echo ">> Exporting $PRESET ($BUILD_MODE) -> $APK"
echo "   (Godot's headless exporter doesn't stream step-by-step % progress."
echo "    First runs can take several minutes while gradle/templates warm up.)"
EXPORT_FLAG="--export-debug"
[[ "$BUILD_MODE" == "release" ]] && EXPORT_FLAG="--export-release"

START_TS=$(date +%s)
EXPORT_LOG="$BUILD_DIR/export.log"
: > "$EXPORT_LOG"

# Background heartbeat so the user sees the script is alive even when Godot
# goes quiet (e.g. during gradle / resource packing).
(
    while true; do
        sleep 15
        NOW=$(date +%s); EL=$((NOW - START_TS))
        printf "   ... still exporting (%02d:%02d elapsed)\n" $((EL/60)) $((EL%60))
    done
) &
HEARTBEAT_PID=$!
trap 'kill "$HEARTBEAT_PID" 2>/dev/null || true' EXIT

set +e
if [[ "${VERBOSE:-0}" == "1" ]]; then
    "$GODOT_BIN" --headless --verbose --path "$PROJECT_DIR" "$EXPORT_FLAG" "$PRESET" "$APK" 2>&1 \
        | tee "$EXPORT_LOG" \
        | awk -v start="$START_TS" '{
            el = systime() - start;
            printf "  [%02d:%02d] %s\n", int(el/60), el%60, $0;
            fflush();
        }'
    EXPORT_STATUS=${PIPESTATUS[0]}
else
    # Quiet mode: capture everything to log; only surface ERROR/FAILURE lines
    # and the gradle "What went wrong" block live.
    "$GODOT_BIN" --headless --path "$PROJECT_DIR" "$EXPORT_FLAG" "$PRESET" "$APK" >"$EXPORT_LOG" 2>&1 &
    GODOT_PID=$!
    # Tail the log and print only the lines that matter.
    tail -n +1 -F --pid="$GODOT_PID" "$EXPORT_LOG" 2>/dev/null \
        | grep --line-buffered -E '^(ERROR|FAILURE|BUILD FAILED|\* What went wrong:|> )' &
    TAIL_PID=$!
    wait "$GODOT_PID"
    EXPORT_STATUS=$?
    # Let the tail flush, then stop it.
    sleep 0.3
    kill "$TAIL_PID" 2>/dev/null || true
    wait "$TAIL_PID" 2>/dev/null || true
fi
set -e

kill "$HEARTBEAT_PID" 2>/dev/null || true
trap - EXIT

END_TS=$(date +%s); TOTAL=$((END_TS - START_TS))
printf ">> Export finished in %02d:%02d (exit=%s)\n" $((TOTAL/60)) $((TOTAL%60)) "$EXPORT_STATUS"
echo "   Full log: $EXPORT_LOG"

if [[ "$EXPORT_STATUS" -ne 0 ]]; then
    echo "ERROR: Godot export exited with status $EXPORT_STATUS" >&2
    echo "--- last 40 lines of $EXPORT_LOG ---" >&2
    tail -n 40 "$EXPORT_LOG" >&2 || true
    exit "$EXPORT_STATUS"
fi
if [[ ! -f "$APK" ]]; then
    echo "ERROR: Export finished but $APK was not produced." >&2
    exit 1
fi
APK_SIZE=$(du -h "$APK" 2>/dev/null | awk '{print $1}')
echo ">> APK: $APK (${APK_SIZE:-unknown size})"

# --- Resolve package name ----------------------------------------------------
if [[ -z "${PACKAGE:-}" ]]; then
    PACKAGE=$(awk -F'"' '/package\/unique_name/ {print $2; exit}' "$PROJECT_DIR/export_presets.cfg" || true)
fi
if [[ -z "${PACKAGE:-}" ]]; then
    echo "WARN: Could not parse package name from export_presets.cfg. Set PACKAGE=com.your.app" >&2
fi

# --- Install + launch --------------------------------------------------------
echo ">> Installing on device"
"$ADB_BIN" "${ADB_ARGS[@]}" install -r "$APK"

if [[ -n "${PACKAGE:-}" ]]; then
    echo ">> Launching $PACKAGE"
    # Launch via `am start`, NOT `adb shell monkey`: the monkey tool enables the
    # device's global Auto-Rotate (settings system accelerometer_rotation=1) as a
    # side effect of setting up its test environment, and leaves it on after the
    # app exits. `am start` behaves like tapping the launcher icon — no settings change.
    LAUNCH_ACT="$("$ADB_BIN" "${ADB_ARGS[@]}" shell cmd package resolve-activity --brief "$PACKAGE" 2>/dev/null | tail -1 | tr -d '\r')"
    [[ "$LAUNCH_ACT" == */* ]] || LAUNCH_ACT="$PACKAGE/com.godot.game.GodotAppLauncher"
    "$ADB_BIN" "${ADB_ARGS[@]}" shell am start -n "$LAUNCH_ACT" >/dev/null
    if [[ "${LOGCAT:-0}" == "1" ]]; then
        echo ">> Tailing logcat (Ctrl+C to stop)"
        "$ADB_BIN" "${ADB_ARGS[@]}" logcat -v time godot:V "*:S"
    fi
fi

echo ">> Done."
echo ">> Finished: $(date '+%A, %B %-d, %Y at %-I:%M %p')"
