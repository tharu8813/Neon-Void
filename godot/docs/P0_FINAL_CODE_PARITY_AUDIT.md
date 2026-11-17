# Final code parity checkpoint for the original Godot port

**Reference:** Python baseline `245663ba15149cc1262304b966062989c24c13f6` and source `neonvoid/view.py`, `game.py`, `world/session.py`, `audio.py`. Stacked Draft PR #83 on #82 → #81 → #80 → #79 → #76. This document distinguishes code written, CI exercised, and **not yet accepted on real Windows**.

## Implemented in this checkpoint

1. **Sector exit gate and F**: the original Python exit at `CPU_EXIT=(100,360)` with strict distance <112px becomes a low-profile 3D frame, vertical scan beam, approach glow and configurable F key prompt. In the **active combat/clean** state, near F exits through the existing safe E checkpoint transaction and a distant F cannot abandon the sector. The original no-shoot zone is not modified. The current durable `between` state still requires F to advance the next wave after a reward, unlike Python's automatic transition. This discrepancy is not hidden and requires a separate E-compatible behavior migration.
2. **Purification**: a zero-effect request (infection==0 and integrity==100) now refuses to consume any of the three charges. Successful purification leaves C's exact 32 infection / 9 integrity recovery and E's rollback logic in place, then shows a green ring and plays the existing `up` sound after commit success.
3. **Combat readout**: 40 capped original-style 3D floating damage numbers derived exclusively from successive C enemy HP snapshots, a two-and-a-half-second kill-combo indicator with its 10th-kill audio cue, red low-HP vignette, hit flash, and boss telegraph warning copy. **Combo score is intentionally unchanged** because C/A's score formula must not be adjusted under a visual-only migration.
4. **Memory-safe VFX**: fixes an indexed ring-pool bug that previously could move a surviving pulse to an expired pulse's coordinates when the earlier slot recycled; surviving pulses carry their original immutable logical positions. Existing projectile MultiMesh buffers remain bounded.
5. **Original audio fidelity and controls**: restores the 30ms synthesized hi-hat percussion on every odd arpeggio in the 100BPM source loop. M mute, F11 fullscreen and Alt+Enter borderless are connected to the existing validated E settings transaction; M/F11 remain available when the gameplay input router is paused. Existing native speaker audio remains unverified.

## Automated contract

`godot/tests/p0_final_code_parity_smoke.gd` exercises:
- exact Python EXIT coordinate and strict 112px boundary, sector-only presence, near and far F, scan/near prompt and reduced-effects handling
- no-op purification preserving charge; genuine purification 32/9 recovery and feedback
- original M mute, fullscreen/borderless toggles, audio facade state
- enemy HP source of truth unchanged by 3D damage labels; the VFX pool retaining the correct second pulse after the first expires
- low-HP HUD / reduced motion, on-screen mute and boss attack warning

This runs on Windows and Linux headless as part of A's 4×5 simulation/integration suite. Existing B/C/D/E and Python tests remain mandatory.

## Do not classify as coded/accepted without verification

- [ ] Actual Windows/Tk-to-Godot screenshots for physical EXIT/F label, hit text, low-HP effects, scan, fonts and camera; 100/125/150/200% DPI and 16:9/4:3/21:9
- [ ] Real GPU, CPU/VRAM, p50/p95/p99 frame timing and 30–60min 4-zone play, including Alt+Tab and sound/mute test
- [ ] All original score/shard pickup/combo multiplier and drone offense rules require separate code audit against Python and C; newly added floating damage is not proof that the entire gameplay rule is identical
- [ ] Original Python automatic wave continuation vs Godot's explicit F between-wave checkpoint state needs a safe persistence-compatible migration
- [ ] Visual chain arcs, homing/drone/shard graphics and the dynamic impact particles require dedicated actual Python-vs-Godot A/B evidence
- [ ] Final user-approved Windows results under #77 and #78, plus no-data-loss verification, before any main merge/release

**Important:** Headless checks never prove physical display/audio or GPU utilization. Do not mark #52/#62/#63/#77/#78 fully complete and never delete Python code, its five JSON files, or legacy user save slots. This PR is a staged code completion **milestone**, not a claim of finished/accepted game.
