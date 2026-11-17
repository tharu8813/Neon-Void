# P9: Restore Neon Void's original visual identity

## Why the port looked flatter

Original Tk `python/neonvoid/view.py` layers moving background stars, 80px neon grid, a slow vertical scanline, visible score/floating numbers, procedural hit/kill bursts, 2D screen flash/sweep, neon cursors, moving hub dust and conduit packets, an outlined/player thrust animation, a two-part boss silhouette and animated banners. The first 3D migration correctly reconstructed hitboxes, top-down composition and polygon silhouettes but left many of these **cosmetic temporal cues** static or absent.

## Added implementation

| Python visual reference | Godot representation | Pool / behavior |
|---|---|---|
| `_build_background/_move_background` | `scripts/visuals/legacy_atmosphere.gd` | 52 stars; seeded twinkle/drift; 17+10 line 80px arena grid; 34px/s scan |
| `_render_hub` | same atmosphere module | 34 hub dust motes, 1.9Hz core breathing, 4 base / 6 OS conduit pulses with 0.42 travel |
| `step_player` dash afterimage | `scripts/visuals/legacy_player_echo.gd` | 14 shared-actor silhouette mesh echoes, 28ms emission, 0.28s fade |
| `burst` and `ring` | `scripts/visuals/legacy_sparks.gd` | 380 GPU MultiMesh capped particles; original-ish distinct hit/crit/boss/dash/skill/purify colors and speeds |
| `render` wave popup and `_render_screen_fx` | `scripts/ui/legacy_screen_fx.gd` | original cubic eased wave title + thin rule, progress sweep, short screen flash, sector CLEAN copy |
| `draw_player` / `draw_world` | `main.gd`, `legacy_enemy_visual.gd` | moving exhaust glow, breathing player identity ring, boss core and beacon, rusher warning pulse |

All visual-only animations operate on read-only data and must never change C simulation hitboxes, B input, deterministic gameplay RNG, E save formats, XP or run ID. Shared meshes, cached materials and a bounded particle budget protect performance.

## Acceptance criteria and manual checks

- Start in the antivirus base: verify dust, breathing core, moving data packets, clear terminal labels and top-down camera.
- Deploy and enter CPU: 52 subtle stars, 80px grid, scrolling scan, original `WAVE 1` reveal and short sweep appear without covering actual combat/aim UI. Repeat in MEMORY/STORAGE/NETWORK.
- Dash: neon translucent afterimages appear only while dashing, fade immediately afterward; original arrow shape/aim unaffected.
- Test direct hit, crit, chain, explosion, first boss, Q/E, hurt and purification; inspect distinct effect colors, intensity, placement and pooled expiry. Make sure all hit hazards stay readable.
- Test reduced-effects setting: stop dust/star movement, scan, screen flash, dash echoes and burst sparks, **but do not hide grid, 3D shapes, warning areas or game UI**.
- Verify no FPS drop in a packed wave, GPU usage still positive, no leaking nodes/mesh instances after sector retreat, camera and cursor/DPI alignment.
- Audio remains **unverified**, since the user cannot test it in their current environment. Do not claim parity.
- This is a code-level/source-style restoration. Physical Windows screenshot, subjective game feel, p95/p99 timing, multi-DPI and full-run QA remain the signoff authority.

The dedicated `tests/p9_legacy_atmosphere_smoke.gd` is deliberately **headless structural smoke**, not visual acceptance.
