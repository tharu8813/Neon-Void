extends RefCounted
## Data-driven 3-slot active skills + one separate movement-direction dash.
## Bindings/UI and learned-skill persistence belong to A/E.
const Stats = preload("res://scripts/rework/combat/stats.gd")
const FIXED_HZ: float = 60.0
var learned: Dictionary = {}
var slots: Array[String] = ["", "", ""]
var ready_at: Dictionary = {}
var max_mana: float = 100.0
var mana: float = 100.0
var mana_regen: float = 5.0
var dash_ready_at: int = 0

func configure(stats: Dictionary) -> void:
    max_mana = maxf(0.0, float(stats.get("max_mana", 100.0)))
    mana = clampf(float(stats.get("mana", max_mana)), 0.0, max_mana)
    mana_regen = maxf(0.0, float(stats.get("mana_regen", 5.0)))

func learn(definition: Dictionary) -> bool:
    var skill_id: String = str(definition.get("id", ""))
    if skill_id.is_empty():
        return false
    learned[skill_id] = definition.duplicate(true)
    return true

func equip(slot: int, skill_id: String, progression_authorized: bool) -> bool:
    ## A/E MUST enforce hub vs first-time-in-run replacement permission.
    if not progression_authorized or slot < 0 or slot >= 3:
        return false
    if not learned.has(skill_id):
        return false
    for i in range(slots.size()):
        if i != slot and slots[i] == skill_id:
            return false
    slots[slot] = skill_id
    return true

func tick() -> void:
    mana = minf(max_mana, mana + mana_regen / FIXED_HZ)

func cast(slot: int, at_tick: int, haste: int, flags: Dictionary = {}) -> Dictionary:
    if slot < 0 or slot >= 3 or slots[slot].is_empty():
        return {"ok": false, "reason": "empty_slot"}
    var skill_id: String = slots[slot]
    var def: Dictionary = learned[skill_id]
    if bool(flags.get("stopped", false)) or bool(flags.get("silenced", false)):
        return {"ok": false, "reason": "control_blocked"}
    if bool(def.get("is_movement_skill", false)) and bool(flags.get("rooted", false)):
        return {"ok": false, "reason": "rooted"}
    if at_tick < int(ready_at.get(skill_id, 0)):
        return {"ok": false, "reason": "cooldown"}
    var cost: float = maxf(0.0, float(def.get("mana_cost", 0.0)))
    if mana < cost:
        return {"ok": false, "reason": "insufficient_mana"}
    var base_ticks: int = maxi(1, int(def.get("cooldown_ticks", 1)))
    var cd: int = Stats.effective_cooldown_ticks(base_ticks, haste)
    mana -= cost
    ready_at[skill_id] = at_tick + cd
    return {
        "ok": true, "skill_id": skill_id, "slot": slot,
        "mana_spent": cost, "ready_at_tick": ready_at[skill_id],
        "attack": def.get("attack", {}).duplicate(true),
        "status_effects": def.get("status_effects", []).duplicate(true),
        "can_crit": bool(def.get("can_crit", false))
    }

func read_only_state(at_tick: int, haste: int) -> Dictionary:
    var entries: Array[Dictionary] = []
    for slot in range(slots.size()):
        var uid: String = slots[slot]
        var def: Dictionary = learned.get(uid, {})
        var until: int = int(ready_at.get(uid, 0))
        entries.append({"slot": slot, "skill_id": uid,
            "mana_cost": float(def.get("mana_cost", 0.0)),
            "remaining_cooldown_ticks": maxi(0, until - at_tick),
            "cooldown_ticks": Stats.effective_cooldown_ticks(
                int(def.get("cooldown_ticks", 1)), haste) if not uid.is_empty() else 0,
            "ready_at_tick": until,
            "known": not uid.is_empty() and learned.has(uid)})
    return {"slots": entries, "max_mana": max_mana, "mana": mana,
        "mana_regen_per_second": mana_regen,
        "known_skills": learned.keys().duplicate(),
        "dash_remaining_ticks": maxi(0, dash_ready_at - at_tick)}

func dash(move_direction: Vector2, at_tick: int, definition: Dictionary, flags: Dictionary = {}) -> Dictionary:
    ## Dash distance/speed/cooldown are UNCONFIRMED, supplied by A's config.
    ## Nothing in this module binds dash direction to the mouse cursor.
    if bool(flags.get("stopped", false)) or bool(flags.get("rooted", false)):
        return {"ok": false, "reason": "control_blocked"}
    if move_direction.length_squared() <= 0.000001:
        return {"ok": false, "reason": "no_movement_direction"}
    if at_tick < dash_ready_at:
        return {"ok": false, "reason": "cooldown"}
    if not definition.has("distance") or not definition.has("cooldown_ticks"):
        return {"ok": false, "reason": "missing_dash_configuration"}
    var distance: float = float(definition["distance"])
    if distance <= 0.0:
        return {"ok": false, "reason": "invalid_dash_distance"}
    var cooldown: int = maxi(1, int(definition["cooldown_ticks"]))
    dash_ready_at = at_tick + cooldown
    return {"ok": true, "direction": move_direction.normalized(),
        "distance": distance, "ready_at_tick": dash_ready_at}
