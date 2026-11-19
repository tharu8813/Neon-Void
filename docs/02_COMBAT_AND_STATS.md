# Combat Rules & Simulation Contract | v2.0

> 표기의 `[확정]`은 이전 사용자 선택, `[초안]`은 검증용 수치·연산 순서다. 그래픽/에셋과 게임 판정이 섞이지 않도록 Unity 구현에서 시스템 경계를 지킨다.

## 1. 업데이트와 좌표 [확정/개발 권장]

- **고정 논리 60Hz**: `tickIndex`를 기준으로 이동, 스킬, 상태, 탄환, 피해, XP 트리거, 플러그인 연쇄를 진행. 렌더 프레임이 30/60/144fps라도 같은 입력/시드면 결과 동일해야 한다.
- Unity `FixedUpdate`의 설정은 1/60초를 목표로 하되, physics/render 코드에서 float 델타·시뮬레이션 step 순서·physics nondeterminism을 문서화. 완전한 물리 비트 동일성을 주장하려면 별도 검증 필요. 규칙 RNG는 시뮬레이션 소유.
- 이동 평면은 `XZ`, 높이는 `Y`. 조준은 마우스 화면 좌표를 `XZ` 지면으로 Raycast/plane-intersect. 카메라 따라 움직이며 조준이 흔들리는 피드백 루프 금지.
- **입력은 구조화** (`MovementVector`, `AimWorldPoint`, `Dash`, `Skill1..3`, `Interact`); UI가 열린 상태에서는 전투 입력을 차단하고 포커스를 명확히 한다.
- 사망·영구 보상·파기는 전투 엔진 내부에서 저장하지 않고 progression 저장 서비스에 명령한다.

## 2. 기본 능력치 [확정인 숫자만 확정]

| 키 | 단위 | 시작값 | 지위 |
|---|---|---:|---|
| `attackDamage` | 피해 계수 | 5 | [확정] |
| `abilityPower` | 피해 계수 | 0 | [확정] |
| `critDamageMultiplier` | 배 | 1.20 | [확정] |
| `projectileSpeedMultiplier` | 배 | 1.00 | [확정] |
| `luck` | 퍼센트 | 0 | [확정] |
| `projectileSize` | 정수 | 10 | [확정] (실제 월드 단위 환산 [초안]) |
| `onHitLifesteal` | 퍼센트 | 0 | [확정] |
| `omnivamp` | 퍼센트 | 0 | [확정] |
| `maxHealth` | HP | 100 | [초안] |
| `moveSpeed` | Unity units/s | 5 | [초안] |
| `attacksPerSecond` | attacks/s | 2 | [초안] |
| `maxMana` | MP | 100 | [초안], 능력치 존재 자체는 [확정] |
| `manaRegenPerSecond` | MP/s | 5 | [초안], 자동 회복은 [확정] |
| `physicalArmor`, `magicResistance` | 저항 수치 | 0 | [초안] |
| `physicalPenFlat`, `physicalPenPercent`, `magicPenFlat`, `magicPenPercent` | 절댓값/%p | 0 | [초안] |
| `critChance`, `skillHaste`, `tenacity` | %/정수/% | 0 | [초안] |

파생값은 `base → E 승인 영구 투자 → 플러그인/버프·디버프 → 캡·검증 → 최종 전투 스냅샷` 순서. 같은 영구 투자나 플러그인이 구역 재진입 시 두 번 적용되지 않도록 **원본 기반 재계산**. 음수/NaN/무한대/잘못된 ID 거부.

## 3. 피해 [확정 원리 + 초안 공식]

**[확정]** `physical`, `magic`, `true` 세 피해 타입. 각 공격은 `flat + adScale*AD + apScale*AP`를 정의. 물리는 방어력, 마법은 마법저항, 고정 피해는 두 일반 저항·관통을 우회. 치명타 가능 여부는 공격별 데이터. 관통은 고정/비율 구분.

**[초안 계산 예시]**

```text
raw = baseDamage + AD*adRatio + AP*apRatio
if canCrit && critRoll: raw *= critDamageMultiplier
resistAfterPen = resistBase * (1 - percentPen) - flatPen
if resistAfterPen >= 0: mitigation = 100 / (100 + resistAfterPen)
else: mitigation = 2 - 100 / (100 - resistAfterPen)
appliedBeforeShield = max(0, raw * mitigation)
actualHealthDamage = max(0, appliedBeforeShield - shieldAbsorbed)  // 보호막 순서/흡혈 기준은 [미확정]
```

스킬 가속 H일 때 쿨타임 `baseCooldown/(1+H/100)` [초안]. 쿨타임 최솟값은 0보다 커야 하며 정확한 제한 수치 미확정. 공격속도에 상한은 있지만 값 미확정. UI에서 방어력 내부 수치와 '실제 감소율'을 혼동하지 않도록 분리.

**테스트 벡터 [초안]**: AD 5의 기본 공격이 100% AD라면 방어 전 5 피해, 초당 2발이면 이론 10 DPS. 저항력 100에서 동일 피해는 50%, 저항력 -100에서 150% (위 식). 120% 치명타는 1.2배. 보스 HP 500은 어디까지나 밸런스 샘플.

## 4. 흡혈 [확정 핵심]

- `onHitLifesteal`과 `omnivamp`는 별개 자원/트리거.
- `omnivamp`는 단일/광역/지속 피해에 적용 가능. 광역 OR 지속인 경우 **효율 0.33**, 광역 AND 지속인 경우에도 **0.33 한 번만 적용**.
- 예: 실피해 100, 모든 피해 흡혈 10%, 단일 피해 → 10회복; 광역 또는 DoT → 3.3 회복(예시 실수 정밀도 초안).
- **[미확정]** 보호막에 흡수된 피해가 '실제 피해'인지, 실제 HP 초과 피해 오버킬을 흡혈에 산입하는지, 한 사건의 onHit+omni 회복 합성 순서. 테스트/실험 가능한 전략 모듈로 분리하고 최종 위험 보상에 연결 전 승인.

## 5. 상태이상 [확정 카탈로그]

| 버프 | 규칙 |
|---|---|
| 강화 | AD 증가 |
| 이동속도 증가 | 이동 속도 가산/배수 [초안] |
| 탄환속도 증가 | 탄환 속도 |
| 발사속도 증가 | 초당 발사횟수 |
| 스킬 가속 | 가속 정수 |
| 재생 | 지속 HP 회복 |
| 무적 | 공격/피격 판정상 피해 무효 |
| 관통 | 여러 적 통과; 같은 적에 동일 발사체 중복 피격 금지 |

| 디버프 | 규칙 |
|---|---|
| 나약함 | AD 감소 |
| 구속 | 이동 속도 감소 |
| 발사속도 감소 | 공격 간격 증가 |
| 기능고장 | 발사 속도 감소 + 확률적 발사 실패 |
| 제한 | 액티브 스킬 사용 불가 |
| 속박 | 이동 + 이동 스킬 불가 |
| 정지 | 전투 행동·공격·상호작용 불가 (시스템 메뉴까지 차단 아님) |
| 무결성 공격 | 초당 DoT |
| 방화벽 차단 | 대상 방어력/피해감소 약화 |

**[확정]** 강인함은 제어형 상태이상에 적용. 보스 별도 제어 저항. 각 상태 효과의 중첩/재적용/만료는 효과별 데이터에서 설정하며 전부 동일하게 하드코딩하면 안 된다. 강인함이 DoT/스탯 감소를 무조건 0으로 만드는 식의 과잉 적용 금지.

**[초안 스택 유형]** `RefreshDuration`, `IndependentStacks`, `AdditiveStacks`, `ReplaceStronger`, `Unique`, `UnboundedAggregated`(해체). 즉시 유효 기간/임계치 변경 시 처리 순서와 UI 남은 시간 갱신을 테스트.

## 6. 전투 이벤트와 연쇄 [확정 핵심]

**중요:** `기본 적중→연쇄 번개→폭발→지속 피해`는 **합법적**이고 허용해야 한다. 금지해야 하는 것은 같은 경로에 묶인 `A→B→A` 식의 **순환**이다.

```text
AttackIntent → SpawnProjectile/Hit
→ DamageRequest → HitResolved/ShieldResolved/DeathResolved
→ TriggeredEffect events queued (eventId, origin, target, source, tags, chainPath)
→ Verify proc permission + cooldown + path cycle
→ Resolve next event(s)
→ Emit read-only Render/SFX/Telemetry events
```

필수 메타데이터: `eventId`, `runId`, `tick`, `sourceActor`, `targetActor`, `attackId`, `damageType`, `isArea`, `isPeriodic`, `isCritical`, `sourceTags`, `triggerId`, `chainPath`, `randomStream`. 시뮬레이션 변경은 **한 곳의 event queue**에서 수행하고 VFX에서 직접 피해/XP를 만들지 않는다.

**권장 사이클 판정:** 같은 효과가 동일한 발동 **경로**에서 자신을 다시 호출할 때 차단; 서로 다른 원발 공격/독립 분기는 새 경로. 효과의 대상이 바뀌는 합법적 연쇄를 무조건 막지 않도록 `effectId+causalParent` 정보와 허용 태그를 함께 판정. 안정성 예산 초과 시 오류/진단과 시뮬레이션 회복 설계를 사용하되 게임 규칙을 조용히 임의 누락하지 않는다.

**위험 상호작용 테스트:** 연쇄 번개+폭발/추가탄/해체 DoT/유도탄, 레이저+부메랑, 피버타임 공격속도+연속 타격, 적 수천 개와 중첩이 무제한인 해체. 프레임별 오브젝트 생성/GC 할당·CPU·GPU를 측정. VFX의 렌더 수량만 줄일 수 있고 **판정/보상은 생략 금지**.

## 7. 보호막·스킬·대시

- **[확정]** 기본 이동방향 대시 + 별도 3슬롯 액티브 스킬. 자동 마나 재생, 스킬별 마나 코스트·CD·타입·데이터 계수.
- **[확정]** 보호막을 만드는 일반 플러그인 30초 주기 / 스킬 사용 / 대시·점멸 연계 등이 있다.
- **[미확정]** 보호막 흡수 순서(신규부터? 오래된 것부터?), 여러 보호막의 갱신·합산, 만료/DoT/흡혈 판정, 스킬·대시 충전 및 무적 프레임.
- **[초안 기본 입력]** `WASD/방향키` 이동, `Space` 대시, `F` 상호작용, `Q/E/R` 스킬, `Esc` 메뉴, `Tab` 상세 패널. **R은 Python/Godot의 재시작/철수 키와 충돌할 수 있어 확정 전까지 리바인딩/메뉴 충돌 테스트**. 대시 방향은 커서가 아닌 이동 벡터.
- 정지 중 대시는 방향 규칙이 미확정이므로 기본 프로토타입에서는 **미발동**(가역적 임시값)으로 만들고 사용자 결정 후 조정.

## 8. AI + 환경 피해 API

- 적과 환경 피해는 모두 `DamageRequest` 진입점을 공유하여 플레이어/적 공통 방어·무적·보호막·상태·플러그인 반응을 사용.
- 레이저/함정/낙하물은 실제 접촉/범위 판정 이벤트를 발생시키고 시각효과 및 환경 차이는 별도 데이터에서 보장. Python/Godot 과거의 환경 피해 API가 이 Unity 코드에 존재한다고 가정 금지.
- AI 이동은 목표 선정/경로계산/공격 의도 생성만 맡고 데미지 직접 차감 금지. VFX만 있는 함정은 완료로 기록하지 않는다.

## 9. 빠져서는 안 되는 자동 테스트

1. 30/60/144fps 조건에서 동일 입력 시드에 따른 틱 단위 피해/상태/카드 결과가 동일(물리적 미세 차 허용 여부 별도 기록).
2. 단일/광역/DoT/광역+DoT 전체 피해 흡혈 (33%는 단 1회).
3. 양수/음수 저항력, 관통, AD/AP 혼합 스킬, 치명타 여부와 120% 기본 배율.
4. `A→B→C` 합법적 연쇄 통과, `A→B→A` 순환만 차단, 독립 루트 발동 보존.
5. 상태이상 적용·만료·중첩·보스 저항/기능고장·MP부족·쿨다운 거부.
6. 대시가 입력 이동 방향을 따르고 커서 위치가 바뀌어도 대시 방향 불변.
7. 보호막 3종 동시 적용/만료/DoT 및 흡혈의 순서 (정책 승인 뒤 고정).
8. 한 발사체의 동일 적 대상 중복 피격 방지, 타깃 소실/보스 페이즈 도중 안전함.
