# P9 Python combat effects and audio restoration

**Status: implemented as a passive Godot renderer and original audio synthesizer, not physically certified.** Parent #78, P9 visual issue #62, #63 QA, #77 Windows physical approval. This work stacks on the unmerged #81 → #80 → #79 → #76 chain; never merge directly into main.

## Source-of-truth and responsibilities

- Python frozen baseline: `neonvoid/view.py::draw_world/_render_screen_fx` and `neonvoid/audio.py::_build_effects/_build_music`.
- Godot: `scripts/visuals/legacy_combat_fx.gd`, `scripts/audio/legacy_synth.gd`, `scripts/audio/legacy_audio.gd`, `scripts/main.gd` integration.
- C owns enemies, telegraphs, bullet positions/velocities, hits, mana, wave state, DoT, RNG and events. B owns dash mechanics. E owns all five original JSON save schemas. **VFX/audio must not modify any of these.**
- P9 retains the GPU-backed MultiMesh projectile core and adds two separate capped **MultiMeshInstance3D** velocity-streak batches. No per-projectile Node3D allocations, no reads back from the GPU.

## Visual restoration matrix

| Python effect | Godot implementation | Data source |
|---|---|---|
| Player bullet `pool_streak` (32ms) | Neon-green box instance aligned with observed velocity | `sim.bullets` |
| Enemy bullet streak | Pink shorter observed velocity tail | `sim.enemy_bullets` |
| `virus_pending` attack warning | Pooled 3D gold ring, red when remaining <=0.18s | `sim.telegraphs` + `sim.enemies` |
| Encryption pulse | Warning ring of the exact frozen `Catalog.PULSE_RADIUS` | `telegraph.action.behavior` |
| Dash shock ring | Capped transient cyan 3D ring | B `dash_started` signal |
| Skill pulse | Capped transient cyan 155px ring | Successful C `try_cast` only |
| Incoming hit / enemy kill | Capped short red/pink transient ring | Existing C damage/kill signals |
| Boss defeat | Larger magenta pulse | Existing C kill/boss identity |
| Reduced effects | Streaks disabled, cosmetic burst motion reduced | Existing settings; **attack warning stays visible** |
| Base and OS hub | Combat FX hidden and cleared | A `_set_world(false)` |

Only warning positions and timing belong to C. Audio/visual effects **observe** them. The synchronous enemy death signal contains the original ID but no position; A reads the pre-existing view node position for a one-shot pulse and never modifies C.

## Original procedural audio

`legacy_synth.gd` ports the exact tone frequencies, wave shapes, attack/decay envelopes and final mix normalization from Python audio.py. It generates **16** effects: `shoot`, `hit`, `kill`, `boom`, `dash`, `hurt`, `up`, `pick`, `boss`, `dead`, `eshoot`, `hshot`, `wave`, `phase`, `lowhp`, `combo`. Format: 22050 Hz mono PCM16 WAV memory buffers. Godot's generated pseudo-random noise uses a different PRNG than Python, so noise samples are **not byte-for-byte identical** despite matching frequencies/envelopes/seeds.

The looping **4-bar 100 BPM Am/F/C/G instrumental** uses the source composition's root pitches, chord voices, bass, kick and eight-note arpeggios. No external music or copyrighted files are downloaded or bundled. The source's randomized hi-hat texture is not yet reproduced, so physical soundtrack comparison remains necessary. Source music is 8000 Hz PCM16 and loops from its start.

`legacy_audio.gd` uses **12 pooled Godot AudioStreamPlayer voices** plus one dedicated looping background-music player, with source default per-cue gains and shot-rate minimum gaps. Existing Godot Master audio bus setting/mute persistence is authoritative. SFX on death/impact/dash/shot/wave/confirmed reward are triggered only by observed B/C state or after E's save acknowledgment. UI opening or save failure never falsely emits a successful reward sound. Headless deliberately skips actual `play()` and cannot establish speaker playback.

## CI gates and risks

`godot/tests/p9_fx_audio_smoke.gd` checks all 16 generated PCM sounds, gains, generated 9.6s music loop, mute/rate limiting, read-only shot streak input snapshots, red telegraphs with true C radius, reduced effects, pulse cleanup, and hiding FX when returning to the OS hub. Added to A's **Linux and Windows** headless integration CI; existing D UI, B movement, E save, 4×5 waves and Windows export stay mandatory.

**Windows physical verification still required before any merge or release:**
- [ ] Original Python/Tk vs Godot real monitor 1280×720 screenshots/video for auto-shoot, heavy bullet waves, pulsing skill, dash, enemy death, boss/encryption warning, OS CLEAN/purify.
- [ ] Real audio playback on Windows: check frequencies and perceived levels, scene transitions, cue overlap/voice stealing, volume/mute and uninterrupted music for >30 minutes.
- [ ] Match original music texture/hi-hat/percussion and 16-voice sound balance where practical. No bit-perfect claim.
- [ ] 100/125/150/200% DPI and 960×540 / 1920×1080 / 21:9: attack-warning readability, HUD occlusion and input hitbox alignment.
- [ ] 64 enemy/260 hostile projectile stress benchmark: record visible GPU utilization and 1080p60 p50/p95/p99 frame time. Headless tests do **not** verify physical GPU performance.
- [ ] Final side-by-side user approval #77 before merging the stacked drafts or replacing the Tk game.
