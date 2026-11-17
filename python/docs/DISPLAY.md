# Display scaling, window modes and performance verification

The design resolution is **1280×720 logical game units**. Do not multiply game-world coordinates or hitboxes by the physical pixel ratio in gameplay logic. \`neonvoid/display.py\` owns the transformation between logical game positions and native Tk Canvas pixels.

## Layout and pointer contract

\`Viewport.fit(client_width, client_height)\` selects \`min(client_width/1280, client_height/720)\`. It centers the logical scene and creates black letterbox bars when the window aspect ratio differs from 16:9. Every Canvas drawing call (creation, \`coords\`, \`move\`, fonts, line widths) uses the same transform, while \`coords\`/bbox lookups return *logical* values for existing gameplay hit-testing code. Existing Canvas tags, z-order, object pooling and animations are retained.

Real widget mouse events are inverse-transformed before reaching \`Game.on_motion\`, \`on_mouse_down\`, \`on_mouse_up\` or cursor-enter handlers. A click in black padding is ignored. This is important for combat aiming, upgrades, settings, profile and OS deployment/extraction UI. A native \`tk.Canvas\` method invoked directly on the ScaledCanvas can still inspect physical coordinates for smoke validation.

Resizing transforms existing Tk items once; normal per-frame game calls remain in logical coordinates and are mapped on submission. Unlike a one-off \`Canvas.scale('all')\` call, dynamic draws and scaled fonts remain consistent after many window resizes.

## Modes and recovery

- Windowed: drag the native window edges; save size after 900 ms without a resize, or select four presets through **O → General → Window size**. The startup geometry fits the monitor's available size.
- Fullscreen: **F11** or **O → General → Display mode**.
- Borderless screen-sized window: **Alt+Enter** or select borderless in general settings. Press the shortcut again to return to windowed mode.
- The settings file uses schema v5, migrating v1–v4 without dropping input bindings, audio settings, reduced-effects, or FPS preferences. Display mode and preferred window geometry are saved alongside existing preferences, not in any gameplay/XP data file.
- If the preferred mode cannot be established, the window controller attempts safe windowed fallback. To recover manually, close the game, back up and rename \`settings.json\`, then relaunch. This resets only preferences; keep \`save.json\`, \`profile.json\`, \`antivirus_db.json\` and \`os_checkpoint.json\` untouched.

## Reproducible automated checks

Run in the repository:

\`\`\`powershell
python -m unittest discover -s tests -v
python -m tests.gui_display_smoke
python neon_void.py --selftest 3600 --no-sound --god --seed 42
\`\`\`

On Linux, run the GUI smoke via \`xvfb-run -a python -m tests.gui_display_smoke\`. CI additionally runs all existing keybinding, settings, hub, four-sector, combat, database/extraction and checkpoint GUI tests on Windows and Linux, and builds and launches the Windows onefile EXE.

The GUI smoke records Canvas object count, average render time over a fixed idle/HUD sample, and Python allocation peak (via \`tracemalloc\`). The \`--fps\` overlay in a *real play session* shows exponentially smoothed FPS, rolling average/p95 render milliseconds, Canvas object count and OS-reported process RSS (Windows/Linux). The initial idle scene is not a proxy for enemy-heavy waves and OS runner results are **not** an end-user hardware FPS guarantee.

## Manual Windows matrix (issue #24)

| Test | Display scaling | Geometry and behavior |
|---|---|---|
| 1 | 100% | Resize 960×540 ↔ 1280×720; mouse aim, software cards, OS terminal |
| 2 | 125% | Same plus settings/profile/antivirus DB and system status terminal |
| 3 | 150% | Fullscreen ↔ windowed ↔ borderless; Alt+Tab, focus return and sound |
| 4 | 200% | Small-window readability and clipping, pointer/cursor/hitboxes |
| 5 | Any | Windows EXE + source program; all four sectors, particle-heavy boss wave and CLEAN extraction |

Record monitor size, OS scale, source/EXE version, visible clipping or off-by-pixel clicks, CPU/RSS and observed p95 render latency. Any failures should become a new reproducible issue rather than closing #24 on CI alone.
