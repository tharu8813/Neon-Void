# Neon Void — 외부 에셋·라이선스 기록

> **상태:** 관리용 템플릿. 실제로 채택되지 않은 외부 에셋을 사용 중이라고 주장하지 않는다.
> 구체적인 소싱 방법은 [ASSET_SOURCING_AND_INTEGRATION.md](ASSET_SOURCING_AND_INTEGRATION.md) 참고.

## 1. 현재 문서 예시

| asset_id | 제작자/소스 | 라이선스/권리 | 프로젝트 경로 | 상태 |
| --- | --- | --- | --- | --- |
| `gdd-hub-concept` | Neon Void 자체 신규 기획 도식 | 프로젝트 내부 기획 예시 | `docs/assets/gdd/hub-world-concept.svg` | GDD 전용 |
| `gdd-cpu-hud` | Neon Void 자체 신규 기획 도식 | 프로젝트 내부 기획 예시 | `docs/assets/gdd/cpu-combat-hud-concept.svg` | GDD 전용 |
| `gdd-plugin-draft` | Neon Void 자체 신규 기획 도식 | 프로젝트 내부 기획 예시 | `docs/assets/gdd/plugin-draft-ui.svg` | GDD 전용 |
| `gdd-db-ui` | Neon Void 자체 신규 기획 도식 | 프로젝트 내부 기획 예시 | `docs/assets/gdd/vaccine-database-ui.svg` | GDD 전용 |
| `gdd-extract-ui` | Neon Void 자체 신규 기획 도식 | 프로젝트 내부 기획 예시 | `docs/assets/gdd/extraction-ui.svg` | GDD 전용 |
| `gdd-boss-vfx` | Neon Void 자체 신규 기획 도식 | 프로젝트 내부 기획 예시 | `docs/assets/gdd/boss-vfx-storyboard.svg` | GDD 전용 |

## 2. 외부 에셋 채택 시 작성할 레지스트리

**한 줄에 한 자산/팩.** 미검수·시험 도입도 반드시 상태로 구분한다.

| asset_id | 제목/버전 | 제작자 | 원본 URL | 다운로드/구매 날짜 | **라이선스 원문/버전** | 상업 사용 | 수정 | 출처 표기 | 원본 재배포 | 프로젝트 경로 | 검수 PR/담당 | 상태 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `example-only` | 예시 (미도입) | 미정 | 미정 | 미정 | **미검수** | 확인 전 | 확인 전 | 확인 전 | 확인 전 | 없음 | 없음 | 사용 금지 |

- 검수 상태: `candidate` → `licensed` → `imported` → `playtested` → `approved` 또는 `rejected`.
- 라이선스·다운로드 파일에 포함된 `LICENSE`/구매 영수증/버전을 보존하고 본문을 링크한다. 공개 repo에 라이선스상 허용되지 않은 원본 팩을 올리지 않는다.
- CC0라도 작업일/팩 버전/출처 URL은 기록하여 추후 감사·교체 가능하게 유지한다.
- 에셋을 삭제할 때 관련 메타데이터/의존성·씬의 깨진 참조도 확인한다.

## 3. 최종 출시 직전 검수

- [ ] 사용 중인 모든 외부 모델·재질·아이콘·효과음·폰트의 원저작자 및 라이선스 기록.
- [ ] 유료 라이선스의 게임 빌드 동봉 허용·원본 소스 재배포 금지 확인.
- [ ] GPL/CC-BY-SA/CC-BY 등 고유 의무 확인. 모호한 경우 채택 보류.
- [ ] 패키지·셰이더·플러그인 Godot 4.7.2 호환과 Windows/Linux 배포 검증.
- [ ] 다른 게임의 저작물 무단 복사/스크린샷·상표 오인 요소 없음.
- [ ] 내부 시안의 가상 텍스트가 실제 게임의 확정 수치로 유입되지 않았음.
