# Performance Audit — Hex (GDScript Scope)

**Engine:** Godot 4.6 (GL Compatibility renderer, C# project)
**Audit Date:** 2026-05-15

## Executive Summary

**Health Score: 8/10**

The Hex project is primarily a **C# Godot 4 project**; the only `.gd` files reside under `android/build/src/instrumented/assets/` and are Godot's shipped Android plugin **instrumented test scaffolding** (not gameplay code). They contain no `_process` / `_physics_process` hot paths, no rendering or physics work, and run only during Android plugin test invocation. Identified issues are minor: redundant `Engine.get_singleton` and `JavaClassWrapper.wrap` calls (each is a cross-language / dictionary lookup) which have been cached.

## Files Audited

| File | LOC | Backup |
|------|-----|--------|
| `D:/Hex/android/build/src/instrumented/assets/main.gd` | 62 | `main.gd.bak` |
| `D:/Hex/android/build/src/instrumented/assets/test/base_test.gd` | 52 | `base_test.gd.bak` |
| `D:/Hex/android/build/src/instrumented/assets/test/file_access/file_access_tests.gd` | 84 | `file_access_tests.gd.bak` |
| `D:/Hex/android/build/src/instrumented/assets/test/javaclasswrapper/java_class_wrapper_tests.gd` | 171 | `java_class_wrapper_tests.gd.bak` |

Total: 4 files, 369 LOC. 3 files patched, 1 unchanged (`base_test.gd` — no perf issues).

## Fixes Applied

1. **`main.gd`** — Resolved `Engine.get_singleton("AndroidRuntime")` once in `_ready` into `_android_runtime`; both `_on_vibration_button_pressed` and `_on_gd_script_toast_button_pressed` now reuse the cached reference instead of re-resolving the singleton on every button press.
2. **`file_access_tests.gd`** — Added `_get_android_runtime()` and `_get_environment_class()` lazy caches for the `AndroidRuntime` singleton and the `android.os.Environment` `JavaClass`; six test methods now use cached lookups instead of repeating singleton/wrap calls.
3. **`java_class_wrapper_tests.gd`** — Added member-level `_TestClass` / `_TestClass2` / `_TestClass3` caches via `_get_test_class*()` helpers; eight test methods that previously each called `JavaClassWrapper.wrap('com.godot.game.test.javaclasswrapper.TestClass')` now share a single wrapped reference, reducing JNI class lookups during test runs.

All original behaviour, comments, and control flow are preserved. Every original file was backed up to `<file>.gd.bak` prior to editing.

## Project Settings Recommendations

The project's `project.godot` is consistent with a mobile / low-spec target — `gl_compatibility` renderer on both desktop and `.mobile`, ETC2/ASTC VRAM compression enabled, landscape orientation, expand stretch. Suggestions:

- **`application/run/low_processor_mode`** — consider enabling for the UI-only menus / debug modals to reduce idle CPU draw on Android.
- **`physics/2d/run_on_separate_thread`** — only relevant if the gameplay (C# `Hex/`, `Tokens/`, `Board/`) does heavy physics work; defer until profiling shows main-thread physics cost.
- **`rendering/textures/canvas_textures/default_texture_filter`** — set to *Nearest* if Hex artwork is pixel-art (saves bilinear sampling cost on mobile GPUs).
- **`rendering/limits/opengl/max_renderable_elements`** — default is fine; revisit if board tile counts grow.
- **`debug/settings/stdout/verbose_stdout`** — ensure disabled for release exports.
- The autoloads `DebugLog` and `GameSession` are C# — consider that on Android the .NET runtime startup is the largest single startup cost; pre-cache scenes via `ResourceLoader.load_threaded_request` from C# if cold-start is profiled as slow.

These project-settings notes are advisory; no `project.godot` changes were applied since the audit scope is GDScript files.

## Identified Issues

### CPU

- **[3/10]** `D:/Hex/android/build/src/instrumented/assets/main.gd:36` — `Engine.get_singleton("AndroidRuntime")` called per button press in `_on_vibration_button_pressed`. Singleton lookup is a hashed dictionary access on every invocation.
  *Fix applied:* cached in `_android_runtime` during `_ready`.
- **[3/10]** `D:/Hex/android/build/src/instrumented/assets/main.gd:54` — Same pattern in `_on_gd_script_toast_button_pressed`.
  *Fix applied:* uses cached `_android_runtime`.
- **[2/10]** `D:/Hex/android/build/src/instrumented/assets/main.gd:43` — `JavaClassWrapper.wrap("android.os.VibrationEffect")` called per vibration press. Wrap is a JNI class lookup.
  *Flagged only:* button is invoked rarely; caching across presses would add member state that the original template intentionally keeps local. Safe to leave.
- **[2/10]** `D:/Hex/android/build/src/instrumented/assets/main.gd:59` — `JavaClassWrapper.wrap("android.widget.Toast")` inside a `Callable` captured for `runOnUiThread`. Per-press JNI lookup on the UI thread.
  *Flagged only:* moving the wrap out of the lambda is logically fine but risks the wrapped reference being used cross-thread; conservative choice is to leave it.
- **[4/10]** `D:/Hex/android/build/src/instrumented/assets/test/javaclasswrapper/java_class_wrapper_tests.gd:29,42,53,63,82,119,127,128,129,145,154` — `JavaClassWrapper.wrap('...TestClass')` (and `TestClass2` / `TestClass3`) repeated across 8+ test functions; each `wrap` performs a JNI `FindClass` equivalent.
  *Fix applied:* introduced `_TestClass` / `_TestClass2` / `_TestClass3` lazy caches with `_get_test_class*()` accessors; all repeat sites now reuse them.
- **[3/10]** `D:/Hex/android/build/src/instrumented/assets/test/file_access/file_access_tests.gd:39,48,57,66` — `Engine.get_singleton("AndroidRuntime")` called per test method (4 test methods).
  *Fix applied:* `_get_android_runtime()` lazy cache.
- **[3/10]** `D:/Hex/android/build/src/instrumented/assets/test/file_access/file_access_tests.gd:75,81` — `JavaClassWrapper.wrap("android.os.Environment")` called twice in close succession.
  *Fix applied:* `_get_environment_class()` lazy cache.
- **[2/10]** `D:/Hex/android/build/src/instrumented/assets/test/base_test.gd:23` — `__get_stack_frame` calls `get_stack()` and linearly scans it. `get_stack()` is moderately expensive (debug-only API).
  *Flagged only:* invoked only on assertion failure paths; not hot. Changing it would alter diagnostic behaviour.

### Memory

- **[2/10]** `D:/Hex/android/build/src/instrumented/assets/main.gd:14-28` — `_launch_tests` constructs a new `BaseTest`-derived instance per request and does not free it. Test instances are RefCounted, so they will be cleaned up when references go out of scope; behaviour is correct but flagged for awareness.
  *Flagged only:* no pool needed — tests run once per Android plugin invocation.
- **[1/10]** `D:/Hex/android/build/src/instrumented/assets/test/javaclasswrapper/java_class_wrapper_tests.gd:54-57,137-138` — Per-call `Array[Object]` allocations passed to `testMethod` / `testObjectOverloadArray`.
  *Flagged only:* test-only allocations; not a hot path.

### Rendering

- No GDScript-side rendering work observed. `main.gd` extends `Node2D` but performs no drawing; rendering is driven by the scene (`scenes/game.tscn`) and C# code.
  *Flagged only:* nothing actionable in GDScript scope.

### Physics

- No `_physics_process` overrides and no physics queries in any audited `.gd` file.
  *Flagged only:* nothing actionable in GDScript scope.

### Architecture

- **[3/10]** `D:/Hex/android/build/src/instrumented/assets/main.gd:9` — `_android_plugin.connect("launch_tests", _launch_tests)` has no matching disconnect; if `main.gd` ever exited the tree before the plugin, the callable would leak. In practice this scene lives for the app lifetime.
  *Flagged only:* not worth a teardown hook for a single root-scene script.
- **[3/10]** `D:/Hex/android/build/src/instrumented/assets/main.gd:6-12` — `_ready` calls `get_tree().quit()` on missing plugin; acceptable for a test harness but couples the main scene to a debug singleton.
  *Flagged only:* this is test-harness scaffolding intentionally tied to the plugin.
- **[2/10]** `D:/Hex/android/build/src/instrumented/assets/test/base_test.gd` — `BaseTest` is a `@abstract class_name` without `extends`. With Godot 4.6 this is valid (RefCounted by default) but means test instances are reference-counted; cheap, just noted.
  *Flagged only:* no change.
- **[2/10]** `D:/Hex/android/build/src/instrumented/assets/test/javaclasswrapper/java_class_wrapper_tests.gd` and `file_access_tests.gd` — These files are part of Godot's shipped Android build template under `android/build/`. Edits will be overwritten the next time the user re-installs the Android build template from the editor.
  *Flagged only:* user requested in-place fixes; recommend keeping `.bak` files and re-applying after template re-install.

## Notes on the Project as a Whole

The bulk of `D:/Hex` is **C# code** (`scripts/Board`, `scripts/Hex`, `scripts/Tokens`, `scripts/UI`, plus autoloads `DebugLog.cs` and `GameSession.cs`). This audit's GDScript scope therefore does not cover gameplay performance. For a full project audit, a C#-focused pass over `scripts/` would be needed (allocations per frame, `_Process` overrides, signal connections, node lookups via `GetNode<T>` caching, etc.).
