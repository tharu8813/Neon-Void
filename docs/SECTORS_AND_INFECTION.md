# OS sectors, virus behaviors, and player infection (issue #11)

**Scope:** B-owned, headless content/API. The original `void` arena remains
playable with exactly the same seeded 5-wave cadence. New sectors become
**playable only after A** connects this API to the in-world gates, wave
lifecycle, projectiles, collision and renderer. B does not change the game's
existing controls, equipment, 30 software upgrades, Q/E skills, or UI.

## Sector data contract

| `sector_id` | Display name | Signature | Wave-5 `boss_kind` |
|---|---|---|---|
| `CPU` | CPU CORE | Rapid melee, heat and overclock | `overseer` |
| `MEMORY` | MEMORY SECTOR | Worms, copies and splitters | `replicator` |
| `STORAGE` | STORAGE VAULT | Armored ransomware and guardians | `encryptor` |
| `NETWORK` | NETWORK GRID | Swarms, feints and armor-bypassing volleys | `zero_day` |

`SECTORS` in `neonvoid.content.sectors` contains immutable
`SectorDefinition` values with `sector_id`, `name`, `danger`,
`feature`, `entry_gate`, `exit_gate`, `unlocked` (all true initially),
`required`, `clear_condition="five_waves_and_boss"`, `boss_kind`,
`stage` (a `StageDefinition`), and five `WaveDefinition` entries.
Each wave contains `wave_index`, `objective`, `feature`, `danger`,
`exposure_per_second` and `purify_on_clear` values. Gate IDs are logical
identifiers, not coordinates. A places gates in the rendered world.

Four independent 1..5 waves are required. Waves 1..4 have
`clear_condition="eliminate_hostiles"`; wave 5 has
`clear_condition="defeat_boss_and_hostiles"`. There are no locked regions.
Wave-5 boss and remaining support enemies must all be defeated to finish.
Completing the four regions marks the OS clean, but **does not trigger
automatic extraction**. Extraction and XP belong to A/C.

### Example: planning and playing a sector

```python
from neonvoid.content.sectors import (
    SECTOR_IDS, get_sector, plan_sector_wave, SectorCampaign,
)
from neonvoid.events import EventBus

bus = EventBus()
bus.subscribe("wave_cleared", lambda event: print(event.payload))

sector = get_sector("CPU")
assert sector.boss_kind == "overseer"
encounter = plan_sector_wave("CPU", wave_index=5, seed=42)
assert encounter.sector_id == "CPU"
assert encounter.wave_index == 5
assert encounter.boss_kind == "overseer"
assert encounter.clear_condition == "defeat_boss_and_hostiles"

campaign = SectorCampaign(run_id="os-run-0001", seed=42, events=bus)
campaign.enter_sector("CPU")
for _ in range(5):
    encounter = campaign.start_wave()
    # A schedules encounter.spawns [(seconds, enemy_kind), ...]
    # After game-owned enemy+boss death verification:
    campaign.clear_wave(
        hostiles_remaining=0,
        boss_defeated=encounter.boss_kind is not None,
    )
campaign.leave_sector()
assert campaign.progress["CPU"].is_clean
assert not campaign.os_clean  # other 3 sectors are still infected
```

`plan_sector_wave(sector_id, wave_index, seed=...)` is order-independent and
repeatable. It returns a frozen `SectorEncounter` carrying
`sector_id`, `wave_index`, `boss_kind`, `clear_condition`, `definition`
and `wave: WavePlan` plus the `spawns` shortcut. IDs accept upper- and
lowercase input and normalize to uppercase. Classic
`get_stage("void")`/`plan_wave` behavior is unchanged;
`get_stage("CPU")` resolves sector stage data. Sector wave planning only
accepts waves 1 through 5.

`SectorCampaign` holds independently mutable `progress[sector_id]` values
(`completed_waves`, `active_wave`, `is_clean`). Leaving discards an
active wave, **not** completed waves. Re-enter and `start_wave()` to retry.
A repeated `clear_wave` returns false and emits nothing. For every confirmed
clear, the campaign emits `wave_cleared`, with `sector_id`, `wave_index`,
`boss_kind`, `purify_on_clear` and `run_id`; wave 5 also emits
`sector_cleared`. Finishing all four emits one `os_cleaned` event.
`sector_entered`, `sector_wave_started` and `sector_left` are provided.
No XP is granted here. C must call `grant_xp_once` using run/sector/wave IDs
before awarding a bonus; A must not pay on repeat/retry events.

`checkpoint()` saves run ID, seed and only *confirmed* wave counts.
`SectorCampaign.from_checkpoint(snapshot, events=bus)` validates them
and recreates an inactive campaign. Incomplete waves must be retried.
On player death/failure, the world owner applies the run's failure policy;
this module never awards extraction rights.

## Virus action contract

The `neonvoid.content.virus_catalog` module defines `PATTERNS`,
`get_virus_pattern`, `infection_for_hit`, `boss_phase`, and
`VirusRuntime`. Its 11 new virus/boss kinds have compatible `E_PARAMS`
entries. `VirusRuntime` **plans actions, not physical combat**.

```python
from neonvoid.content.virus_catalog import VirusRuntime, infection_for_hit
from neonvoid.infection import InfectionState

runtime = VirusRuntime()     # new instance/reset_wave() per sector wave
infection = InfectionState()  # one state per deployed OS, not per wave

actions = runtime.step(
    source_id="enemy-27", enemy_kind="rootkit", dt=1 / 60,
    active_enemy_count=8, hp_fraction=1.0, paused=False,
)
for action in actions:
    # A translates action.behavior to a charge/bullet/shield/pulse/spawn.
    # The action carries count, power, projectile_count, armor_pen,
    # infection_on_hit, spawn_kind and telegraph_seconds.
    pass

# A calls this ONLY on a confirmed player hit, not on DoT ticks:
infection.apply_infection(infection_for_hit("rootkit"))
runtime.forget("enemy-27")  # when the enemy dies
```

Implement these behavior labels in A's enemy update/collision layer:

| Behavior | Kinds | World-side action |
|---|---|---|
| `telegraphed_charge` | overheater | Warning then directional charge |
| `replicate` | worm, replicator | Spawn `count` copies of `spawn_kind` |
| `feint_volley` | trojan | Warning, then a projectile spread |
| `encryption_pulse` | ransomware, encryptor | Telegraph area pressure, damage/OS corruption |
| `armor_piercing_volley` | rootkit | Projectile fan with `armor_pen` |
| `quick_volley` | packet_swarm | Short ranged/frequent projectile volley |
| `armor_pulse` | guardian | Temporary enemy-side protection |
| `overload_barrage` | overseer | Phase-based area volley |
| `breach_barrage` | zero_day | Phase-based armor-penetrating burst |

Boss HP ratio changes phase at 2/3 and 1/3. Boss projectile count grows and
cadence shortens as phases advance. Apply `armor_pen` to hostile bullets
through the existing combat damage calculation, **not** by changing player
armor permanently. Confirmed hits can add infection, but never disable aim,
dash, movement, skills or input. Never use infection to blind/stun/slow.

Limits: 64 live viruses recommended, 24 lifetime replications per wave,
source-local worm/replicator clone caps, at most 6 actions per update, and
128 simultaneously tracked source IDs. A must recheck live-entity capacity
when consuming spawn requests and call `reset_wave()` each wave. Pausing
or `dt=0` yields no actions. The original three-stack DoT cap remains intact;
new virus actions must not recursively trigger on-hit effects via DoT.

**Integration limitation:** legacy `Game.make_enemy`, `step_enemies`,
`update_boss` and `kill_enemy` branch on old `boss`, `splitter`,
`shooter` kind names. A must route sector bosses/viruses into these new
actions, label boss HP properly, manage split-spawn caps, and trigger campaign
clear only after verifying that the wave-5 boss has died. Merely assigning
`Game.stage = get_stage("CPU")` is not yet sufficient to activate the AI.

## Infection state contract

`neonvoid.infection.InfectionState` is independent of player combat
debuffs. This is **player infection**, never an HP multiplier.

| Stage (0..3) | Infection percentage | Meaning |
|---|---|---|
| `NORMAL` (0) | 0 to less than 25 | Normal |
| `DETECTED` (1) | 25 to less than 50 | Detected |
| `SPREADING` (2) | 50 to less than 75 | Spreading / purification suggested |
| `CRITICAL` (3) | 75 to 100 | OS integrity pressure |

```python
from neonvoid.infection import InfectionState

status = InfectionState(player_infection=0, os_integrity=100)
status.apply_infection(31)       # on confirmed hostile attack hit
status.purify(12)                # in-world purification station
info = status.tick(
    dt=1/60,
    exposure_per_second=0.45,   # sector.wave.exposure_per_second
    paused=False,
)
print(info.player_infection, info.infection_stage, info.os_integrity)
player_infection = status.player_infection
needs_purification = status.needs_purification
```

All numeric inputs must be finite/nonnegative. Values clamp to [0,100].
`apply_infection(amount, paused=False)`, `purify(amount, paused=False)`,
`repair_integrity(amount, paused=False)` return the actual applied amount.
`tick(dt, exposure_per_second=0, purification_per_second=0, paused=False)`
returns `InfectionTick` with `player_infection`, `infection_stage`,
`os_integrity`, `integrity_lost`, `needs_purification`. Threshold pressure
integrates against simulated elapsed time, not frame count. At 50% infection,
OS integrity drains 0.3 per second; at 75%, an additional 0.7 per second.
OS integrity at zero is **never automatic player death**. The world owner
decides what repair/cleanup objectives appear.

`snapshot()`/`from_snapshot()` allow saving an in-progress OS state.
`reset_for_new_os()` is for a truly new deployment only, **not** moving
between sectors, returning to a gate, pausing, or loading a checkpoint.
A owns death/restart policy; B does not delete or reset player DB records.

## Tests

```bash
python -m compileall -q neonvoid tests
python -m unittest tests.test_stages tests.test_sectors tests.test_infection tests.test_combat -v
python -m unittest discover -s tests -v
# Existing CI runs the 3600-frame gameplay smoke on Windows and Linux.
```

**File boundaries:** this PR changes `content/stages.py`,
`content/enemies.py` and adds `content/sectors.py`,
`content/virus_catalog.py`, `infection.py`, two tests and this document.
It does **not** edit `game.py`, `view.py`, `entities.py`,
`profile.py`, `settings.py`, `content/upgrades.py`, README, or CI.
