extends MultiMeshInstance3D
const FrozenMapping = preload("res://scripts/runtime_config.gd")
## P9 VISUAL ONLY: immutable logical XY snapshots become instanced 3D meshes.
## Does not own collision, game simulation, RNG, save state, or damage rules.
@export_range(16, 8192, 16) var capacity: int = 2048
@export_range(0.01, 0.20, 0.005) var radius: float = 0.055
@export var elevation: float = 0.13

func _ready() -> void:
    _ensure_batch()

func _ensure_batch() -> void:
    if multimesh != null:
        return
    var sphere: SphereMesh = SphereMesh.new()
    sphere.radius = radius
    sphere.height = radius * 2.0
    sphere.radial_segments = 8
    sphere.rings = 4
    var surface: StandardMaterial3D = StandardMaterial3D.new()
    surface.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    surface.vertex_color_use_as_albedo = true
    surface.emission_enabled = true
    surface.emission = Color(0.2, 0.85, 1.0)
    surface.emission_energy_multiplier = 1.3
    sphere.material = surface
    var instances: MultiMesh = MultiMesh.new()
    instances.transform_format = MultiMesh.TRANSFORM_3D
    instances.use_colors = true
    instances.mesh = sphere
    instances.instance_count = maxi(1, capacity)
    instances.visible_instance_count = 0
    multimesh = instances
    custom_aabb = AABB(Vector3(-3.0, -1.0, -3.0), Vector3(32.0, 6.0, 24.0))

func set_projectiles(logical_positions: PackedVector2Array, tints: PackedColorArray = PackedColorArray()) -> void:
    _ensure_batch()
    var count: int = mini(logical_positions.size(), multimesh.instance_count)
    for i in range(count):
        # Standalone --script tests do not expose autoload global identifiers.
        # Read the source-of-truth scale directly from the P1 script resource.
        var scale: float = FrozenMapping.WORLD_UNITS_PER_PIXEL
        var p: Vector3 = Vector3(logical_positions[i].x * scale, elevation, logical_positions[i].y * scale)
        multimesh.set_instance_transform(i, Transform3D(Basis.IDENTITY, p))
        var tint: Color = tints[i] if i < tints.size() else Color(0.35, 0.92, 1.0)
        multimesh.set_instance_color(i, tint)
    multimesh.visible_instance_count = count

func clear_projectiles() -> void:
    if multimesh != null:
        multimesh.visible_instance_count = 0
