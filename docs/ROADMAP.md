# NEON VOID growth roadmap

## P0 | Project foundation (this PR)

- [x] Preserve gameplay and legacy launch commands.
- [x] Extract audio, rendering, entities, content, persistence and CLI modules.
- [x] Add minimal event bus and lifecycle hooks.
- [x] Preserve save.json schema/path and write it atomically.
- [x] Add unit tests, CI matrix and gameplay smoke tests.
- [ ] Confirm all CI and real Windows playtesting before merge.

## P1 | Product-ready settings and data

- [ ] Settings menu: sound, key binding, DPI/scaling, accessibility.
- [ ] Versioned profile, migrations, better save error handling.
- [ ] Content validation/data-driven definitions and balancing presets.
- [ ] Run history, achievements and permanent unlock structures.
- [ ] Baseline performance and screenshot/regression tests.

## P2 | Expanded gameplay

- [ ] Multiple stages/arenas, hazards and encounter progression.
- [ ] More enemy archetypes, bosses and attack-pattern catalogs.
- [ ] Weapon/build variety, meta-progression and difficulty modes.
- [ ] UI/HUD refinements and polished sound with quality presets.

## P3 | Release readiness

- [ ] Resolution independence and input/controller abstraction.
- [ ] Assess a future graphical backend based on profiling, not fashion.
- [ ] Windows installer, error logs, signed release pipeline.
- [ ] Localization and accessibility reviews.

Do not implement all future phases in one PR. This phase establishes guardrails for safe incremental development.
