# Third-party Assets & Libraries: Allowed and Encouraged

> **프로젝트 방침 [사용자 요구]:** 새로운 Neon Void는 **외부 에셋, Unity Asset Store 콘텐츠, 오픈소스 라이브러리, 상용 개발 도구를 적극적으로 써도 된다.** 팀의 구현 시간을 모델/타일/효과음을 0부터 만드는 데 다 쓰지 말고, 전투·게임 루프·시스템 고유성을 만드는 데 집중한다. 외부 콘텐츠 사용은 *무제한 복제/무료 다운로드/유료 구매 자동 승인*과 다르다.

## 1. 적극 활용할 항목 [권장]

| 범위 | 1순위 후보 | 직접 제작/수정할 부분 | 도입 관문 |
|---|---|---|---|
| 3D 환경 | Unity Asset Store 모듈형 SF/기판/금속 키트, Poly Haven/ambientCG 머티리얼 | CPU/GPU/RAM/SSD/HDD별 상징 지형·색·위험 기믹 | URP 호환, 탑다운 가독성, 재배포 권한 |
| 모델/소품 | Kenney/조건 확인된 공개 팩, Quaternius(팩별 라이선스 확인), 유료 SF 패키지 | 플레이어 백신·다섯 보스 실루엣/패턴 | 폴리곤·애니메이션·충돌, 라이선스 |
| UI | Unity UI Toolkit + 필요 시 라이선스 허용 아이콘·폰트, 유료 UI 팩 | 카드·DB·상태·복제의 의미체계 | 재사용 가능한 USS/UXML, DPI·색각 |
| 블록아웃 | Unity primitives / ProBuilder | 각 구역의 접근성·그래프·상호작용 | 결정론·NavMesh·프로파일링 |
| VFX/셰이더 | URP Shader Graph, ParticleSystem/VFX Graph, 공개 라이선스 효과 | 번개·해체·레이저·정화·파기 고유 표현 | 포인트/데칼/오브젝트 풀, 정확한 텔레그래프 |
| 오디오 | CC0/상업허용 SFX, 구매한 SFX 라이브러리 | 하드웨어 고유 리듬/위험 신호·피드백 | 재배포와 게임 동기화 이용 허가 |
| 캐릭터 애니 | Unity Animation, 라이선스된 리깅/애니메이션 팩 | 백신 이동·반동·대시/피격 실루엣 | Unity 리그/URP 호환·배포권 |
| 디버깅 | Unity Profiler, Test Framework, Memory Profiler, 검증된 오픈소스 로깅 | 보상 영수증·시드 재현 디버그 패널 | 프로젝트 전체 결합도 최소화 |
| 스토리지 | C# 검증된 serialization 라이브러리/저널 기술(선별) | 새 Unity의 영구 보상·복제·DB 트랜잭션 | 라이선스·파일시스템 안전/Windows 테스트 |

**추천 조합:** 우선 Unity 공식 URP/Input/UI Toolkit/Test Framework + CC0 placeholder + 내부 Unity Graybox. 실제 플레이/전투를 검증한 뒤 필요한 외부 고급 그래픽·오디오 에셋을 각 영역에 사용. 선행 자산 임포트 때문에 게임플레이 구현이 멈추지 않게 한다.

## 2. 검색을 시작할 수 있는 공식/제작자 사이트

- [Unity Asset Store](https://assetstore.unity.com/) : 유료/무료 모두 **개별 Standard EULA·Restricted Asset·시트 요건** 점검.
- [Unity Package Manager / 패키지 설명](https://docs.unity.com/en-us/engine/6000.3/manual/packages-list/packages-all) : 공식 패키지 버전/에디터 호환.
- [Kenney](https://kenney.nl/assets) : 게임 템플릿·오브젝트·UI, 개별 팩 라이선스 확인.
- [Poly Haven](https://polyhaven.com/) / [ambientCG](https://ambientcg.com/) : 텍스처/HDRI/재질; 개별 라이선스 확인.
- [Quaternius](https://quaternius.com/) : 모델/환경, **시기 및 개별 팩의 QAL/CC0 조건을 각각 검토**. 2026년 라이선스 변경을 무시하지 않음.
- [Fab](https://www.fab.com/) / [itch.io](https://itch.io/game-assets) / [OpenGameArt](https://opengameart.org/) : 플러그인/3D/오디오 후보. 사이트 명칭만으로 상업 이용 허용을 추론하지 않음.
- [Freesound](https://freesound.org/) : 파일별 CC0, BY, BY-NC 등 조건이 다름. **NC, ND/권리 불명확한 오디오를 유료/상용 배포에 무단 사용 금지**.
- [Unity LTS 정책](https://unity.com/releases/unity-6/support), [Asset Store EULA FAQ](https://assetstore.unity.com/browse/eula-faq), [Asset Store 약관](https://unity.com/legal/as-terms).

**중요:** 이 파일은 구매·선정·다운로드·도입 완료 목록이 아니다. 현재 ZIP에는 제3자 에셋 바이너리를 따로 포함하지 않았다. `docs/assets/gdd/*.svg` 6개는 기존 프로젝트 내에서 생성된 **기획용 참고 시안**이며 외부 패키지가 아니다.

## 3. 에셋 선정 프로세스

1. **필요**: 현재 기획 단계에서 어떤 기능이 부족한지 정의. 예: 'GPU 레이저 위험선이 읽히지 않음'.
2. **후보**: 유료/무료 최소 2개 비교 [초안]. 미리보기에서 탑다운 시점 실루엣/텍스처 크기/성능 확인.
3. **권리**: 제작자, 실제 다운로드 URL·일자, 라이선스 문구, 상업 사용, GitHub 공개 업로드/원본 재배포, 수정/크레딧 의무를 읽고 기록.
4. **버전**: Unity 6.3/URP/새 Input System 및 플랫폼 지원, 실제 패키지 버전/해시 기록.
5. **격리 테스트**: 전용 브랜치/씬에 소량 임포트, 충돌/셰이더/압축/GPU 프레임비용 측정.
6. **통일**: 색·라이팅·재질·스케일·애니메이션 속도 리워크, 다른 회사의 게임 화면/상표·디자인 그대로 복제 금지.
7. **채택**: 외부 패키지 자체는 되도록 wrapper로 격리, 사용 자산만 커밋 가능한 라이선스인지 확인하고 등록. 변경 PR에서 출처 기록과 QA 제출.
8. **제거/업데이트**: 사용 안 하는 대형 assets/데모 씬 제외, 업데이트/취약성/패키지 종속성 별도 평가.

## 4. 실제 기록 항목과 명령

`third_party/manifest.csv` 컬럼: `asset_or_package`, `provider`, `source_url`, `version_or_hash`, `license`, `commercial_use`, `redistribution_in_repo`, `credit_required`, `unity_compatible`, `review_status`, `reviewer`, `notes`.

- **사용 승인 상태:** `candidate → licensing-review → tested → approved → in-production`. 문서에 'verify'라고 남긴 것은 도입 라이선스 확인 완료가 아니다.
- **유료 에셋:** 사용자가 구매 승인하기 전 대금 지불·라이선스 획득을 가정하지 않는다. 구매했더라도 **Unity 프로젝트와 소스 저장소에 재배포 가능 여부는 별도**.
- **Asset Store EULA 주의:** 게임 내 임베드/배포 권한이 있다고 해서 에셋 원본 소스나 프리팹을 불특정 GitHub 공개 저장소에 그대로 배포할 권한이 생기는 것은 아님.
- **GPL/AGPL 오픈소스:** Unity/상용 배포와 결합했을 때 의무를 정확히 확인한다. 라이선스 검토 전 도입 금지. 코드/아트를 AI 모델 학습 데이터로 재사용하는 것과 게임 제작용 사용은 별개의 권리로 확인.
- **프로젝트에 에셋 파일 미포함/소스 비공개:** 허용되는 라이선스이거나 팀 내부 프라이빗 소스에서만 접근하도록 관리. 오픈 저장소에 restricted raw binary가 실수로 push되지 않게 파일별 확인.

## 5. 테스트가 꼭 필요한 에셋 위험

- 모델: 원본에 Collider가 없거나 NavMesh로 막힘; 스케일/좌표가 잘못돼 공격/철수 경로가 불가능.
- VFX: 매우 밝은 bloom/투명 바닥·레이저가 타격 판정을 가림; 파티클 발동이 `Update`에서 새 GameObject/Material 무한 생성.
- UI: TextMesh Pro/폰트 라이선스/한글 fallback, 키보드 focus 사라짐, 200% DPI에서 버튼 자르기.
- 음향: 로컬 파일은 정상이나 Windows EXE에서 리소스/압축 실패, 재생 장치 변경 후 무음.
- 라이브러리: Unity 6.3 vs 특정 package assembly 충돌, Domain Reload/에디터 버전 호환, 보상 난수 버전 업그레이드로 결과 변화.

## 6. 사용자에게 제안할 실제 구매 우선순위 [초안]

**우선 무료:** 프로토타입 3D 지형, 간단한 UI 아이콘, SF 재질, 일반 소리. **우선 구매 고려:** 하드웨어별 모듈형 환경 + 적/보스 리깅 및 애니메이션 + 전문 SFX 패키지. 게임 시스템·저장·플러그인 연쇄 로직을 '올인원 에셋'에 전적으로 맡기지 않는다. 구매 필요할 때는 후보·가격·라이선스·의존성·대안 비교 뒤 사용자 승인 요청.
