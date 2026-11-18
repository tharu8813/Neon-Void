extends RefCounted
## Explicit B-owned active-skill IDs. BALANCE_DRAFT prototypes until A approves
## GDD skill pool. Unknown E-known IDs intentionally fail closed, never improvise.
##
## Only the skill ID is persisted by E. Everything below is B-owned runtime data
## resolved from the registry, so a balance/VFX change never rewrites a save.
## GDD 11 leaves the concrete skill pool [미확정]; these nine entries are a
## playable draft set, not approved final balance.
const DEFINITIONS: Dictionary = {
    "nv_skill_arc_pulse": {
        "id": "nv_skill_arc_pulse", "name": "Arc Pulse", "name_ko": "아크 펄스",
        "description_ko": "전방위 아크 4발을 방출해 근접한 적을 정리한다.",
        "mana_cost": 18.0, "cooldown_ticks": 90, "can_crit": false,
        "attack": {"damage_type": "magic", "base_damage": 8.0,
            "ad_ratio": 0.0, "ap_ratio": 0.75, "can_crit": false},
        "cast": {"shape": "radial", "count": 4, "speed": 11.0,
            "lifetime_ticks": 42, "radius": 0.16}
    },
    "nv_skill_penetrator": {
        "id": "nv_skill_penetrator", "name": "Penetrator", "name_ko": "관통자",
        "description_ko": "직선 레일을 발사해 최대 4명을 꿰뚫는다.",
        "mana_cost": 22.0, "cooldown_ticks": 120, "can_crit": true,
        "attack": {"damage_type": "physical", "base_damage": 5.0,
            "ad_ratio": 1.2, "ap_ratio": 0.0, "can_crit": true},
        "cast": {"shape": "piercing", "speed": 30.0, "pierce": 4,
            "lifetime_ticks": 36, "radius": 0.11}
    },
    "nv_skill_root_packet": {
        "id": "nv_skill_root_packet", "name": "Root Packet", "name_ko": "속박 패킷",
        "description_ko": "적중한 적을 1.5초 동안 속박한다.",
        "mana_cost": 30.0, "cooldown_ticks": 180, "can_crit": false,
        "attack": {"damage_type": "magic", "base_damage": 4.0,
            "ad_ratio": 0.0, "ap_ratio": 0.5, "can_crit": false},
        "status_effects": [{
            "id": "nv_skill_root_packet_root", "duration_ticks": 90,
            "control": true, "stack_mode": "refresh", "flags": ["rooted"]
        }],
        "cast": {"shape": "projectile", "speed": 17.0, "radius": 0.15}
    },
    "nv_skill_overclock": {
        "id": "nv_skill_overclock", "name": "Overclock", "name_ko": "오버클럭",
        "description_ko": "5초 동안 공격속도 +2.0, 이동속도 +1.5.",
        "mana_cost": 24.0, "cooldown_ticks": 300, "can_crit": false,
        "attack": {},
        "cast": {"shape": "self"},
        "self_status_effects": [{
            "id": "nv_skill_overclock_buff", "duration_ticks": 300,
            "stack_mode": "refresh", "flags": ["overclocked"],
            "stat_modifiers": {"attack_speed": 2.0, "move_speed": 1.5}
        }]
    },
    "nv_skill_patch_kit": {
        "id": "nv_skill_patch_kit", "name": "Patch Kit", "name_ko": "패치 키트",
        "description_ko": "최대 체력의 22%를 회복하고 마나 25를 되돌린다.",
        "mana_cost": 20.0, "cooldown_ticks": 420, "can_crit": false,
        "attack": {},
        "cast": {"shape": "self", "heal_ratio": 0.22, "mana_restore": 25.0}
    },
    "nv_skill_blink_step": {
        "id": "nv_skill_blink_step", "name": "Blink Step", "name_ko": "점멸 보행",
        "description_ko": "조준 방향으로 6.5m 점멸하고 2초간 이동속도 +1.5.",
        "mana_cost": 26.0, "cooldown_ticks": 300, "can_crit": false,
        "is_movement_skill": true,
        "attack": {},
        "cast": {"shape": "self", "blink_distance": 6.5},
        "self_status_effects": [{
            "id": "nv_skill_blink_haste", "duration_ticks": 120,
            "stack_mode": "refresh", "flags": ["blink_haste"],
            "stat_modifiers": {"move_speed": 1.5}
        }]
    },
    "nv_skill_scatter": {
        "id": "nv_skill_scatter", "name": "Scatter Burst", "name_ko": "분산 사격",
        "description_ko": "부채꼴로 3발을 발사해 넓게 견제한다.",
        "mana_cost": 20.0, "cooldown_ticks": 150, "can_crit": true,
        "attack": {"damage_type": "physical", "base_damage": 4.0,
            "ad_ratio": 0.8, "ap_ratio": 0.0, "can_crit": true},
        "cast": {"shape": "scatter", "count": 3, "spread_radians": 0.34,
            "speed": 20.0, "lifetime_ticks": 48, "radius": 0.1}
    },
    "nv_skill_emp_ring": {
        "id": "nv_skill_emp_ring", "name": "EMP Ring", "name_ko": "EMP 링",
        "description_ko": "전방위 EMP 8발. 적중한 적을 1.5초 둔화한다.",
        "mana_cost": 34.0, "cooldown_ticks": 240, "can_crit": false,
        "attack": {"damage_type": "magic", "base_damage": 6.0,
            "ad_ratio": 0.0, "ap_ratio": 0.6, "can_crit": false},
        "status_effects": [{
            "id": "nv_skill_emp_slow", "duration_ticks": 90,
            "stack_mode": "refresh", "stat_modifiers": {"move_speed": -1.2}
        }],
        "cast": {"shape": "radial", "count": 8, "speed": 9.0,
            "lifetime_ticks": 48, "radius": 0.18}
    },
    "nv_skill_seeker_swarm": {
        "id": "nv_skill_seeker_swarm", "name": "Seeker Swarm", "name_ko": "추적 군체",
        "description_ko": "유도 탄환 2발이 가장 가까운 적을 추적한다.",
        "mana_cost": 28.0, "cooldown_ticks": 210, "can_crit": true,
        "attack": {"damage_type": "magic", "base_damage": 6.0,
            "ad_ratio": 0.0, "ap_ratio": 0.7, "can_crit": true},
        "cast": {"shape": "homing", "count": 2, "spread_radians": 0.5,
            "speed": 10.0, "turn_rate_radians_per_second": 4.2,
            "lifetime_ticks": 180, "radius": 0.13}
    }
}

static func definition(skill_id: String) -> Dictionary:
    if not DEFINITIONS.has(skill_id):
        return {}
    return DEFINITIONS[skill_id].duplicate(true)

static func ids() -> Array[String]:
    var result: Array[String] = []
    for skill_id in DEFINITIONS:
        result.append(str(skill_id))
    return result

static func validate_loadout(known: Variant, equipped: Variant) -> Dictionary:
    if not (known is Array) or not (equipped is Array):
        return {"ok": false, "reason": "invalid_skill_lists"}
    if equipped.size() > 3:
        return {"ok": false, "reason": "too_many_skill_slots"}
    var learned: Dictionary = {}
    for uid in known:
        if not (uid is String) or str(uid).is_empty():
            return {"ok": false, "reason": "invalid_known_skill"}
        if learned.has(uid):
            return {"ok": false, "reason": "duplicate_known_skill:" + str(uid)}
        learned[uid] = true
    var used: Dictionary = {}
    for uid in equipped:
        if not (uid is String) or str(uid).is_empty():
            return {"ok": false, "reason": "invalid_equipped_skill"}
        if used.has(uid):
            return {"ok": false, "reason": "duplicate_equipped_skill:" + str(uid)}
        if not learned.has(uid):
            return {"ok": false, "reason": "skill_not_learned:" + str(uid)}
        if not DEFINITIONS.has(uid):
            return {"ok": false, "reason": "skill_definition_missing:" + str(uid)}
        used[uid] = true
    return {"ok": true, "equipped": (equipped as Array).duplicate(true)}
