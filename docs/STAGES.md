# Stage catalog and deterministic wave director

As of this milestone, there is only one **playable** stage, `void`, the original NEON VOID arena. This refactor establishes content hooks for future stages but **does not** add playable levels, scenery, hazards, or new boss types yet.

## How it works

`neonvoid/content/stages.py` defines an immutable `StageDefinition` catalog. A stage currently specifies:

- a stable ID and display name
- boss-wave interval, boss type and supported minion pool
- spawn budget baseline and per-wave increase
- wave thresholds unlocking enemy archetypes

`plan_wave(wave, rng, stage)` returns a frozen `WavePlan` with scheduled `(seconds, enemy_id)` entries, banner text and sound cues. `Game.start_wave` applies that plan, not individual spawn rules. All active wave transitions still use the 60 Hz simulation and the original Canvas renderer.

The default `void` stage reproduces the original pre-refactor budget, spawn times, boss cadence and **exact RNG sequence**. Unit tests compare 30 waves for four seeds to a reference implementation.

## Registering a stage

```python
from neonvoid.content.stages import StageDefinition, STAGES, validate_stage

stage = StageDefinition(
    id="example",
    name="EXAMPLE ARENA",
    boss_interval=4,
    base_budget=10.0,
    budget_per_wave=4.5,
)
validate_stage(stage)
STAGES[stage.id] = stage
```

Registering an ID is not yet adding stage-specific graphics or AI. The new `--stage` CLI option accepts registered IDs and currently defaults to `void`. User-facing stage selection, asset loading, encounter mechanics and new bosses remain future tasks.

## Validation

```powershell
python -m unittest discover -s tests -v
python -m neonvoid --stage void --selftest 3600 --no-sound --seed 42 --god
```

For each new stage, add tests for spawn costs/unlocks, initial wave accessibility, boss timing and fixed-seed reproducibility. Existing enemies and upgrades keep their own data modules, and new AI behaviors must still be explicitly implemented and tested in the game layer.
