extends RefCounted
## AI state changes in logical XY. 3D meshes are passive observers.
const Rules = preload("res://scripts/combat/combat_rules.gd")
const Catalog = preload("res://scripts/enemies/enemy_catalog.gd")

static func advance(enemy: Dictionary, player_pos: Vector2, dt: float, rng: RandomNumberGenerator) -> Array:
    var actions: Array = []
    var kind: String = str(enemy["kind"])
    var pos: Vector2 = enemy["pos"]
    var vel: Vector2 = enemy["vel"]
    var to_player: Vector2 = player_pos - pos
    var distance: float = maxf(1.0, to_player.length())
    var unit: Vector2 = to_player / distance
    var speed: float = float(enemy["speed"])
    enemy["guard"] = maxf(0.0, float(enemy["guard"]) - dt)
    enemy["vulnerable"] = maxf(0.0, float(enemy["vulnerable"]) - dt)
    if kind in ["drifter", "mini"]:
        vel = vel.lerp(unit * speed, minf(1.0, 4.5 * dt))
    elif kind == "shooter":
        var move: float = (1.0 if distance > 390 else (-1.0 if distance < 270 else 0.0))
        vel = vel.lerp((unit * move + Vector2(-unit.y, unit.x) * 0.55) * speed, minf(1.0, 3.0 * dt))
        enemy["cooldown"] = float(enemy["cooldown"]) - dt
        if float(enemy["cooldown"]) <= 0.0 and distance < 700.0:
            enemy["cooldown"] = rng.randf_range(1.9, 2.6)
            actions.append({"type": "aim", "count": 3, "spread": 0.18, "speed": 330.0, "pen": 0.0, "infection": 0.0})
    elif kind == "splitter":
        vel = vel.lerp(unit * speed, minf(1.0, 3.4 * dt))
    elif kind == "rusher":
        if int(enemy["state"]) == 0:
            vel = vel.lerp(unit * speed, minf(1.0, 4.0 * dt))
            if distance < 320.0:
                enemy["state"] = 1
                enemy["timer"] = 0.55
                vel *= 0.2
        elif int(enemy["state"]) == 1:
            enemy["timer"] = float(enemy["timer"]) - dt
            if float(enemy["timer"]) <= 0.0:
                enemy["state"] = 2
                enemy["timer"] = 0.5
                vel = unit * 780.0
        elif int(enemy["state"]) == 2:
            enemy["timer"] = float(enemy["timer"]) - dt
            if float(enemy["timer"]) <= 0.0:
                enemy["state"] = 3
                enemy["timer"] = 0.9
        else:
            enemy["timer"] = float(enemy["timer"]) - dt
            vel *= maxf(0.0, 1.0 - 3.0 * dt)
            if float(enemy["timer"]) <= 0.0:
                enemy["state"] = 0
    elif kind == "boss":
        enemy["elapsed"] = float(enemy["elapsed"]) + dt
        var t: float = float(enemy["elapsed"])
        var frac: float = maxf(0.0, float(enemy["hp"]) / float(enemy["max_hp"]))
        var phase: int = 1 if frac > 0.66 else (2 if frac > 0.33 else 3)
        enemy["state"] = phase
        var target: Vector2 = Vector2(640.0 + cos(t * 0.55) * 300.0, 360.0 + sin(t * 0.85) * 150.0)
        vel = vel.lerp((target - pos) * 0.9, minf(1.0, 1.6 * dt))
        enemy["cooldown"] = float(enemy["cooldown"]) - dt
        if float(enemy["cooldown"]) <= 0.0:
            if phase == 1:
                enemy["cooldown"] = 2.1
                actions.append({"type": "radial", "count": 16, "speed": 240.0, "offset": t, "pen": 0.0, "infection": 0.0})
            elif phase == 2:
                enemy["cooldown"] = 1.35
                actions.append({"type": "radial", "count": 22, "speed": 270.0, "offset": t * 1.7, "pen": 0.0, "infection": 0.0})
                if rng.randf() < 0.6:
                    actions.append({"type": "aim", "count": 5, "spread": 0.13, "speed": 350.0, "pen": 0.0, "infection": 0.0})
            else:
                enemy["cooldown"] = 0.95
                actions.append({"type": "radial", "count": 26, "speed": 300.0, "offset": t * 2.4, "pen": 0.0, "infection": 0.0})
                actions.append({"type": "radial", "count": 12, "speed": 190.0, "offset": -t, "pen": 0.0, "infection": 0.0})
    elif Catalog.VIRUS.has(kind):
        enemy["elapsed"] = float(enemy["elapsed"]) + dt
        var t: float = float(enemy["elapsed"])
        if Catalog.is_boss(kind):
            var target: Vector2 = Vector2(640.0 + cos(t * 0.45) * 240.0, 360.0 + sin(t * 0.7) * 130.0)
            vel = vel.lerp((target - pos) * 0.8, minf(1.0, 2.0 * dt))
        elif kind in ["trojan", "rootkit", "packet_swarm"]:
            var move: float = 1.0 if distance > 270.0 else (-1.0 if distance < 180.0 else 0.0)
            vel = vel.lerp((unit * move + Vector2(-unit.y, unit.x) * 0.4) * speed, minf(1.0, 3.0 * dt))
        else:
            vel = vel.lerp(unit * speed, minf(1.0, 3.5 * dt))
        var pattern: Array = Catalog.VIRUS[kind]
        var phase: int = Rules.boss_phase(clampf(float(enemy["hp"]) / float(enemy["max_hp"]), 0.0, 1.0)) if Catalog.is_boss(kind) else 1
        var interval: float = float(pattern[1]) / (1.0 + 0.2 * float(phase - 1))
        enemy["virus_elapsed"] = float(enemy["virus_elapsed"]) + dt
        var emitted: int = 0
        while float(enemy["virus_elapsed"]) >= interval and emitted < 6:
            enemy["virus_elapsed"] = float(enemy["virus_elapsed"]) - interval
            actions.append({
                "type": "virus", "behavior": str(pattern[0]), "power": float(pattern[2]),
                "count": int(pattern[3]) + (2 * (phase - 1) if Catalog.is_boss(kind) else 0),
                "pen": float(pattern[4]), "infection": float(pattern[5]),
                "clone_kind": str(pattern[6]), "clones_per_cast": int(pattern[7]),
                "clone_source_cap": int(pattern[8]), "telegraph": float(pattern[9]),
                "phase": phase
            })
            emitted += 1
        if float(enemy["virus_elapsed"]) >= interval:
            enemy["virus_elapsed"] = fmod(float(enemy["virus_elapsed"]), interval)
    enemy["vel"] = vel
    enemy["pos"] = pos + vel * dt
    enemy["angle"] = unit.angle()
    if not Catalog.is_boss(kind):
        var clipped: Vector2 = enemy["pos"]
        enemy["pos"] = Vector2(clampf(clipped.x, -30.0, 1310.0), clampf(clipped.y, -30.0, 750.0))
    return actions
