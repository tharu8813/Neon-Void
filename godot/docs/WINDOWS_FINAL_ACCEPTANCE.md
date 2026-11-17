# Neon Void Godot 4.7.2: Windows final acceptance protocol

**Status: manual acceptance required.** This is the Godot 3D migration test candidate from [integration PR #76](https://github.com/tharu8813/Neon-Void/pull/76) and [parent epic #52](https://github.com/tharu8813/Neon-Void/issues/52). The legacy Python/Tk release remains authoritative and is NOT replaced by this candidate.

## 1. Evidence and artifact

1. At [PR #76](https://github.com/tharu8813/Neon-Void/pull/76), open the **latest-head** `Godot 4 port checks` workflow run. Confirm *Windows x64 export and executable headless boot* passed. Download the short-retention `NeonVoid3D-windows-export-smoke` artifact from its **Artifacts** section. Ensure it came from the current PR head and not an older successful run.
2. Test the EXE on a real Windows desktop with an actual visible window, not only CI's `--headless` boot. Record OS build, native screen resolution, display scale/DPI, GPU/driver, connected monitors, selected window mode and renderer mode.
3. Retain the **original five legacy files** (if present): `save.json`, `settings.json`, `profile.json`, `antivirus_db.json`, `os_checkpoint.json`. Copy them to a separate backup BEFORE touching any new build. Do not put real or sensitive files in GitHub issues; report only redacted evidence.
4. Keep the Python version installed/playable. Check `git status` on the repo and do not treat Godot as the default replacement until the entire checklist has passed.

## 2. P0/P1/P2 real-screen baseline

| Windows test | Required observation |
|---|---|
| Tk original at 960×540, 1280×720, 1920×1080 | Record original top-down camera, player position, UI, and pointer targets |
| 1024×768 (4:3) and 21:9 | No content stretched offscreen; actual letterboxing and mouse target position agree |
| Windows display scale 100%, 125%, 150%, 200% where available | Screenshot/render and mouse click coordinates agree with original; no inaccessible UI |
| Windowed/fullscreen/borderless, moving across monitors | Resolution and UI/input transitions behave correctly |
| Camera following in hub, stationary arena camera | Same zoom, center/clamp and top-down orientation as original |

A real screenshot comparison, not headless camera coordinate unit tests, closes P0 #53, P1 #54 and P2 #55 visual gates. Capture differences quantitatively and attach redacted side-by-side PNGs.

## 3. Gameplay parity across four sectors

- Move with WASD/arrow keys; verify diagonal normalization, collisions, direction of dash and recharge, aim follows mouse, custom cursor visibility/focus return, Shift/Space mapping, controls rebinding, Q/E skills and cooldowns, R retreat, F interact, ESC pause, Tab loadout. Verify Alt+Tab and focus loss does not hold input.
- Start a **new OS**; walk in the hub to each of CPU CORE, MEMORY SECTOR, STORAGE VAULT and NETWORK GRID. Traverse them in a non-default order. Complete all five waves and bosses in every sector. Check enemy types and abilities, special warning telegraphs, auto-fire, damage, infection and purification.
- Compare the 30 software IDs, rarity/caps, three-card selection, boss signature rewards, damage/armor/penetration, HP/mana, status effects and ability timing against the Tk original.
- Interrupt a wave, retreat and re-enter. Progress must reflect only the last **confirmed** wave; an unfinished encounter must not award XP or duplicate a boss reward.
- Recover a saved OS after app restart, including at the **three-card reward screen**. Verify zero duplicate XP/rewards, progress and available software are preserved.
- Complete OS CLEAN, choose **one** software to permanently archive, then start a subsequent OS and choose one archived starting module. Check antivirus XP/level and that only one new starting module is equipped.
- Validate that the score, best-wave record and profile's recent run history update on completion, abandonment and death, without duplication.

## 4. Migration and data loss testing: COPIES ONLY

- Start with a **copy of legacy saves**. In the Godot title menu, explicitly choose `기존 Python 저장 데이터 가져오기` and point to the copied source directory. Verify a separate Godot slot exists and original/copied source bytes and SHA-256 are unchanged.
- Verify the migration manifest and `legacy_originals` backup. Attempt a second import while a Godot slot already exists; it must refuse overwriting.
- Use independent synthetic copies to exercise truncated JSON, unsupported schema version, low disk space/denied permissions, power interruption during save and process kill/restart. The app must not silently reset corrupt saves or commit XP without a wave checkpoint.
- Confirm changes to volume, screen mode and key bindings persist after restart. Force-close during a run, restart and verify no incomplete wave is promoted to cleared.
- On explicit abandonment, player death, OS integrity failure, and successful extraction verify the terminal receipt, closed checkpoint, profile and high score are **committed together**. Never test destructive faults with the original Python profile.

## 5. Renderer, responsiveness and endurance

- In Task Manager/GPU-Z, observe **GPU 3D** engine utilization during combat and heavy projectile loads. Headless CI cannot prove it. Record idle and combat CPU/GPU use, render framerate, p50/p95/p99 frametime and memory/VRAM in a comparable windowed 1920×1080 setup.
- Test the heaviest projectile and enemy wave (especially NETWORK GRID), D HUD, text readability, glow, player/enemy identification, effects reductions, and whether 3D lighting occludes aim targets. Compare with the Python gameplay baseline.
- Run an uninterrupted **30–60 minute** session through the full deployment → sectors → CLEAN → extraction → new deployment cycle. Log crashes, mismatched hitboxes, stuck input, save errors, lost assets, and frame hitches.
- The standalone `godot/benchmarks/p10_benchmark.tscn` arithmetic benchmark does **not** replace rendered-frame measurement on a physical Windows GPU.

## 6. Final release decision

Do not squash-merge PR #65–#76 into `main`, mark issues #53–#63 complete, or publish a Godot replacement release until:
1. All **latest HEAD** automated checks on the final combined commit pass, including Windows export and its boot.
2. Real Windows screenshots and DPI/pointer parity are reviewed.
3. Full real gameplay, imported-save recovery, and irreversible rewards are approved.
4. Physical GPU/performance/quality measurements and crash/error regressions are reviewed.
5. All review blockers and integration conflicts are resolved, while the original Tk version remains recoverable.

**Suggested defect report format:** observed behavior; expected legacy behavior; current PR HEAD and build artifact; step-by-step reproduction; Windows display/GPU environment; screenshots or sanitized logs; whether user data was modified (do not attach real saves).
