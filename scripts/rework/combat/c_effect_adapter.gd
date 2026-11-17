extends RefCounted
## B-owned adapter for C's 27 real effect recipes. C owns effect definitions,
## cooldowns and reward rules. B owns live HP/status/projectile truth.
## No C catalog copies, no save writes, no Godot scene dependencies.
const EFFECT_PATH := "res://scripts/rework/plugins/plugin_effect_runtime.gd"
const SINK_PATH := "res://scripts/rework/plugins/plugin_b_combat_sink.gd"
const Damage = preload("res://scripts/rework/combat/damage.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")

var combat: Variant = null
var c_engine: Variant = null
var c_sink: Script = null
var player_id: String = "VACCINE"
var run_id: String = ""
var enabled: bool = false
var fault: String = ""
var pending: Array[Dictionary] = []
var next_event_number: int = 0
var notices: Array[Dictionary] = []
var generated_in_last_step: int = 0
var maximum_events_per_tick: int = 512
var _auxiliary: Dictionary = {}
var _context: Dictionary = {}

func attach(runtime: Variant, actor_id: String, encounter_id: String,
        seed: int = 0) -> bool:
    if runtime == null or actor_id.is_empty():
        return false
    combat = runtime
    player_id = actor_id
    run_id = encounter_id
    pending.clear()
    notices.clear()
    next_event_number = 0
    fault = ""
    enabled = false
    _auxiliary = {"buffs": [], "actor_shield": 0.0,
        "basic_attack_blocked": false, "boomerang_outstanding": 0}
    if not ResourceLoader.exists(EFFECT_PATH) or not ResourceLoader.exists(SINK_PATH):
        fault = "C_effect_runtime_not_installed"
        return false
    var api: Script = load(EFFECT_PATH)
    c_sink = load(SINK_PATH)
    c_engine = api.new()
    if not c_engine.configure({}, seed):
        fault = "C_effect_runtime_invalid"
        return false
    combat.stat_provider = Callable(self, "_derive_actor_stats")
    combat.attack_provider = Callable(self, "_project_attack")
    combat.damage_absorber = Callable(self, "_absorb_shield")
    combat.fire_gate_provider = Callable(self, "_can_fire")
    enabled = true
    return true

func configure_levels(levels: Dictionary, seed: int) -> bool:
    if not enabled or c_engine == null:
        return false
    if not c_engine.configure(levels, seed):
        return false
    pending.clear()
    _auxiliary = {"buffs": [], "actor_shield": 0.0,
        "basic_attack_blocked": false, "boomerang_outstanding": 0}
    return true

func upgrade_plugin(plugin_id: String, level: int) -> bool:
    ## Call only after A/E confirmed reward and durable receipt.
    return enabled and c_engine != null and c_engine.set_plugin_level(plugin_id, level)

func active_levels() -> Dictionary:
    return c_engine.levels.duplicate(true) if enabled else {}

func stats_for_player() -> Dictionary:
    if combat == null or not combat.actors.has(player_id):
        return {}
    return combat._effective_stats(combat.actors[player_id])

func _derive_actor_stats(actor_id: String, base: Dictionary, tick: int) -> Dictionary:
    if not enabled or actor_id != player_id:
        return base
    var with_plugins: Dictionary = c_engine.derived_stats(base, tick)
    var short_buffs: Array = _auxiliary.get("buffs", [])
    for buff in short_buffs:
        if tick < int(buff.get("start_tick", 0)) or tick >= int(buff.get("expires_tick", 0)):
            continue
        if str(buff.get("stat", "")) == "move_speed":
            with_plugins["move_speed"] = float(with_plugins.get("move_speed", 0.0)) + float(buff.get("value", 0.0))
    return with_plugins

func _project_attack(actor_id: String, target_id: String, attack: Dictionary,
        _event: Dictionary) -> Dictionary:
    if not enabled or actor_id != player_id or not combat.actors.has(target_id):
        return attack
    var health: Dictionary = combat.actors[target_id]["stats"]
    var denominator: float = maxf(0.001, float(health.get("max_health", 1.0)))
    return c_engine.derived_attack(attack, {
        "target_health_ratio": float(health.get("health", 0.0)) / denominator
    })

func _absorb_shield(target_id: String, calculation: Dictionary) -> Dictionary:
    ## Shield-first is an explicitly provisional rule until A/GDD approves.
    if not enabled or target_id != player_id or combat == null:
        return calculation
    if bool(Statuses.active_flags(combat.actors[player_id]["status_state"]).get("invulnerable", false)):
        return calculation
    var stored: float = maxf(0.0, float(_auxiliary.get("actor_shield", 0.0)))
    var proposed: float = maxf(0.0, float(calculation.get("calculated_damage", 0.0)))
    var absorbed: float = minf(stored, proposed)
    if absorbed > 0.0:
        _auxiliary["actor_shield"] = stored - absorbed
        calculation["calculated_damage"] = proposed - absorbed
        notices.append({"kind": "shield_absorbed", "actor_id": target_id, "amount": absorbed})
    return calculation

func _can_fire(actor_id: String) -> bool:
    return actor_id != player_id or not bool(_auxiliary.get("basic_attack_blocked", false))

func _next_id(kind: String, tick: int) -> String:
    next_event_number += 1
    return "%s:C:%d:%d:%s" % [run_id, tick, next_event_number, kind]

func _enqueue(kind: String, source: Dictionary) -> void:
    var actor_id: String = str(source.get("actor_id", ""))
    if actor_id != player_id:
        return
    var tick: int = int(source.get("tick", combat.fixed_tick))
    var normalized: Dictionary = source.duplicate(true)
    normalized["event_id"] = _next_id(kind, tick)
    normalized["kind"] = kind
    normalized["tick"] = tick
    normalized["actor_id"] = actor_id
    normalized["source_tags"] = source.get("source_tags", []).duplicate()
    normalized["allowed_proc_triggers"] = source.get("allowed_proc_triggers", []).duplicate()
    normalized["proc_path"] = source.get("proc_path", []).duplicate()
    var actor_stats: Dictionary = combat.actors[player_id]["stats"]
    normalized["actor_health_ratio"] = float(actor_stats.get("health", 0.0)) / maxf(
        0.001, float(actor_stats.get("max_health", 1.0)))
    var target_id: String = str(normalized.get("target_id", ""))
    if combat.actors.has(target_id):
        var target_stats: Dictionary = combat.actors[target_id]["stats"]
        normalized["target_health_ratio"] = float(target_stats.get("health", 0.0)) / maxf(
            0.001, float(target_stats.get("max_health", 1.0)))
    pending.append(normalized)

func _accept_combat_event(event: Dictionary) -> void:
    var kind: String = str(event.get("kind", ""))
    var from_actor: String = str(event.get("actor_id", ""))
    if kind == "damage":
        if float(event.get("actual_damage", 0.0)) <= 0.0:
            return
        if from_actor == player_id:
            if not bool(event.get("is_dot", false)):
                _enqueue("on_hit", event)
                if bool(event.get("critical", false)):
                    _enqueue("on_crit", event)
                if not event.get("proc_path", []).is_empty():
                    _enqueue("on_hit_proc", event)
                if event.get("source_tags", []).has("skill_damage"):
                    _enqueue("skill_damage", event)
            _enqueue("damage_dealt", event)
    elif kind == "damage_taken" and from_actor == player_id:
        var after_hit: Dictionary = event.duplicate(true)
        after_hit["actor_health_ratio"] = float(event.get("health_fraction", 1.0))
        _enqueue("damage_taken", after_hit)
    elif kind in ["basic_attack", "skill_cast", "dash", "kill"] and from_actor == player_id:
        _enqueue(kind, event)

func _world() -> Dictionary:
    var targets: Dictionary = {}
    var positions: Dictionary = {}
    var status_map: Dictionary = {}
    for id in combat.actors.keys():
        # Include the just-killed impact target for C AoE origin and on-kill;
        # never count dead actors as newly defeated on sink publication.
        if str(id) == player_id or (
                not bool(combat.actors[id]["alive"]) and
                str(id) != str(_context.get("target_id", ""))):
            continue
        var a: Dictionary = combat.actors[id]
        targets[str(id)] = a["stats"].duplicate(true)
        var pos: Vector3 = a["position"]
        positions[str(id)] = Vector2(pos.x, pos.z)
        status_map[str(id)] = a["status_state"].duplicate(true)
    return {
        "actor_stats": stats_for_player(), "targets": targets,
        "target_statuses": status_map, "target_positions": positions,
        "buffs": _auxiliary.get("buffs", []).duplicate(true),
        "actor_shield": float(_auxiliary.get("actor_shield", 0.0)),
        "basic_attack_blocked": bool(_auxiliary.get("basic_attack_blocked", false)),
        "boomerang_outstanding": int(_auxiliary.get("boomerang_outstanding", 0)),
        "projectiles": [], "projectile_transforms": [],
        "generated_events": [], "knockbacks": [], "pending_progression_requests": []
    }

func _validate_commands(commands: Array) -> Dictionary:
    var transform_shapes: Dictionary = {}
    for command in commands:
        if not (command is Dictionary):
            return {"ok": false, "reason": "invalid_C_command"}
        var kind: String = str(command.get("type", ""))
        if kind == "rare_reward_request":
            # Only A/E may issue durable reward receipts.
            return {"ok": false, "reason": "E_receipt_callback_required"}
        if kind == "projectiles" and int(command.get("count", 0)) > 128:
            return {"ok": false, "reason": "invalid_projectile_count"}
        if kind == "projectile_transform":
            var shape: String = str(command.get("shape", ""))
            if not shape in ["laser", "boomerang"]:
                return {"ok": false, "reason": "unsupported_projectile_transform"}
            transform_shapes[shape] = true
    if transform_shapes.size() > 1:
        # The GDD leaves simultaneous laser+boomerang ordering unresolved.
        return {"ok": false, "reason": "conflicting_projectile_transforms_need_approval"}
    return {"ok": true}

func _authoritative_sink(commands: Array, world: Dictionary) -> Dictionary:
    var validation: Dictionary = _validate_commands(commands)
    if not bool(validation["ok"]):
        return validation
    var next: Dictionary = world.duplicate(true)
    var executable: Array = []
    for command in commands:
        if str(command.get("type", "")) == "damage" and str(command.get("shape", "")) == "lightning":
            # C's standalone sink rejects a one-target map. An empty chain is
            # an acknowledged no-op, never a fabricated adjacent victim.
            var impact_id: String = str(command.get("target_id", ""))
            var positions: Dictionary = next.get("target_positions", {})
            var origin: Variant = positions.get(impact_id, null)
            var other_found: bool = false
            if origin is Vector2:
                var reach: float = float(command.get("search_distance_mult", 1.0)) * float(next.get("lightning_base_reach", 8.0))
                for other_id in next.get("targets", {}).keys():
                    if str(other_id) == impact_id:
                        continue
                    var other_position: Variant = positions.get(other_id, null)
                    if other_position is Vector2 and origin.distance_to(other_position) <= reach and float(next["targets"][other_id].get("health", 0.0)) > 0.0:
                        other_found = true
                        break
            if not other_found:
                continue
        executable.append(command)
    if executable.is_empty():
        return {"ok": true, "commands_applied": 0}
    var applied: Dictionary = c_sink.apply(executable, next)
    if not bool(applied.get("ok", false)):
        return applied
    # Preflight target updates before publishing a single change.
    for id in next.get("targets", {}).keys():
        if not combat.actors.has(str(id)):
            return {"ok": false, "reason": "C_unknown_combat_target"}
    var source_event: Dictionary = _context
    for id in next["targets"].keys():
        var target_id: String = str(id)
        var a: Dictionary = combat.actors[target_id]
        var old: float = float(a["stats"]["health"])
        var current: float = maxf(0.0, float(next["targets"][id].get("health", old)))
        a["stats"]["health"] = current
        if next["target_statuses"].has(id):
            a["status_state"] = next["target_statuses"][id].duplicate(true)
        if current < old:
            var dealt: float = old - current
            notices.append({"kind": "hit", "actor_id": player_id,
                "target_id": target_id, "amount": dealt, "damage_type": "plugin",
                "critical": false, "event_id": str(source_event.get("event_id", ""))})
            if current <= 0.0 and bool(a["alive"]):
                a["alive"] = false
                combat.counters["killed"] += 1
                notices.append({"kind": "death", "actor_id": target_id,
                    "killer_id": player_id, "event_id": str(source_event.get("event_id", ""))})
                _enqueue("kill", {
                    "actor_id": player_id, "target_id": target_id,
                    "source_tags": ["plugin_attack"], "tick": combat.fixed_tick,
                    "proc_path": source_event.get("proc_path", []),
                    "allowed_proc_triggers": ["on_hit"]
                })
    _auxiliary["buffs"] = next.get("buffs", []).duplicate(true)
    _auxiliary["actor_shield"] = float(next.get("actor_shield", 0.0))
    _auxiliary["basic_attack_blocked"] = bool(next.get("basic_attack_blocked", false))
    _auxiliary["boomerang_outstanding"] = int(next.get("boomerang_outstanding", 0))
    for produced in next.get("generated_events", []):
        if float(produced.get("actual_damage", 0.0)) > 0.0:
            _enqueue("on_hit_proc", produced)
    for requested in next.get("projectiles", []):
        _spawn_c_projectile(requested, source_event)
    for changed in next.get("projectile_transforms", []):
        _transform_basic_projectile(changed, source_event)
    for knock in next.get("knockbacks", []):
        notices.append({"kind": "knockback_requested", "command": knock.duplicate(true)})
    # C has already applied the damage via B APIs to the copied world.
    # Do NOT feed the same damage through B's event queue a second time.
    world.clear()
    world.merge(next, true)
    return {"ok": true, "commands_applied": commands.size()}

func _spawn_c_projectile(command: Dictionary, parent: Dictionary) -> void:
    if not combat.actors.has(player_id):
        return
    var aim: Vector2 = combat.actors[player_id]["aim"]
    var target_id: String = str(command.get("target_id", parent.get("target_id", "")))
    if combat.actors.has(target_id):
        var origin: Vector3 = combat.actors[player_id]["position"]
        var target: Vector3 = combat.actors[target_id]["position"]
        aim = Vector2(target.x - origin.x, target.z - origin.z).normalized()
    var ancestry: Array = parent.get("proc_path", []).duplicate()
    var signature: String = str(command.get("plugin_id", "")) + "|" + player_id + "|" + target_id
    if not ancestry.has(signature):
        ancestry.append(signature)
    var spread: float = float(command.get("spread_radians", 0.0))
    var count: int = maxi(0, int(command.get("count", 1)))
    var n: int = int(command.get("projectile_index", 0))
    var launch_dir: Vector2 = aim.rotated((float(n) - float(maxi(1, count) - 1) * 0.5) * spread)
    var opts: Dictionary = {
        "proc_path": ancestry,
        "allowed_proc_triggers": command.get("allowed_proc_triggers", []).duplicate(),
        "on_hit_damage_efficiency": float(command.get("on_hit_efficiency", 1.0))
    }
    var tags: Array = command.get("source_tags", ["plugin_attack"]).duplicate()
    if not tags.has("plugin_attack"):
        tags.append("plugin_attack")
    var attack: Dictionary = {
        "damage_type": "physical",
        "base_damage": float(command.get("base_damage", 0.0)),
        "ad_ratio": 0.0, "ap_ratio": 0.0, "can_crit": false,
        "is_on_hit": true
    }
    var pid: String = combat._spawn(player_id, launch_dir, attack, tags, opts)
    if not pid.is_empty():
        notices.append({"kind": "projectile_spawn",
            "id": pid, "position": combat.projectiles.back()["position"],
            "velocity": combat.projectiles.back()["velocity"],
            "source_tags": tags.duplicate()})
        var bullet: Dictionary = combat.projectiles.back()
        if str(command.get("shape", "")) == "homing":
            bullet["homing"] = true
            bullet["turn_rate_radians_per_second"] = maxf(0.0,
                float(command.get("turn_rate", 0.0)))

func _transform_basic_projectile(command: Dictionary, parent: Dictionary) -> void:
    var attack_id: String = str(parent.get("attack_id", ""))
    if attack_id.is_empty():
        return
    for p in combat.projectiles:
        if str(p.get("id", "")) != attack_id:
            continue
        if str(command.get("shape", "")) == "laser":
            p["infinite_pierce"] = bool(command.get("penetrating", false))
            p["radius"] = float(p["radius"]) * maxf(1.0,
                float(command.get("beam_width_mult", 1.0)))
            p["on_hit_damage_efficiency"] = float(command.get("on_hit_efficiency", 1.0))
            p["shape"] = "laser"
        elif str(command.get("shape", "")) == "boomerang":
            p["shape"] = "boomerang"
            p["return_threshold"] = maxi(1, int(p["ttl"]) / 2)
            var total: int = maxi(1, int(command.get("count", 1)))
            var ancestor: Array = parent.get("proc_path", []).duplicate()
            var signature: String = str(command.get("plugin_id", "")) + "|" + player_id + "|" + str(parent.get("target_id", ""))
            if not ancestor.has(signature):
                ancestor.append(signature)
            for index in range(1, total):
                var angle: float = float(index) * TAU / float(total)
                var new_id: String = combat._spawn(player_id,
                    p["velocity"].normalized().rotated(angle), {
                        "damage_type": "physical",
                        "base_damage": float(command.get("base_damage", 0.0)),
                        "ad_ratio": 0.0, "can_crit": false, "is_on_hit": true
                    }, ["plugin_attack", "derived_attack"], {
                        "proc_path": ancestor,
                        "allowed_proc_triggers": [],
                        "lifetime_ticks": int(p["ttl"])
                    })
                if not new_id.is_empty():
                    var extra: Dictionary = combat.projectiles.back()
                    extra["shape"] = "boomerang"
                    extra["return_threshold"] = maxi(1, int(extra["ttl"]) / 2)
                    notices.append({"kind": "projectile_spawn",
                        "id": new_id, "position": extra["position"],
                        "velocity": extra["velocity"]})
        break

func after_fixed_step(raw_events: Array[Dictionary]) -> Array[Dictionary]:
    # Caller clears notices BEFORE simulation.step_fixed; callbacks in B's
    # damage calculation may already have reported shield absorption.
    generated_in_last_step = 0
    if not enabled or not fault.is_empty():
        return []
    var living_buffs: Array = []
    for buff in _auxiliary.get("buffs", []):
        if combat.fixed_tick < int(buff.get("expires_tick", 0)):
            living_buffs.append(buff)
    _auxiliary["buffs"] = living_buffs
    for event in raw_events:
        _accept_combat_event(event)
    _enqueue("tick", {
        "actor_id": player_id, "tick": combat.fixed_tick,
        "target_id": player_id, "source_tags": ["tick"],
        "allowed_proc_triggers": []})
    var quota: int = maxi(1, maximum_events_per_tick)
    while not pending.is_empty() and generated_in_last_step < quota:
        var event: Dictionary = pending.pop_front()
        _context = event.duplicate(true)
        var world: Dictionary = _world()
        var result: Dictionary = c_engine.process(event, world,
            Callable(self, "_authoritative_sink"))
        if not bool(result.get("ok", false)):
            fault = str(result.get("reason", "C_unknown_effect_failure"))
            notices.append({"kind": "plugin_error", "reason": fault,
                "event_id": str(event.get("event_id", ""))})
            pending.push_front(event)
            break
        generated_in_last_step += 1
    return notices.duplicate(true)

func outstanding_events() -> int:
    return pending.size()

func shield_value() -> float:
    return maxf(0.0, float(_auxiliary.get("actor_shield", 0.0)))

func handle_boomerang_return(projectile: Dictionary) -> void:
    if not enabled or str(projectile.get("shape", "")) != "boomerang":
        return
    var outstanding: int = int(_auxiliary.get("boomerang_outstanding", 0))
    if outstanding > 0:
        _auxiliary["boomerang_outstanding"] = outstanding - 1
        _auxiliary["basic_attack_blocked"] = outstanding - 1 > 0
