extends RefCounted
## Defender Hub physical interaction coordinates (world XZ units).
## Terminals are requests: E/A perform every actual database/progression change.
const BOUNDS := Rect2(Vector2.ZERO, Vector2(26.0, 18.0))
const SPAWN := Vector3(13.0, 0.0, 9.0)
const TERMINALS := {
    "vaccine": {"position": Vector3(7.0, 0.0, 5.0), "radius": 1.8, "action": "vaccine_config", "label": "VACCINE CONFIG"},
    "database": {"position": Vector3(19.0, 0.0, 5.0), "radius": 1.8, "action": "plugin_database", "label": "PLUGIN DATABASE"},
    "skills": {"position": Vector3(7.0, 0.0, 13.0), "radius": 1.8, "action": "skill_loadout", "label": "SKILL LOADOUT"},
    "dispatch": {"position": Vector3(19.0, 0.0, 13.0), "radius": 2.0, "action": "start_mission", "label": "DISPATCH"}
}

static func terminals() -> Dictionary:
    return TERMINALS.duplicate(true)

static func is_inside(world_position: Vector3) -> bool:
    return BOUNDS.has_point(Vector2(world_position.x, world_position.z))

static func request_terminal(terminal_id: String, player_world: Vector3) -> Dictionary:
    if not TERMINALS.has(terminal_id) or not is_inside(player_world):
        return {}
    var target: Dictionary = TERMINALS[terminal_id]
    var anchor: Vector3 = target["position"]
    var offset := Vector2(player_world.x - anchor.x, player_world.z - anchor.z)
    if offset.length() > float(target["radius"]):
        return {}
    return {
        "schema": 1, "type": "hub_terminal_requested",
        "terminal_id": terminal_id, "action": str(target["action"])
    }

static func nearest_terminal(player_world: Vector3) -> Dictionary:
    var best_distance: float = INF
    var best_request: Dictionary = {}
    for terminal_id in TERMINALS:
        var request: Dictionary = request_terminal(terminal_id, player_world)
        if request.is_empty():
            continue
        var anchor: Vector3 = TERMINALS[terminal_id]["position"]
        var distance: float = Vector2(player_world.x - anchor.x, player_world.z - anchor.z).length()
        if distance < best_distance:
            best_distance = distance
            best_request = request
    return best_request
