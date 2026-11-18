extends Node3D
## Isolated walkable-space geometry and explicit interaction bridge for A input.
## No player controller is added: B/A provide their real CharacterBody3D.
signal terminal_requested(event: Dictionary)
const Layout = preload("res://scripts/rework/world/hub_layout.gd")

func _ready() -> void:
    _build()

func request_interaction(player_world: Vector3) -> bool:
    var request: Dictionary = Layout.nearest_terminal(player_world)
    if request.is_empty():
        return false
    terminal_requested.emit(request)
    return true

func request_named_terminal(terminal_id: String, player_world: Vector3) -> bool:
    var request: Dictionary = Layout.request_terminal(terminal_id, player_world)
    if request.is_empty():
        return false
    terminal_requested.emit(request)
    return true

func _build() -> void:
    if get_node_or_null("WorldGeometry") != null:
        return
    var holder := Node3D.new()
    holder.name = "WorldGeometry"
    add_child(holder)
    var floor_mat := StandardMaterial3D.new()
    floor_mat.albedo_color = Color(0.035, 0.075, 0.14)
    var floor_mesh := BoxMesh.new()
    floor_mesh.size = Vector3(Layout.BOUNDS.size.x, 0.25, Layout.BOUNDS.size.y)
    var floor := MeshInstance3D.new()
    floor.name = "HubFloor"
    floor.mesh = floor_mesh
    floor.material_override = floor_mat
    floor.position = Vector3(Layout.BOUNDS.get_center().x, -0.125, Layout.BOUNDS.get_center().y)
    holder.add_child(floor)
    _collider(holder, "HubFloorCollider", floor.position, floor_mesh.size)
    # Four perimeter barriers; open interior keeps all terminals physically accessible.
    _wall(holder, "NorthWall", Vector3(13, 0.8, 0), Vector3(26, 1.6, 0.35))
    _wall(holder, "SouthWall", Vector3(13, 0.8, 18), Vector3(26, 1.6, 0.35))
    _wall(holder, "WestWall", Vector3(0, 0.8, 9), Vector3(0.35, 1.6, 18))
    _wall(holder, "EastWall", Vector3(26, 0.8, 9), Vector3(0.35, 1.6, 18))
    var accent := StandardMaterial3D.new()
    accent.albedo_color = Color(0.12, 0.55, 0.75)
    accent.emission_enabled = true
    accent.emission = Color(0.035, 0.22, 0.34)
    accent.emission_energy_multiplier = 1.1
    for terminal_id in Layout.TERMINALS:
        var data: Dictionary = Layout.TERMINALS[terminal_id]
        var position: Vector3 = data["position"]
        # Interactable hubs are FLOOR pads, not 1.2m blocking pedestals.
        # A player can stand directly on the marked F area. The gameplay
        # request is gated by Layout's true radius, never mesh collision.
        var marker := MeshInstance3D.new()
        marker.name = terminal_id.capitalize() + "Terminal"
        var mesh := BoxMesh.new()
        mesh.size = Vector3(1.4, 0.06, 1.4)
        marker.mesh = mesh
        marker.material_override = accent
        marker.position = position + Vector3.UP * 0.03
        holder.add_child(marker)
        var sign := Label3D.new()
        sign.name = terminal_id + "Nameplate"
        sign.text = str(data["label"]) + "  [F]"
        sign.font_size = 26
        sign.pixel_size = 0.0045
        sign.rotation_degrees = Vector3(-90, 0, 0)
        sign.position = position + Vector3(0.0, 0.11, -0.85)
        holder.add_child(sign)
        # No StaticBody3D on a terminal pad. The perimeter and floor retain
        # their collision, and Layout still rejects out-of-range F requests.

func _wall(holder: Node3D, label: String, position: Vector3, dimensions: Vector3) -> void:
    var marker := MeshInstance3D.new()
    marker.name = label
    var mesh := BoxMesh.new()
    mesh.size = dimensions
    marker.mesh = mesh
    marker.position = position
    holder.add_child(marker)
    _collider(holder, label + "Collider", position, dimensions)

func _collider(holder: Node3D, label: String, position: Vector3, dimensions: Vector3) -> void:
    var body := StaticBody3D.new()
    body.name = label
    body.position = position
    var shape := CollisionShape3D.new()
    var box := BoxShape3D.new()
    box.size = dimensions
    shape.shape = box
    body.add_child(shape)
    holder.add_child(body)
