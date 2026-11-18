extends RefCounted
## Deterministic XZ broadphase and swept projectile/circle hits. No PhysicsServer.
## Node3D visuals and world barriers are A/D integration responsibilities.
static func _cell(position: Vector3, size: float) -> Vector2i:
    return Vector2i(floori(position.x / size), floori(position.z / size))

static func _key(cell: Vector2i) -> String:
    return str(cell.x) + ":" + str(cell.y)

static func build(actors: Dictionary, cell_size: float = 8.0) -> Dictionary:
    var cells: Dictionary = {}
    var max_radius: float = 0.0
    var size: float = maxf(0.01, cell_size)
    var ids: Array = actors.keys()
    ids.sort()
    for actor_id in ids:
        var actor: Dictionary = actors[actor_id]
        if not bool(actor.get("alive", true)):
            continue
        var radius: float = maxf(0.0, float(actor.get("radius", 0.5)))
        max_radius = maxf(max_radius, radius)
        var key: String = _key(_cell(Vector3(actor["position"]), size))
        if not cells.has(key):
            cells[key] = []
        cells[key].append(str(actor_id))
    return {"cells": cells, "max_radius": max_radius, "cell_size": size}

static func segment_candidates(grid: Dictionary, start: Vector3, finish: Vector3,
        projectile_radius: float = 0.0) -> Array[String]:
    var size: float = maxf(0.01, float(grid.get("cell_size", 8.0)))
    var padding: float = maxf(0.0, projectile_radius) + float(grid.get("max_radius", 0.0))
    var from_x: int = floori((minf(start.x, finish.x) - padding) / size)
    var to_x: int = floori((maxf(start.x, finish.x) + padding) / size)
    var from_z: int = floori((minf(start.z, finish.z) - padding) / size)
    var to_z: int = floori((maxf(start.z, finish.z) + padding) / size)
    var found: Dictionary = {}
    var cells: Dictionary = grid.get("cells", {})
    for x in range(from_x, to_x + 1):
        for z in range(from_z, to_z + 1):
            var key: String = _key(Vector2i(x, z))
            for actor_id in cells.get(key, []):
                found[str(actor_id)] = true
    var ids: Array[String] = []
    for actor_id in found:
        ids.append(str(actor_id))
    ids.sort()
    return ids

static func swept_fraction(start: Vector3, finish: Vector3,
        center: Vector3, combined_radius: float) -> float:
    ## Returns first impact fraction 0..1, or -1 if no intersection.
    var a: Vector2 = Vector2(start.x, start.z)
    var b: Vector2 = Vector2(finish.x, finish.z)
    var c: Vector2 = Vector2(center.x, center.z)
    var delta: Vector2 = b - a
    var offset: Vector2 = a - c
    var r: float = maxf(0.0, combined_radius)
    if offset.length_squared() <= r * r:
        return 0.0
    var aa: float = delta.length_squared()
    if aa <= 0.00000001:
        return -1.0
    var bb: float = 2.0 * offset.dot(delta)
    var cc: float = offset.length_squared() - r * r
    var discriminant: float = bb * bb - 4.0 * aa * cc
    if discriminant < 0.0:
        return -1.0
    var t: float = (-bb - sqrt(discriminant)) / (2.0 * aa)
    return t if t >= 0.0 and t <= 1.0 else -1.0

static func segment_hits(grid: Dictionary, actors: Dictionary, start: Vector3,
        finish: Vector3, radius: float, attacker_team: String) -> Array[Dictionary]:
    var hits: Array[Dictionary] = []
    for actor_id in segment_candidates(grid, start, finish, radius):
        if not actors.has(actor_id):
            continue
        var actor: Dictionary = actors[actor_id]
        if not bool(actor.get("alive", true)) or str(actor.get("team", "")) == attacker_team:
            continue
        var t: float = swept_fraction(start, finish, Vector3(actor["position"]),
            radius + maxf(0.0, float(actor.get("radius", 0.5))))
        if t >= 0.0:
            hits.append({"actor_id": actor_id, "fraction": t})
    hits.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
        if is_equal_approx(float(a["fraction"]), float(b["fraction"])):
            return str(a["actor_id"]) < str(b["actor_id"])
        return float(a["fraction"]) < float(b["fraction"])
    )
    return hits
