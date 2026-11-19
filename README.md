# NEON VOID · Unity Rework

**새 프로젝트 선언 (2026-11-20):** 과거 Godot 프로젝트는 폐기되었고, 여기에는 존재하지 않습니다. `unity/`는 Unity 6.3 LTS 기반의 **신규 개발 출발점**, `python/`은 **절대 수정·삭제하지 않는 레퍼런스 게임**입니다. Unity 게임의 제작·실행·품질 검증은 아직 시작 단계입니다.

## 60초 온보딩

1. Unity Hub에서 **Unity 6.3 LTS (권장 프로젝트 에디터: `6000.3.24f1`)**와 Windows Build Support를 설치.
2. 이 저장소의 **`unity/` 폴더**를 Unity Hub에서 프로젝트로 추가해 엽니다. 저장소 루트가 아닌 `unity/`를 선택하세요.
3. 최초 패키지 임포트 완료 후 `Tools > Neon Void > Generate Prototype Scene` 선택. 생성되는 씬 `Assets/_NeonVoid/Scenes/Prototype_Bootstrap.unity`를 엽니다.
4. Play로 임시 그레이박스 이동·카메라 테스트를 확인합니다. **실제 게임 완성이나 Unity 기기 검수 완료를 뜻하지 않습니다.**
5. Claude Code를 **저장소 루트**에서 실행하고 `CLAUDE.md` → `docs/00_MASTER_GDD.md` → `docs/10_FIRST_TASK.md` 순서로 읽게 하세요.

> Unity Editor는 이 결과물을 만든 환경에서 실행할 수 없어, 에디터 컴파일·씬 생성·실기기 플레이는 미검증입니다. 첫 작업은 반드시 패키지 복원, 컴파일, EditMode/PlayMode 검증을 수행해야 합니다.

## 꼭 읽을 문서

| 문서 | 역할 |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | 클로드 코드용 작업 헌장·금지사항·완료 조건 |
| [`docs/00_MASTER_GDD.md`](docs/00_MASTER_GDD.md) | **Unity 신작의 최우선 게임 기획 명세** |
| [`docs/01_WORLD_AND_CONTENT.md`](docs/01_WORLD_AND_CONTENT.md) | 5개 하드웨어·적·보스·목표·맵 설계 |
| [`docs/02_COMBAT_AND_STATS.md`](docs/02_COMBAT_AND_STATS.md) | 60Hz 전투·능력치·연쇄·상태이상 계약 |
| [`docs/03_PLUGINS_AND_SKILLS.md`](docs/03_PLUGINS_AND_SKILLS.md) | 기존 27종+신화 초안·액티브 스킬·카드 추첨 |
| [`docs/04_PROGRESSION_AND_SAVE.md`](docs/04_PROGRESSION_AND_SAVE.md) | 복제 3회·4번째 사망·DB·영수증·저장 트랜잭션 |
| [`docs/05_UI_VISUAL_AUDIO.md`](docs/05_UI_VISUAL_AUDIO.md) | UI/UX·연출·아트/사운드 지침 |
| [`docs/06_UNITY_ARCHITECTURE.md`](docs/06_UNITY_ARCHITECTURE.md) | Unity 기술 선택·모듈/폴더 경계·패키지 |
| [`docs/07_ROADMAP_QA.md`](docs/07_ROADMAP_QA.md) | 단계별 개발과 자동/실기기 검수 |
| [`docs/08_DECISIONS_AND_CONFLICTS.md`](docs/08_DECISIONS_AND_CONFLICTS.md) | 확정·초안·미확정, 이전 기획과의 충돌 관리 |
| [`docs/09_EXTERNAL_ASSETS.md`](docs/09_EXTERNAL_ASSETS.md) | **외부 에셋·라이브러리 사용 허용 정책**, 검증·라이선스 |
| [`docs/10_FIRST_TASK.md`](docs/10_FIRST_TASK.md) | 클로드 코드가 바로 수행할 첫 개발 단위 |
| [`docs/NEON_VOID_GDD_V1.md`](docs/NEON_VOID_GDD_V1.md) | 역사적 원문 보관. **새 명세와 충돌하면 00~10 우선** |

## 핵심 규칙 요약

3D **수직 탑다운** + 60Hz 규칙 업데이트, 이동방향 Space 대시, 자동사격/마우스 조준, 별도 액티브 스킬 3칸, 5개 하드웨어(CPU·GPU·RAM·SSD·HDD) 자유 순서, 매 의뢰 Lv.1 리셋, 백신 장기 성장, 복제 가능 3회 이후 4번째 사망에서 해당 백신 영구 파기, 정화 1개 후 철수 허용, 출전 플러그인 사망 시 소실, 보관 DB는 복제 사망에 안전.

게임의 목표 분량은 **하드웨어 1개당 평균 8~12분**이며, 전체 5개 정화는 약 40~60분을 목표로 하는 **기획상 추산**입니다. 아직 플레이 테스트로 검증되지 않았습니다.

## 저장 및 안전

Python 레거시 다섯 JSON(`save.json`, `settings.json`, `profile.json`, `antivirus_db.json`, `os_checkpoint.json`)과 기존 소스는 읽기 전용 참고입니다. Unity 신규 저장은 **전혀 다른 별도 경로와 스키마**를 쓰며 자동 가져오기·덮어쓰기·삭제·초기화 금지. 사용자 명시 승인 없는 이전 데이터 변환도 금지합니다. 테스트는 오직 합성 데이터를 임시 경로에서 실행합니다.

## 개발 진행 현황

- [x] 대화 기록과 기존 GDD의 요구사항 재정리 / 충돌 사항 기록
- [x] 신규 Unity 프로젝트 파일 및 임시 C# 그레이박스 생성 코드 배치
- [x] Unity 6.3 패키지 복원·컴파일·씬 생성 실검증 (P0-01)
- [x] 결정적 60Hz 이동·대시·조준·카메라 (P0-02)
- [x] 피해·흡혈·상태이상·프록 큐·스킬 규칙과 전투 연결 (P1)
- [ ] 적 AI·스킬 실연결·보상/플러그인 (P2)
- [ ] 게임 정식 시스템·콘텐츠/전체 Windows QA 및 배포

## CI

`.github/workflows/unity-editmode-tests.yml`이 push와 PR마다 EditMode 테스트를 실행합니다.
GameCI 같은 제3자 액션 대신 **Unity 공식 CLI**를 씁니다 (`unity ci init`이 뼈대를 생성).

**최초 1회 설정이 필요합니다.** `UNITY_LICENSE_FILE_BASE64` 시크릿이 없으면 테스트 잡은
**건너뛰고 경고만 남깁니다** — 아무것도 검증하지 않은 녹색 체크보다 눈에 보이는 공백이 낫기 때문입니다.
이 프로젝트가 쓰는 Unity Personal은 시리얼이나 서비스 계정으로 CI 활성화가 **불가능**하며,
오프라인 `.ulf` 라이선스 파일만 가능합니다. 생성 절차는 워크플로 파일 상단 주석에 있습니다.

`.ulf`는 계정·머신에 묶인 **자격증명**입니다. 시크릿에만 두고 저장소에 커밋하지 않습니다.

**제3자 패키지나 에셋을 쓰는 것은 적극 허용합니다.** 단 출처·라이선스·버전·배포 권한 확인 없이 소스 저장소에 넣거나 유료 에셋을 무단 복제하지 않습니다. 자세한 기준은 `docs/09_EXTERNAL_ASSETS.md`에 있습니다.
