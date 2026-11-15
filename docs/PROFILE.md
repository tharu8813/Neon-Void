# Versioned player profile (P1)

The existing `save.json` stays unchanged and continues to store `best_score`, `best_wave`, and `runs`. New lifetime statistics live in a **separate** `profile.json` beside it.

## Version 1 schema

```json
{
  "schema_version": 1,
  "total_kills": 2,
  "boss_kills": 0,
  "total_score": 350,
  "completed_runs": 1,
  "abandoned_runs": 0,
  "best_combo": 2,
  "recent_runs": [
    {
      "status": "dead",
      "score": 350,
      "wave": 2,
      "kills": 2,
      "boss_kills": 0,
      "best_combo": 2,
      "duration_seconds": 47.2
    }
  ]
}
```

The profile begins tracking when the new build is first run; historical runs in the original `save.json` cannot be reconstructed. Up to 20 recent run summaries are stored (newest first). Supported run outcomes are `dead` and `abandoned`, the latter triggered by restart or closing an active session. No saves occur for automated `--selftest` sessions.

## Flow

- `ProfileTracker` subscribes to the existing single-threaded `EventBus` and gathers kills/boss kills/combo without touching combat logic.
- On `run_ended` or `run_abandoned`, it appends a summary and atomically writes the profile.
- If the JSON is missing or corrupted, the game falls back to an empty profile. The original save file is not modified by profile loading.
- The title menu now displays new lifetime totals, independently of the original high score.

## Migration strategy

Profile format is explicitly versioned. Version 1 is the only supported schema; future versions should include explicit migration functions before writing new fields. Unsupported schemas are not understood by this build and currently use defaults. Back up profiles when experimenting with future builds.

## Limits

- Progress is saved on death, manual restart or closing normally. A system crash during an active run can still lose the current run's lifetime statistics.
- This is a statistics record, not checkpoint/resume or permanent unlock progression.
- `save.json` is still the source of truth for the original high score, best wave and started-run count.
