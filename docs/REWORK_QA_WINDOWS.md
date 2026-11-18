# Windows REWORK QA executable: identify the *actual* launch scene

**A-only opt-in QA packaging. No main/legacy scene or existing save is modified.**

## Previous #140 artifact was incorrectly described

PR #140's GitHub Windows artifact `NeonVoid3D-windows-export-smoke` exported the default `project.godot` `run/main_scene="res://scenes/main.tscn"`. That is the **legacy-faithful Godot port**. Its successful headless export and its separately run rework smoke scripts do **not** prove that the shipped EXE starts the rework pilot. The user correctly reported legacy mechanics despite rework tests passing.

## Download the correct build

Use **Actions → Godot 4 port checks → Windows x64 export and executable headless boot → Artifacts → `NeonVoid-REWORK-QA-windows`**. This downloads a ZIP containing **`NeonVoid-Rework-QA.exe`**.

Do **not** launch `NeonVoid3D.exe` or `NeonVoid3D-legacy-windows-export-smoke` for rework QA.

The CI action explicitly switches only its **second build's temporary project settings** to `res://scenes/rework/integration/rework_qa_boot.tscn`, reimports, exports, checks EXE boot and runs a project main-scene/assertion script **against the exported QA EXE itself**, then restores the original `project.godot` in the runner. The source branch retains `scenes/main.tscn` as default and the legacy artifact is explicitly named.

## Verify that the rework actually started

- Window title: **Neon Void | REWORK QA (not legacy)**.
- Top-right overlay: **REWORK QA · NOV 18**, 22 permanent E stats, 27 C plugin catalog entries, 3 B provisional active skill IDs and 5 D hardware zones.
- HUB Tab: five-section management panel, including vaccine/copies, 22 permanent investment fields, DB, skills and B combat details.
- First-run QA test slot `nv-rework-qa-20261118` gives a **synthetic demo-only starter loadout** with three E-authorized skills (`Q` Arc Pulse, `E` Penetrator, `C` Root Packet), 5 investable points, 3 vaccine copies, 2 archived common plugins and 1 carried common plugin. This is **not normal game earning or approved game balance**. Only the initial, absent QA slot is created; existing QA progress is never overwritten, reset, or double-granted. No Python or legacy saves are touched.
- Real 27 C plugin definitions are a **reward catalog**, not 27 simultaneously equipped modules. Level-up shows three random cards to choose one; archive and carried modules persist under E contracts. Normal play now has a **draft** discovery flow: after `hardware_cleansed` is E-acknowledged, three deterministic skill cards appear and the choice is stored by E. The pool (9 IDs) and its numbers are **BALANCE_DRAFT**, not approved final balance; this QA scene still seeds the same three demo skills directly so combat can be tested without playing a full mission.

## Recommended manual Windows QA

1. Start the **REWORK QA** EXE and verify the stamp and window title. If neither appears, report the EXE filename and action run.
2. Press **Tab** in the hub. Confirm copies `3/3`, five available points and 22 stat controls; invest attack damage and save.
3. Check **스킬 장착** shows Arc Pulse, Penetrator, Root Packet; check **데이터베이스** for the 2 archived plugins and one carried plugin.
4. Start a mission by walking to the in-world deployment terminal, press **F**, then fight the motherboard intro. Confirm Q/E/C consume mana and trigger real cooldown, and the tactical HUD shows B resources.
5. Enter CPU, GPU, RAM, SSD, HDD. Different hardware has different D objectives, hazards and theme colors; **current D geometry uses one shared procedural maze family and generic 3D meshes**. The distinct boss *roles* and rules have been implemented, but this build does **not** claim unique polished boss models/animations or final combat/environment balance. Record gameplay and screenshots where appearance still feels identical.
6. For persistence, after a **completed** mission, exit to the hub normally and restart the EXE. Verify E-saved level, fragments, copies, acquired modules and equipped skills. **Do not force-close mid-mission to test recovery:** this build deliberately does not resume interrupted active missions and enters a reconciliation-safe state rather than fabricating a saved run.

## Remaining genuine work

- Approve the regular-player skill pool and balance, then verify the draft discovery UI in real mouse/keyboard play (tooltips, rebinding, interruption and full-pool behavior).
- Improve differentiation of boss visual models, environmental material and hazards, region navigation, and status effect VFX. CI currently verifies logic and receipts, not human perception of art/game feel.
- Review B/GDD draft skill and stat increments, plus Windows GPU/audio/DPI/framerate and full player-controlled long-session QA.

See `tests/rework/integration/rework_qa_boot_smoke.gd` for the saved-slot and QA scene contract; it is deliberately run in Linux, Windows, and the second exported Windows EXE.
