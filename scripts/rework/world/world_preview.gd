extends Node3D
## Opt-in, standalone level visualization. Not the application's main scene.
const Planner = preload("res://scripts/rework/world/mission_planner.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
@export var preview_seed: int = 42
@export_enum("CPU", "GPU", "RAM", "SSD", "HDD") var region_id: String = "CPU"
var current_map: Dictionary = {}

func _ready() -> void:
    rebuild()

func rebuild() -> void:
    var region: Dictionary = Planner.create_mission(preview_seed)["regions"].get(region_id, {})
    if region.is_empty():
        return
    current_map = Maps.generate(region_id, int(region["map_seed"]), region["environment"])
    var holder: Node3D = get_node_or_null("TileContainer") as Node3D
    if holder == null:
        holder = Node3D.new()
        holder.name = "TileContainer"
        add_child(holder)
    for node in holder.get_children():
        holder.remove_child(node)
        node.queue_free()
    var floor_mesh := BoxMesh.new()
    floor_mesh.size = Vector3(Maps.TILE_SIZE * 0.98, 0.12, Maps.TILE_SIZE * 0.98)
    var wall_mesh := BoxMesh.new()
    wall_mesh.size = Vector3(Maps.TILE_SIZE, 1.4, Maps.TILE_SIZE)
    var floor_material := StandardMaterial3D.new()
    floor_material.albedo_color = Color(0.055, 0.18, 0.26)
    var wall_material := StandardMaterial3D.new()
    wall_material.albedo_color = Color(0.1, 0.12, 0.24)
    for y in range(int(current_map["height"])):
        for x in range(int(current_map["width"])):
            var is_open: bool = Maps.is_floor(current_map, Vector2i(x, y))
            var cell_mesh := MeshInstance3D.new()
            cell_mesh.mesh = floor_mesh if is_open else wall_mesh
            cell_mesh.material_override = floor_material if is_open else wall_material
            cell_mesh.position = Maps.cell_to_world(Vector2i(x, y)) + Vector3(0, -0.08 if is_open else 0.60, 0)
            holder.add_child(cell_mesh)
    _marker(holder, current_map["spawn"], Color(0.2, 0.9, 0.9))
    _marker(holder, current_map["boss"], Color(0.95, 0.2, 0.35))
    for objective in current_map["objectives"]:
        _marker(holder, objective["cell"], Color(0.9, 0.8, 0.25))

func _marker(holder: Node3D, cell: Vector2i, color: Color) -> void:
    var marker := MeshInstance3D.new()
    var mesh := SphereMesh.new()
    mesh.radius = 0.26
    mesh.height = 0.52
    marker.mesh = mesh
    var material := StandardMaterial3D.new()
    material.albedo_color = color
    material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    marker.material_override = material
    marker.position = Maps.cell_to_world(cell) + Vector3.UP * 0.32
    holder.add_child(marker)
