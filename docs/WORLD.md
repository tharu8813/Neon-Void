# NEON VOID in-world OS lifecycle (A1 → checkpoint)

**Current status (2026-11-16):** Four playable sectors, OS CLEAN/extraction and DB progression from A2/A3 are implemented. The **#22 checkpoint feature** now stores a resumed OS in a dedicated atomic `os_checkpoint.json`; earlier historical A1/A3 notes below describe the former session-only behavior.

## OS checkpoint / crash recovery

1. While an OS deployment is active, a valid save records its `run_id`, deterministic `seed`, per-sector completed waves, the player's equipped software and key combat resources, infection/integrity, purification charges, score and elapsed time.
2. Saves happen at deployment, wave starts/clears, upgrade choice, sector entry/retreat, purification and roughly every five simulated seconds. Closing the window **suspends** the OS rather than issuing an abandonment receipt; pressing R to abandon, dying or extracting still finalizes the run.
3. On the next launch, approach the outer hub's **OS DEPLOYMENT** gate. Choose **Continue old OS** to resume the identical `run_id`, or **Abandon and deploy new**. That explicit discard writes a C database closure receipt before removing the checkpoint. A checkpoint with an already closed/extracted OS receipt is always treated as stale and removed instead of being resurrected.
4. Crash midwave: the previous **fully completed** waves, temporary software and resources are restored. Live enemies/projectiles are *not* saved; the interrupted wave restarts from the beginning. A wave cleared in C's database before the OS file was replaced is reconciled using durable XP receipts. XP and OS extraction remain idempotent.
5. A save with an interrupted software-selection screen restores the same pending choices. After a successful extraction, death or deliberate abandonment, stale checkpoints cannot create extra software or XP. A saved **dead** marker cannot be resumed into a living player after a crash.
6. Versioned JSON with strict schema validation, 128 KiB size limit, atomic `os.replace`, previous-good `.bak` file, CAS-style revision and a cross-process lock protects against two concurrent game processes. Corrupt bytes are preserved for manual recovery as `.corrupt` after explicit user choice, never silently overwritten.
7. The primary save is `os_checkpoint.json`. It is **not** authoritative for lifetime XP or permanently archived software. C's `antivirus_db.json` remains the source of truth, unchanged. Python sources save beside the launcher; bundled Windows EXE uses `%LOCALAPPDATA%\\NeonVoid`.
8. This is **safe-boundary resume**, not exact frame-by-frame replay: after an abrupt process stop, the last few seconds of HP/MP/position or unfinished combat may not be retained. Real Windows display/audio/extended playtesting remains in #24. DPI/fullscreen improvements are #23.

Automated validation: `python -m unittest discover -s tests -v`, `python -m tests.gui_checkpoint_smoke` (Windows) or `xvfb-run -a python -m tests.gui_checkpoint_smoke` (Linux). The GUI test verifies CPU 3/5 + MEMORY 1/5 restoration, unchanged run ID, XP idempotence, pending software-choice screen, explicit new OS, stale closed run cleanup, and terminal death marker.

## Historical design evolution

This is the first playable vertical slice of the free-exploration overhaul specified by [Epic #10](https://github.com/tharu8813/Neon-Void/issues/10) and [A integration #13](https://github.com/tharu8813/Neon-Void/issues/13). It does **not** yet implement B's full multi-sector content nor C's persistent antivirus database and extraction.

## Game flow

1. Launch normally (`python neon_void.py`) into the live **ANTIVIRUS CORE SYSTEM HUB**, already controlling the avatar. There is no stage-selection screen.
2. Move the avatar (WASD/arrows), dash (Shift/Space), approach the **OS DEPLOYMENT** terminal to the east and press the remappable interaction key (**F**).
3. Enter the OS junction, use the north gate for **CPU CORE**. The Memory/Storage/Network gates are visible but deliberately marked **SEALED** until the B content integration.
4. Fight CPU waves 1–5 with all existing auto-fire, Q/E skills, mana, armor, DoT and software selection; wave 5 is the existing legacy boss. This uses the **void** stage director as a bootstrap, not the future CPU-specific enemy/boss catalog.
5. Approach the portal on the left of the arena and press **F** to retreat. The completed wave count persists **in the current session**, and the unfinished wave is retried upon re-entry. Already completed waves never pay their upgrade choice twice. The player retains HP/MP and collected software during a nonfatal retreat.
6. After clearing CPU wave 5, its gate displays **CLEAN**, but the OS remains infected because the other three regions are not yet connected. Full OS CLEAN/extraction/new run is an A2/A3 integration task.

## Technical contracts

- `neonvoid/world/session.py`: `WorldSession.room` (base/os), `WorldSession.active_sector` (None or id), `sector_waves: dict[str,int]`, `sector_clean: set[str]`, `WorldSession.os_clean`, `enter_sector`, `leave_sector`, `wave_cleared`, `reset_to_base`.
- `move_on_world`: bounded 2D world coordinates with axis-separated circle/rectangle collision.
- `camera_for`: top-left camera clamp for a scrolling 2400×1560 world in the 1280×720 view.
- **Game owns transitions and presentation**, world module has no dependency on Tk. B must supply sector content and infection APIs, C supplies database and extraction logic. All dynamic game/UX work stays with A.
- Core events: `os_deployed`, `sector_entered`, `wave_cleared`, `sector_cleared`, `sector_exited`, `returned_to_base`. An existing event bus profile tracker continues receiving its older events.
- The old `--selftest` entrypoint still runs through the legacy title + unlimited wave generator, so its existing 3,600-frame regression retains meaning.
- **Save policy:** A1 world progress is **session-only**. Closing the app abandons the active run, so do not promise checkpoint restore or permanent wave completion yet. C introduces permanent profile data; A later adds OS checkpoint save/restore if the final architecture needs it.

## Testing

```powershell
python -m unittest discover -s tests -v
python -m tests.gui_world_smoke
python neon_void.py --selftest 3600 --no-sound --god --seed 42
```

Windows and Linux CI run these tests (Linux GUI via Xvfb). They cannot verify subjective gameplay, hardware audio or real monitor DPI: manual Windows testing is still required.

## A2 integration: all sectors and infection (implemented in PR A2)

The four sector gates use the B-owned catalog rather than the legacy default stage. The visible world and B campaign are synchronized through `WorldSession` (lowercase geographical IDs) and `SectorCampaign` (uppercase B IDs). Each wave uses a stable sector/seed-specific spawn plan and verifies the final boss was actually killed before clearing wave 5.

Real viruses now have geometric artwork and bounded pattern execution. Replication, projectiles, overheat charge, armor pulse, encryption area pulse, boss barrages and armor penetration are wired into the 60Hz game simulation. Contact/projectile attacks only add infection when actually hitting the player; damage-over-time cannot recursively proc infection.

`InfectionState` grows from sector-specific exposure and confirmed hits. It drains OS integrity at high stages but **never modifies move speed, aim, dash, visibility, or skill availability**. The OS junction has a purification station with three uses per deployment, each reducing infection by up to 32% and restoring up to 9% integrity. The HUD shows infection and OS integrity.

The B campaign emits `sector_entered`, `wave_cleared`, `sector_cleared`, `os_cleaned`; per-sector progress cannot be duplicated. The A3 phase connects these events to C's persistent database XP/level receipts, OS CLEAN extraction terminal and re-deployment. **A2 does not yet award XP, save OS checkpoints, or extract software.**

Additional GUI validation: `python -m tests.gui_sectors_smoke` traverses all four sectors in a deliberately non-linear order and verifies 20 separate waves, bosses, infection accumulation, campaign/world progression and OS CLEAN.

## A3: durable antivirus database, CLEAN and extraction

The full OS loop uses B's `SectorCampaign` and C's `AntivirusDatabaseStore` (separate `antivirus_db.json`) without modifying either team's modules. Campaign events are validated in this order: five independently completed waves, wave-5 confirmed boss kill, sector-clean award, all-four-sector OS-clean award. The permanent DB records every receipt using a unique OS run ID; replay of an event is a no-op. `sync_campaign_rewards` retries previously confirmed achievements before extraction if a transient write earlier failed. Failed/corrupt DBs **do not allow extraction**, and the game does not advertise reward success before disk commit.

Interact with the **ANTIVIRUS DATABASE** terminal (F) in the outer hub to select zero or one archived starting module. Deploying creates a new run ID, applies fixed level-1 stats plus permanent level bonuses to a new Player, then installs only one selected software copy. The active Player is not retroactively buffed if XP levels the DB during an OS.

After all four sectors have clean wave-5 boss receipts, the OS hub's **SYSTEM STATUS** becomes the extraction terminal. It is never auto-triggered. Select one eligible currently equipped software from the UI and press Enter. Only after a successful transactional `commit_extraction_once` can the player return to the outer hub; the ProfileTracker records an `extracted` run (profile schema v2 transparently reads schema v1). Subsequent OS deployment resets temporary software and sector progress but retains XP, level and archived modules.

The DB is fail-closed on corruption and has an independent backup and atomic write strategy. See `docs/ANTIVIRUS_DATABASE.md` for exact return conditions. If `prepare_extraction` reports `no_software` or `archive_full`, the CLEAN status terminal offers a **one-time software salvage** from existing game software that is not yet owned in this run and whose permanent archive is below its own stack cap. The actual A-owned `apply_upgrade` installs that one salvage choice in the active loadout. C still independently verifies it is really owned and only archives one copy after the durable transaction. If **all** software archive caps are reached, the terminal offers an explicit **no-additional-reward return**: the deployment is recorded as abandoned rather than falsely extracted, earned XP stays persistent, and no software cap is bypassed.

`_close_database_run` retries a transient `save_failed` closure receipt at most three times and reports any remaining error; a failed abandonment does **not** erase the active OS or emit `run_abandoned`. Closing the window after failed persistence asks for explicit confirmation before forfeiting the unsaved terminal receipt. This is not a crash-resilient OS checkpoint. OS integrity reaching zero triggers emergency retreat without software, preserving XP and verifying the run-close receipt. Successful extractions show a CLEAN transfer flash/ring/banner for 2.5 seconds.

The game-world per-sector checkpoint is **session-only**. Unexpected exits lose unfinished OS state, but already persisted XP stays earned. Proper crash recovery and persistent sector checkpoints are a separate deliverable. Running unit/GUI CI does not replace manual Windows visual/audio/DPI playtesting.

Additional Windows/Linux Tk integration: `python -m tests.gui_extraction_smoke` executes all four sectors, obtains 29 unique reward receipts, extracts precisely one software, checks the durable DB, and deploys a second OS with level bonuses and one archived starting module.
