# NEON VOID architecture

The first extraction is **behavior-preserving**, not an engine rewrite. Python 3.11+ and tkinter remain the only runtime requirements.

## Module ownership

| Module | Ownership |
| --- | --- |
| `neon_void.py` / `neonvoid/cli.py` | Legacy entrypoint and CLI |
| `neonvoid/constants.py` | Timing, palette, viewport, legacy save path |
| `neonvoid/audio.py` | Procedural PCM, winmm/winsound fallback |
| `neonvoid/storage.py` | Legacy JSON reader, atomic writer |
| `neonvoid/entities.py` | Mutable Player/Enemy/Bullet/etc state |
| `neonvoid/content/` | Enemy and upgrade definitions |
| `neonvoid/render_pool.py` | Pooled Canvas primitives |
| `neonvoid/view.py` | VisualMixin: HUD, screens and drawing |
| `neonvoid/game.py` | Simulation coordinator, input and fixed-step loop |
| `neonvoid/events.py` | GameEvent/EventBus, optional hooks |
| `neonvoid/profile.py` | Versioned lifetime records and event-driven run summaries |

## Dependencies

```text
launcher -> cli -> Game(VisualMixin)
                  +-- entities, content, audio, storage
                  +-- render_pool, events, constants
                  +-- VisualMixin -> constants
```

## Extension strategy

1. New enemies go in `content/enemies.py`; AI behavior is still in `game.py` until a separate system extraction.
2. New upgrades belong in `content/upgrades.py` and require tests for identifiers/caps/effects.
3. Subscribe to `game.events` for optional achievements, run stats and telemetry. Emitted event names are `run_started`, `wave_started`, `enemy_killed`, `upgrade_picked`, `run_ended`. All callbacks execute synchronously in the game thread. Exceptions are logged/isolated.
4. Keep backward compatibility for `save.json` (currently stored alongside the launcher, fields: `best_score`, `best_wave`, `runs`). Do not silently change the location.
5. Preserve the 60 Hz fixed-step loop, sound fallback and command-line selftest when changing systems.

## Honest limitations

- VisualMixin uses `Game` state directly. It is **not yet an independent renderer**. Separate immutable snapshots/renderer interfaces should follow baseline GUI regression tests.
- Simulation, combat, AI and input still share `game.py` for now. Do not prematurely split these without deterministic tests.
- Tkinter 1280x720 fixed resolution is unchanged. Screen scaling and graphical backend options remain future work.
- The smoke test opens a Tk window (hidden for test runs); Linux needs Xvfb.
