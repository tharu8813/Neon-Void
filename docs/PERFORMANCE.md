# Rendering performance and GPU diagnostics

NEON VOID uses Python 3.11+ and **tkinter Canvas**. This is a CPU-driven
renderer, not a Vulkan/DirectX/OpenGL engine. Windows Task Manager showing
little or no GPU *3D* activity is expected. The window compositor can still
use the GPU; its usage is not the same as game rendering acceleration.

## What the FPS overlay means

The simulation runs with a fixed `DT = 1/60` seconds and normally renders
when at least one simulation step has advanced. The old overlay displayed
Tk timer callback frequency, which could exceed 100 even though the game
was not rendering that many frames. The overlay now counts **render()
submissions per elapsed wall-clock time**. Tk does not expose a hardware
present counter, so this is not measured display/monitor FPS.

`render mean / p95` represent Python/Tk command submission time, not
synchronized monitor presentation or GPU frame time. The overlay also shows
Canvas item count and the process resident memory (RSS). A healthy fast
system may show around 60 submitted FPS, matching the fixed-step simulation.

## CPU optimizations in this patch

- Pool objects retain their last fill/outline/width/text, avoiding Tcl
  `itemconfigure` calls unless the style changes or a hidden slot reappears.
- `ScaledCanvas.itemconfigure` bypasses `find_withtag` and style conversion
  for simple text, colour, and visibility changes. Numeric IDs use the direct
  style path for widths and fonts; tagged items still use the full path.
- A hub scene that is already visible no longer toggles every hub item
  visible each frame, only to hide inactive nodes again.

None of these changes alters world coordinates, collision detection,
input, save schemas, timing constants or graphical appearance.

## Manual A/B on the same Windows computer

Run the same release/main build and the optimized PR build, using the
same window size, Windows display scaling, game preferences and stage:

```powershell
python neon_void.py --fps --no-sound
```

Measure idle hub, hub movement, a combat wave, and a particle-heavy boss.
For each, record 30-60 seconds of submitted FPS, average/p95 render
milliseconds, Canvas object count, RSS and CPU usage. Compare like-for-like
scenes and repeat at least three times. The first run can include Windows
font/Tk warm-up and is not a reliable baseline.

Run automated regression checks:

```powershell
python -m compileall -q neon_void.py neonvoid tests
python -m unittest discover -s tests -v
python -m tests.gui_display_smoke
python -m tests.gui_world_smoke
python -m tests.gui_combat_smoke
python neon_void.py --selftest 3600 --no-sound --seed 42 --god
```

CI checks behavior on Windows and Linux, **not representative gameplay FPS
on your hardware**. Do not report a percent speedup without a local A/B.

## What is required for genuine GPU rendering?

A separate, opt-in graphics backend (for example OpenGL via pyglet/ModernGL
or an SDL-based renderer) would need to replace Tk Canvas item creation,
transforms, text and tag ordering with batched GPU draw calls. Input, DPI
scaling, HUD, menus and the custom cursor must remain functional.

A safe migration path is: renderer interface + deterministic scene snapshots,
feature-parity prototype, Windows GPU profiling, and full game/UI regression.
Switching libraries without this work does not guarantee a frame-rate win.
Keep the CPU/Tk backend available until parity and measurable gains are proven.
