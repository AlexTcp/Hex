#!/usr/bin/env bash
# Thin wrapper: delegates to the shared Android exporter in the Bus root, passing
# this project's name. Env overrides (GODOT, ADB, PRESET, BUILD_MODE, PACKAGE,
# DEVICE, LOGCAT, VERBOSE, SKIP_PREFLIGHT) are documented in ../export_android.sh.
DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)"
exec "$DIR/../export_android.sh" "$(basename "$DIR")" "$@"
