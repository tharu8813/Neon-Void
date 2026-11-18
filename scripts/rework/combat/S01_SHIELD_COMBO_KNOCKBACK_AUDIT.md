# S01 B · 보호막 / 레이저·부메랑 / 넉백 실전 검증 계약

> B 구현·테스트 범위. GDD `[확정]`과 `[미확정]`을 구분한다.
> 이 문서는 **사용자 최종 밸런스 승인**이나 실제 Windows 플레이 수용 증거가 아니다.
> Issue #148 · PR #158 · 통합 PR #152.

## 1. 보호막 처리 순서

기존 B `combat_runtime.gd` 피해 경로와 `c_effect_adapter.gd` 레이어를 재사용한다.

1. 유효한 원본 공격 → AD/AP 및 속성에 따른 기본 피해 → 물리/마법 방어와 관통 → 치명타 → C/B 보호막 `post_resistance` 처리(현재 **[초안]**) → HP 실제 감소 → 실제 HP 감소가 양수일 때만 흡혈·상태·사망·`damage_taken` 이벤트.
2. C가 제공한 주기적·스킬·대시 보호막은 동일한 B 레이어로 들어간다. `add`에서는 오래된 레이어부터 소비하며, `expires_at`은 각자 가진다. 구성 정책 `strongest` / `replace` / `true_damage_bypasses`는 기존 실험 옵션으로 남기고 확인되지 않은 기본값을 확정하지 않는다.
3. 보호막이 전부 소진될 때 잔여 피해만 HP로 이동한다. 보호막만 맞은 공격에서 `actual_damage==0`은 현재 on-hit, DoT 부여, 피해 트리거/흡혈을 실행하지 않는다. 이것은 **[미확정]** 상호작용 규칙이다.
4. 유효하지 않은 금액(음수, NaN, INF)과 음수 만료 시간은 C/B 사전 검증에서 전체 명령을 거부한다. 데미지를 받을 때, 상태 DoT로 받을 때, 무적일 때의 공통 피해 큐를 유지한다.

## 2. 레이저 + 부메랑 합성 (임시 테스트 정책)

GDD의 동시 변형 합성 규칙은 **[미확정]**. 플러그인을 강제 제거하지 않기 위해 테스트 가능한 **[BALANCE_DRAFT]** 구성으로 제안한다.

1. 하나의 기본탄 `attack_id`에 두 C `projectile_transform` 명령이 오면 레이저의 폭·관통·on-hit 효율을 먼저, 부메랑의 귀환/복제/범위 피해를 다음으로 적용한다. C 레시피의 선언 순서가 바뀌어도 합성 결과가 같다.
2. 추가 부메랑도 레이저 폭·관통·on-hit 효율을 상속한다. 부메랑 각 탄환은 적중 대상별 동일 `hit_key` 중복을 막고 주변 적에게 합법적인 범위 피해를 추가한다. 공격자 귀환 또는 충돌·시간 만료 시 해당 발사체가 종료되며 최종 발사체 종료 후 기본 사격 잠금 해제.
3. 발사체가 벽이나 근거리 적중으로 이미 사라졌다면 해당 탄환의 변형 명령만 무시한다. 존재하지 않는 부메랑 수량을 C 원장에 추가해서 **영구 사격 잠금**에 빠지면 안 된다.
4. 중첩된 동일 변형, 잘못된 부메랑 개수, 발사체 수용량 초과는 fail-closed. 기존 C 유효 연쇄 이벤트를 삭제하지 않는다.
5. 범위 피해 반경/다단 히트/원본 vs 복제 기본 피해/자세한 귀환·시각 애니메이션은 최종 승인 대상이다. S01 코드는 프로토타입 작동 규칙이다.

## 3. 실제 넉백과 상태이상

- C `nv_r_low_hp_knockback`의 `knockback` 명령은 더 이상 텍스트 이벤트만 만들지 않는다. B `apply_radial_knockback(source_id, force, radius)`에서 살아 있는 상대를 안정적인 ID 순서로 탐색하고, 물리 위치를 바꾼다.
- 강인함 및 보스 CC 저항은 강제 이동 거리를 각 비율만큼 감소시키는 **[BALANCE_DRAFT]**. 속박·정지는 자발적 이동을 금지하지만 외부 넉백을 별도로 지우지 않는다. 무적 대상의 넉백 면역 여부와 기본 반경 `knockback_radius=5.0`은 **[미확정]** 정책이다.
- B 런타임은 기존 `blink_filter`를 재사용해 D 벽/바닥 경계를 자르고, B 생산형 장면에서 `move_and_collide`로 실제 적 `CharacterBody3D`를 이동시킨 뒤 실위치로 다시 동기화한다. 나머지 DoT/제어 상태 기간은 보존된다.
- C/A 호환 `knockback_requested`를 계속 발행하고, 실제 이동 후 B의 `knockback_applied`(target/source/from/to/tick)를 추가 제공한다.

## 4. 검증

```bash
godot --headless --path . --script res://tests/rework/combat/combat_foundation_smoke.gd
```

`tests/rework/combat/s01_shield_combo_knockback_cases.gd`와 기존
`production_cpu_adapter_cases.gd`는 다음을 검사한다: 세 보호막 중첩/20 상한/독립 만료,
shield→HP/true bypass/DoT, laser+boomerang 실제 합성·원본+추가탄·범위 피해·귀환/잠금해제,
이미 사라진 발사체에서 잠금 방지, low-HP C 트리거의 강제 이동, 속박 상태 보존,
보스 저항/강인함, 물리 적 몸체의 이동 결과와 HUD notice.

## 5. A/C/D/E 후속

- **A**: `knockback_applied`, shield layer snapshot, 각 탄환의 `laser_active`/boomerang return을 HUD/VFX에 연결하고 실제 화면 검증.
- **C**: 병합 후 27종 생산형 C 이벤트 조합 회귀. 원본 `knockback_requested` 이벤트 호환 및 `boomerang_outstanding` 이중 카운트 없음 확인.
- **D**: 실제 5개 지형 벽/코너·보스 CC·해저드와 강제 이동 시 충돌/끼임 수동 테스트.
- **E**: 저장/스키마/영수증 변경 없음. 세 효과의 플러그인 획득/레벨은 여전히 E ACK 이후에만 반영.
- **사용자/A [미확정]**: shield-first/true bypass/레이저+부메랑 합성의 최종 수치 및 강제이동 CC 정책 승인 필요.
