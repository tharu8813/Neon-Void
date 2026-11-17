# Final code parity checkpoint for the original Godot port

**Reference:** Python baseline `245663ba15149cc1262304b966062989c24c13f6` and source `python/neonvoid/view.py`, `game.py`, `world/session.py`, `audio.py`. Stacked Draft PR #83 on #82 → #81 → #80 → #79 → #76. This document distinguishes code written, CI exercised, and **not yet accepted on real Windows**.

## Implemented in this checkpoint

1. **Sector exit gate and F**: the original Python exit at `CPU_EXIT=(100,360)` with strict distance <112px becomes a low-profile 3D frame, vertical scan beam, approach glow and configurable F key prompt. In the **active combat/clean** state, near F exits through the existing safe E checkpoint transaction and a distant F cannot abandon the sector. The original no-shoot zone is not modified. **After an E-confirmed card choice, non-final waves auto-start the next 60Hz C wave**, matching Python's `pick_upgrade`. E's `scene=play` restart checkpoint also auto-starts the next unconfirmed wave. The `between` state remains only as an explicitly recoverable failure/manual fallback and never awards a duplicate XP receipt.
2. **Purification**: a zero-effect request (infection==0 and integrity==100) now refuses to consume any of the three charges. Successful purification leaves C's exact 32 infection / 9 integrity recovery and E's rollback logic in place, then shows a green ring and plays the existing `up` sound after commit success.
3. **Combat readout**: 40 capped original-style 3D floating damage numbers derived exclusively from successive C enemy HP snapshots, a 2.6-second kill-combo indicator, source-catalog enemy score × original `1.0 + min(combo,40)*0.1` score multiplier and its 10th-kill audio cue, red low-HP vignette, hit flash, boss telegraph warning, and 1.15-second low-health heartbeat. The original flat ten-point-per-kill placeholder is eliminated without creating new save fields.
4. **Memory-safe VFX**: fixes an indexed ring-pool bug that previously could move a surviving pulse to an expired pulse's coordinates when the earlier slot recycled; surviving pulses carry their original immutable logical positions. Original direct-hit/critical, explosion-radius and 0.12-second chain-lightning cues now observe the *actual* C projectile collisions through a side-effect-free signal. A GPU instancing pool for bullet/shards, 24 chain arcs, 12 burst rings and 40 text labels stays bounded.
5. **Original audio fidelity and controls**: restores the 30ms synthesized hi-hat percussion on every odd arpeggio in the 100BPM source loop. M mute, F11 fullscreen and Alt+Enter borderless are connected to E's validated settings transaction; M/F11 remain available when the gameplay input router is paused.
6. **C gameplay mechanics missing from the port**: 52px rotating orbital drones fire the same real C projectiles autonomously at 0.55s intervals with 62% base damage and 80% radius; 14-second score shards use isolated deterministic drop randomness, exact magnet acceleration/damping and 21px player pickup radius, with A scoring receipt and GPU-instanced shard + drone meshes. Original dash-blast software now deals real mitigated 78px initial damage and recurring 70px-radius 34% damage every three active dash frames with source knockback. C's established spawn/crit RNG stream remains untouched. **Time scale parity:** Source Python kill hitstop (0.028s normal / 0.16s boss, ×0.16 world time) and slow software (0.45× world time, 0.6s cap) are implemented in C. The scale applies to B movement, C enemy AI, projectiles, telegraphs, DoT, shards and infection, while cooldowns/auto-fire/spawns/drone firing remain on the real 60Hz clock. Headless tests cover world/projectile speed and mana/cooldown independence. Existing native speaker audio remains unverified.

## Automated contract

`tests/p0_final_code_parity_smoke.gd`, `p4_drone_shard_score_smoke.gd`, `p4_temporal_parity_smoke.gd`, `integration_smoke.gd` and `live_save_integration_smoke.gd` exercise:
- exact Python EXIT coordinate and strict 112px boundary, sector-only presence, near and far F, scan/near prompt and reduced-effects handling
- no-op purification preserving charge; genuine purification 32/9 recovery and feedback
- original M mute, fullscreen/borderless toggles, audio facade state
- enemy HP source of truth unchanged by 3D damage labels; the VFX pool retaining the correct second pulse after the first expires
- low-HP HUD / reduced motion, on-screen mute and boss attack warning
- drone-origin radius, shot damage, cadence, magnet/pickup mechanics, RNG isolation, first and second enemy combo score, dash-blast on entry and every third frame, instanced score shards and visible orbital drone count
- auto-next-wave after successful card choice and safe E checkpoint recovery, with XP receipts preserved
- C slow-time/normal hitstop/boss hitstop stacking, exact 60Hz real timers versus scaled world motion
- genuine critical, explosion and chain signal production and bounded short-lived FX pools

This runs on Windows and Linux headless as part of A's 4×5 simulation/integration suite. Existing B/C/D/E and Python tests remain mandatory.

## Do not classify as coded/accepted without verification

- [ ] Actual Windows/Tk-to-Godot screenshots for physical EXIT/F label, hit text, low-HP effects, scan, fonts and camera; 100/125/150/200% DPI and 16:9/4:3/21:9
- [ ] Real GPU, CPU/VRAM, p50/p95/p99 frame timing and 30–60min 4-zone play, including Alt+Tab and sound/mute test
- [ ] New drone, shard, dash-blast and score/combination code has structural automated parity coverage but still needs source-to-source acceptance for corner cases, deterministic noise/drop differences, long play and save/restore scenarios. Python uses a shared unrelated random generator for drops; C deliberately isolates drop RNG to preserve the combat RNG contract. No bit-for-bit drop sequence claim.
- [ ] Automatic wave continuation is implemented with E-safe card commit and reload checks, but actual Windows click-to-next-wave animation and recovery must still be manually inspected.
- [ ] Kill-triggered hitstop and slow/crit/chain/explosion feedback have structural tests; actual physical timing and color/trajectory equivalence remain unverified. Dynamic particle clouds and per-pixel/original motion matching still require A/B captures.
- [ ] Visual chain arcs, homing/drone/shard graphics and dynamic impact particle aesthetics require actual Python-vs-Godot screenshots/video A/B (no physical GPU draw or font-matching claim).
- [ ] Final user-approved Windows results under #77 and #78, plus no-data-loss verification, before any main merge/release

**Important:** Headless checks never prove physical display/audio or GPU utilization. Do not mark #52/#62/#63/#77/#78 fully complete and never delete Python code, its five JSON files, or legacy user save slots. This PR is a staged code completion **milestone**, not a claim of finished/accepted game.
