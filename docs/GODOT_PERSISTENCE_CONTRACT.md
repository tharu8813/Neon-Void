# Godot P8 persistence and migration contract (Issue #61)

> This port is **not** the legacy writer. Python/Tk files are the source of truth
> for Python; the Godot port writes only its own `user://` slot. No automatic
> import or transparent overwrite is allowed. PR based on P1 #65, so keep it
> unmerged until P5/P6/P7 and the physical Windows gates pass.

## Inventory and ownership (frozen Python baseline)

| Legacy filename | Origin | Schema | Godot authority |
| --- | --- | --- | --- |
| `save.json` | `neonvoid/storage.py` | unversioned best_score, best_wave, runs | score/run history only |
| `settings.json` | `neonvoid/settings.py` | v1–v5; v5 has 9 remappable controls | settings UI, **not** database XP |
| `profile.json` | `neonvoid/profile.py` | v1–v2; v2 adds extracted_runs | statistics/recent 20 runs only |
| `antivirus_db.json` | `neonvoid/database_storage.py` | exact v1, strict receipts | **SOLE authority** for cumulative XP, level, archived software, extraction/closed run receipts |
| `os_checkpoint.json` | `neonvoid/os_checkpoint.py` | exact v1 | one confirmed OS run state and completed waves; never active enemies or bullets |

Python source execution resolves files in the repository root.
Frozen Python Windows builds resolve to
`%LOCALAPPDATA%\\NeonVoid\\`, falling back to `~/.neonvoid` when
LocalAppData is inaccessible. These are distinct potential sources.
The importer **never scans** for files automatically: the user must select
the exact source directory. It never modifies that directory.

Godot default target: `user://persistence/slot-v1` under the Godot
application's OS-specific user data root, which is **not** the Python root.
Godot project name / app identifier affect the resolved physical location.
Use `ProjectSettings.globalize_path("user://")` to display the precise
target in the import dialog. Imported backups live under
`slot-v1/legacy_originals/<original filename>`; `migration_manifest.json`
records schema versions, source location, exact byte counts and SHA-256 for
each present original. The five converted documents live in a single
`slot-v1/state.json` envelope, checksum-covered and revisioned. Supported older settings v1–v3 are normalized to settings v5 with the\noriginal Python binding-migration priorities, and profile v1 to profile v2;\noriginal bytes remain in the backup. Missing optional legacy documents are\nexplicitly recorded as absent and given safe in-memory defaults. No silently corrupt file is treated as absent.

## Explicit import protocol

1. Ask the user to pick a *legacy directory* and acknowledge conversion.
   Call `migration_service.gd.import_legacy(source_dir, target, true)`.
2. Reject overlapping source/target paths, existing Godot slots, unsupported
   future versions, corrupt JSON, inconsistent receipts, unsupported precision,
   and bad/oversized checkpoints **before** any committed destination exists.
3. Stage inside a brand-new sibling directory on the same filesystem. Copy
   exact original bytes to `legacy_originals`; verify backup SHA-256.
   Write the manifest and converted state envelope. Re-read every source and
   compare checksums to catch concurrent Python changes.
4. Atomically rename the staged directory into the absent Godot target.
   On failure, remove **only** the newly owned staging directory. A process
   crash before commit can leave an `.importing-*` staging directory; it is
   not considered imported and should be inspected before manual cleanup.
   Never automatically recover by writing back to legacy sources.
5. Importing again into an existing slot is refused, even if the sources changed.
   Users must not be invited to delete an existing Godot slot as a shortcut.

JSON values above IEEE-754's exact integer range (2^53-1) are refused.
This includes Python-supported `2^63-1` XP values. Refusal is intentional
rather than silently corrupting high progression; lossless big-integer
support needs a separately reviewed format.

## Runtime transaction and recovery

`save_store.gd` manages one envelope containing all five documents:
`{format:1, revision:N, payload_sha256:..., payload_json:"..."}`. The SHA-256 is computed over the **exact serialized payload_json string** to\navoid type-representation changes after parsing; the inner JSON and all\ncurrent schema fields are revalidated before use.
A runtime mutation takes a directory lock (`<slot>.write-lock`), re-reads
the current envelope, computes a new immutable-style state, validates it,
writes the old full snapshot to `state.json.bak`, writes a synced temporary
envelope, and renames it to the primary path. A response reports
`applied=true` **only after** the new primary was verified.

A stale or corrupt primary is NEVER reset to defaults. Manual
`SaveStore.recover_from_backup(slot, true)` first verifies the backup in an
isolated directory, preserves primary bytes under
`state.json.before-recovery-*`, then restores the backup. The backup is the
**previous snapshot**, potentially missing the most recent awarded XP.
Recovery is an explicit user decision, not automatic. A crash can leave a
`.write-lock` sidecar; a user/admin must verify no game process is running
before manually removing it. File rename and directory rename atomicity
depends on the target filesystem; durable power-loss guarantees on all
filesystems require platform-specific fsync and remain an integration gate.

Do not mix Python and Godot writers, change the Godot project identifier
after release without migration, or promise perfect power-loss durability
based solely on headless tests.

## C sector events and D GUI integration

Public pure logic: `antivirus_progression.gd`:
`base_stats_for_level(level)`, `xp_progress(db)`,
`award_event(state, run_id, event, sector_id, wave_index)`,
`eligible_for_extraction(...)`, `extract_once(...)`,
`starting_software(db, selected_id)`.

Public persistent APIs:
- `Progression.persist_event(slot, run_id, event, sector_id="", wave_index=0)`
- `Progression.persist_close_run(slot, run_id, status)`
- `Progression.persist_extraction(slot, run_id, clean_sectors, software_levels, chosen_id)`
- `Progression.load_database(slot)`
- `Checkpoint.persist_checkpoint(slot, snapshot, expected_cp_revision)`
- `Checkpoint.persist_confirmed_wave(slot, snapshot, expected_cp_revision, run_id, sector_id, wave_index)`
- `Checkpoint.clear_after_run(slot, expected_cp_revision)`
- `Checkpoint.resume(loaded_state)`
- `Store.read(slot)`, `Store.persist_document(slot, name, value, expected_revision)` for `save.json`, `settings.json` or `profile.json` only (stale UI snapshots return `revision_conflict`); `Store.recover_from_backup(slot, true)`

Reward reasons include `granted`, `duplicate`, `run_closed`,
`prerequisites_missing`, `sectors_not_clean`,
`already_extracted`, `invalid_selection`, `save_failed`, and
`locked`. **Never** play an extraction success animation, transition back
to the hub, or increase an XP/level HUD if `applied` is false.

C should use **stable** `run_id` per operating system and the sector IDs
`cpu_core`, `memory_sector`, `storage_vault`, `network_grid`.
Events must be ordered: 5 `wave_cleared` per sector (20 XP each), then
`boss_killed` (75), `sector_cleared` (50); after all four, `os_cleaned`
(200). `Checkpoint.persist_confirmed_wave` combines a wave XP receipt
with the newly confirmed checkpoint in one atomic update. C should use it
for wave clears, rather than two independent writes.

Extraction requires both the four reported CLEAN sectors and the **29
persisted progress receipts**, plus a currently owned eligible software ID.
Exactly one new permanent copy is recorded alongside the terminal run state.
A duplicate `run_id` cannot claim more copies. Next OS starts with zero
or one archived software at level 1; it never installs all archived stacks.

At NEW OS initialization only, snapshot base HP/damage/armor/mana:
level 1 = 100/10/4/100, every higher level = +2/+0.2/+0.15/+1,
level cap 30. Do not retroactively change the live character at level-up.

Checkpoint migration preserves `campaign.completed_waves` and
`campaign.run_id`; it never attempts to restore active projectiles, live
enemies or an unfinished wave. `Checkpoint.resume` returns each zone's
`next_waves` = saved completed waves + 1 (0 means finished).
Checkpoint revisions are checked and cannot skip/regress confirmed waves.
Starting a new run must close the previous run first.

**Integration ownership:** E owns `godot/scripts/persistence/**`,
`godot/scripts/progression/**`, the P8 test and this contract. C owns
sector events/campaign; D owns UI/menus. Neither team should write the
DB or checkpoint JSON directly.

## Automated checks / remaining gates

`godot --headless --path godot --script res://tests/persistence_smoke.gd`
runs synthetic import fixtures under a newly generated
`user://p8-test-*` directory and checks import refusal, original-byte\nimmutability, normalized v2 settings/profile migration, guarded settings UI\nwrites, backup write failure (no success receipt), backups, XP receipts, extraction, revision conflicts,
prior-snapshot recovery and future-schema rejection. It deletes only its
own synthetic fixture directory. No real user saves are ever opened.

Required before merge: green Linux and Windows Godot Actions for latest
HEAD; manual Windows import from **copies** of known versions with varied
DPI/permissions, running two Windows processes, interrupted writes,
recovering from abrupt termination, settings and profile UI parity,
P5–P7 integration, and all outstanding P0/P1 acceptance criteria.
