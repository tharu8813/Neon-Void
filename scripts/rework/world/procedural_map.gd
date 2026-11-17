extends RefCounted
## Deterministic perfect-maze backbone + optional loops: every floor tile is reachable.
## Tile size, objective count and density are [draft v0.1].
const Catalog = preload("res://scripts/rework/world/hardware_catalog.gd")
const TILE_SIZE: float = 1.2
const DIRECTIONS := [Vector2i(0, -1), Vector2i(1, 0), Vector2i(0, 1), Vector2i(-1, 0)]

static func _index(x: int, y: int, width: int) -> int:
    return y * width + x

static func is_floor(plan: Dictionary, cell: Vector2i) -> bool:
    var w: int = int(plan.get("width", 0))
    var h: int = int(plan.get("height", 0))
    if cell.x < 0 or cell.y < 0 or cell.x >= w or cell.y >= h:
        return false
    var tiles: PackedInt32Array = plan.get("tiles", PackedInt32Array())
    return tiles.size() == w * h and tiles[_index(cell.x, cell.y, w)] == 1

static func distance_field(plan: Dictionary, start: Vector2i) -> PackedInt32Array:
    var w: int = int(plan.get("width", 0))
    var h: int = int(plan.get("height", 0))
    var distances := PackedInt32Array()
    distances.resize(w * h)
    distances.fill(-1)
    if not is_floor(plan, start):
        return distances
    var queue: Array[Vector2i] = [start]
    var head: int = 0
    distances[_index(start.x, start.y, w)] = 0
    while head < queue.size():
        var current: Vector2i = queue[head]
        head += 1
        for direction in DIRECTIONS:
            var adjacent: Vector2i = current + direction
            if not is_floor(plan, adjacent):
                continue
            var index: int = _index(adjacent.x, adjacent.y, w)
            if distances[index] >= 0:
                continue
            distances[index] = distances[_index(current.x, current.y, w)] + 1
            queue.append(adjacent)
    return distances

static func generate(region_id: String, map_seed: int, environment: Dictionary = {}) -> Dictionary:
    if not Catalog.has_hardware(region_id):
        return {}
    var size: Vector2i = Vector2i(17, 13)
    var effect: String = str(environment.get("id", "normal"))
    if effect == "spread":
        size = Vector2i(25, 19)
    elif effect == "initial":
        size = Vector2i(13, 9)
    elif region_id == "HDD":
        size = Vector2i(21, 15)
    var width: int = size.x
    var height: int = size.y
    var tiles := PackedInt32Array()
    tiles.resize(width * height)
    tiles.fill(0)
    var rng := RandomNumberGenerator.new()
    rng.seed = map_seed
    var stack: Array[Vector2i] = [Vector2i(1, 1)]
    tiles[_index(1, 1, width)] = 1
    while not stack.is_empty():
        var cell: Vector2i = stack.back()
        var candidates: Array[Vector2i] = []
        for dir in DIRECTIONS:
            var next: Vector2i = cell + dir * 2
            if next.x > 0 and next.y > 0 and next.x < width - 1 and next.y < height - 1:
                if tiles[_index(next.x, next.y, width)] == 0:
                    candidates.append(next)
        if candidates.is_empty():
            stack.pop_back()
            continue
        var chosen: Vector2i = candidates[rng.randi_range(0, candidates.size() - 1)]
        var mid: Vector2i = (cell + chosen) / 2
        tiles[_index(mid.x, mid.y, width)] = 1
        tiles[_index(chosen.x, chosen.y, width)] = 1
        stack.append(chosen)
    # Only open walls adjoining existing walkable tiles, never disconnect paths.
    for y in range(1, height - 1):
        for x in range(1, width - 1):
            if tiles[_index(x, y, width)] != 0 or rng.randf() > 0.12:
                continue
            var horizontal: bool = x > 1 and x < width - 2 and tiles[_index(x - 1, y, width)] == 1 and tiles[_index(x + 1, y, width)] == 1
            var vertical: bool = y > 1 and y < height - 2 and tiles[_index(x, y - 1, width)] == 1 and tiles[_index(x, y + 1, width)] == 1
            if horizontal or vertical:
                tiles[_index(x, y, width)] = 1
    var spawn := Vector2i(1, 1)
    var plan: Dictionary = {
        "schema": 1, "region_id": region_id, "seed": map_seed, "environment": environment.duplicate(true),
        "width": width, "height": height, "tile_size": TILE_SIZE, "tiles": tiles, "spawn": spawn
    }
    var distances := distance_field(plan, spawn)
    var boss: Vector2i = spawn
    var farthest: int = -1
    for y in range(height):
        for x in range(width):
            var d: int = distances[_index(x, y, width)]
            if d > farthest:
                farthest = d
                boss = Vector2i(x, y)
    var objectives: Array[Dictionary] = []
    for objective_id in Catalog.objectives(region_id):
        var best: Vector2i = spawn
        var best_score: float = -1.0
        for y in range(1, height - 1):
            for x in range(1, width - 1):
                var candidate := Vector2i(x, y)
                var path_distance: int = distances[_index(x, y, width)]
                if path_distance < 3 or candidate == boss:
                    continue
                var separation: int = 99
                for existing in objectives:
                    var used: Vector2i = existing["cell"]
                    separation = mini(separation, absi(used.x - x) + absi(used.y - y))
                var score: float = float(path_distance) + float(separation) * 0.4 + rng.randf() * 0.01
                if score > best_score:
                    best_score = score
                    best = candidate
        objectives.append({"id": str(objective_id), "cell": best, "world": cell_to_world(best)})
    plan["boss"] = boss
    plan["extraction"] = boss
    plan["objectives"] = objectives
    plan["world_bounds"] = Rect2(Vector2.ZERO, Vector2(width, height) * TILE_SIZE)
    return plan

static func cell_to_world(cell: Vector2i) -> Vector3:
    return Vector3((float(cell.x) + 0.5) * TILE_SIZE, 0.0, (float(cell.y) + 0.5) * TILE_SIZE)

static func all_floor_reachable(plan: Dictionary) -> bool:
    var start: Vector2i = plan.get("spawn", Vector2i(-1, -1))
    var distances := distance_field(plan, start)
    var tiles: PackedInt32Array = plan.get("tiles", PackedInt32Array())
    if tiles.size() != distances.size():
        return false
    for i in range(tiles.size()):
        if tiles[i] == 1 and distances[i] < 0:
            return false
    return distances.has(0)
