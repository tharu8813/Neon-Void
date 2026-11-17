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
- [x] Canvas 프레임 측정/객체 수·RSS 자동 계측 및 GUI 반복 리사이즈 검증 (PR #26).
- [ ] 픽셀 수준 스크린샷 회귀 테스트/실기기 고부하 성능 목표 (별도 후속 작업).

## P2 | Expanded gameplay

- [x] A1: 인월드 백신 허브, 이동형 OS 배포 및 CPU 출입 게이트, 독립 웨이브 체크포인트, 월드 카메라·충돌 (PR 최종 검증 후 확인)
- [x] A2: B #11의 네 구역 콘텐츠 및 감염·정화 API 연결 (#17)
- [x] A3: C #12의 백신 DB/XP/영구 소프트웨어, OS CLEAN → 철수 → 새 OS 배포 통합 (PR 검증 완료 후)
- [x] Epic #10 / A 통합 #13 구현 및 후속 검증 완료 (2026-11-16)
- [x] #22 OS 캠페인 체크포인트/크래시 복구와 XP 영수증 중복 방지 (PR CI 통과 후 확정)
- [x] #23 논리 캔버스·창 크기 변경·전체화면/보더리스·프로세스 RSS·렌더 p95 자동검증 (PR #26; CI 병합 후 확정)
- [ ] Windows 실제 100/125/150/200% DPI 화질·입력 정확성 수동 확인 (#24)
- [ ] #24 Windows 실기기 전체 게임 루프 확인


- [ ] Multiple playable stages/arenas, hazards and encounter progression (catalog is ready).
- [ ] More enemy archetypes, bosses and attack-pattern catalogs.
- [ ] Weapon/build variety, meta-progression and difficulty modes.
- [ ] UI/HUD refinements and polished sound with quality presets.

## P3 | Release readiness

- [x] 기본 해상도 독립적 Canvas 비율 대응 및 마우스 좌표 역변환 (PR #26).
- [ ] 게임패드·컨트롤러 추상화 및 실제 Windows DPI 100~200% 수동 검증 (#24).
- [ ] Assess a future graphical backend based on profiling, not fashion.
- [x] Tested standalone Windows EXE build workflow and Python traceback log routing (upon CI success); installer, signing and release pipeline remain.
- [ ] Localization and accessibility reviews.

Do not implement all future phases in one PR. This phase establishes guardrails for safe incremental development.
