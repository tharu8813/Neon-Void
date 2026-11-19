# Claude Code instructions · Neon Void Unity Rework

## Mission
Unity 6.3 LTS 기반의 신규 3D 탑다운 싱글플레이 로그라이트 게임 Neon Void를 개발한다. **과거 Godot 프로젝트는 없다.** Python은 참조 구현일 뿐이다. 아래의 문서를 읽고 작업을 시작한다.

1. `docs/00_MASTER_GDD.md` (우선 기획), `docs/08_DECISIONS_AND_CONFLICTS.md` (미결정·충돌)
2. 해당 작업에 관련된 전문 문서 (`01`~`06`)
3. `docs/07_ROADMAP_QA.md`와 `docs/10_FIRST_TASK.md`
4. 근거 확인이 필요할 때만 역사적 `docs/NEON_VOID_GDD_V1.md`와 `python/`을 **읽기 전용** 조사한다.

## Non-negotiable rules

- Unity 프로젝트: `unity/`. Godot `.godot`, `.tscn`, `.gd`, 이전 Godot 테스트·설계 문서 생성 금지.
- **`python/` 및 기존 `docs/NEON_VOID_GDD_V1.md`·SVG 원본 수정/삭제 금지.** 개인 레거시 JSON 파일은 절대 변경하거나 자동 마이그레이션하지 않는다.
- 문서 명세의 `[확정]`은 구현 불변조건. `[초안]`은 기획자 미승인 튜닝 변수. `[미확정]`은 설계·검증 가설은 제시할 수 있지만 확정값으로 간주하거나 위험한 영구 저장 동작을 만들어서는 안 된다.
- 전투 시스템은 60Hz 논리 틱 기준. 결정적 RNG 스트림, 물리 판정과 렌더/VFX 분리. FPS가 전투 결과를 바꾸면 실패.
- 플러그인 등급은 일반/레어/전설/신화. 원안 27종은 일반17+레어7+전설3이며 **신화는 별도 신규 콘텐츠 (미확정)**. '소프트웨어'는 UI 표시명으로 쓰지 않는다.
- 사용자 데이터 정책: 기존 Python 5개 JSON/레거시 경로 **읽기·수정·삭제 금지**, 테스트 시 synthetic fixture·임시 저장 디렉터리만 사용. 새 Unity 저장도 별도 경로에서 영수증·백업·체크섬·원자적 변경.
- 보상/사망/철수/DB 이동은 **저장 커밋 승인 후** UI 및 게임 상태 확정. 실패·ACK 유실·중복 시 재지급 금지.
- 환경/구역/스킬/밸런스는 ScriptableObject 또는 검증 가능한 데이터로 관리. 캐릭터와 UI의 하드코딩된 숫자 금지.
- 5개 하드웨어를 한 개의 복붙 맵으로 취급하지 말 것. 서로 다른 목표·적·보스·환경 상호작용·플레이 리듬이 필요.
- 어떤 라이브러리·에셋·툴도 사용 가능. **필요성/라이선스/버전/성능/리포지토리 재배포 권한**을 조사하고 `third_party/manifest.csv`에 작성한 뒤 적용. 유료 에셋은 임의 구매 금지. 미검증 도구는 `평가 중`으로 둔다.
- 외부 상용 게임의 코드·모델·오디오/아이콘을 무단 복제하지 않는다. 사용 시 권리 확인.
- 사전 승인 없는 GitHub main 직접 병합 금지. 브랜치/PR 작업, CI 및 실제 기능 검증 보고 후 병합.
- '에디터 테스트 통과', '윈도우 실플레이 완료', '시스템 완성'은 **실제로 테스트했을 때만** 기록. 미실행·검증 불가는 그 이유와 함께 남긴다.

## Code / scene ownership

- `unity/Assets/_NeonVoid/Runtime/{Core,Combat,Plugins,Progression,Missions,Player,UI,Presentation}`를 도메인 경계로 사용한다.
- `unity/Assets/_NeonVoid/Editor/`는 씬/데이터 생성 등 편집기 한정 코드이다. 런타임 assemblies에 `UnityEditor` 참조 금지.
- `unity/Assets/_NeonVoid/Tests/`에는 EditMode와 PlayMode 테스트. 핵심 알고리즘은 가능하면 일반 C# 순수 함수로 작성한다.
- 각 프로젝트 자산에는 Unity `.meta`를 생성·버전 관리한다. `Library`, `Temp`, `Obj`, `Logs`, `Builds`, `.vs`, IDE 캐시는 커밋 금지.
- 새 외부 의존성 추가 전 기본 Unity 엔진·공식 패키지로 해결 가능한지 확인. 새 패키지가 꼭 필요하다면 핀/대체/라이선스 결정 기록.

## Procedure per task

1. 현재 작업 단계와 승인/미확정 결정을 확인하고 범위 밖은 변경하지 않는다.
2. 구현 전 검증 가능한 완료 조건, 영향 파일, 데이터/저장/성능 위험을 기록한다.
3. 작은 증분으로 구현하고, 테스트를 추가하고, Unity Editor 컴파일·EditMode·PlayMode·Windows 테스트 가능 여부를 따로 보고한다.
4. 완료 보고: 변경 파일, 의도, 실제 실행 명령, 성공/실패/미실행, 남은 위험, 다음 단계, 스크린샷/로그 경로.
5. 미확정 정책은 결정을 기다릴 수 있는 인터페이스로 분리한다. 임시 모의 구현은 `Prototype` 표기를 붙이고 상용 동작으로 혼동하지 않는다.

## First job
`docs/10_FIRST_TASK.md`의 **P0 프로젝트 부트스트랩·컴파일·입력·카메라 증명**을 먼저 끝낸다. 지금 포함된 Editor-generated 프로토타입 코드는 실제 Unity 실행 검증 전까지 시범 자료이다. 이후에 저장·보상·게임 루프 순서로 진행한다.
