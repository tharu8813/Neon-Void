# Windows physical QA evidence sheet: P0 #53 / P1 #54

**Blank checklist, not executed evidence.** Headless CI and Windows binary export alone cannot prove real on-screen Windows rendering or DPI/controls. Use an actual display and record source PNGs, not mockups.

## Environment and traceability

- Python baseline commit: `245663ba15149cc1262304b966062989c24c13f6`.
- Exact Godot PR HEAD: `<fill>`; CI run: `<fill>`; exported EXE SHA256: `<fill>`.
- Date, OS build, GPU+driver, monitor, Windows display scaling, app client pixel size: `<fill>`.
- Use isolated synthetic data or verified backup. **Never delete, rewrite or reset** original `save.json`, `settings.json`, `profile.json`, `antivirus_db.json`, `os_checkpoint.json`.

## P0 actual Python screenshots

| Client size | DPI 100% | DPI 125% | DPI 150% | DPI 200% |
|---|---|---|---|---|
| 960x540 | pending | pending | pending | pending |
| 1280x720 | pending | pending | pending | pending |
| 1920x1080 | pending | pending | pending | pending |
| 1024x768 (4:3) | pending | pending | pending | pending |

For each feasible cell record screenshot of title, hub, sector combat, terminal/settings, software selection and HUD. Record the exact input path, pointer hover/click target in logical and physical pixels, and any DPI mode unavailable on the host. For 1024x768 baseline content has top/bottom letterbox of **96 px** each. Do not compare Windows title bars with game viewport.

From repository root:
```powershell
python -m neonvoid --selftest 3600 --no-sound --seed 42 --god
python neon_void.py
```
First command verifies simulation only, not GUI. For captures document the same seed and human input sequence.

## P1 real Godot Windows verification

1. Verify **current PR head** Windows+Linux Python and Godot headless checks all success, plus Windows x64 export+exported EXE headless boot **success**; record artifact/hash. A skipped export is incomplete.
2. Open official **Godot 4.7.2 stable** project `godot/project.godot` on actual Windows desktop. Record an uncropped screenshot of primitive 3D floor/sphere, vertical orthographic camera, renderer/GPU info and runtime errors.
3. Run actual exported Windows EXE **without** `--headless` and capture on-screen output, including Forward+ result. Try `--rendering-method gl_compatibility` separately and record visual fallback differences.
4. Test viewport and DPI matrix above, record pointer/camera discrepancy and letterbox coordinates where possible. P1 has no playable game UI; this step checks renderer stability only, while precise pointer and camera parity remains P2.
5. Suggested filenames: `p0-legacy_1280x720_dpi100_hub.png`, `p1-godot_1280x720_dpi100_scene.png`. Link evidence from #53/#54 without uploading private save data.

## Sign-off gates

- [ ] P0 actual Windows screenshots and DPI/pointer reference reviewed, issue #53 updated.
- [ ] P1 all required CI green on latest HEAD, including Windows export+exported EXE boot.
- [ ] P1 real Windows GPU scene visible in editor AND exported EXE; version/driver/capture recorded.
- [ ] P1 camera/viewport observations documented without claiming P2 pixel-perfect parity.
- [ ] After prerequisite sign-offs, PR #65 no conflicts/reviews blocked; only then squash merge and close #54.

## P10 future evidence (NOT claimed complete)

For each sector, boss, packed bullets and hub: same seed and hardware, actual CPU/GPU engine utilization, average/p95 frametime, draw calls, memory; 30-60 minute real campaign + OS CLEAN/extract-one, save crash/rollback, F11, DPI variants, Alt+Tab and audio. Existing Python build must remain usable until approved.
