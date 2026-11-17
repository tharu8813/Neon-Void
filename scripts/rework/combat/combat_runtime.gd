extends RefCounted
## B isolated deterministic 60 Hz XZ combat runtime. A owns Node3D wiring.
## World movement/barrier resolution is provisional free-plane, not 3D physics.
const Stats = preload("res://scripts/rework/combat/stats.gd")
const Damage = preload("res://scripts/rework/combat/damage.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")
const Events = preload("res://scripts/rework/combat/events.gd")
const Grid = preload("res://scripts/rework/combat/collision_grid.gd")
const AI = preload("res://scripts/rework/combat/enemy_ai.gd")
const Plugins = preload("res://scripts/rework/combat/plugin_effects.gd")
const Slots = preload("res://scripts/rework/skills/skill_slots.gd")
const DT: float = 1.0 / 60.0

var actors: Dictionary = {}
var projectiles: Array[Dictionary] = []
var projectile_pool: Array[Dictionary] = []
var bus = Events.new()
var rng: RandomNumberGenerator = RandomNumberGenerator.new()
var status_rng: RandomNumberGenerator = RandomNumberGenerator.new()
var fixed_tick: int = 0
var next_projectile_id: int = 0
var presentation: Array[Dictionary] = []
var mission_id: String = "simulation"
var settings: Dictionary = {
    "grid_cell_size": 8.0,
    "max_recycled_projectiles": 128,
    "event_work_per_tick": 512,
    "default_projectile_speed": 18.0,
    "projectile_radius_scale": 0.025,
    "default_projectile_lifetime_ticks": 120,
    "vamp_overlap_policy": "unresolved"
}
var counters: Dictionary = {}
var plugin_hit_counts: Dictionary = {}

func _init(seed_value: int = 42, config: Dictionary = {}) -> void:
    begin(seed_value, config)

func begin(seed_value: int, config: Dictionary = {}) -> void:
    actors.clear()
    projectiles.clear()
    projectile_pool.clear()
    fixed_tick = 0
    next_projectile_id = 0
    presentation.clear()
    bus.reset()
    settings.merge(config, true)
    mission_id = str(settings.get("encounter_id", "simulation"))
    rng.seed = seed_value
    status_rng.seed = seed_value ^ 0x4354524c
    plugin_hit_counts.clear()
    counters = {"shots": 0, "collisions": 0, "damage_events": 0,
        "killed": 0, "projectiles_reused": 0, "broadphase_candidates": 0}

func register_actor(actor_id: String, team: String, position: Vector3,
        base_stats: Dictionary = {}, options: Dictionary = {}) -> bool:
    if actor_id.is_empty() or team.is_empty() or actors.has(actor_id):
        return false
    var stats: Dictionary = Stats.make(base_stats)
    stats["health"] = clampf(float(stats.get("health", stats["max_health"])),
        0.0, float(stats["max_health"]))
    var actor: Dictionary = {
        "id": actor_id, "team": team, "position": position, "stats": stats,
        "alive": float(stats["health"]) > 0.0,
        "status_state": {"statuses": [], "tenacity": float(stats.get("tenacity", 0.0)),
            "boss_control_resistance": float(stats.get("boss_control_resistance", 0.0))},
        "radius": maxf(0.01, float(options.get("radius", 0.5))),
        "player_controlled": bool(options.get("player_controlled", false)),
        "attack_definition": options.get("attack_definition", {
            "damage_type": "physical", "base_damage": 0.0, "ad_ratio": 1.0,
            "ap_ratio": 0.0, "can_crit": true, "is_on_hit": true
        }).duplicate(true),
        "projectile_speed": float(options.get("projectile_speed", settings["default_projectile_speed"])),
        "projectile_radius": float(options.get("projectile_radius", -1.0)),
        "projectile_lifetime_ticks": maxi(1, int(options.get("projectile_lifetime_ticks",
            settings["default_projectile_lifetime_ticks"]))),
        "projectile_pierce": maxi(0, int(options.get("projectile_pierce", 0))),
        "preferred_min_range": float(options.get("preferred_min_range", 0.0)),
        "preferred_max_range": float(options.get("preferred_max_range", 8.0)),
        "attack_range": float(options.get("attack_range", 12.0)),
        "fire_credit": 1.0 if float(stats.get("attack_speed", 0.0)) > 0.0 else 0.0,
        "aim": Vector2.RIGHT, "plugins": [],
        "dash_definition": options.get("dash_definition", {}).duplicate(true)
    }
    actors[actor_id] = actor
    var skills = Slots.new()
    skills.configure(stats)
    actor["skills"] = skills
    return true

func remove_actor(actor_id: String) -> void:
    actors.erase(actor_id)

func set_actor_position(actor_id: String, position: Vector3) -> bool:
    if not actors.has(actor_id):
        return false
    actors[actor_id]["position"] = position
    return true

func set_actor_plugins(actor_id: String, instances: Array) -> bool:
    if not actors.has(actor_id):
        return false
    actors[actor_id]["plugins"] = instances.duplicate(true)
    return true

func learn_skill(actor_id: String, definition: Dictionary, slot: int = -1,
        progression_authorized: bool = false) -> bool:
    if not actors.has(actor_id):
        return false
    var skills = actors[actor_id]["skills"]
    if not skills.learn(definition):
        return false
    if slot >= 0:
        return skills.equip(slot, str(definition["id"]), progression_authorized)
    return true

func snapshot(actor_id: String) -> Dictionary:
    if not actors.has(actor_id):
        return {}
    var a: Dictionary = actors[actor_id]
    return {"id": actor_id, "team": a["team"], "alive": a["alive"],
        "position": a["position"], "stats": a["stats"].duplicate(true),
        "status_state": a["status_state"].duplicate(true),
        "mana": a["skills"].mana, "slots": a["skills"].slots.duplicate()}

func projectile_snapshots() -> Array[Dictionary]:
    return projectiles.duplicate(true)

func _ids() -> Array:
    var ids: Array = actors.keys()
    ids.sort()
    return ids

func _closest_opponent(actor_id: String, origin: Vector3 = Vector3.INF,
        ignored_id: String = "") -> String:
    if not actors.has(actor_id):
        return ""
    var actor: Dictionary = actors[actor_id]
    if origin == Vector3.INF:
        origin = actor["position"]
    var best: String = ""
    var distance: float = INF
    for other_id in _ids():
        if other_id == actor_id or other_id == ignored_id:
            continue
        var other: Dictionary = actors[other_id]
        if not bool(other["alive"]) or other["team"] == actor["team"]:
            continue
        var pos: Vector3 = other["position"]
        var squared: float = Vector2(pos.x - origin.x, pos.z - origin.z).length_squared()
        if squared < distance:
            best = str(other_id)
            distance = squared
    return best

func _effective_stats(actor: Dictionary) -> Dictionary:
    var merged: Dictionary = Statuses.modified_stats(actor["stats"], actor["status_state"])
    var bonus: Dictionary = Plugins.passive_offsets(actor["plugins"])
    for key in bonus:
        merged[key] = float(merged.get(key, 0.0)) + float(bonus[key])
    return merged

func _heal(actor_id: String, amount: float) -> float:
    if not actors.has(actor_id) or not bool(actors[actor_id]["alive"]):
        return 0.0
    var stats: Dictionary = actors[actor_id]["stats"]
    var before: float = float(stats["health"])
    stats["health"] = minf(float(stats["max_health"]), before + maxf(0.0, amount))
    return float(stats["health"]) - before

func _aim_at(origin: Vector3, world: Vector3) -> Vector2:
    var toward: Vector2 = Vector2(world.x - origin.x, world.z - origin.z)
    return toward.normalized() if toward.length_squared() > 0.00000001 else Vector2.RIGHT

func _spawn(actor_id: String, direction: Vector2, attack: Dictionary,
        tags: Array = ["basic_attack", "projectile_hit"],
        overrides: Dictionary = {}) -> String:
    if not actors.has(actor_id) or direction.length_squared() <= 0.000001:
        return ""
    var actor: Dictionary = actors[actor_id]
    var stat: Dictionary = _effective_stats(actor)
    var projectile: Dictionary = {}
    if not projectile_pool.is_empty():
        projectile = projectile_pool.pop_back()
        projectile.clear()
        counters["projectiles_reused"] += 1
    next_projectile_id += 1
    var id: String = mission_id + ":p:" + str(next_projectile_id)
    var radius: float = float(actor["projectile_radius"])
    if radius < 0.0:
        radius = maxf(0.01, float(stat.get("projectile_size", 10)) * float(settings["projectile_radius_scale"]))
    var velocity: Vector2 = direction.normalized() * maxf(0.0,
        float(overrides.get("speed", actor["projectile_speed"])) *
        float(stat.get("projectile_speed_mult", 1.0)))
    projectile.merge({
        "id": id, "hit_key": id, "actor_id": actor_id, "team": actor["team"],
        "position": actor["position"], "velocity": velocity,
        "ttl": maxi(1, int(overrides.get("lifetime_ticks", actor["projectile_lifetime_ticks"]))),
        "radius": radius, "remaining_pierce": maxi(0, int(overrides.get("pierce", actor["projectile_pierce"]))),
        "infinite_pierce": bool(Statuses.active_flags(actor["status_state"]).get("infinite_pierce", false)),
        "attack": attack.duplicate(true), "source_tags": tags.duplicate(),
        "allowed_proc_triggers": overrides.get("allowed_proc_triggers",
            ["on_hit", "on_crit"] if tags.has("basic_attack") or tags.has("skill_damage") else []).duplicate(),
        "on_hit_damage_efficiency": float(overrides.get("on_hit_damage_efficiency", 1.0))
    })
    projectiles.append(projectile)
    counters["shots"] += 1
    presentation.append({"kind": "projectile_spawn", "id": id,
        "position": projectile["position"], "velocity": velocity})
    if tags.has("basic_attack"):
        bus.submit_root({"kind": "basic_attack", "trigger_kind": "basic_attack",
            "actor_id": actor_id, "target_id": _closest_opponent(actor_id),
            "attack_id": id, "source_tags": tags.duplicate(),
            "allowed_proc_triggers": ["basic_attack"]})
    return id

func _fire_if_ready(actor_id: String, aim: Vector2) -> void:
    var actor: Dictionary = actors[actor_id]
    var stats: Dictionary = _effective_stats(actor)
    actor["fire_credit"] = float(actor["fire_credit"]) + maxf(0.0,
        float(stats.get("attack_speed", 0.0))) * DT
    if bool(Statuses.active_flags(actor["status_state"]).get("stopped", false)):
        actor["fire_credit"] = minf(float(actor["fire_credit"]), 1.0)
        return
    while float(actor["fire_credit"]) >= 1.0:
        actor["fire_credit"] = float(actor["fire_credit"]) - 1.0
        # Malfunction failure is rolled ONCE per shot attempt, not once per tick.
        if Statuses.action_allowed(actor["status_state"], "attack", status_rng):
            _spawn(actor_id, aim, actor["attack_definition"])

func _move(actor_id: String, direction: Vector2, velocity_scale: float = 1.0) -> void:
    var actor: Dictionary = actors[actor_id]
    if not Statuses.action_allowed(actor["status_state"], "move", status_rng):
        return
    var movement: Vector2 = direction.limit_length(1.0)
    var pace: float = maxf(0.0, float(_effective_stats(actor).get("move_speed", 0.0)))
    var pos: Vector3 = actor["position"]
    actor["position"] = pos + Vector3(movement.x, 0.0, movement.y) * pace * DT * velocity_scale

func _step_actor(actor_id: String, input: Dictionary) -> void:
    var actor: Dictionary = actors[actor_id]
    if not bool(actor["alive"]):
        return
    var direction: Vector2 = Vector2.ZERO
    var aim: Vector2 = actor["aim"]
    var firing: bool = false
    if bool(actor["player_controlled"]):
        direction = input.get("move", Vector2.ZERO)
        aim = _aim_at(actor["position"], input.get("aim_world",
            Vector3(actor["position"]) + Vector3(aim.x, 0.0, aim.y)))
        firing = bool(input.get("fire", true))
    else:
        var opponent: String = _closest_opponent(actor_id)
        if opponent.is_empty():
            return
        var decision: Dictionary = AI.decide(actor, actors[opponent])
        direction = decision["move"]
        aim = decision["aim"]
        firing = bool(decision["shoot"])
    actor["aim"] = aim
    if bool(input.get("dash", false)) and bool(actor["player_controlled"]):
        var flags: Dictionary = Statuses.active_flags(actor["status_state"])
        var dash: Dictionary = actor["skills"].dash(direction, fixed_tick,
            actor["dash_definition"], flags)
        if bool(dash.get("ok", false)):
            var step: Vector2 = dash["direction"]
            var pos: Vector3 = actor["position"]
            actor["position"] = pos + Vector3(step.x, 0.0, step.y) * float(dash["distance"])
            presentation.append({"kind": "dash", "actor_id": actor_id,
                "from": pos, "to": actor["position"]})
            bus.submit_root({"kind": "dash", "trigger_kind": "dash",
                "actor_id": actor_id, "target_id": _closest_opponent(actor_id),
                "source_tags": ["dash"], "allowed_proc_triggers": ["dash"]})
    _move(actor_id, direction)
    if bool(actor["player_controlled"]) and input.has("skill_slot"):
        var control: Dictionary = Statuses.active_flags(actor["status_state"])
        var stats: Dictionary = _effective_stats(actor)
        var cast: Dictionary = actor["skills"].cast(int(input["skill_slot"]), fixed_tick,
            int(stats.get("skill_haste", 0)), control)
        if bool(cast.get("ok", false)):
            presentation.append({"kind": "skill_cast", "actor_id": actor_id,
                "skill_id": cast["skill_id"], "mana": actor["skills"].mana})
            var root: Dictionary = bus.submit_root({"kind": "skill_cast",
                "actor_id": actor_id, "target_id": _closest_opponent(actor_id),
                "trigger_kind": "skill_cast", "source_tags": ["skill_cast"],
                "allowed_proc_triggers": ["skill_cast"]})
            if not root.is_empty():
                pass
            if not cast["attack"].is_empty():
                _spawn(actor_id, aim, cast["attack"],
                    ["skill_damage", "projectile_hit"])
    if firing:
        _fire_if_ready(actor_id, aim)

func _retire(projectile: Dictionary) -> void:
    bus.retire_hit_key(str(projectile["hit_key"]))
    presentation.append({"kind": "projectile_retire", "id": projectile["id"]})
    if projectile_pool.size() < int(settings["max_recycled_projectiles"]):
        projectile_pool.append(projectile)

func _step_projectiles() -> void:
    var grid: Dictionary = Grid.build(actors, float(settings["grid_cell_size"]))
    var remaining: Array[Dictionary] = []
    for p in projectiles:
        var start: Vector3 = p["position"]
        var velocity: Vector2 = p["velocity"]
        var end: Vector3 = start + Vector3(velocity.x, 0.0, velocity.y) * DT
        p["ttl"] = int(p["ttl"]) - 1
        var ended: bool = false
        var candidates: Array[Dictionary] = Grid.segment_hits(grid, actors,
            start, end, float(p["radius"]), str(p["team"]))
        counters["broadphase_candidates"] += candidates.size()
        for candidate in candidates:
            var victim: String = str(candidate["actor_id"])
            var root: Dictionary = bus.submit_root({
                "kind": "damage", "trigger_kind": "on_hit",
                "actor_id": str(p["actor_id"]), "target_id": victim,
                "attack_id": "projectile", "hit_key": p["hit_key"],
                "attack": p["attack"], "source_tags": p["source_tags"],
                "on_hit_damage_efficiency": p["on_hit_damage_efficiency"],
                "allowed_proc_triggers": p["allowed_proc_triggers"]
            })
            if root.is_empty():
                continue
            counters["collisions"] += 1
            if not bool(p["infinite_pierce"]):
                if int(p["remaining_pierce"]) <= 0:
                    ended = true
                    end = start.lerp(end, float(candidate["fraction"]))
                    break
                p["remaining_pierce"] = int(p["remaining_pierce"]) - 1
        p["position"] = end
        if ended or int(p["ttl"]) <= 0:
            _retire(p)
        else:
            remaining.append(p)
    projectiles = remaining

func _plugin_requests(event: Dictionary) -> Array[Dictionary]:
    var actor_id: String = str(event.get("actor_id", ""))
    if not actors.has(actor_id):
        return []
    return Plugins.requests_for(actors[actor_id]["plugins"], event, plugin_hit_counts)

func _select_command_target(actor_id: String, current_id: String,
        command: Dictionary) -> String:
    var policy: String = str(command.get("target_policy", "current"))
    if policy == "nearest_other_enemy":
        var pos: Vector3 = actors[current_id]["position"] if actors.has(current_id) else Vector3.INF
        var chosen: String = _closest_opponent(actor_id, pos, current_id)
        if chosen.is_empty() or not command.has("max_range") or not actors.has(current_id):
            return chosen
        var destination: Vector3 = actors[chosen]["position"]
        var dxz: Vector2 = Vector2(destination.x - pos.x, destination.z - pos.z)
        return chosen if dxz.length() <= maxf(0.0, float(command["max_range"])) else ""
    if policy == "self":
        return actor_id
    return str(command.get("target_id", current_id))

func _resolve_event(event: Dictionary) -> Array:
    var kind: String = str(event.get("kind", ""))
    var actor_id: String = str(event.get("actor_id", ""))
    var target_id: String = str(event.get("target_id", ""))
    var commands: Array = []
    if kind in ["skill_cast", "dash", "periodic", "basic_attack", "kill", "damage_taken"]:
        commands.append_array(_plugin_requests(event))
        return commands
    if kind in ["status", "stat_modifier", "heal", "spawn_projectile"]:
        var command: Dictionary = event.get("command", {})
        target_id = _select_command_target(actor_id, target_id, command)
        if kind == "heal":
            _heal(target_id, float(command.get("amount", 0.0)))
        elif kind == "spawn_projectile":
            if actors.has(actor_id):
                var aim: Vector2 = actors[actor_id]["aim"]
                _spawn(actor_id, aim, command.get("attack", {}),
                    ["plugin", "projectile_hit"], command)
        elif actors.has(target_id):
            var status: Dictionary = command.get("status", {}).duplicate(true)
            if kind == "stat_modifier":
                status = {
                    "id": str(event.get("effect_id", "stat_modifier")),
                    "duration_ticks": int(command.get("duration_ticks", 60)),
                    "stack_mode": str(command.get("stack_mode", "refresh")),
                    "stat_modifiers": command.get("stats", {})
                }
            status["source_id"] = actor_id
            Statuses.apply(actors[target_id]["status_state"], status)
        return []
    if kind != "damage" or not actors.has(actor_id) or not actors.has(target_id):
        return []
    var attacker: Dictionary = actors[actor_id]
    var defender: Dictionary = actors[target_id]
    if not bool(defender["alive"]) or attacker["team"] == defender["team"]:
        return []
    var attack: Dictionary = event.get("attack", {})
    if event.has("command"):
        var command: Dictionary = event["command"]
        target_id = _select_command_target(actor_id, target_id, command)
        if not actors.has(target_id):
            return []
        defender = actors[target_id]
        attack = command.get("attack", {})
        if not bool(defender["alive"]) or attacker["team"] == defender["team"]:
            return []
    var source: Dictionary = _effective_stats(attacker)
    var target: Dictionary = _effective_stats(defender)
    var crit: bool = bool(attack.get("can_crit", false)) and Damage.roll_critical(
        rng, float(source.get("crit_chance", 0.0)))
    if event.has("proc_damage_efficiency"):
        attack = attack.duplicate(true)
        attack["base_damage"] = float(attack.get("base_damage", 0.0)) * maxf(0.0,
            float(event["proc_damage_efficiency"]))
        attack["ad_ratio"] = float(attack.get("ad_ratio", 0.0)) * maxf(0.0,
            float(event["proc_damage_efficiency"]))
        attack["ap_ratio"] = float(attack.get("ap_ratio", 0.0)) * maxf(0.0,
            float(event["proc_damage_efficiency"]))
    var result: Dictionary = Damage.calculate(source, target, attack, crit)
    var flags: Dictionary = Statuses.active_flags(defender["status_state"])
    var dealt: Dictionary = Damage.apply_damage(defender["stats"], result,
        bool(flags.get("invulnerable", false)))
    var actual: float = float(dealt.get("actual_damage", 0.0))
    if actual <= 0.0:
        return []
    counters["damage_events"] += 1
    event["actual_damage"] = actual
    event["target_id"] = target_id
    event["critical"] = crit
    presentation.append({"kind": "hit", "actor_id": actor_id, "target_id": target_id,
        "amount": actual, "damage_type": str(attack.get("damage_type", "physical")),
        "critical": crit, "event_id": event["event_id"]})
    var vamp: Dictionary = Damage.vamp_components(source, attack, actual)
    var omni: float = float(vamp["omni_vamp_heal"])
    var on_hit: float = float(vamp["on_hit_vamp_heal"])
    if omni > 0.0 and on_hit > 0.0:
        var policy: String = str(settings.get("vamp_overlap_policy", "unresolved"))
        if policy == "sum":
            _heal(actor_id, omni + on_hit)
        elif policy == "omni_only":
            _heal(actor_id, omni)
        elif policy == "on_hit_only":
            _heal(actor_id, on_hit)
        else:
            presentation.append({"kind": "vamp_policy_pending", "actor_id": actor_id,
                "omni": omni, "on_hit": on_hit})
    else:
        _heal(actor_id, omni + on_hit)
    for status in attack.get("status_effects", []):
        if status is Dictionary:
            var definition: Dictionary = status.duplicate(true)
            definition["source_id"] = actor_id
            Statuses.apply(defender["status_state"], definition)
    if bool(dealt.get("killed", false)):
        defender["alive"] = false
        counters["killed"] += 1
        presentation.append({"kind": "death", "actor_id": target_id,
            "killer_id": actor_id, "event_id": event["event_id"]})
        bus.submit_root({"kind": "kill", "trigger_kind": "kill",
            "actor_id": actor_id, "target_id": target_id,
            "source_tags": ["kill"], "allowed_proc_triggers": ["kill"]})
    bus.submit_root({"kind": "damage_taken", "trigger_kind": "damage_taken",
        "actor_id": target_id, "target_id": actor_id,
        "source_tags": ["damage_taken"],
        "health_fraction": float(defender["stats"]["health"]) /
            maxf(0.00001, float(defender["stats"]["max_health"])),
        "allowed_proc_triggers": ["damage_taken"]})
    commands.append_array(_plugin_requests(event))
    return commands

func step_fixed(inputs: Dictionary = {}) -> Array[Dictionary]:
    ## A calls exactly once per gameplay tick. inputs keyed by stable actor ID.
    fixed_tick += 1
    bus.set_tick(fixed_tick)
    presentation.clear()
    for actor_id in _ids():
        var actor: Dictionary = actors[actor_id]
        if not bool(actor["alive"]):
            continue
        actor["skills"].tick()
        if not actor["plugins"].is_empty():
            bus.submit_root({"kind": "periodic", "trigger_kind": "periodic",
                "actor_id": str(actor_id), "target_id": str(actor_id),
                "source_tags": ["periodic"], "allowed_proc_triggers": ["periodic"]})
        var status_result: Dictionary = Statuses.tick(actor["status_state"])
        _heal(str(actor_id), float(status_result["healing_request"]))
        for dot in status_result["damage_requests"]:
            var attacker_id: String = str(dot.get("source_id", ""))
            if not actors.has(attacker_id):
                attacker_id = str(actor_id)
            bus.submit_root({"kind": "damage", "trigger_kind": "on_hit",
                "actor_id": attacker_id, "target_id": str(actor_id),
                "attack_id": "status:" + str(dot["status_id"]),
                "attack": dot, "source_tags": ["dot_damage"],
                "allowed_proc_triggers": []})
    for actor_id in _ids():
        _step_actor(str(actor_id), inputs.get(actor_id, {}))
    _step_projectiles()
    var quota: int = maxi(1, int(settings.get("event_work_per_tick", 512)))
    bus.drain_batch(quota, Callable(self, "_resolve_event"))
    return presentation.duplicate(true)
