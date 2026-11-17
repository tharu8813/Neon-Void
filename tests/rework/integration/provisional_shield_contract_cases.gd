extends RefCounted
## A-owned real-B shield acceptance canary for three C proposals.
## Does NOT approve shield stacking, expiry, damage ordering, or balance.
## B/C own the actual implementations; this only verifies observed live HP.

const Runtime = preload("res://scripts/rework/combat/combat_runtime.gd")
const CAdapter = preload("res://scripts/rework/combat/c_effect_adapter.gd")

static func _close(a: float, b: float) -> bool:
    return absf(a - b) <= 0.001

static func run(check: Callable) -> void:
    var candidates: Array[String] = [
        "nv_c_periodic_shield", "nv_c_skill_shield", "nv_r_dash_shield"
    ]
    for uid in candidates:
        var b: Variant = Runtime.new(77, {"encounter_id": "A-shield-" + uid})
        check.call(b.register_actor("VACCINE", "vaccine", Vector3.ZERO,
            {"health": 100.0, "max_health": 100.0, "attack_speed": 0.0}, {
                "player_controlled": true, "dash_definition": {
                    "distance": 2.0, "cooldown_ticks": 60
                }
            }), uid + ": register real B player")
        check.call(b.register_actor("FOE", "enemy", Vector3(3, 0, 0),
            {"health": 100.0, "max_health": 100.0, "attack_speed": 0.0}, {
                "player_controlled": true
            }), uid + ": register hostile attacker")
        var adapter: Variant = CAdapter.new()
        if not adapter.attach(b, "VACCINE", "A-shield-" + uid, 77):
            check.call(false, uid + ": B<->C adapter attached")
            continue
        if not adapter.configure_levels({uid: 1}, 77):
            check.call(false, uid + ": C plugin is valid and configured")
            continue
        var input: Dictionary = {"fire": false, "move": Vector2.ZERO}
        if uid == "nv_c_periodic_shield":
            # Cross the real 1800-tick proposal boundary without simulating
            # unrelated 30 seconds of idle scene frames in a 25s CI budget.
            b.fixed_tick = 1799
            b.bus.set_tick(1799)
        elif uid == "nv_c_skill_shield":
            check.call(b.learn_skill("VACCINE", {
                "id": "shield-contract", "mana_cost": 5.0,
                "cooldown_ticks": 60, "attack": {}
            }, 0, true), uid + ": equip authorized B active skill")
            input["skill_slot"] = 0
        else:
            input["move"] = Vector2.RIGHT
            input["dash"] = true
        b.step_fixed({"VACCINE": input, "FOE": {"fire": false}})
        adapter.after_fixed_step(b.last_processed_events)
        var shield: float = adapter.shield_value()
        check.call(shield > 0.0 and shield < 100.0, uid +
            ": real B skill/dash/tick event produced C live shield pool")
        if shield <= 0.0:
            continue
        var damage: float = shield + 5.0
        b.bus.submit_root({
            "kind": "damage", "trigger_kind": "on_hit",
            "actor_id": "FOE", "target_id": "VACCINE",
            "source_tags": ["hostile_attack"], "allowed_proc_triggers": [],
            "attack": {
                "damage_type": "true", "base_damage": damage,
                "ad_ratio": 0.0, "ap_ratio": 0.0,
                "is_on_hit": true, "can_crit": false
            }
        })
        b.step_fixed({
            "VACCINE": {"fire": false, "move": Vector2.ZERO},
            "FOE": {"fire": false}
        })
        adapter.after_fixed_step(b.last_processed_events)
        var hp: float = float(b.snapshot("VACCINE")["stats"]["health"])
        check.call(_close(hp, 95.0) and _close(adapter.shield_value(), 0.0),
            uid + ": B real true-damage HP is shield-first, never heal-after-hit")
        b.bus.submit_root({
            "kind": "damage", "trigger_kind": "on_hit",
            "actor_id": "FOE", "target_id": "VACCINE",
            "source_tags": ["hostile_attack"], "allowed_proc_triggers": [],
            "attack": {"damage_type": "true", "base_damage": 8.0,
                "ad_ratio": 0.0, "ap_ratio": 0.0,
                "is_on_hit": true, "can_crit": false}
        })
        b.step_fixed({
            "VACCINE": {"fire": false, "move": Vector2.ZERO},
            "FOE": {"fire": false}
        })
        adapter.after_fixed_step(b.last_processed_events)
        check.call(_close(float(b.snapshot("VACCINE")["stats"]["health"]), 87.0),
            uid + ": a depleted shield cannot be re-applied on next hit")
