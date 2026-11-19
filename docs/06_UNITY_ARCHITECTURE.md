# Unity Technical Architecture & Integration Rules | v2.0

> 이 폴더는 Unity Editor를 실제로 돌릴 수 없는 환경에서 생성한 **개발용 프로젝트 시작점**이다. Unity Editor가 패키지를 가져오고 씬·`.meta`를 만드는 작업은 Claude Code/사용자가 실제 Unity에서 수행해야 한다. 처음부터 새 구조로 만들며 폐기된 Godot 폴더를 재생성하지 않는다.

## 1. 엔진/기술 선택 (2026-11-20 기준)

| 역할 | 권장 선택 | 근거/조건 |
|---|---|---|
| Unity Editor | **Unity 6.3 LTS**; 프로젝트 초기 메타 `6000.3.24f1` | 공식 LTS 지원(2027-12까지). 같은 패치로 팀 테스트 권장 |
| 언어/게임 로직 | C# / Unity MonoBehaviour 경계 + 테스트 가능한 순수 C# 도메인 | 전투/저장 로직이 씬/렌더에 덜 묶임 |
| 렌더러 | **URP 3D** | 탑다운 네온, PC 중급 GPU 대상 유연성. HDRP는 기술/성능 검증 전 도입 안 함 |
| 입력 | **New Input System** | 리바인딩/키보드·마우스, 액션 기반 입력 |
| UI | **UI Toolkit** 우선(메뉴·카드·DB), 전투 HUD는 실제 검증 후 UI Toolkit/uGUI 중 선택 | 게임플레이 데이터 바인딩·초점, 디자이너/코드 분리 |
| 카메라 | 커스텀 50% aim-lookahead 구현; **Cinemachine 선택적** | 카메라 피드백 루프/경계 검증을 자동 카메라보다 우선 |
| 맵/블록아웃 | Unity primitives → 필요시 ProBuilder/모듈형 프리팹 | 우선 게임성 검증, 아트 종속성 낮춤 |
| 데이터 | ScriptableObject 정의 + 런타임 인스턴스 분리 | 등급·스킬·보스·환경·난이도 데이터 버전화 |
| 저장 | 전용 serializer + 원자적 저장 계층/영수증 | JsonUtility만으로 모든 복합 스냅샷/보안 요구 충족하리라 가정 금지 |
| 테스트 | Unity Test Framework (EditMode + PlayMode) / 수동 Windows QA | CI 자동 통과 ≠ 실제 Windows 플레이 검증 |
| VFX | URP Shader Graph / ParticleSystem, 필요시 VFX Graph | 판정과 VFX 분리, 성능 증명 후 확장 |
| 성능 | Unity Profiler, Frame Debugger, Memory Profiler(선택) | CPU/GPU 프레임타임·메모리·GC 추적 |
| 빌드/배포 | 우선 Windows x64 | 타 플랫폼/스토어/프로필/클라우드 정책은 별도 결정 |

참고 공식 문서: [Unity LTS](https://unity.com/releases/unity-6/support), [Input System](https://docs.unity.com/en-us/engine/6000.3/manual/input), [URP](https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline), [UI Toolkit](https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/uielements), [Test Framework](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/test-framework-introduction).

## 2. 권장 구조 (기술 경계)

```text
Neon-Void-Unity-Rework/
  README.md  CLAUDE.md  .gitignore
  docs/                 # 신규 GDD v2 + 원본 보관 문서/SVG
  python/               # 변경금지 예시 게임+tests
  unity/                # **Unity Hub에서 이 폴더를 프로젝트로 연다**
    Assets/_NeonVoid/
      Runtime/
        Core/            # tick, game state, RNG, typed events, validation
        Player/          # movement, dash, aim, camera adapter
        Combat/          # damage, stats, status, hit, AI, proc event queue
        Plugins/         # catalog, runtime effects, rarity/reward rolls
        Missions/        # mainboard, procedural generation, area states, objectives
        Progression/     # vaccine/run levels, clone, DB, save gateway
        UI/              # input focus, views, presentation binding
        Presentation/    # visuals, audio, pooling, world renderer
      Content/           # future SO assets, prefabs, audio, shared materials
      Scenes/            # generated Prototype_Bootstrap and later Core scenes
      Editor/            # scene/bootstrap + content validators, build menu
      Tests/EditMode/    # deterministic rules / validation
      Tests/PlayMode/    # scene integration
    Packages/manifest.json
    ProjectSettings/ProjectVersion.txt
  third_party/           # registry + permitted licenses; NOT unauthorized assets
  tools/                 # optional build scripts, fixture generation
```

**금지:** `Runtime`에서 `UnityEditor` 참조, UI가 전투 체력 수정, 렌더러가 RNG/보상을 소유, 에셋/프리팹에 고정 밸런스 텍스트 난립, 외부 플러그인 에디터 전역 수정 후 문서화 누락, 실제 레거시 사용자 데이터를 테스트에 사용.

## 3. 입력·카메라·캐릭터 기술 계약

- **최종 권장:** `InputActions` 자산과 `PlayerInput`, JSON 리바인드 저장(전역 설정) / 키 충돌 검사. 입력 이름: `Move, Aim, Dash, Skill1, Skill2, Skill3, Interact, Inspect, Menu, Confirm, Cancel`.
- 실시간 이동은 XZ, 마우스 조준은 카메라 viewport에서 월드 plane 혹은 world Raycast로 변환. 50% 룩어헤드+상한 및 map bounds를 수학적으로 테스트.
- 움직임/대시 시뮬레이션은 60Hz 논리 틱을 우선하며 Unity `CharacterController`/Physics collider 동작을 보정. **그레이박스 예시 코드의 Update 이동은 임시 데모로 판정하지 않는다.**
- Python 레거시 이동 322px/s, 대시 940px/s·0.145초, 무적 0.32초, 허브 2400×1560은 과거 2D 참고 데이터다. 특히 '대시 지속보다 무적 시간이 긴' 사례가 있어 Unity 확정 수치로 자동 채택 금지. 체감 비교용 A/B 영상·측정 추천.

## 4. 시뮬레이션/네트워크/랜덤

- 오프라인 싱글플레이로 시작. 멀티플레이용 서버·Netcode는 초기에 도입하지 않는다(사용자가 요청한 확정 목표 아님).
- `SimulationClock(60Hz)`와 `RenderClock`을 분리. 모든 피해/상태/플러그인 발동은 일관된 고정 순서. PC 물리 엔진 내부 floating-point 순서를 넘어선 **완전 deterministic**은 추가 입증해야 함.
- RNG 세 분류: `(i) 임무/난이도/맵`, `(ii) 전투 판정/플러그인 발동`, `(iii) 보상 카드/천장`. **VFX 임의수는 별도**. 시드/버전/상태를 저장하고 재현할 수 있어야 함.
- 이벤트 버스는 `AttackIntent → HitResolved → ProcQueue → PresentationEvent`. 발동 이벤트가 또 다른 발동을 허용하면 규칙대로 이어가고, 순환만 차단.

## 5. Unity 라이브러리/에셋 도입 원칙

**허용:** Unity Asset Store 무료/유료(구매 전 사용자 승인), UPM 패키지, GitHub 오픈소스, Kenney/Poly Haven/ambientCG 등 조건 확인된 CC0 콘텐츠, FMOD/Wwise/기타 미들웨어, AI가 직접 만들어 사용 권리 확보된 오리지널 에셋. 필요한 도구를 사용하되 최초 P0에는 **의존성 최소화**.

**라이선스가 불명확하거나 구매하지 않은** 에셋을 다운로드·프로젝트에 무단 번들·GitHub 공개 업로드 금지. 유료 제품/시트별·팀원별 라이선스, Standard/Restricted Asset, GPL·AGPL 의무, 시각·오디오 자산의 재판매·재배포 제한 점검. `third_party/manifest.csv`에 실제 사용 자산의 버전·출처·license·재배포 여부 저장.

선택 후보: `com.unity.inputsystem 1.20.1`, `com.unity.render-pipelines.universal 17.3.0`(6000.3 호환성 최종 Editor 검증), `com.unity.cinemachine 3.1.7`(선택). 정확한 버전/종속성은 **Unity Package Manager의 설치 성공 결과**를 우선하고 이상 발생 시 잠금과 문서를 일치시킨다.

## 6. 에셋/코드 파일의 생성·배포 처리

- Unity는 `.meta` 파일과 GUID가 참조 관계의 근본이다. 에셋 복사 시 `.meta`가 사라지면 씬/프리팹 참조가 깨진다. Unity Editor에서 생성·재생성되는 GUID를 버전 관리.
- `Library`, `Temp`, `Obj`, IDE 캐시, `*.sln`, 빌드 바이너리, 사용자 저장 데이터는 버전 관리에서 제외.
- 무료라도 'CC0' 표기가 **개별 에셋 페이지의 현재 라이선스**인지 확인. 해당 폴더에 들어 있는 파일들의 실사용/크레딧 여부 기록.
- 외부 코드 자체는 직접 수정하지 말고 adapter/wrapper에 감싼다. 버전 교체가 쉬워야 하며, 공급자 업데이트로 내부 save/schema나 RNG의 결과가 바뀌면 리그레션 검증.

## 7. 실행·검증·CI

1. **Unity Hub에서 `unity/` 폴더 열기** → Import/UPM 완료 → Console error 0 → Tools 메뉴로 Prototype 생성(첫 버전) → Play.
2. Editor CLI 배치 테스트 (Unity 경로 확인 후): `Unity -batchmode -nographics -quit -projectPath <repo>/unity -runTests -testPlatform editmode -testResults <out>/editmode.xml -logFile <out>/unity-edit.log`. 실제 옵션/헤드리스 지원은 설치 버전으로 확인.
3. EditMode: 시드/피해/상태/카드/저장/복제/절차 그래프 검증. PlayMode: 실제 Collider·카메라·월드 이벤트·HUD·보상 상태 전환.
4. Windows: 동작 영상·로그·버전/빌드 해시, 해상도/DPI, 키 입력/Alt+Tab, 오디오, 8~12분 시연, 5개 전체 성공·사망/보상/복구·저장 위치.
5. `git diff --name-status`로 `python/`/레거시 GDD가 바뀌지 않았는지 확인. 실제 저장 파일을 복사/테스트하지 않음.

## 8. 퍼포먼스 목표 [초안]

우선 화면상 적당한 품질에서 **1080p60 평균 16.7ms/프레임**을 목표로 삼고 p50/p95/p99 CPU/GPU 프레임타임·GC alloc·메모리·드로우콜을 **실기기에서 측정**. 숫자만 만족하고 액티브 스킬/대시/플러그인 효과가 끊기는 것은 합격이 아니다. 대형 연쇄 상태에서도 시뮬레이션 논리 틱 손실/무한 대기 없음. 최소 사양은 사용자 미확정.
