extends Node3D
## Optional 3D preview with input-injection methods; NEVER replaces main.tscn.
const Model = preload("res://scripts/rework/world/motherboard_layout.gd")
signal board_event(event: Dictionary)
var board: RefCounted = Model.new()

func _ready() -> void:
    board.board_event.connect(_relay)
    _build()

func setup_run(run_id: String, mission_snapshot: Dictionary) -> void:
    if is_instance_valid(board) and board.board_event.is_connected(_relay):
        board.board_event.disconnect(_relay)
    board = Model.new(run_id, mission_snapshot)
    board.board_event.connect(_relay)

func confirm_intro_enemy_defeated(enemy_id: String, receipt_id: String) -> bool:
    return board.confirm_intro_defeat(enemy_id, receipt_id)

func request_gate_near_player(player_world: Vector3) -> Dictionary:
    var nearest_id: String = ""
    var best_distance: float = INF
    for region_id in Model.GATES:
        var anchor: Vector3 = Model.GATES[region_id]["position"]
        var distance := Vector2(player_world.x - anchor.x, player_world.z - anchor.z).length()
        if distance < best_distance:
            best_distance = distance
            nearest_id = region_id
    if nearest_id.is_empty():
        return {}
    return board.request_gate(nearest_id, player_world)

func return_to_board(region_id: String) -> bool:
    return board.return_to_board(region_id)

func _relay(event: Dictionary) -> void:
    board_event.emit(event.duplicate(true))

func _build() -> void:
    if get_node_or_null("BoardGeometry") != null:
        return
    var holder := Node3D.new()
    holder.name = "BoardGeometry"
    add_child(holder)
    var floor := MeshInstance3D.new()
    floor.name = "MainboardFloor"
    var mesh := BoxMesh.new()
    mesh.size = Vector3(Model.BOUNDS.size.x, 0.25, Model.BOUNDS.size.y)
    floor.mesh = mesh
    floor.position = Vector3(Model.BOUNDS.get_center().x, -0.125, Model.BOUNDS.get_center().y)
    holder.add_child(floor)
    var surface := StaticBody3D.new()
    surface.name = "FloorCollider"
    surface.position = floor.position
    var shape := CollisionShape3D.new()
    var block := BoxShape3D.new()
    block.size = mesh.size
    shape.shape = block
    surface.add_child(shape)
    holder.add_child(surface)
    # Gate geometry; B/A provide hostile actors, interaction prompt and transitions.
    for region_id in Model.GATES:
        var gate := MeshInstance3D.new()
        gate.name = region_id + "Gate"
        var pillar := CylinderMesh.new()
        pillar.top_radius = 0.65
        pillar.bottom_radius = 0.9
        pillar.height = 0.4
        gate.mesh = pillar
        gate.position = Model.GATES[region_id]["position"] + Vector3.UP * 0.2
        var material := StandardMaterial3D.new()
        material.albedo_color = Color(0.15, 0.4, 0.8)
        gate.material_override = material
        holder.add_child(gate)
    for enemy in Model.INTRO:
        var anchor := Marker3D.new()
        anchor.name = str(enemy["id"])
        anchor.position = enemy["position"]
        holder.add_child(anchor)
