# CLAUDE CODE: FIRST TASK (P0-01) | Start Here

> 목적: 이 ZIP을 풀고 **바로 작업을 시작**할 수 있도록 첫 작업의 범위와 완료 기준을 구체화한다. 현재 Unity 프로젝트는 에디터 실행을 통한 실제 검증 전 상태이다. 본 문서를 읽은 Claude Code가 **P0 검증·수정부터 시작**한다.

## 작업 시작 지시

```text
너는 Neon Void Unity 6.3 LTS 신작 리워크의 통합 개발자다.
프로젝트 루트의 CLAUDE.md와 docs/00_MASTER_GDD.md,
docs/06_UNITY_ARCHITECTURE.md, docs/08_DECISIONS_AND_CONFLICTS.md,
docs/07_ROADMAP_QA.md를 먼저 읽어라.

우선 P0-01을 진행하라:
1) unity/ 의 ProjectVersion/Packages/Assets를 확인하고
   Unity 6000.3.24f1 + Input System/URP 종속성이 실제로 복원되는지 검증하라.
2) Unity Editor에서 컴파일 오류가 있으면 최소 변경으로 고쳐라.
3) Tools > Neon Void > Generate Prototype Scene 을 호출해
   그레이박스 씬을 생성하고 Play로 WASD/화살표 이동,
   Space 이동 방향 대시, 마우스 조준, 카메라 50% 룩어헤드,
   기본 자동발사 임시 타깃을 시연·검증하라.
4) 씬과 생성된 .meta 파일을 커밋 가능한 형태로 남겨라.
5) 순수 C# 고정 틱·입력·카메라 수학의 테스트 구조를 만들고
   60Hz 시뮬레이션 엔진 작업을 다음 PR로 분리하라.
6) python/ 과 구 GDD/SVG 및 모든 레거시 JSON은 절대 수정하지 말라.
7) 변경점/Unity 에디터/테스트/Windows 검증 여부를 보고하라.
8) 승인이 없는 영구 데이터 파기/의뢰 포기/DB 회수 정책은
   임시로 실제 저장에 연결하지 말라.
9) 외부 라이브러리·에셋은 사용 가능하다. 단 가격 지출은
   사용자 승인 후 진행하고 라이선스·버전·권리를
   third_party/manifest.csv에 기록하라.
```

## P0-01의 범위

- Unity Hub에서 열 수 있는 구조, Unity 6.3 LTS + URP 파이프라인 설정, 패키지 Import 성공.
- 씬 1개, 바닥·벽/적 dummy 3개, 플레이어 임시 캡슐, 마우스 지면 조준, WASD/방향키/Space 및 50% 카메라, 사격 트리거(임시).
- **샘플의 속도·기본 발사 로직은 실제 게임 룰이 아니므로 Prototype 네임스페이스 아래에 둔다.** P1에서 정식 60Hz Combat/Player/AI 데이터 기반으로 교체.
- 실제 Unity Editor의 컴파일/Play 증거, 에셋 메타 포함, 테스트 출발점.
- `00_MASTER_GDD.md`의 확정/미확정이 섞이지 않는 방식(커서 대시 금지, Godot 이식물 도입 금지).

## 완료 체크리스트

- [ ] `unity/Packages/manifest.json` 파싱·UPM 복원 성공, Console compile error 없음.
- [ ] Unity Editor에서 `Tools/Neon Void/Generate Prototype Scene` 메뉴 정상.
- [ ] 임시 씬 열기/Play 시 화면과 캐릭터가 나타남, 이동/카메라·조준/Space 대시 가능.
- [ ] 씬/프리팹/머티리얼을 재오픈해도 참조 GUID 끊김 없음.
- [ ] 카메라/조준 1280×720 / 1920×1080 / 울트라와이드에서 플레이어 화면 이탈·커서 조준 오차 회귀 확인.
- [ ] 정식 전투 60Hz 구현 여부는 **P1 별도**로 구분, 프로토타입을 완성으로 쓰지 않음.
- [ ] `python/`과 보관 GDD에 변경 없음 (`git diff --name-only` / 이전 파일 checksum).
- [ ] EditMode 자동테스트 실행 결과/Windows 수동검증 결과에 실제 증거/미검증 표기.
- [ ] 외부 에셋이 있다면 출처/권리/버전/사용 위치 기록.

## 곧바로 이어질 두 번째 작업 (P0-02)

**핵심**: `FixedTick 1/60`/마우스 조준/입력 분리, 대시 방향 고정/카메라 안정성. `docs/02_COMBAT_AND_STATS.md` 요구를 토대로 임시 Controller를 정식 도메인 모델로 대체. Input System action asset/리바인딩 구조를 만들고 Unity `Update`/`FixedUpdate` 차이에서 입력 누락 없게 한다.

**끝나고 이어질 작업**: P1 순수 C# Damage/Status/Proc 테스트 → P2 영수증/저장 기반 → P3 CPU 전후방 전체 루프. 'CPU만 돌아간다'를 5하드웨어 완료로 오해하지 않는다.

## 처음부터 막아야 할 실패 패턴

- Unity에서 정상 compile하지 않은 코드를 '구현 완료'로 보고.
- 멋진 메뉴·그림만 만들고 정작 3D에서 게임 플레이가 안 됨.
- 이름만 5개 하드웨어로 바꾸고 동일 적·보스·맵을 복사.
- `software`·Python/Godot의 낡은 세이브 경로를 '편리하니까' 그대로 사용.
- 사용자가 승인하지 않은 DB 회수·클론 복구·영구 파기 규칙을 임의로 덮어쓰기.
- 모든 것이 `GameManager.cs` 하나에 들어가서 저장/전투/렌더가 서로 직접 변경.
- 외부 유료 에셋/상용 폰트를 구매하지 않고 가져와 프로젝트에 포함.
