extends RefCounted
## P4 fixed-tick headless combat core. All positions and radii are legacy XY pixels,
## mapped to XZ only by a renderer. No Godot rigidbody collision or wall timing.
signal wave_ready(boss_defeated: bool)
signal enemy_died(enemy_id: int, kind: String)
signal player_hit(damage: float, infection: float)

const Rules = preload("res://scripts/combat/combat_rules.gd")
const Catalog = preload("res://scripts/enemies/enemy_catalog.gd")
const Brain = preload("res://scripts/enemies/enemy_brain.gd")
const DT: float = 1.0 / 60.0
const ARENA: Vector2 = Vector2(1280.0, 720.0)

var rng: RandomNumberGenerator = RandomNumberGenerator.new()
var player: Dictionary = {}
var enemies: Array[Dictionary] = []
var bullets: Array[Dictionary] = []
var enemy_bullets: Array[Dictionary] = []
var spawn_queue: Array[Dictionary] = []
var telegraphs: Array[Dictionary] = []
var events: Array[Dictionary] = []
var wave: int = 1
var active: bool = false
var paused: bool = false
var external_invulnerability_clock: bool = false
var boss_kind: String = ""
var boss_defeated: bool = false
var wave_ready_sent: bool = false
var wave_empty_time: float = 0.0
var total_replications: int = 0
var next_enemy_id: int = 1
var frames: int = 0

func _init(seed_value: int = 42) -> void:
    rng.seed = seed_value
    player = {
        "pos": Vector2(640, 360), "radius": 13.0,
        "hp": 100.0, "max_hp": 100.0, "mana": 100.0, "max_mana": 100.0,
        "aim": -PI * 0.5, "fire_cd": 0.0, "pulse_cd": 0.0, "guard_cd": 0.0,
        "guard": 0.0, "inv": 0.0, "infection": 0.0,
        "stats": {"rate": 5.4, "dmg": 10.0, "bspeed": 880.0,
        "pierce": 0, "split": 0, "homing": 0.0, "explode": 0.0,
        "ricochet": 0, "crit": 0.05, "chain": 0, "rear": 0,
        "armor": 4.0, "armor_pen": 0.0, "mana_regen": 6.0,
        "hp_regen": 0.0, "burn_chance": 0.0, "burn_dps": 0.0,
        "toxin_chance": 0.0, "toxin_dps": 0.0, "life_on_hit": 0.0,
        "lifesteal": 0.0, "slow": 0.0, "thorns": 0.0,
        "skill_power": 1.0, "skill_cooldown": 1.0,
        "skill_efficiency": 1.0, "effect_duration": 1.0}
    }

func set_player_snapshot(snapshot: Dictionary) -> void:
    ## B bridge: pass logical pos, aim, inv, HP, mana, stats; missing keys unchanged.
    ## Stats are merged, not replaced. Call once before step_fixed().
    for key in snapshot:
        if key == "stats" and snapshot[key] is Dictionary:
            var new_stats: Dictionary = player["stats"]
            new_stats.merge(snapshot[key], true)
        elif player.has(key):
            player[key] = snapshot[key]

func get_player_snapshot() -> Dictionary:
    return player.duplicate(true)

func begin_wave(wave_index: int, schedule: Array, expected_boss: String = "") -> void:
    assert(wave_index >= 1)
    wave = wave_index
    # Wave 5 must never auto-clear if the caller omits its boss metadata.
    boss_kind = expected_boss if not expected_boss.is_empty() else ("__missing_boss__" if wave_index == 5 else "")
    enemies.clear()
    bullets.clear()
    enemy_bullets.clear()
    spawn_queue.clear()
    telegraphs.clear()
    events.clear()
    next_enemy_id = 1
    frames = 0
    total_replications = 0
    boss_defeated = false
    wave_ready_sent = false
    wave_empty_time = 0.0
    active = true
    for entry in schedule:
        ## Entry is {"at": seconds, "kind": String}. Caller preserves ordering.
        spawn_queue.append({"at": float(entry["at"]), "kind": str(entry["kind"])})

func abort_wave() -> void:
    active = false
    enemies.clear()
    bullets.clear()
    enemy_bullets.clear()
    spawn_queue.clear()
    telegraphs.clear()
    wave_empty_time = 0.0

func spawn_enemy(kind: String, position: Vector2 = Vector2.INF) -> int:
    if enemies.size() >= Catalog.MAX_LIVE_ENEMIES:
        return -1
    if not Catalog.STATS.has(kind):
        return -1
    if not position.is_finite():
        position = _spawn_point()
    var id: int = next_enemy_id
    next_enemy_id += 1
    var enemy: Dictionary = Catalog.make_enemy(kind, id, position, wave, rng)
    if Catalog.is_boss(kind):
        enemy["pos"] = Vector2(640.0, -80.0)
    enemies.append(enemy)
    events.append({"type": "spawn", "id": id, "kind": kind})
    return id

func _spawn_point() -> Vector2:
    var side: int = rng.randi_range(0, 3)
    if side == 0:
        return Vector2(rng.randf_range(60.0, 1220.0), -50.0)
    if side == 1:
        return Vector2(rng.randf_range(60.0, 1220.0), 770.0)
    if side == 2:
        return Vector2(-50.0, rng.randf_range(60.0, 660.0))
    return Vector2(1330.0, rng.randf_range(60.0, 660.0))

func step_fixed() -> void:
    ## Must be called at exactly one 1/60 simulated second, not render _process.
    if not active or paused:
        return
    frames += 1
    # With B player attached, its own P3 simulation already decrements inv.
    if not external_invulnerability_clock:
        player["inv"] = maxf(0.0, float(player["inv"]) - DT)
    player["pulse_cd"] = maxf(0.0, float(player["pulse_cd"]) - DT)
    player["guard_cd"] = maxf(0.0, float(player["guard_cd"]) - DT)
    player["guard"] = maxf(0.0, float(player["guard"]) - DT)
    var stats: Dictionary = player["stats"]
    player["mana"] = minf(float(player["max_mana"]), float(player["mana"]) + float(stats["mana_regen"]) * DT)
    var regeneration: float = float(stats["hp_regen"]) + (1.8 if float(player["guard"]) > 0 else 0.0)
    player["hp"] = minf(float(player["max_hp"]), float(player["hp"]) + regeneration * DT)
    _step_statuses()
    _step_spawns()
    _step_enemies()
    _step_telegraphs()
    _auto_fire()
    _step_bullets()
    _step_contacts()
    if spawn_queue.is_empty() and enemies.is_empty():
        wave_empty_time += DT
        if wave_empty_time > 0.75 and not wave_ready_sent:
            if boss_kind.is_empty() or boss_defeated:
                wave_ready_sent = true
                events.append({"type": "wave_ready", "boss_defeated": boss_defeated})
                wave_ready.emit(boss_defeated)
    else:
        wave_empty_time = 0.0

func _step_spawns() -> void:
    var remaining: Array[Dictionary] = []
    for entry in spawn_queue:
        entry["at"] = float(entry["at"]) - DT
        if float(entry["at"]) <= 0 and enemies.size() < Catalog.MAX_LIVE_ENEMIES:
            spawn_enemy(str(entry["kind"]))
        else:
            remaining.append(entry)
    spawn_queue = remaining

func _step_enemies() -> void:
    for enemy in enemies.duplicate():
        if not enemies.has(enemy) or float(enemy["hp"]) <= 0:
            continue
        var actions: Array = Brain.advance(enemy, player["pos"], DT, rng)
        for action in actions:
            if action["type"] == "virus":
                _plan_virus_action(enemy, action)
            else:
                _execute_action(enemy, action)

func _plan_virus_action(enemy: Dictionary, action: Dictionary) -> void:
    var kind: String = str(action["clone_kind"])
    if not kind.is_empty():
        var request: int = mini(int(action["clones_per_cast"]), int(action["clone_source_cap"]) - int(enemy["clone_count"]))
        request = mini(request, Catalog.MAX_REPLICATIONS - total_replications)
        request = mini(request, Catalog.MAX_LIVE_ENEMIES - enemies.size())
        if request <= 0:
            return
        enemy["clone_count"] = int(enemy["clone_count"]) + request
        total_replications += request
        action["count"] = request
    if float(action["telegraph"]) > 0.0:
        if telegraphs.size() < 96:
            telegraphs.append({"remaining": float(action["telegraph"]), "source_id": enemy["id"], "action": action})
            events.append({"type": "telegraph", "id": enemy["id"], "behavior": action["behavior"]})
    else:
        _execute_action(enemy, action)

func _step_telegraphs() -> void:
    var remains: Array[Dictionary] = []
    for pending in telegraphs:
        var owner: Dictionary = get_enemy(int(pending["source_id"]))
        if owner.is_empty():
            continue
        pending["remaining"] = float(pending["remaining"]) - DT
        if float(pending["remaining"]) <= 0.0:
            _execute_action(owner, pending["action"])
        else:
            remains.append(pending)
    telegraphs = remains

func _execute_action(enemy: Dictionary, action: Dictionary) -> void:
    if float(enemy["hp"]) <= 0:
        return
    var kind: String = str(action.get("type", ""))
    if kind == "virus":
        kind = str(action["behavior"])
    if kind == "replicate":
        for n in range(int(action["count"])):
            if enemies.size() >= Catalog.MAX_LIVE_ENEMIES:
                break
            var offset: Vector2 = Vector2(rng.randf_range(-24.0, 24.0), rng.randf_range(-24.0, 24.0))
            var id: int = spawn_enemy(str(action["clone_kind"]), enemy["pos"] + offset)
            var clone: Dictionary = get_enemy(id)
            if not clone.is_empty():
                clone["hp"] = float(clone["max_hp"]) * 0.72
    elif kind == "armor_pulse":
        enemy["guard"] = 2.0
    elif kind == "telegraphed_charge":
        var direction: Vector2 = (Vector2(player["pos"]) - Vector2(enemy["pos"])).normalized()
        enemy["vel"] = direction * float(action["power"]) * 2.5
    elif kind == "encryption_pulse":
        if Vector2(enemy["pos"]).distance_to(player["pos"]) < Catalog.PULSE_RADIUS:
            hurt_player(float(action["power"]), float(action["pen"]), float(action["infection"]))
        if str(enemy["kind"]) in ["ransomware", "encryptor"]:
            enemy["vulnerable"] = 2.25
    elif kind in ["aim", "feint_volley", "armor_piercing_volley", "quick_volley"]:
        _fire_enemy_aim(enemy, int(action.get("count", 1)), float(action.get("spread", 0.15)), float(action.get("speed", action.get("power", 330.0))), float(action.get("pen", 0.0)), float(action.get("infection", 0.0)))
    elif kind in ["radial", "overload_barrage", "breach_barrage"]:
        _fire_enemy_radial(enemy, int(action.get("count", 10)), float(action.get("speed", action.get("power", 250.0))), float(action.get("offset", float(enemy["elapsed"]) * 0.55)), float(action.get("pen", 0.0)), float(action.get("infection", 0.0)))

func _fire_enemy_aim(enemy: Dictionary, count: int, spread: float, speed: float, penetration: float, infection: float) -> void:
    var origin: Vector2 = enemy["pos"]
    var base: float = (Vector2(player["pos"]) - origin).angle()
    for i in range(count):
        if enemy_bullets.size() >= Catalog.MAX_ENEMY_BULLETS:
            break
        var angle: float = base + (float(i) - (float(count) - 1.0) / 2.0) * spread
        _enemy_bullet(origin + Vector2.RIGHT.rotated(angle) * (float(enemy["radius"]) + 2.0), angle, speed, 9.0, 4.2, 6.0, penetration, infection)

func _fire_enemy_radial(enemy: Dictionary, count: int, speed: float, offset: float, penetration: float, infection: float) -> void:
    var origin: Vector2 = enemy["pos"]
    for i in range(count):
        if enemy_bullets.size() >= Catalog.MAX_ENEMY_BULLETS:
            break
        var angle: float = offset + TAU * float(i) / float(count)
        _enemy_bullet(origin + Vector2.RIGHT.rotated(angle) * (float(enemy["radius"]) + 4.0), angle, speed, 10.0, 4.6, 7.0, penetration, infection)

func _enemy_bullet(origin: Vector2, angle: float, speed: float, dmg: float, radius: float, life: float, penetration: float, infection: float) -> void:
    enemy_bullets.append({
        "pos": origin, "vel": Vector2.RIGHT.rotated(angle) * speed, "damage": dmg,
        "radius": radius, "life": life, "pen": penetration, "infection": infection, "dead": false
    })

func _auto_fire() -> void:
    player["fire_cd"] = float(player["fire_cd"]) - DT
    if float(player["fire_cd"]) > 0.0:
        return
    var stats: Dictionary = player["stats"]
    player["fire_cd"] = 1.0 / maxf(0.8, float(stats["rate"]))
    var aim: float = float(player["aim"])
    var pos: Vector2 = Vector2(player["pos"]) + Vector2.RIGHT.rotated(aim) * 16.0
    _fire_friendly(pos, aim)
    for i in range(int(stats["split"])):
        var offset: float = 0.14 * float(i + 1)
        _fire_friendly(pos, aim + offset, 0.85)
        _fire_friendly(pos, aim - offset, 0.85)
    if int(stats["rear"]) > 0:
        _fire_friendly(player["pos"], aim + PI, 0.7)

func _fire_friendly(origin: Vector2, angle: float, mult: float = 1.0) -> void:
    var stats: Dictionary = player["stats"]
    bullets.append({
        "pos": origin, "vel": Vector2.RIGHT.rotated(angle) * float(stats["bspeed"]),
        "damage": float(stats["dmg"]) * mult, "radius": 3.6, "life": 1.4,
        "pierce": int(stats["pierce"]), "homing": float(stats["homing"]),
        "explode": float(stats["explode"]), "ricochet": int(stats["ricochet"]),
        "crit": float(stats["crit"]), "chain": int(stats["chain"]),
        "pen": float(stats["armor_pen"]), "hits": [], "dead": false
    })

func _step_bullets() -> void:
    for bullet in bullets:
        if bullet["dead"]:
            continue
        var pos: Vector2 = bullet["pos"]
        var velocity: Vector2 = bullet["vel"]
        if float(bullet["homing"]) > 0.0:
            var target: Dictionary = nearest_enemy(pos, 320.0)
            if not target.is_empty():
                var wanted: float = (Vector2(target["pos"]) - pos).angle()
                var current: float = velocity.angle()
                var change: float = wrapf(wanted - current, -PI, PI)
                var turn: float = clampf(change, -float(bullet["homing"]) * DT, float(bullet["homing"]) * DT)
                velocity = Vector2.RIGHT.rotated(current + turn) * velocity.length()
                bullet["vel"] = velocity
        pos += velocity * DT
        bullet["pos"] = pos
        bullet["life"] = float(bullet["life"]) - DT
        if float(bullet["life"]) <= 0.0:
            bullet["dead"] = true
            continue
        if pos.x < -60 or pos.x > 1340 or pos.y < -60 or pos.y > 780:
            if int(bullet["ricochet"]) > 0:
                bullet["ricochet"] = int(bullet["ricochet"]) - 1
                if pos.x < 0 or pos.x > 1280:
                    velocity.x *= -1
                    pos.x = clampf(pos.x, 2.0, 1278.0)
                if pos.y < 0 or pos.y > 720:
                    velocity.y *= -1
                    pos.y = clampf(pos.y, 2.0, 718.0)
                bullet["pos"] = pos
                bullet["vel"] = velocity
            else:
                bullet["dead"] = true
                continue
        for enemy in enemies.duplicate():
            if bullet["dead"]:
                break
            if float(enemy["hp"]) <= 0.0 or int(enemy["id"]) in bullet["hits"]:
                continue
            if pos.distance_squared_to(enemy["pos"]) <= pow(float(bullet["radius"]) + float(enemy["radius"]), 2.0):
                bullet["hits"].append(int(enemy["id"]))
                _on_bullet_hit(bullet, enemy)
    bullets = bullets.filter(func(b: Dictionary) -> bool: return not bool(b["dead"]))
    for bullet in enemy_bullets:
        if bullet["dead"]:
            continue
        var pos: Vector2 = Vector2(bullet["pos"]) + Vector2(bullet["vel"]) * DT
        bullet["pos"] = pos
        bullet["life"] = float(bullet["life"]) - DT
        if float(bullet["life"]) <= 0.0 or pos.x < -40.0 or pos.x > 1320.0 or pos.y < -40.0 or pos.y > 760.0:
            bullet["dead"] = true
            continue
        if float(player["inv"]) <= 0.0:
            var rr: float = float(bullet["radius"]) + float(player["radius"])
            if pos.distance_squared_to(player["pos"]) < rr * rr:
                bullet["dead"] = true
                hurt_player(float(bullet["damage"]), float(bullet["pen"]), float(bullet["infection"]))
    enemy_bullets = enemy_bullets.filter(func(b: Dictionary) -> bool: return not bool(b["dead"]))

func _on_bullet_hit(bullet: Dictionary, enemy: Dictionary) -> void:
    var crit: bool = rng.randf() < float(bullet["crit"])
    var raw: float = float(bullet["damage"]) * (2.0 if crit else 1.0)
    damage_enemy(int(enemy["id"]), raw, float(bullet["pen"]), false, Vector2(bullet["vel"]) * 0.02)
    var stats: Dictionary = player["stats"]
    if float(stats["life_on_hit"]) > 0.0:
        player["hp"] = minf(float(player["max_hp"]), float(player["hp"]) + float(stats["life_on_hit"]))
    var victim: Dictionary = get_enemy(int(enemy["id"]))
    if not victim.is_empty():
        if float(stats["burn_chance"]) > 0.0 and rng.randf() < float(stats["burn_chance"]):
            Rules.add_dot(victim["statuses"], "burn", float(stats["burn_dps"]), 4.0 * float(stats["effect_duration"]))
        if float(stats["toxin_chance"]) > 0.0 and rng.randf() < float(stats["toxin_chance"]):
            Rules.add_dot(victim["statuses"], "toxin", float(stats["toxin_dps"]), 5.0 * float(stats["effect_duration"]))
    if float(bullet["explode"]) > 0.0:
        for target in enemies.duplicate():
            if Vector2(target["pos"]).distance_to(bullet["pos"]) < float(bullet["explode"]) + float(target["radius"]):
                damage_enemy(int(target["id"]), float(bullet["damage"]) * 0.55)
    if int(bullet["chain"]) > 0:
        var nearest: Dictionary = nearest_enemy(bullet["pos"], 190.0, int(enemy["id"]))
        if not nearest.is_empty():
            damage_enemy(int(nearest["id"]), float(bullet["damage"]) * 0.5 * int(bullet["chain"]))
    if int(bullet["pierce"]) > 0:
        bullet["pierce"] = int(bullet["pierce"]) - 1
        bullet["damage"] = float(bullet["damage"]) * 0.82
    else:
        bullet["dead"] = true

func _step_statuses() -> void:
    for enemy in enemies.duplicate():
        var ticks: Array = Rules.advance_dots(enemy["statuses"], DT)
        for amount in ticks:
            if get_enemy(int(enemy["id"])).is_empty():
                break
            damage_enemy(int(enemy["id"]), float(amount), 0.0, true)

func _step_contacts() -> void:
    if float(player["inv"]) > 0.0:
        return
    for enemy in enemies.duplicate():
        var rr: float = float(enemy["radius"]) + float(player["radius"])
        if Vector2(enemy["pos"]).distance_squared_to(player["pos"]) <= rr * rr:
            var infection: float = 0.0
            if Catalog.VIRUS.has(str(enemy["kind"])):
                infection = float(Catalog.VIRUS[str(enemy["kind"])][5])
            hurt_player(float(enemy["damage"]), 0.0, infection)
            var stats: Dictionary = player["stats"]
            if float(stats["thorns"]) > 0.0:
                var push: Vector2 = (Vector2(enemy["pos"]) - Vector2(player["pos"])).normalized()
                damage_enemy(int(enemy["id"]), float(stats["thorns"]), -1.0, false, push * 260.0)
            break

func get_enemy(enemy_id: int) -> Dictionary:
    for enemy in enemies:
        if int(enemy["id"]) == enemy_id:
            return enemy
    return {}

func nearest_enemy(position: Vector2, radius: float, exclude_id: int = -1) -> Dictionary:
    var best: Dictionary = {}
    var distance: float = radius * radius
    for enemy in enemies:
        if int(enemy["id"]) == exclude_id or float(enemy["hp"]) <= 0:
            continue
        var d: float = Vector2(enemy["pos"]).distance_squared_to(position)
        if d < distance:
            distance = d
            best = enemy
    return best

func damage_enemy(enemy_id: int, raw_damage: float, penetration: float = -1.0, dot: bool = false, knockback: Vector2 = Vector2.ZERO) -> float:
    var enemy: Dictionary = get_enemy(enemy_id)
    if enemy.is_empty() or raw_damage <= 0.0:
        return 0.0
    var stats: Dictionary = player["stats"]
    var pen: float = float(stats["armor_pen"]) if penetration < 0.0 else penetration
    var armor: float = float(enemy["armor"]) + (12.0 if float(enemy["guard"]) > 0 else 0.0)
    if str(enemy["kind"]) in ["ransomware", "encryptor"] and float(enemy["vulnerable"]) > 0:
        armor = maxf(0.0, armor - 24.0)
    var actual: float = raw_damage if dot else Rules.mitigate_damage(raw_damage, armor, pen)
    enemy["hp"] = float(enemy["hp"]) - actual
    if knockback != Vector2.ZERO:
        enemy["vel"] = Vector2(enemy["vel"]) + knockback
    if float(enemy["hp"]) <= 0.0:
        _kill_enemy(enemy)
    return actual

func _kill_enemy(enemy: Dictionary) -> void:
    var kind: String = str(enemy["kind"])
    if not enemies.has(enemy):
        return
    enemies.erase(enemy)
    if kind == boss_kind:
        boss_defeated = true
    enemy_died.emit(int(enemy["id"]), kind)
    events.append({"type": "enemy_killed", "id": enemy["id"], "kind": kind})
    var stats: Dictionary = player["stats"]
    if float(stats["lifesteal"]) > 0.0:
        player["hp"] = minf(float(player["max_hp"]), float(player["hp"]) + float(stats["lifesteal"]) * 0.5)
    if kind == "splitter":
        for i in range(mini(2, Catalog.MAX_LIVE_ENEMIES - enemies.size())):
            spawn_enemy("mini", Vector2(enemy["pos"]) + Vector2(rng.randf_range(-14.0, 14.0), rng.randf_range(-14.0, 14.0)))

func hurt_player(raw_damage: float, penetration: float = 0.0, infection: float = 0.0) -> float:
    if float(player["inv"]) > 0.0:
        return 0.0
    var stats: Dictionary = player["stats"]
    var armor: float = Rules.effective_armor(float(stats["armor"]), float(player["guard"]))
    var actual: float = Rules.mitigate_damage(raw_damage, armor, penetration)
    player["hp"] = maxf(0.0, float(player["hp"]) - actual)
    player["infection"] = clampf(float(player["infection"]) + infection, 0.0, 100.0)
    player["inv"] = 0.9
    player_hit.emit(actual, infection)
    events.append({"type": "player_hit", "damage": actual, "infection": infection})
    for enemy in enemies:
        var delta: Vector2 = Vector2(enemy["pos"]) - Vector2(player["pos"])
        if delta.length() < 150.0 and delta.length() > 0.0:
            enemy["vel"] = Vector2(enemy["vel"]) + delta.normalized() * (1.0 - delta.length() / 150.0) * 320.0
    return actual

func try_skill(kind: String) -> bool:
    if not active or paused:
        return false
    var stats: Dictionary = player["stats"]
    var cost: float
    var cooldown: float
    var key: String
    if kind == "pulse":
        key = "pulse_cd"
        cost = Rules.skill_cost(35.0, float(stats["skill_efficiency"]))
        cooldown = Rules.skill_cooldown(7.0, float(stats["skill_cooldown"]))
    elif kind == "guard":
        key = "guard_cd"
        cost = Rules.skill_cost(25.0, float(stats["skill_efficiency"]))
        cooldown = Rules.skill_cooldown(12.0, float(stats["skill_cooldown"]))
    else:
        return false
    if float(player[key]) > 0.0 or float(player["mana"]) + 0.000000001 < cost:
        return false
    player["mana"] = maxf(0.0, float(player["mana"]) - cost)
    player[key] = cooldown
    if kind == "pulse":
        for enemy in enemies.duplicate():
            if Vector2(enemy["pos"]).distance_to(player["pos"]) > 155.0 + float(enemy["radius"]):
                continue
            damage_enemy(int(enemy["id"]), 22.0 * float(stats["skill_power"]))
            var target: Dictionary = get_enemy(int(enemy["id"]))
            if not target.is_empty():
                Rules.add_dot(target["statuses"], "shock", 6.0 * float(stats["skill_power"]), 3.0 * float(stats["effect_duration"]))
    else:
        player["guard"] = 4.0 * float(stats["effect_duration"])
    events.append({"type": "skill_cast", "kind": kind, "mana": player["mana"]})
    return true
