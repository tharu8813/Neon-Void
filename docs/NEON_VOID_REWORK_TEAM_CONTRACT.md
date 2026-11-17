# Neon Void Rework v1.0 — 5채팅 병렬 개발 계약

> **상태:** A 총괄이 정의한 초기 통합/파일 소유권 계약. 아직 실제 코드 인터페이스가 존재한다고 주장하지 않는다.  
> **기준:** [GDD v1.0](NEON_VOID_GDD_V1.md) · [총괄 EPIC #95](https://github.com/tharu8813/Neon-Void/issues/95) · 현행 main의 Godot 4.7.2 프로젝트  
> **작성:** 2026-11-17 KST  
> **중요:** 기존 4섹터 Godot 이식 프로젝트는 작동하는 기준선으로 보존한다. 이 문서는 5하드웨어 신규 설계를 위한 별도 개발 계약이다.

## 1. 역할 및 경계

| 담당 | 개발 이슈 | 책임 | 새 모듈 소유 경로 |
|---|---|---|---|
| A 총괄 | [#95](https://github.com/tharu8813/Neon-Void/issues/95) | 인터페이스 확정, 통합/런타임 배선, Control UI, CI, QA, 병합 | `scripts/rework/integration/**`, `scripts/rework/ui/**`, `scenes/rework/ui/**`, `tests/rework/integration/**`, 공통 docs; *기존* `project.godot`, `scenes/main.tscn`, `.github/workflows/**` 수정 권한 |
| B 전투 | [#96](https://github.com/tharu8813/Neon-Void/issues/96) | 능력치/피해/버프 디버프/스킬/마나/전투 발동 | `scripts/rework/combat/**`, `scripts/rework/skills/**`, `tests/rework/combat/**`, `tests/rework/skills/**` |
| C 플러그인 | [#97](https://github.com/tharu8813/Neon-Void/issues/97) | 카탈로그/레벨별 효과/추첨/행운/천장/단일 카드 리롤/발동 권한 | `scripts/rework/plugins/**`, `data/rework/plugins/**`, `tests/rework/plugins/**` |
| D 월드 | [#98](https://github.com/tharu8813/Neon-Void/issues/98) | 허브/메인보드/5하드웨어/절차적 맵/구역별 정화·철수/룩어헤드 | `scripts/rework/world/**`, `scripts/rework/camera/**`, `scenes/rework/world/**`, `tests/rework/world/**`, `tests/rework/camera/**` |
| E 저장 | [#99](https://github.com/tharu8813/Neon-Void/issues/99) | 백신/의뢰 성장, 스킬 보존, 데이터베이스, 출전·학습, 복제·파기, 트랜잭션 저장 | `scripts/rework/progression/**`, `scripts/rework/persistence/**`, `tests/rework/progression/**`, `tests/rework/persistence/**` |

- 다른 담당자의 소유 경로에 **사전 합의 없는 직접 수정 금지**. 공동 수정을 요구하는 API가 발견되면 이슈에 제안 → A 승인 → A가 통합 PR에서 연결.
- 새 모듈은 **기존 루트를 즉시 덮어쓰지 않는 격리된 `rework/` 네임스페이스**에서 시작. 다른 팀의 통합 완료 여부와 관계없이 테스트 가능해야 함.
- 기존 `python/**` 전체, 원본 JSON 파일, 기존 Godot 세이브 및 신뢰된 백업은 **수정 불가**. 새 스키마/새 저장 슬롯으로 격리하고 원본은 읽기 전용 + 사용자 동의 없는 자동 마이그레이션 금지.
- 기존 이식 EPIC #52의 4섹터 동일성은 **레거시 호환 목표**이고 새 GDD는 **명시적 게임 디자인 변경**이다. 둘을 단일 수치/구현으로 혼동하면 안 된다.

## 2. GDD 의사결정 위계

1. 사용자 승인된 **[확정]**이 가장 우선.
2. 아직 승인되지 않은 수치/구체 공식은 **[초안 v0.1]**으로 데이터/설정화하여 조정 가능하도록 만든다.
3. **[미확정]**은 임의로 확정하지 않는다. 필요한 경우 임시 기본값/대체 방안을 이슈에 제안하고 A가 사용자에게 질문할 수 있도록 한다.
4. 구현 전 잠재적 충돌(예: 초당 발사속도 vs 전투 60Hz, 추가 연쇄와 무한 루프, 학습한 플러그인 출전·귀환)이 발견되면 계약/테스트로 해결한다.
5. 미리보기 설명이나 VFX는 게임플레이의 실제 판정과 일치해야 한다.

## 3. 팀 간 메시지·인터페이스 초안 (확정 API 아님)

아래는 이름의 제안이며 함수의 최종 서명/클래스 위치는 첫 PR의 리뷰에서 결정한다. 각 담당이 **읽기 전용 스냅샷과 명시적 명령·이벤트**로 통신하는 것이 원칙이다.

| 발행 | 소비 | 요청/이벤트 | 최소 데이터 제안 |
|---|---|---|---|
| D World | B Combat | 전투 시작/완료, 구역·난이도·환경 정보 | `run_id, region_id, encounter_id, seed, difficulty, environment` |
| B Combat | C Plugin | 적중/치명타/스킬 사용/처치 이벤트 | `event_id, actor_id, target_id, damage_type, source_tags, proc_path, fixed_tick` |
| C Plugin | B Combat | 추가 공격/버프/효과 실행 요청 | `plugin_id, level, trigger_id, effect_payload, proc_permissions` |
| D World | E Progression | 목표/정화/철수/사망 사실 이벤트 | `run_id, region_id, objective_id, receipt_id, outcome` |
| C Plugin | E Progression | 플러그인 보상 선택 및 귀환 학습 요청 | `offer_id, plugin_id, rarity, level, learnable` |
| E Progression | A UI | 백신·의뢰 레벨, 플러그인 보관, 복제 수, 능력치·미정산 보상 스냅샷 | read-only versioned DTO |
| D Camera | A UI/Player input | 카메라 및 커서 월드 위치 계산 결과 | view/input coordinates, world XZ, smoothing/bounds info |

**강제 조건**
- 모든 난수는 재현 가능하게 시드·난수 스트림을 분리한다. UI/VFX 난수로 게임플레이 결과가 바뀌면 안 된다.
- 단위에 `world units/sec`, `logical px`, `ticks`, `percentage points`, `XP` 등 구체적으로 명시. 기존 1280×720 논리 화면, 60Hz 고정 틱 기준 검증.
- 처리 결과/보상 기록에는 **안정적인 ID + 영수증 ID**를 넣어 중복 호출/저장 재시도 시 중복 지급하지 않는다.
- 효과 발동은 무한 재귀 방지 + 다단계 정상 연쇄 허용. 광역·지속 피해 흡혈은 각각 효율 33%, 겹쳐도 33%.
- 변경이 필요한 타 팀 API가 생기면 먼저 이슈 링크·새 인터페이스·호환/테스트 영향을 협의한다.

## 4. 단계별 통합 순서

1. **P0 (모두 병렬)**: 현재 main 분석 + 새 파일 소유권 준수 + 데이터 구조/API 제안 + 독립 테스트/초기 드래프트 PR. A는 차기 실행 모드의 격리 방식/CI 설계.
2. **P1 (병렬 완료 뒤)**: B 전투 규칙 ↔ C 발동/플러그인·추첨, D 맵/정화 ↔ E 의뢰 보상/저장이라는 두 세로 축을 어댑터로 연결. **주요 의존 계약 확정 후** 순차 병합.
3. **P2 (통합 단일 vertical slice)**: 허브 → CPU 1개 → 전투·레벨업 플러그인 → 정화·철수 → 데이터베이스 보관 → 재실행 복구. 저장/수명 불변식 먼저 확인.
4. **P3**: GPU/RAM/SSD/HDD, 환경 전체, 스킬 풀/플러그인 콘텐츠, 정화 보너스, 3회 복제·4회 파기 end-to-end.
5. **P4**: 룩어헤드/커서·HUD·연출·사운드·접근성·성능/밸런스. Windows 실기기 QA 및 최종 배포.

**PR 정책**: 독립된 기능마다 작은 Draft PR, 본문에 `Closes #... (완료 시만)` 또는 `Refs #...` 사용. 요구 CI의 **최신 HEAD 성공**, 병합 가능, 교차 경로 충돌/저장 위험 없음을 A가 확인한 뒤 squash 병합. 미완성 PR이 다른 팀 작업을 막지 않도록 개인 브랜치 작업. CI가 대기·실패·진행 중인 상태에서 병합 금지. Windows의 사운드·GPU·DPI·게임 감각 검증은 별도이며 headless CI로 대체하지 않는다.

## 5. 초기 완료 기준 (담당별 1차 PR)

- **B:** 피해 계산 + 상태이상/흡혈/스킬 구조의 순수 함수/시뮬레이션과 결정적 테스트, 이벤트 스키마.
- **C:** 사용자 승인 3슬롯/단일카드 리롤/레벨 상한/천장 화면 카운트의 결정적 추첨 모듈과 플러그인 메타데이터, B 연계 문서.
- **D:** 다섯 하드웨어 모델 + 최소 쉬움 1개 생성 + 절차적 맵 시드/출구 상태 머신 + 50% 카메라 계산 테스트.
- **E:** 두 레벨 성장 + 데이터베이스 + 3클론/4번째 파기 + 20~70% 미정산 보상 + 버전/영수증 저장 경계 및 장애 테스트.
- **A:** 소유권/API 충돌 예방, PR/CI 정책 검증, 통합용 최소 안전 러너 기획 및 UI/경계 인수 검사.

## 6. 각 작업의 보고 양식

- 담당 / 이슈 / 브랜치 / 최신 커밋 SHA / PR URL
- 변경 파일(소유권 위반 여부)
- 현재 완료 기능(실제 검증된 범위만) / 추가 API / 데이터 버전 영향
- 테스트 명령·결과 / Windows-Linux CI 최신 HEAD 상태
- 실제 Windows에서 검증해야 할 항목(미완료 시 미완료로 표기)
- 다른 담당 의존성·병합 차단 원인·다음 작업

**이 문서에 서술한 것은 팀 협업 계약이지, 구현과 QA 완료의 증거가 아니다.**
