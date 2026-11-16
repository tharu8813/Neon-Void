# Antivirus database: C → A integration contract

Issue: #12. Parent: #10. This module intentionally does **not** change Game,
Player, legacy save/profile/settings, stage content, combat, rendering or events.

## Durable file and schema

`neonvoid.database_storage.database_path()` resolves the independent
`<runtime_paths.data_directory()>/antivirus_db.json` at call time, including
the LocalAppData folder used by a frozen Windows build. It never touches
`save.json`, `profile.json` or `settings.json`.

Schema v1 example:

```json
{
  "schema_version": 1,
  "antivirus_level": 1,
  "xp": 20,
  "software_archive": {},
  "awarded_xp_ids": {"run-uuid:cpu_core:wave:1": 20},
  "processed_extractions": {},
  "closed_runs": {}
}
```

- `xp`: lifetime cumulative XP (not spent; level is derived from it).
- `software_archive[id]`: number of permanently extracted **copies**
  (1..the original `UP_BY_ID[id][4]` stack cap). It is NOT the active level.
- `awarded_xp_ids`: unique `run_id:event_id -> awarded amount` receipts.
- `processed_extractions[run_id]`: permanently claimed software ID.
- `closed_runs[run_id]`: terminal `dead`, `abandoned` or `extracted`.
- All fields required. Schema version is exact. Bad JSON, unknown IDs,
  bools in place of integers, negative XP, contradictory receipt totals,
  future schema, and missing fields raise `DatabaseCorruptError`.
- A **missing** file creates a default in-memory level-1 DB without writing.
  An **existing corrupt** DB is never silently reset/overwritten. The last
  good *previous* version is stored in `antivirus_db.json.bak` on updates;
  `load_database_backup()` only inspects it. Restoring it is an explicit
  manual recovery decision: it might be older than awarded receipts.
- `save_database(db, path=None) -> bool` validates; writes a synced temp
  file + atomic `os.replace`; false on write errors. Use it only for
  exceptional manual state creation/export, **never** for read-modify-save
  XP/extraction transactions (a stale copy risks clobbering another process).

## Public functions and return types

```python
from neonvoid.metaprogression import (
    AntivirusDatabase, REQUIRED_SECTOR_IDS, XP_REWARDS,
    xp_threshold, xp_progress, base_stats_for_level,
    grant_xp_once, award_progress_once, close_run_once,
)
from neonvoid.extraction import (
    eligible_software, prepare_extraction, commit_extraction_once,
    select_starting_software, starting_software_levels,
)
from neonvoid.database_storage import (
    AntivirusDatabaseStore, load_database, save_database,
    load_database_backup, DatabaseCorruptError, DatabaseStorageError,
)
```

Pure functions (return a **NEW** DB if changed, never mutate input):

- `xp_threshold(level: int) -> int`: `100 + 50 * (level-1)`;
  level 30 is the hard cap.
- `base_stats_for_level(level: int) -> dict[str, float]`: keys
  `maxhp`, `dmg`, `armor`, `maxmana`. Level 1 values are
  (100, 10, 4, 100); each additional level adds (+2, +0.2, +0.15, +1).
- `xp_progress(db) -> tuple[int, int]`: XP into current level and next
  threshold; (0,0) at cap. Total XP continues increasing at cap.
- `grant_xp_once(db, *, run_id, event_id, amount) ->
  tuple[AntivirusDatabase, RewardResult]`: low-level unique reward receipt.
- `award_progress_once(db, *, run_id, event, sector_id=None,
  wave_index=None) -> tuple[AntivirusDatabase, RewardResult]`:
  recommended high-level predefined reward amounts and prerequisite checks.
  `RewardResult(applied, reason, amount, level_before, level_after)`.
- `close_run_once(db, *, run_id, status="dead"|"abandoned") ->
  tuple[AntivirusDatabase, MutationResult]` ends a failed run; earned XP
  stays. This blocks all later XP and extraction on that `run_id`.
- `eligible_software(software_levels, archive) -> tuple[str, ...]`:
  sorted, actually owned and below archive cap.
- `prepare_extraction(db, *, run_id, clean_sector_ids, software_levels,
  was_defeated=False, abandoned=False) -> ExtractionOffer(allowed, reason,
  eligible_software)`: no changes, safe for the in-world selection terminal.
- `commit_extraction_once(db, *, run_id, clean_sector_ids, software_levels,
  selected_software_id, was_defeated=False, abandoned=False) ->
  tuple[AntivirusDatabase, ExtractionResult]`: pure transition that writes
  EXACTLY one copy + extraction receipt + terminal state in one new DB.
  `ExtractionResult(applied, reason, software_id)`.
- `select_starting_software(db, software_id: str | None) -> str | None`:
  returns one archived ID or None; unowned/unknown IDs raise ValueError.
  `starting_software_levels(db, software_id) -> {id: 1}` or `{}`.
  The entire archive must **not** be installed for a new run.

For persistent gameplay, use these `AntivirusDatabaseStore(path=None)`
methods instead of the pure mutation methods:

```python
store = AntivirusDatabaseStore()
db = store.load()
db, receipt = store.award_progress_once(
    run_id=run_id, sector_id="cpu_core", event="wave_cleared", wave_index=1)
db, receipt = store.grant_xp_once(
    run_id=run_id, event_id="custom:unique_event", amount=5)
db, receipt = store.close_run_once(run_id=run_id, status="dead")
db, receipt = store.commit_extraction_once(
    run_id=run_id,
    clean_sector_ids={"cpu_core", "memory_sector", "storage_vault", "network_grid"},
    software_levels=dict(game.levels),
    selected_software_id="rapid",
)
```

The store loads the **latest on-disk state under a cross-process exclusive
lock**, performs the pure transition, writes a valid JSON backup and atomically
replaces the main DB. The returned `receipt.applied=True` means the write
**finished successfully**. On a failure, it returns the original DB and
`applied=False, reason="save_failed"`. Do not leave the OS, update gameplay
inventory or emit success UI until after this result. The old main file stays
valid. Existing corruption raises `DatabaseCorruptError` and blocks rewards.

Non-success reasons:

| Reason | Meaning |
|---|---|
| `duplicate` | XP event receipt already present, no XP awarded |
| `prerequisites_missing` | Sector clean needs wave 5 + boss receipts; OS clean needs 4 sector-clean receipts |
| `run_closed` | XP denied after death, abandonment or successful extraction; repeated run close |
| `sectors_not_clean` | At least 1 of the four IDs or their durable clean/OS receipts is absent |
| `run_failed_or_closed` | Defeat/abandon flag supplied or run has terminal status |
| `already_extracted` | This `run_id` already claimed the ONE copy |
| `no_software` | No owned module this run; no reward consumed; player may acquire one |
| `archive_full` | All owned modules reached their individual archive cap |
| `invalid_selection` | Selected ID is not among eligible owned modules |
| `save_failed` | No new receipt/award persisted: show retry/error instead of success |

Invalid arguments (malformed run IDs, unknown sector IDs, invalid in-run
software levels) raise `ValueError` rather than become reward receipts.

## Sector IDs and XP event mapping

The A/B integration must use these stable IDs in its progress events:

```
cpu_core          CPU CORE
memory_sector     MEMORY SECTOR
storage_vault     STORAGE VAULT
network_grid      NETWORK GRID
```

The recommended XP policy is fixed in `XP_REWARDS`: wave first clear 20,
boss kill 75, sector clean 50, OS clean 200. Different OS instances **must**
use independent stable run IDs (e.g. `uuid.uuid4().hex`). Never reassign a
historical run ID to a new OS. For each sector award:

1. `event="wave_cleared", sector_id=..., wave_index=1..5`;
2. `event="boss_killed", sector_id=...` once for its boss;
3. `event="sector_cleared", sector_id=...` after wave 5 and boss;
4. after all four sector clean receipts, `event="os_cleaned"` once.

These can be safely retried: no progress receipt is duplicated. Sector
re-entry/replayed wave completion does not grant another XP reward. For
extraction, both current `clean_sector_ids` **and** the stored four sector
receipts plus OS-clean receipt are required. This makes a caller passing
`all_sectors_clean=True` without actual durable progress insufficient.

## New OS deployment and EventBus integration (A owns this)

```python
from uuid import uuid4
from neonvoid.database_storage import AntivirusDatabaseStore
from neonvoid.metaprogression import base_stats_for_level
from neonvoid.extraction import starting_software_levels
from neonvoid.content.upgrades import apply_upgrade

store = AntivirusDatabaseStore()
db = store.load()
run_id = uuid4().hex                # persist with the active OS checkpoint
selected = "rapid"                   # chosen at an in-world DB terminal, or None
starting = starting_software_levels(db, selected)

# Create a NEW Player before bonuses, then apply level base stats once:
stats = base_stats_for_level(db.antivirus_level)
p.maxhp = p.hp = stats["maxhp"]
p.st["dmg"] = stats["dmg"]
p.st["armor"] = stats["armor"]
p.maxmana = p.mana = stats["maxmana"]
# At initial OS spawn, ONLY install one copy using the existing game's
# apply_upgrade(g, uid) exactly once. It updates g.levels itself.
for uid in starting:
    apply_upgrade(g, uid)
```

At run start, **snapshot the level bonuses**; do not reapply them during that
run when XP makes the database level higher. Only update XP bar and level UI.
Existing `Player.st` fields other than dmg/armor, player movement, dashing,
and Q/E mechanics remain untouched.

A must emit `database_leveled` **only after a successful store receipt**
when `receipt.level_after > receipt.level_before`. It may emit
`extraction_committed` only if `receipt.applied` is true. Reuse existing
`EventBus.emit(name, **payload)`. None of the C modules attaches listeners
to existing gameplay events or changes event order.

A should call `store.close_run_once(... status="dead"|"abandoned")`
immediately on failure/abandonment and save the current run ID checkpoint.
The extraction terminal should call `prepare_extraction` on fresh
`store.load()`, display its `eligible_software`, then call
`store.commit_extraction_once` on selection, and return to SYSTEM HUB
only after `applied=True`. Do not save a local extracted flag first.
A crash after committed extraction but before the HUB transition is safe:
the receipt on disk will reject another reward for that run.

## Testing / limitations

`python -m unittest tests.test_metaprogression tests.test_extraction -v`

Coverage: strict schema and ID validation, XP thresholds/level cap,
cross-run and same-run duplicates, four-sector proof, OS clean XP, one-copy
extraction, archive stacking caps, starting loadout, failed runs,
corrupt-file fail-closed behavior, atomic write errors and retriable
transactions, multi-store reload, concurrent unique receipts.
Actual Windows UI/EXE integration belongs to A; CI passing is not manual QA.
