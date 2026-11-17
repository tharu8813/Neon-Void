extends Node3D
## Isolated physical procedural hardware map. No combat/save/GUI dependencies.
signal interaction_requested(fact: Dictionary)
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const Catalog = preload("res://scripts/rework/world/hardware_catalog.gd")
var run_id: String = ""
var region_id: String = ""
var map_plan: Dictionary = {}
var goal_activated: Dictionary = {}
var goal_completed: Dictionary = {}
var boss_available: bool = false
var extraction_open: bool = false

func configure(new_run_id: String, new_region: String, planned_map: Dictionary) -> bool:
    if new_run_id.is_empty() or not Catalog.has_hardware(new_region):
        return false
    if str(planned_map.get("region_id", "")) != new_region or not Maps.all_floor_reachable(planned_map):
        return false
    var objectives: Array = planned_map.get("objectives", [])
    if objectives.size() != Catalog.objectives(new_region).size():
        return false
    var reserved: Dictionary = {}
    for objective in objectives:
        if not objective is Dictionary:
            return false
        var cell: Vector2i = objective.get("cell", Vector2i(-1, -1))
        if not Maps.is_floor(planned_map, cell) or reserved.has(cell):
            return false
        if cell == planned_map.get("spawn") or cell == planned_map.get("boss"):
            return false
        reserved[cell] = true
    run_id = new_run_id
    region_id = new_region
    map_plan = planned_map.duplicate(true)
    goal_activated.clear()
    goal_completed.clear()
    boss_available = false
    extraction_open = false
    for goal in objectives:
        goal_activated[str(goal["id"])] = false
        goal_completed[str(goal["id"])] = false
    _build()
    return true

func spawn_world() -> Vector3:
    return Maps.cell_to_world(map_plan.get("spawn", Vector2i(1, 1)))

func boss_world() -> Vector3:
    return Maps.cell_to_world(map_plan.get("boss", Vector2i(1, 1)))

func extraction_world() -> Vector3:
    return Maps.cell_to_world(map_plan.get("extraction", Vector2i(1, 1)))

func objective_world(objective_id: String) -> Vector3:
    for objective in map_plan.get("objectives", []):
        if str(objective["id"]) == objective_id:
            return objective["world"]
    return Vector3(INF, INF, INF)

func world_to_cell(point: Vector3) -> Vector2i:
    return Vector2i(floori(point.x / Maps.TILE_SIZE), floori(point.z / Maps.TILE_SIZE))

func can_walk(point: Vector3) -> bool:
    return Maps.is_floor(map_plan, world_to_cell(point))

func interact_at(player_world: Vector3) -> Dictionary:
    if run_id.is_empty() or region_id.is_empty():
        return {}
    var p := Vector2(player_world.x, player_world.z)
    if extraction_open:
        var start := spawn_world()
        if p.distance_to(Vector2(start.x, start.z)) <= 0.75:
            var return_fact := {
                "schema": 1, "type": "hardware_board_return_requested",
                "run_id": run_id, "region_id": region_id
            }
            interaction_requested.emit(return_fact.duplicate(true))
            return return_fact
        var ex := extraction_world()
        if p.distance_to(Vector2(ex.x, ex.z)) <= 1.15:
            var fact := {
                "schema": 1, "type": "hardware_extraction_requested",
                "run_id": run_id, "region_id": region_id
            }
            interaction_requested.emit(fact.duplicate(true))
            return fact
    for objective in map_plan.get("objectives", []):
        var id: String = str(objective["id"])
        if bool(goal_completed.get(id, false)) or (region_id == "CPU" and bool(goal_activated.get(id, false))):
            continue
        var place: Vector3 = objective["world"]
        if p.distance_to(Vector2(place.x, place.z)) <= 1.1:
            goal_activated[id] = true
            var fact := {
                "schema": 1, "type": "hardware_objective_activated",
                "run_id": run_id, "region_id": region_id, "objective_id": id
            }
            interaction_requested.emit(fact.duplicate(true))
            return fact
    return {}

func set_objective_complete(objective_id: String) -> bool:
    if not goal_activated.has(objective_id) or bool(goal_completed[objective_id]):
        return false
    goal_completed[objective_id] = true
    return true

func set_boss_available() -> bool:
    if boss_available:
        return false
    for done in goal_completed.values():
        if not bool(done):
            return false
    boss_available = true
    return true

func set_extraction_open() -> bool:
    if extraction_open or not boss_available:
        return false
    extraction_open = true
    _marker(get_node("Geometry"), "EXTRACT", extraction_world(), Color(0.25, 1.0, 0.48))
    return true

func _build() -> void:
    var old: Node = get_node_or_null("Geometry")
    if old != null:
        remove_child(old)
        old.queue_free()
    var holder := Node3D.new()
    holder.name = "Geometry"
    add_child(holder)
    var tile_size: float = float(map_plan["tile_size"])
    var floor_mesh := BoxMesh.new()
    floor_mesh.size = Vector3(tile_size * 0.98, 0.14, tile_size * 0.98)
    var wall_mesh := BoxMesh.new()
    wall_mesh.size = Vector3(tile_size, 1.5, tile_size)
    var floor_mat := StandardMaterial3D.new()
    var themes: Dictionary = {
        "CPU": [Color(0.06, 0.20, 0.30), Color(0.10, 0.12, 0.22)],
        "GPU": [Color(0.29, 0.08, 0.10), Color(0.37, 0.13, 0.07)],
        "RAM": [Color(0.14, 0.10, 0.30), Color(0.18, 0.13, 0.34)],
        "SSD": [Color(0.06, 0.25, 0.22), Color(0.08, 0.30, 0.28)],
        "HDD": [Color(0.17, 0.18, 0.20), Color(0.30, 0.27, 0.21)]
    }
    var palette: Array = themes.get(region_id, themes["CPU"])
    floor_mat.albedo_color = palette[0]
    var wall_mat := StandardMaterial3D.new()
    wall_mat.albedo_color = palette[1]
    var floors: Array[Transform3D] = []
    var walls: Array[Transform3D] = []
    var solid := StaticBody3D.new()
    solid.name = "WallCollisions"
    holder.add_child(solid)
    for y in range(int(map_plan["height"])):
        for x in range(int(map_plan["width"])):
            var cell := Vector2i(x, y)
            var center: Vector3 = Maps.cell_to_world(cell)
            if Maps.is_floor(map_plan, cell):
                floors.append(Transform3D(Basis(), center + Vector3(0, -0.08, 0)))
            else:
                walls.append(Transform3D(Basis(), center + Vector3(0, 0.7, 0)))
                var shape := CollisionShape3D.new()
                var box := BoxShape3D.new()
                box.size = wall_mesh.size
                shape.shape = box
                shape.position = center + Vector3.UP * 0.7
                solid.add_child(shape)
    _draw_instances(holder, "FloorTiles", floor_mesh, floor_mat, floors)
    _draw_instances(holder, "WallTiles", wall_mesh, wall_mat, walls)
    var support := StaticBody3D.new()
    support.name = "FloorCollider"
    var support_shape := CollisionShape3D.new()
    var support_box := BoxShape3D.new()
    support_box.size = Vector3(float(map_plan["width"]) * tile_size, 0.15, float(map_plan["height"]) * tile_size)
    support_shape.shape = support_box
    support.add_child(support_shape)
    support.position = Vector3(support_box.size.x * 0.5, -0.15, support_box.size.z * 0.5)
    holder.add_child(support)
    _marker(holder, "SPAWN", spawn_world(), Color(0.25, 0.9, 0.95))
    for objective in map_plan["objectives"]:
        _marker(holder, str(objective["id"]), objective["world"], Color(1.0, 0.8, 0.25))
    _marker(holder, "BOSS", boss_world(), Color(1.0, 0.25, 0.3))
    # Distinct return path allows clearing the next hardware without ending the
    # mission; this pad is not interactive until this hardware is cleansed.
    _marker(holder, "BOARD RETURN AFTER CLEAR", spawn_world(), Color(0.26, 0.65, 1.0))

func _draw_instances(parent: Node3D, name: String, mesh: Mesh, material: Material,
        transforms: Array[Transform3D]) -> void:
    var batch := MultiMesh.new()
    batch.transform_format = MultiMesh.TRANSFORM_3D
    batch.mesh = mesh
    batch.instance_count = transforms.size()
    for i in range(transforms.size()):
        batch.set_instance_transform(i, transforms[i])
    var visual := MultiMeshInstance3D.new()
    visual.name = name
    visual.multimesh = batch
    visual.material_override = material
    parent.add_child(visual)

func _marker(parent: Node3D, label: String, position: Vector3, color: Color) -> void:
    var marker := MeshInstance3D.new()
    marker.name = "Point_" + label
    var mesh := CylinderMesh.new()
    mesh.top_radius = 0.28
    mesh.bottom_radius = 0.32
    mesh.height = 0.12
    marker.mesh = mesh
    marker.position = position + Vector3.UP * 0.08
    var mat := StandardMaterial3D.new()
    mat.albedo_color = color
    mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    marker.material_override = mat
    parent.add_child(marker)
    var sign := Label3D.new()
    sign.name = "Label_" + label
    sign.text = label.to_upper() if label == "SPAWN" or label == "BOSS" else label.to_upper() + "  [F]"
    sign.font_size = 36
    sign.pixel_size = 0.006
    sign.rotation_degrees = Vector3(-90, 0, 0)
    sign.position = position + Vector3.UP * 0.7
    parent.add_child(sign)
