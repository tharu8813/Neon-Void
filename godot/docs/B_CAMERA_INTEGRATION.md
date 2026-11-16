# B Camera P2 integration contract (issue #55)

Do not overwrite A-owned project.godot, main.gd, main.tscn.

1. Logical (x,y) -> Vector3(x * 0.01, 0, y * 0.01). Top of screen is -Z; camera rotation (-90,0,0), orthographic height 7.2, no tilt. Arena camera centered on (640,360), hub follows player and clamps the 2400x1560 world.
2. Instance res://scenes/camera/legacy_camera.tscn in the owner-provided scene, set hub_mode and follow_target. The original P1 placeholder stays untouched until A integrates it.
3. REQUIRED FROM A: a constant 1280x720 camera viewport for project_ray_* and 16:9 aspect-keep/letterbox composite. If Godot extends 3D viewport on 21:9, that is a regression. The camera ray requires a *logical* viewport point. For raw native client area pixels, map using LegacyViewport.window_to_logical. Do not double-convert InputEventMouse already in logical coordinates.
4. Native client coordinates exclude OS titlebar and decorations. Black bars must reject pointer interactions. A owns the final UI/input routing into fixed viewport.
5. Windows 100/125/150/200% DPI, borderless/fullscreen, real camera projection and visual parity are NOT proven by headless tests. Compare reference screenshots before closing #55.
6. Source: docs/GODOT_MIGRATION_BASELINE.md, neonvoid/display.py, neonvoid/world/session.py.
