# S01 B · 환경 피해/전투 상태 연결 계약 (2026-11-18)

> 범위: **B 소유 인터페이스 제안**, A/D/C/E 통합과 확정 GDD 정책 아님. [#148](https://github.com/tharu8813/Neon-Void/issues/148), [통합 PR #152](https://github.com/tharu8813/Neon-Void/pull/152).

## 게임 엔트리/책임

- 일반 REWORK: `rework_main.tscn` → **A** `production_b_world_facade.gd` → **B** `production_cpu_adapter.gd` → `combat_runtime.gd`.
- QA 부팅/초기 스탯·스킬 지급과 일반 E-승인 스냅샷은 반드시 분리. B는 일반 스킬을 스스로 해금/수여하지 않는다.
- D가 위험 지점 접촉을 계산하지만 HP·보호막·CC·DoT·사망 판정은 전부 B 단일 엔진이 수행한다. 환경 피해는 E에 영구 보상/저장 요청을 보내지 않는다.

## D → A → B 환경 피해 호출 (제안)

D의 `world_combat_router`(D 소유)에서 formal B만 호출하도록 A/D가 라우팅:

```gdscript
# Example descriptor, **BALANCE_DRAFT** magnitude (not approved as game value)
var result = formal_b.queue_environment_hit({
    "run_id": current_run_id,
    "region_id": "CPU",
    "source_id": "thermal-grid:cell-4-9",
    "event_id": "contact:thermal-grid:cell-4-9",
    "target_id": "VACCINE",
    "damage_type": "magic", # physical | magic | true
    "base_damage": 7.0,
    "repeat_ticks": 30, # 0 = once per encounter, >0 ticks between contacts
    "is_area": false, "is_dot": false,
    "status_effects": [] # optional status dictionaries following statuses.gd
})
if not result.get("ok", false):
    # Not necessarily an error: environment_contact_cooldown is expected.
    pass
```

- 실제 허용 필드: `source_id`, `region_id`, `event_id`, `target_id`, `damage_type`, `base_damage`, `repeat_ticks`, 선택 `status_effects`, `is_area`, `is_dot`, `run_id` (Production checks if supplied).
- 인수 `ok:true,event_id,accepted_tick,target_id`. 거절 `ok:false,reason`: `encounter_not_running`, `stale_environment_run`, `invalid_environment_identity`, `invalid_environment_target`, `invalid_damage_type`, `invalid_environment_damage`, `invalid_environment_statuses`, `environment_contact_cooldown`, `environment_queue_full`.
- 60Hz 큐에 **등록만** 하고 다음 `step_fixed`에서 처리. D가 등록 응답만 보고 즉시 피격 사운드/체력 감소 또는 보상을 확정하지 않는다. `repeat_ticks=0`은 동일 contact key에 다시 피해를 입히지 않는다. 재입장 때 새 B encounter가 생성되므로 맵 접촉 기록은 초기화한다.
- `event_id`는 게임 내 일회용 랜덤이나 프레임별 UUID가 아니라 **같은 위험 지점의 접촉을 나타내는 안정적인 키**여야 한다. 동일 위험을 별도로 취급하려면 명시적으로 다른 키를 사용한다.
- 환경 공격자는 `environment:<source_id>`라는 **가상 원인 ID**다. 실제 액터로 등록하지 않는다. 공격자 치명타, 관통/AD/AP, 흡혈, 공격자 플러그인을 적용하지 않으며 대상 피해/보호막/상태 및 `damage_taken`에만 참여한다. 환경 DoT는 독립적인 가상 원인을 유지한다.
- `status_effects`는 실제 HP 피해가 발생할 때만 적용된다. 완전한 보호막 흡수/무적과 상태 무시 여부는 **[미확정] 정책**이므로 상위 검수에서 결정해야 한다.

## A UI / 이벤트

- `B.combat_hud_snapshot()`: encounter-local 틱, B `actor_snapshot`(기초/효과 능력치·HP·status_state·mana·skill_state), 3슬롯, 전투 중 보스 예고, 큐 과부하.
- `combat_notice` `kind=hit`: `actor_id=environment:<source>`, `target_id`, `event_id`(B 버스), `external_event_id`(D), `region_id`, `environment_source=true`, `amount`, `damage_type`. HP와 shield 변동은 B 런타임의 결과만 시각화.
- `combat_notice` `kind=death`: `actor_id=<victim>`, `killer_id=environment:<source>`. A/E가 플레이어 사망 정산과 정화 영수증을 독립 처리한다.
- `combat_notice` `kind=boss_pattern_cancelled`: `actor_id`, `pattern`, `reason=attack_control_blocked`, `tick`. D/A는 기존 예고 VFX를 종료/갱신해야 한다.
- A `production_b_world_facade.gd`, D `world_combat_router.gd`의 forward 메서드 추가 및 실제 구역 호출은 **이 B PR에서 수정하지 않음**.

## 기존 GDD 정책과 검증 경계

- **[확정]** 60Hz 게임플레이, 물리/마법/고정 피해, CC/저항, 3스킬 슬롯, 이벤트 부작용 분리 및 E ACK 이전 보상 금지.
- **[초안]** 방어 곡선·방관 순서, 보스 탄막 타이밍, 9종 스킬 수치/형태, 예시 위험 피해.
- **[미확정]** 보호막 우선순위·완충 시 상태·공격 시 흡혈 간 겹침, 위험과 스킬 수치의 최종 밸런스. 현재 B/C `shield-first`는 실험적 동작일 뿐 최종 사용자 승인 아님.
- 검증: `godot --headless --path . --script res://tests/rework/combat/combat_foundation_smoke.gd` 및 `.../s01_contract_smoke.gd`. CI/실기기 결과는 매번 PR 최신 SHA를 기준으로 기록.
