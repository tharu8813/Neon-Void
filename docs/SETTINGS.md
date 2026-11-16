# P1: Player settings

NEON VOID settings are saved in `settings.json` beside `save.json`, but the two files are independent. The original save file remains untouched by this feature.

## Current controls

- From the title: press **O** or click **게임 설정**.
- During a run: pause with **P** (or Esc), then press **O** or click the new menu entry.
- In the settings overlay: **Up/Down** (or **W/S**) chooses a row, **Left/Right** (or **A/D**) changes it, **Enter** toggles the chosen row, **Esc/O** closes.
- Clicking each row changes its setting; for volume the left/right half steps down/up by 10%.
- **M** in ordinary gameplay or on the title toggles mute and persists it.

## Available preferences

| Preference | Saved? | Behavior |
| --- | --- | --- |
| Master volume (0%–100%, 10% steps) | Yes | Changes software mixer SFX and background music gain immediately; native `winsound` fallback has no independent per-effect gain control |
| Mute | Yes | Silences the mixer and clears pending voices |
| Reduced effects | Yes | Disables screen shake and moving scanline; particles are unchanged |
| Show FPS | Yes | Toggles FPS, average/p95 render time, Canvas item count and current process RSS (Windows/Linux) |
| Display mode | Yes | Resizable window / native fullscreen / borderless fullscreen-window |
| Window size | Yes | Four 16:9 presets and manual resizing; the selected window dimensions are restored when returning from fullscreen |

CLI options `--volume` and `--fps` override the saved preference for that invocation. The volume setting becomes persistent if changed in the settings screen. `--no-sound` always disables audio regardless of preferences.

## Persistence and compatibility

The JSON structure currently contains `schema_version: 5`. Older schemas 1–4 are migrated on load, keeping custom key bindings, volume, mute and reduced-effects settings. Missing files use defaults. Invalid files are ignored with a warning rather than crashing the game. Preferences save atomically and are excluded from Git. No extra dependencies required.

## Testing

```powershell
python -m unittest discover -s tests -v
python -m tests.gui_settings_smoke
python neon_void.py --selftest 3600 --seed 42 --god --no-sound
```

The GUI test requires a real Tk display (Linux CI runs it with Xvfb). It doesn't simulate actual mouse-driven UX judgement; confirm visually on Windows before a release.

## Keyboard remapping (settings schema v4)

Select **O** from the title or a paused run, then **Tab** to open the key-binding page. Navigate the eight actions (up/down/left/right/dash/pause/pulse/guard) with arrow keys or WASD. Press **Enter** or click an action to capture the next key. Press **Esc** to cancel capture. Press **Delete** or **Backspace** on that page to reset all bindings.

- A single letter A-Z (excluding the protected M, R, O commands), or Space, may be assigned to each action.
- Each action must have a unique primary key. An invalid, reserved or duplicate key is refused without changing another action.
- Arrow keys always remain alternate movement inputs, and Shift remains an alternate dash input.
- **O** opens/closes the settings window, **M** toggles mute and **R** restarts, regardless of bindings; **Esc** remains a safety/menu command.
- The settings file uses **schema_version 5** (key remapping was introduced in v4) with a `bindings` mapping. Existing version 1 preferences load with WASD/Space/P/Q/E defaults and retain all volume, mute, FPS and reduced-effects values. Version 2 custom keybindings are migrated without losing their six existing assignments. New pulse and guard keys default to Q/E, or are assigned unused fallback keys when Q/E were already taken. The new schema is written upon the next settings change.

Pulse and guard are normal rebindable actions. In the bindings page, eight compact rows fit above the navigation footer.

This is an input remapping foundation, not gamepad support or a generic OS-wide hotkey system. Visual menu navigation remains on WASD/arrow keys independently of gameplay bindings.

### Additional validation

```powershell
python -m unittest discover -s tests -v
python -m tests.gui_bindings_smoke
```

The GUI smoke check runs on real tkinter under Windows or a Linux virtual display. Actual keyboard-layout behavior (for example, non-US layouts) still needs manual validation.

## World interaction (schema v4)

Boot now opens the playable antivirus base when not running with `--selftest`. The dedicated `interact` action defaults to **F**. Press O, Tab, choose **월드 상호작용**, then Enter to remap. Moving near a glowing terminal or gate shows the interaction prompt.

Schema v3 custom keys (including existing uses of F) are preserved. Migration assigns the first unused world-interaction key among F/G/H/J/K/L/Z/X/C/V/B. v2 and v1 migrations remain supported. When playing CPU combat, interaction with the exit portal retreats to OS hub without claiming the unfinished wave's clear rewards.


## Responsive display and high-DPI (schema v5)

- The game is simulated and laid out at **1280×720 logical units**. A native Tk Canvas scales positions, stroke widths and text fonts uniformly into the real canvas size. Different aspect ratios use opaque black letterboxing instead of stretching enemies, circles or hitboxes.
- All real mouse events are inverse-mapped to logical coordinates **before** aiming, buttons, overlays and world interaction. Empty letterbox bars do not trigger attacks or terminal clicks. Tests that intentionally call Game handlers with logical coordinates continue to work.
- **Window**: drag to resize, or use **O → General → Window size** (960×540, 1280×720, 1600×900, 1920×1080). A manually resized window updates settings after a short debounce.
- **F11** toggles native fullscreen/windowed. **Alt+Enter** toggles borderless/windowed. Both shortcuts work from the game or a terminal, and the display mode can also be selected from general settings.
- Display settings are separate from gameplay saves. If a window-manager switch fails, the game attempts a safe windowed fallback; to recover from an unusable persisted preference, close the game and **rename only settings.json** (back it up first). The next start uses defaults, without removing OS progress or player records.
- `--fps` or the FPS preference shows a low-overhead telemetry line with FPS, render average/p95 (up to 240 samples), the native Canvas item count and process resident memory (RSS) on Windows/Linux. These are **observations**, not GPU frame-time measurements or guarantees about minimum FPS.
- The cross-platform GUI smoke drives viewport sizes **960×540, 960×700, 720×405, 1000×700, 1120×1000, 1280×720, 1920×1080** and verifies mapped mouse hit tests, text/shape geometry, letterbox padding, display shortcuts and stable object counts. OS DPI 100%/125%/150%/200% still needs real Windows monitor QA as in issue #24.

See [display and performance QA](DISPLAY.md).
