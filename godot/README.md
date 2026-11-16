# Godot 4 independent project (P1 / #54)

This is an **additive, visual-only 3D project scaffold**. It cannot play the original game yet and does not read or write any Python JSON files. The original Python game remains the reference; do not ship this as a replacement.

## Pinned toolchain

- **Godot 4.7.2 stable**, official standard editor (GDScript, no .NET). Pinned as `GODOT_TAG=4.7.2-stable` in CI. Do **not** use Godot 4.8 beta for baseline parity.
- Renderer: **Forward+** as intended production target; try `--rendering-method gl_compatibility` when diagnosing older GPU/driver support. Headless CI has **no GPU image validation**, so it cannot prove GPU renderer quality or performance.
- Logical contract: `1280×720`, `60 physics ticks/sec`, prototype 0.01 world units/pixel. The scene holds an overhead **orthographic Camera3D**, flat floor and one placeholder 3D player mesh. These are placeholders for P2's calibrated mapping, NOT a new game.
- GPU/driver compatibility matrix for P10: Vulkan-capable Forward+ device(s) and OpenGL 3.3 Compatibility fallback. Record GPU model, driver, FPS p95, and visual differences rather than assuming identical shading.

## Local start

1. Install official [Godot 4.7.2](https://godotengine.org/download/archive/4.7.2-stable/).
2. Open `godot/project.godot` with Godot Project Manager or launch from repository root:
   - `godot --editor --path godot`
   - `godot --headless --editor --path godot --import --quit`
   - `godot --headless --path godot --script res://tests/port_smoke.gd`
   - `godot --path godot` to view the primitive 3D scene.
3. Python legacy remains available at `python neon_void.py`.

## CI and export status

- `.github/workflows/godot-ci.yml` runs **both Windows and Linux** on relevant PRs: download pinned engine, headless import, GDScript parse, scripted smoke, scene boot. All standard Python validation workflows remain untouched.
- Windows x64 export build is **separate and opt-in through workflow_dispatch**, because the official export-template archive is over 1.2 GB. Its first success must be recorded before P1 can be marked complete, and the generated EXE needs a later actual Windows execution/render check. The export preset does not include signing or a production installer.
- Godot caches `godot/.godot/` and local `godot/build/` are ignored. Do not commit user settings, credentials, export cache or generated binaries.
- The current scene is not a complete game and P0 actual Windows visual baselines remain pending. **Do not merge P1 before P0's defined acceptance gate is satisfied.**
