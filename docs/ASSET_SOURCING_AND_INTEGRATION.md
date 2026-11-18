# Neon Void — 에셋 스토어·오픈소스 라이브러리 도입 가이드

> **성격:** 개발 제안 / 아트 디렉션 초안. **외부 에셋을 실제 구매·다운로드·설치했다는 의미가 아니다.**  
> **대상:** Godot 4.7.2 / 탑다운 3D 네온 사이버펑크 로그라이트 / PC 게임  
> **작성:** 2026-11-18 · 관련 기획 [NEON_VOID_GDD_V1.md](NEON_VOID_GDD_V1.md)  
> **우선순위:** 프로토타이핑을 빠르게 하되, 임시 에셋이 최종 게임 아트 스타일과 게임 밸런스를 결정하지 않도록 한다.

## 1. 직접 만들 것 vs 에셋을 활용할 것

| 자산/시스템 | 권장 전략 | 이유 | 승인 조건 |
| --- | --- | --- | --- |
| 백신 플레이어의 실루엣·외형, 커스텀 커서, 주력 스킬 아이콘 | **직접 제작** | Neon Void 인식의 핵심. 단순 리색 에셋으로 최종 납품 금지 | 3D 탑다운 거리에서 실루엣 판독 |
| 다섯 하드웨어 구역의 시그니처 코어, 대표 보스, 정화/침식 연출 | **직접 제작 + 공용 부품 재활용** | CPU/GPU/RAM/SSD/HDD의 시각적 차별화 | 보스·플레이어·위험 지형을 한눈에 구분 |
| 벽·바닥·케이블·문·콘솔·기둥·상자·기본 로봇 파츠 | **외부 에셋을 블록아웃에 도입 후 재질/색상 통일** | 시간이 많이 드는 반복 소품 제작 단축 | 라이선스·충돌·LOD·공용 재질 검수 |
| PBR 금속/기판/플라스틱 표면, HDRI, 기본 마스크 | **CC0 텍스처 적극 활용** | 재질 품질과 일관성 확보 | 모바일/중급 PC 텍스처 메모리 제약 |
| 플러그인 카드 프레임·버튼·인벤토리 기본 위젯 | **Kenney 등 무료 UI 파츠 참고, 최종은 Godot Theme로 재구성** | 빠른 와이어프레임과 입력/포커스 검증 | 판독 가능한 정보 우선, 마우스/키보드 |
| 전투의 핵심 맞춤형 VFX(정화, 연쇄 번개, 버프, 보스 처형) | **Godot GPUParticles3D/셰이더 직접 제작** | 빌드 시너지와 가시성에 직결 | 피격 정보 가리지 않음, 효과 품질 설정 |
| UI 전환·간단한 글리치·장식 패턴 | **재사용 가능한 애니메이션/셰이더 라이브러리** | 작업 속도/일관성 | Godot 4.7.2 호환성·프로파일링 |
| 근접타·피격·UI 클릭·경고·배경 환경 소리 | **검증된 오디오 팩으로 초안, 핵심 모티브만 직접 제작** | 많은 짧은 효과음 필요 | 음량 표준화·원본 라이선스 보관 |
| 저장, 보상 중복 방지, 성장 규칙, 조작, 핵심 피해 계산 | **프로젝트 자체 코드 사용** | 저장 안전성과 결정론의 핵심 | 외부 게임프레임워크로 대체 금지 |

## 2. 검토할 에셋 소스 (2026-11-18 기준)

| 소스 | 추천 검색어/용도 | 주의 |
| --- | --- | --- |
| [Godot 공식 Asset Store](https://store.godotengine.org/) | `Godot 4`, `shader`, `3D`, `particles`, `editor tools` | **2026년 5월 공개, 아직 베타**. 플러그인별 버전·지원·개별 라이선스 확인 |
| [Godot 공식 Asset Library](https://godotengine.org/asset-library/) | `Godot 4`, `debug`, `dialogue`, `shader`, `mesh` | 무료 오픈소스 애드온 중심. MIT/GPL 등 라이선스가 서로 다름 |
| [Kenney](https://kenney.nl/assets) · [Sci-Fi UI Pack](https://kenney.nl/assets/ui-pack-sci-fi) | HUD 원형/버튼/패널/초기 아이콘 | **CC0**. 엔진에 맞춰 최종 비주얼은 직접 변형 |
| [Poly Haven](https://polyhaven.com/) | metal, panel, industrial, HDRI, PBR | **CC0**. 4K~8K 원본을 전부 게임에 포함하지 않기 |
| [ambientCG](https://ambientcg.com/) | metal plates, concrete, plastic, decals, surface | **CC0**. ORM·normal·roughness 채널 검사 |
| [Fab](https://www.fab.com/) | `sci-fi modular corridor`, `cyberpunk props`, `stylized robotics`, `particle textures` | 표준 라이선스는 엔진을 한정하지 않지만 **형식/상품 라이선스는 별도**. Unreal 전용 프로젝트는 Godot에서 바로 쓰지 못할 수 있음 |
| [itch.io 게임 에셋](https://itch.io/game-assets) | `sci-fi ui`, `top down cyberpunk`, `isometric modular` | 제작자별 사용·재배포·상업 라이선스가 다름. 무료=CC0가 아님 |
| [OpenGameArt](https://opengameart.org/) | CC0 UI 아이콘/텍스처/SFX | 파일별 CC0, CC BY, CC BY-SA, GPL 등 반드시 확인. 배포 형태와 표기 의무 검토 |
| [Quaternius](https://quaternius.com/) | 로우폴리 구조물·로봇·애니메이션 캐릭터 | **중요: 2026-08-28 도입 QAL v1.0**과 과거 CC0 팩이 혼재. 개별 다운로드 시 라이선스 스냅샷 보관 |
| [Sonniss GDC Game Audio](https://gdc.sonniss.com/) | 경고, 기계, 충돌, 전기, 에너지, UI 소리 | 무료 번들이라도 **별도의 Game Audio Bundle 라이선스**, CC0 아님. 원본 사운드 파일의 무단 단독 배포 금지 |
| [Freesound](https://freesound.org/) | 짧은 SFX/배경 소리 | 각 소리 CC0/CC BY/CC BY-NC 등 확인. 상업 게임이면 NC 제외 권장 |

**우선 탐색 세트 [제안]:** Kenney Sci-Fi UI → Poly Haven/ambientCG PBR → Godot 공식 Asset Store/Asset Library의 디버그·렌더 보조 → Fab/itch의 모듈형 산업 소품 → Sonniss의 임시 SFX. 예산이 확정되지 않았으므로 **무료/CC0로 먼저 CPU를 구성**하고 필요할 때만 유료 구매안을 올린다.

## 3. Godot 4 프로젝트에 도입하는 실무 절차

1. **요구사항 한 줄 작성:** 예) `CPU 방 내부 벽/기둥 8개 모듈, 탑다운에서 1.5초 안에 위험지형과 구별`. 단순 '예쁜 사이버펑크 모델' 검색 금지.
2. **안전한 후보 검색:** 공식 Store/AssetLib는 엔진 버전·지원 수준·라이선스 필터를 사용. 3D 모델은 `.glb`/`.gltf` 우선, 원본 `.blend`/`.fbx`는 필요할 때만 변환.
3. **라이선스/유통 검수:** 원저작자, 원본 URL, 구매/다운로드 날짜, 그 시점의 라이선스, 상업적 이용, 수정, 크레딧, 소스 재배포 가능 여부를 기록. 2026년 Quaternius **QAL 변경**에 특히 유의.
4. **격리된 실험:** 본 게임의 기본 장면·오토로드·저장 모듈을 바로 건드리지 않고 `scenes/asset_lab/` 또는 별도 실험 브랜치에 도입. 원본 전체 패키지 대신 사용할 파일만 선택.
5. **Godot 4 가져오기:** `GLB/glTF`를 `res://assets/external/<provider>/<pack>/`에 복사하고 에디터가 Import할 때까지 기다린 후 외형·스케일·축·머티리얼·애니메이션을 확인. Blender 재가공 후에는 재내보내기를 선호. Godot는 `.glb`/`.gltf`를 공식 지원.
6. **플러그인 설치:** Godot 편집기 `AssetLib`/Asset Store에서 필요 플러그인 확인 → 파일 검사 → `addons/<plugin>/`에 추가 → `Project > Project Settings > Plugins`에서 활성화(에디터 플러그인인 경우). **GDExtension 바이너리는 대상 Godot·OS·아키텍처와 호환되는지 CI로 확인**.
7. **스타일 통일:** 이미 정해진 UI 색상/선 굵기/모서리/배경 대비를 적용. 소품의 재질 팔레트와 발광 강도는 월드별 설정으로 통일. 원본 에셋의 색상을 그대로 섞지 않는다.
8. **게임플레이 검증:** 카메라 높이와 실제 전투 속도에서 가독성, 공격 판정 메시·히트박스, CPU/VRAM·GPU 프레임타임, LOD, 폴리곤·드로우콜 확인. 장식 때문에 조준·경고가 묻히면 교체.
9. **권리 기록과 최종 포함:** 검수된 파일만 리포지토리에 넣고 `docs/ASSET_CREDITS.md`에 출처·조건 유지. 유료 에셋 원본 전체를 공용 공개 저장소에 올리지 않는다. CI Windows export에서 리소스 포함을 확인한다.

### 원본 에셋 관리 규칙

```text
assets/
  original/                  # Neon Void 자체 제작 (원본 대용량은 별도 아트 저장소 가능)
  external/
    kenney/
      ui-sci-fi/
    poly-haven/
      metal-panels/
    [provider]/
      [pack]/
scenes/
  asset_lab/                 # 임포트 실험 씬 (게임 루트와 분리)
docs/
  ASSET_SOURCING_AND_INTEGRATION.md
  ASSET_CREDITS.md           # 실제 채택·출처·권한/조건
  assets/gdd/                # GDD 시안 이미지. 게임에 곧장 사용하는 원본 에셋 아님
```

**라이선스 레지스트리 필수 열:** `asset_id | source_url | author | pack_version | acquired_date | exact_license | permitted_use | attribution_required | redistribution_rule | local_path | imported_by | reviewed_by | reviewed_date`.

### GitHub PR 완료 기준

- 설치한 플러그인은 왜 필요한지, 대체 방법·잠재 충돌·지원 엔진 버전을 PR 본문에 기록.
- `addons/` 코드에 GPL/AGPL 등 라이선스 제약이 있을 때는 배포·의무 준수 검토 없이 자동 설치·병합하지 않음.
- 공개 저장소에서는 유료/재배포 금지 패키지 원본 포함 금지. 상업 게임에 동봉할 수 있는 **빌드 산출물**과 소스 자산 자체의 **재배포 허용**은 다름.
- 외부 에셋 자체가 보스 행동·플러그인 능력치·보상 RNG/저장 구조를 변경해서는 안 됨.
- 대형 리소스는 파일 유형/크기·Git LFS 정책 검토. 예시 PNG는 압축/WebP로 최적화 후 `docs/assets/gdd/`에 보관.
- Linux headless import + Windows x64 export + Windows 실제 화면 QA가 모두 통과하면 채택 결정.

## 4. 하드웨어별 맞춤형 검색과 활용안 [초안]

| 구역 | 외부에서 가져와도 되는 부품 | 반드시 Neon Void 고유하게 만들 것 | 검색어 예시 |
| --- | --- | --- | --- |
| CPU | 공장 금속 패널, 히트싱크, 열기둥, 컨베이어 | 다중 코어 보스·오버클럭 경고·코어 정화 연출 | `sci fi heat sink`, `factory modular tile` |
| GPU | 홀로그램 프레임, 입자 텍스처, 디스플레이 | 셰이더 폭주형 맵 이벤트·렌더 가속 보스·VFX 가독성 정책 | `hologram shader`, `energy particles` |
| RAM | 랙·메모리 모듈, 회로 패턴, 램프 | 기억의 잔상·방 구조 재배치·기억 포화 경고 | `server rack`, `memory module` |
| SSD | 서버/데이터 패널, 빠른 문, 광섬유 | 고속 레일 이동·IO 위험 패턴·빠른 보스 | `server room modular`, `fiber optic` |
| HDD | 산업용 기계·기어·회전 판, 폐기물 | 회전 구역·단편화 지형·장기 위험 축적 보스 | `industrial gear`, `hard drive mechanism` |

구역의 세부 보스·기믹은 아직 **[초안]**이므로 특정 유료 에셋을 먼저 구매해 설계를 고정하지 않는다.

## 5. 금지하거나 신중해야 할 에셋 활용

- 기존 상용 게임의 모델·캐릭터·음악·UI 스크린샷을 라이선스 없이 추출/배포 금지.
- 무료=무조건 상업·재배포 허용 아님. `CC BY-NC`는 상업 게임 불가. `CC BY`는 출처 표기 필요.
- 생성한 GDD 콘셉트 그림의 텍스트는 **기획 정답이 아님**. 문서 본문 [확정]/[초안] 내용과 불일치 시 본문이 우선.
- '에셋 스토어에서 받음'만으로 Godot 호환/최종 사용/CI 성공을 주장하지 않는다.
- 배포되지 않은 라이브러리 이름, 테스트하지 않은 플러그인 버전, 불명확한 요금·라이선스는 검증 없이 고정하지 않는다.

## 6. 출처 및 공식 검증 링크

- Godot 공식 Asset Store 발표 (2026-05-22): https://godotengine.org/article/introducing-the-godot-asset-store/
- Godot 공식 Asset Store: https://store.godotengine.org/
- Godot Asset Library 개요: https://docs.godotengine.org/en/stable/community/asset_library/what_is_assetlib.html
- Godot 플러그인 설치: https://docs.godotengine.org/en/stable/tutorials/plugins/editor/installing_plugins.html
- Godot glTF/GLB 임포트: https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/importing_3d_scenes/available_formats.html
- Kenney 라이선스: https://kenney.nl/support
- Poly Haven 라이선스: https://polyhaven.com/license
- ambientCG 라이선스: https://docs.ambientcg.com/license/
- Fab EULA: https://www.fab.com/eula
- Quaternius 현행 QAL v1.0: https://quaternius.com/license.html
- OpenGameArt 라이선스 FAQ: https://opengameart.org/content/faq
- Sonniss GDC bundle licence: https://sonniss.com/gdc-bundle-license/
- Freesound license FAQ: https://freesound.org/help/faq/
