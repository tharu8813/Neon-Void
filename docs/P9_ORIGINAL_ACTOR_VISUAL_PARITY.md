# P9 Actor Visual Fidelity: Python → Godot 4

**Status:** implementation/automated structural tests only. This is **not** acceptance of Windows rendered appearance, GPU usage or play-feel. Parent [#78](https://github.com/tharu8813/Neon-Void/issues/78), visual issue [#62](https://github.com/tharu8813/Neon-Void/issues/62), final QA [#77](https://github.com/tharu8813/Neon-Void/issues/77).

## Frozen source of truth

- Python `neonvoid/view.py::draw_world, draw_player`, `neonvoid/content/enemies.py::E_PARAMS`, `neonvoid/constants.py`
- Godot `scripts/visuals/legacy_actor_shapes.gd`, `legacy_enemy_visual.gd`, `scripts/main.gd::_update_visuals`
- Original pixel-to-world ratio remains **0.01**, vertical top-down Camera3D remains unchanged. Visual material/mesh never owns a CollisionShape, never changes enemy radius, HP, simulation time, spawn budgets, AI or RNG.
- Godot material tint values exactly match the frozen Python E_PARAMS colors, with top-down XZ extruded silhouettes and a shallow 3D thickness.

| Kind | Source 2D outline | Color | Godot 3D shape |
|---|---|---|---|
| player | aiming 4-point ship; nose at +21px | `#6ff2ff` | 4-point extruded hull + 25px aura + dash ring |
| drifter | direction triangle | `#ff3f7f` | extruded triangle |
| mini | direction triangle | `#b06bff` | extruded triangle (smaller radius) |
| shooter | rotating square | `#ffcf5c` | extruded square |
| splitter | rotating pentagon | `#b06bff` | extruded pentagon |
| rusher | forward arrow | `#ff8a3d` | arrow + wind-up ring |
| overheater | triangle | `#ff874c` | extruded triangle |
| worm | pentagon | `#aa80ff` | extruded pentagon |
| trojan | square | `#f79bda` | extruded square |
| ransomware | hexagon | `#ffca69` | extruded hexagon |
| rootkit | octagon | `#dc70e9` | extruded octagon |
| packet_swarm | triangle | `#50dbe9` | extruded triangle |
| guardian | heptagon | `#ffe298` | extruded heptagon |
| boss | hexagon, two ring layers | `#ff5cf0` | hexagon + counter-spinning inset + orbit |
| overseer | hexagon, two ring layers | `#ff6538` | hexagon + counter-spinning inset + orbit |
| replicator | hexagon, two ring layers | `#b878ff` | hexagon + counter-spinning inset + orbit |
| encryptor | hexagon, two ring layers | `#ffc05d` | hexagon + counter-spinning inset + orbit |
| zero_day | hexagon, two ring layers | `#f05ef1` | hexagon + counter-spinning inset + orbit |

## Read-only visual feedback contract

- `legacy_enemy_visual.gd::sync_state` reads the enemy snapshot. `hp` reductions create a temporary white flash (visual-only); `vulnerable` uses mint; burning uses warm orange; rusher `state == 1` enables orange charge ring; guards enable a status ring. All visuals are outside simulation and storage.
- Specialized viruses/bosses spin based on C's `elapsed`; vanilla shooter/splitter use an **independent cosmetic clock**, never writing back to C; reduced-effects suppresses rotational motion.
- Original player bullet tint `#ccff4d`, enemy bullets `#ff9fbb` are applied only to P9 MultiMesh instance colors. No projectile size, speed, hitbox, inventory, skill or wave rule is changed.
- Models reuse the fixed top-down camera and preserve recognizable source radii; B's `LegacyPlayer` hidden collision scene remains the source of truth.

## Automated check and physical A/B gate

`godot/tests/p9_actor_parity_smoke.gd`, invoked by `Godot A integrated gameplay checks` on Linux and Windows, checks every 17 enemy kind's color, polygon point count, actual ArrayMesh hull, boss orbit structure, rusher warning, damage flash, player ship mesh and real main-scene integration. It also checks that visual updates never mutate C enemy snapshots. Existing B/C/D/E and 4×5 wave/save tests continue to run independently.

The **physical Windows A/B signoff is still required**:

- [ ] Match 1280×720 captures of Tk and Godot: player in base hub, early CPU CORE with each enemy, rusher attack wind-up, 4 sector bosses, vulnerability/DoT and player dash.
- [ ] Capture at 960×540, 1920×1080, 4:3 and 21:9, checking projected radii/pointing direction/labels and screen clipping.
- [ ] Confirm player/virus silhouette immediately readable against 3D lighting with glow on/off and reduced-effects on/off.
- [ ] Check 4 bosses are individually recognizable when mixed with regular enemies; original colors are retained under actual Forward+ tonemapping.
- [ ] Record 1920×1080 rendered p50/p95/p99 frame times, CPU/GPU/VRAM when 64 enemies and a dense P9 MultiMesh volley are visible; no FPS claim based only on headless.
- [ ] Verify original HUD layout, terminal interactions, original SFX and 30–60 min gameplay separately. These are **not** covered by this actor model PR.

No main merge or Godot release before the original/Tk screenshot grid and user acceptance from #77.
