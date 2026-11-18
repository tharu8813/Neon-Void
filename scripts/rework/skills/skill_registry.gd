extends RefCounted
## Explicit B-owned active-skill IDs. BALANCE_DRAFT prototypes until A approves
## GDD skill pool. Unknown E-known IDs intentionally fail closed, never improvise.
const DEFINITIONS: Dictionary = {
    "nv_skill_arc_pulse": {
        "id": "nv_skill_arc_pulse", "name": "Arc Pulse",
        "mana_cost": 18.0, "cooldown_ticks": 90, "can_crit": false,
        "attack": {"damage_type": "magic", "base_damage": 8.0,
            "ad_ratio": 0.0, "ap_ratio": 0.75, "can_crit": false}
    },
    "nv_skill_penetrator": {
        "id": "nv_skill_penetrator", "name": "Penetrator",
        "mana_cost": 22.0, "cooldown_ticks": 120, "can_crit": true,
        "attack": {"damage_type": "physical", "base_damage": 5.0,
            "ad_ratio": 1.2, "ap_ratio": 0.0, "can_crit": true}
    },
    "nv_skill_root_packet": {
        "id": "nv_skill_root_packet", "name": "Root Packet",
        "mana_cost": 30.0, "cooldown_ticks": 180, "can_crit": false,
        "attack": {"damage_type": "magic", "base_damage": 4.0,
            "ad_ratio": 0.0, "ap_ratio": 0.5, "can_crit": false},
        "status_effects": [{
            "id": "nv_skill_root_packet_root", "duration_ticks": 90,
            "control": true, "stack_mode": "refresh", "flags": ["rooted"]
        }]
    }
}

static func definition(skill_id: String) -> Dictionary:
    if not DEFINITIONS.has(skill_id):
        return {}
    return DEFINITIONS[skill_id].duplicate(true)

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
