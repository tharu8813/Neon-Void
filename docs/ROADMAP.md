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

- [x] A1: 인월드 백신 허브, 이동형 OS 배포 및 CPU 출입 게이트, 독립 웨이브 체크포인트, 월드 카메라·충돌 (PR 최종 검증 후 확인)
- [x] A2: B #11의 네 구역 콘텐츠 및 감염·정화 API 연결 (#17)
- [x] A3: C #12의 백신 DB/XP/영구 소프트웨어, OS CLEAN → 철수 → 새 OS 배포 통합 (PR 검증 완료 후)
- [ ] 백신 시스템 세계관 Epic #10, 통합 담당 #13의 전체 완료 조건 검증


- [ ] Multiple playable stages/arenas, hazards and encounter progression (catalog is ready).
- [ ] More enemy archetypes, bosses and attack-pattern catalogs.
- [ ] Weapon/build variety, meta-progression and difficulty modes.
- [ ] UI/HUD refinements and polished sound with quality presets.

## P3 | Release readiness

- [ ] Resolution independence and input/controller abstraction.
- [ ] Assess a future graphical backend based on profiling, not fashion.
- [x] Tested standalone Windows EXE build workflow and Python traceback log routing (upon CI success); installer, signing and release pipeline remain.
- [ ] Localization and accessibility reviews.

Do not implement all future phases in one PR. This phase establishes guardrails for safe incremental development.
