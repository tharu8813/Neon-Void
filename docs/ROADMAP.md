# NEON VOID growth roadmap

## P0 | Project foundation (this PR)

- [x] Preserve gameplay and legacy launch commands.
- [x] Extract audio, rendering, entities, content, persistence and CLI modules.
- [x] Add minimal event bus and lifecycle hooks.
- [x] Preserve save.json schema/path and write it atomically.
- [x] Add unit tests, CI matrix and gameplay smoke tests.
- [ ] Confirm all CI and real Windows playtesting before merge.

## P1 | Product-ready settings and data

- [x] Settings and key remapping for movement/dash/pause; DPI/scaling and broader accessibility remain.
- [x] Versioned profile and validated atomic files; version-to-version migrations remain.
- [x] Stage catalog, validation and deterministic wave director; balancing presets remain.
- [x] Recent run history and lifetime stats; achievements/permanent unlocks remain.
- [ ] Baseline performance and screenshot/regression tests.

## P2 | Expanded gameplay

- [ ] Multiple playable stages/arenas, hazards and encounter progression (catalog is ready).
- [ ] More enemy archetypes, bosses and attack-pattern catalogs.
- [ ] Weapon/build variety, meta-progression and difficulty modes.
- [ ] UI/HUD refinements and polished sound with quality presets.

## P3 | Release readiness

- [ ] Resolution independence and input/controller abstraction.
- [ ] Assess a future graphical backend based on profiling, not fashion.
- [ ] Windows installer, error logs, signed release pipeline.
- [ ] Localization and accessibility reviews.

Do not implement all future phases in one PR. This phase establishes guardrails for safe incremental development.
