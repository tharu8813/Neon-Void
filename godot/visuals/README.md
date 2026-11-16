# P9 visual prototype: MultiMesh-instanced 3D bullets

**Not integrated into the game. Do not merge before #55/#57/#58 and P0/P1 gates.**
Standalone Godot 4.7.2 scene: `res://visuals/p9_showcase.tscn`. Use **Run Current Scene** to inspect the emissive 3D example on a physical Windows GPU. Fixed top-down orthographic camera is a showcase placeholder, not a validated P2 transform.

The renderer-only API in `res://visuals/projectile_batch.gd` is `set_projectiles(logical_positions: PackedVector2Array, tints: PackedColorArray = PackedColorArray())` plus `clear_projectiles()`. Input coordinates are immutable legacy XY pixel snapshots; A uses the existing `runtime_config.gd` WORLD_UNITS_PER_PIXEL constant for XZ mapping; standalone SceneTree test scripts must not resolve autoload singletons by name. C owns combat simulation and emits read-only snapshots later. Visual animation never modifies game logic or uses gameplay RNG.

MultiMesh holds at most 2048 visible mesh instances, clips excess visuals, and bounds culling for the hub. This is a **candidate GPU batching design**, not proven draw-call or FPS improvement. Linux headless CI confirms import, structure, **pure CPU coordinate mapping**, count cap and clear only. Headless/dummy rendering cannot validate GPU MultiMesh transform-buffer readback, so no such assertion is made. P1 Windows export is a separate CI gate.

Before P9 merge: test same fixed-seed 100/500/1000/2000-bullet scenes on real Windows Forward+ and Compatibility renderers, p50/p95 frame time, CPU/GPU engine, draw calls, images, 4-sector legibility, particle animation/reduced-motion, integrated gameplay parity. No GPU performance claims without those results.
