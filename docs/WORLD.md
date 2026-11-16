# A1: Playable world, antivirus hub and CPU gate

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
