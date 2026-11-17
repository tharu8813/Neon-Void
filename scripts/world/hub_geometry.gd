extends RefCounted
## Read-only visual geometry for the frozen Python 2400x1560 hub.
## World/collision/interaction authority stays with B's LegacyPlayer and A's
## terminal coordinates. Meshes deliberately have no collision shapes.
const SCALE: float = 0.01
const WORLD_W: int = 2400
const WORLD_H: int = 1560
const GRID_STEP: int = 120
const CORE_PX: Vector2 = Vector2(1200.0, 780.0)
const PILLARS := [
    Rect2(1030.0, 515.0, 40.0, 95.0),
    Rect2(1330.0, 515.0, 40.0, 95.0),
    Rect2(1030.0, 965.0, 40.0, 95.0),
    Rect2(1330.0, 965.0, 40.0, 95.0),
]

static func build(parent: Node3D, antivirus_base: bool, destinations: Array[Vector2]) -> void:
    var decoration := Node3D.new()
    decoration.name = "HubDecoration"
    parent.add_child(decoration)
    var grid := Node3D.new()
    grid.name = "Grid"
    decoration.add_child(grid)
    var center := Node3D.new()
    center.name = "CentralCore"
    decoration.add_child(center)
    var links := Node3D.new()
    links.name = "Conduits"
    decoration.add_child(links)
    var pillars := Node3D.new()
    pillars.name = "Pillars"
    decoration.add_child(pillars)

    # Match the two original Python hub palette variants and 120px grid.
    var grid_color: Color = Color("#153043") if antivirus_base else Color("#27243e")
    var core_color: Color = Color("#65e9fc") if antivirus_base else Color("#ad8cf8")
    var grid_material: StandardMaterial3D = _material(grid_color, true)
    for px in range(0, WORLD_W + 1, GRID_STEP):
        _box(grid, Vector3(float(px) * SCALE, 0.005, float(WORLD_H) * SCALE * 0.5),
            Vector3(0.014, 0.01, float(WORLD_H) * SCALE), grid_material)
    for py in range(0, WORLD_H + 1, GRID_STEP):
        _box(grid, Vector3(float(WORLD_W) * SCALE * 0.5, 0.005, float(py) * SCALE),
            Vector3(float(WORLD_W) * SCALE, 0.01, 0.014), grid_material)

    # Original 2D concentric core becomes concentric low-profile 3D rings.
    var dark: StandardMaterial3D = _material(Color("#09212e") if antivirus_base else Color("#171429"), false)
    var plinth := CylinderMesh.new()
    plinth.top_radius = 1.57
    plinth.bottom_radius = 1.57
    plinth.height = 0.085
    var disc := MeshInstance3D.new()
    disc.name = "CorePlinth"
    disc.mesh = plinth
    disc.material_override = dark
    disc.position = Vector3(CORE_PX.x * SCALE, 0.07, CORE_PX.y * SCALE)
    center.add_child(disc)
    _ring(center, CORE_PX * SCALE, 1.35, _material(core_color.darkened(0.42), true))
    _ring(center, CORE_PX * SCALE, 0.91, _material(core_color, true))
    var core := CylinderMesh.new()
    core.top_radius = 0.22
    core.bottom_radius = 0.28
    core.height = 0.32
    var core_light := MeshInstance3D.new()
    core_light.name = "CoreLight"
    core_light.mesh = core
    core_light.material_override = _material(core_color, true)
    core_light.position = Vector3(CORE_PX.x * SCALE, 0.27, CORE_PX.y * SCALE)
    center.add_child(core_light)

    # The original links run from the core to physically present terminals.
    var track_material: StandardMaterial3D = _material(Color("#19384b"), true)
    for target in destinations:
        var from: Vector2 = CORE_PX * SCALE
        var to: Vector2 = target * SCALE
        var delta: Vector2 = to - from
        if delta.length() < 0.01:
            continue
        var mid: Vector2 = (from + to) * 0.5
        _box(links, Vector3(mid.x, 0.024, mid.y),
            Vector3(0.10, 0.02, delta.length()), track_material,
            atan2(delta.x, delta.y))

    # Four visible boxes match the *existing* B collision rectangles exactly.
    var pillar_material: StandardMaterial3D = _material(Color("#15384c"), false)
    for rectangle in PILLARS:
        var half: Vector2 = rectangle.size * SCALE * 0.5
        var midpoint: Vector2 = rectangle.position * SCALE + half
        _box(pillars, Vector3(midpoint.x, 0.34, midpoint.y),
            Vector3(rectangle.size.x * SCALE, 0.68, rectangle.size.y * SCALE),
            pillar_material)

static func _material(tint: Color, glow: bool) -> StandardMaterial3D:
    var value := StandardMaterial3D.new()
    value.albedo_color = tint
    value.metallic = 0.12
    value.roughness = 0.55
    if glow:
        value.emission_enabled = true
        value.emission = tint
        value.emission_energy_multiplier = 1.2
    return value

static func _box(parent: Node3D, at: Vector3, dimensions: Vector3,
        material: StandardMaterial3D, yaw: float = 0.0) -> void:
    var cube := BoxMesh.new()
    cube.size = dimensions
    var visual := MeshInstance3D.new()
    visual.mesh = cube
    visual.material_override = material
    visual.position = at
    visual.rotation.y = yaw
    parent.add_child(visual)

static func _ring(parent: Node3D, middle: Vector2, radius: float,
        material: StandardMaterial3D) -> void:
    var segments: int = 40
    for i in range(segments):
        var a: float = TAU * float(i) / float(segments)
        var b: float = TAU * float(i + 1) / float(segments)
        var begin: Vector2 = middle + Vector2(cos(a), sin(a)) * radius
        var end: Vector2 = middle + Vector2(cos(b), sin(b)) * radius
        var delta: Vector2 = end - begin
        var midpoint: Vector2 = (begin + end) * 0.5
        _box(parent, Vector3(midpoint.x, 0.12, midpoint.y),
            Vector3(0.031, 0.027, delta.length()), material,
            atan2(delta.x, delta.y))
