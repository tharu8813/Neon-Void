extends Node3D
## Original Python view.py's 52 arena stars, 80px grid, scanline and
## 34 in-world hub dust motes / conduit data pulses, expressed as 3D meshes.
## Every element is display-only; it never influences collision or simulation.
const SCALE: float = 0.01
const ARENA_WIDTH: float = 1280.0
const ARENA_HEIGHT: float = 720.0
const HUB_WIDTH: float = 2400.0
const HUB_HEIGHT: float = 1560.0
const STAR_COUNT: int = 52
const DUST_COUNT: int = 34
const MAX_LINK_PULSES: int = 6
const HUB_CENTER := Vector2(1200.0, 780.0)
const BASE_TARGETS := [
    Vector2(1580.0, 780.0), Vector2(820.0, 610.0),
    Vector2(820.0, 1000.0), Vector2(1540.0, 1030.0)
]
const OS_TARGETS := [
    Vector2(1200.0, 270.0), Vector2(1960.0, 780.0),
    Vector2(1200.0, 1320.0), Vector2(440.0, 780.0),
    Vector2(1200.0, 1050.0), Vector2(1200.0, 780.0)
]
var arena_stars: MultiMeshInstance3D
var hub_dust: MultiMeshInstance3D
var arena_grid: MultiMeshInstance3D
var scanline: MeshInstance3D
var core_halo: MeshInstance3D
var link_pulses: Array[MeshInstance3D] = []
var base_halo_material: StandardMaterial3D
var os_halo_material: StandardMaterial3D
var base_pulse_material: StandardMaterial3D
var os_pulse_material: StandardMaterial3D
var star_specs: Array[Dictionary] = []
var dust_specs: Array[Dictionary] = []
var mode: String = "base"
var reduced_effects: bool = false
var clock: float = 0.0

func _ready() -> void:
    var rng := RandomNumberGenerator.new()
    rng.seed = 441022
    arena_stars = _batch("LegacyArenaStars", STAR_COUNT,
        Color("#4787a6"), Vector3(0.025, 0.014, 0.025),
        AABB(Vector3(-0.2, -0.10, -0.2), Vector3(13.2, 0.35, 7.6)))
    for i in range(STAR_COUNT):
        star_specs.append({
            "x": rng.randf_range(0.0, ARENA_WIDTH),
            "y": rng.randf_range(0.0, ARENA_HEIGHT),
            "depth": rng.randf_range(0.15, 0.5),
            "phase": rng.randf_range(0.0, TAU),
            "twinkle": rng.randf_range(0.7, 2.1),
            "drift": rng.randf_range(3.0, 11.0),
            "size": rng.randf_range(0.7, 1.9),
        })
    hub_dust = _batch("LegacyHubDust", DUST_COUNT,
        Color("#5799b5"), Vector3(0.028, 0.012, 0.028),
        AABB(Vector3(-0.2, -0.10, -0.2), Vector3(24.4, 0.35, 16.0)))
    for i in range(DUST_COUNT):
        dust_specs.append({
            "x": rng.randf_range(0.0, HUB_WIDTH),
            "y": rng.randf_range(0.0, HUB_HEIGHT),
            "phase": rng.randf_range(0.0, TAU),
            "speed": rng.randf_range(5.0, 15.0),
            "size": rng.randf_range(0.8, 2.1),
        })
    var total: int = 0
    for x in range(0, 1281, 80):
        total += 1
    for y in range(0, 721, 80):
        total += 1
    arena_grid = _batch("LegacyArenaGrid", total,
        Color("#12283b"), Vector3.ONE,
        AABB(Vector3(-0.1, -0.10, -0.1), Vector3(13.0, 0.35, 7.4)))
    var grid: MultiMesh = arena_grid.multimesh
    var index: int = 0
    for x in range(0, 1281, 80):
        grid.set_instance_transform(index,
            Transform3D(Basis().scaled(Vector3(0.005, 0.006, 7.2)),
                Vector3(float(x) * SCALE, -0.017, 3.6)))
        grid.set_instance_color(index, Color("#12283b"))
        index += 1
    for y in range(0, 721, 80):
        grid.set_instance_transform(index,
            Transform3D(Basis().scaled(Vector3(12.8, 0.006, 0.005)),
                Vector3(6.4, -0.017, float(y) * SCALE)))
        grid.set_instance_color(index, Color("#12283b"))
        index += 1

    scanline = MeshInstance3D.new()
    scanline.name = "MovingArenaScanline"
    var thin := BoxMesh.new()
    thin.size = Vector3(12.8, 0.006, 0.018)
    scanline.mesh = thin
    scanline.material_override = _emissive(Color("#153a50"))
    scanline.position.y = -0.010
    add_child(scanline)

    core_halo = MeshInstance3D.new()
    core_halo.name = "HubCoreBreathingHalo"
    var halo_mesh := TorusMesh.new()
    halo_mesh.inner_radius = 0.984
    halo_mesh.outer_radius = 1.0
    halo_mesh.rings = 48
    halo_mesh.ring_segments = 6
    core_halo.mesh = halo_mesh
    base_halo_material = _emissive(Color("#2f7f92"))
    os_halo_material = _emissive(Color("#353054"))
    base_pulse_material = _emissive(Color("#8ff6ff"))
    os_pulse_material = _emissive(Color("#c3a3ff"))
    core_halo.material_override = base_halo_material
    core_halo.position = Vector3(12.0, 0.075, 7.8)
    add_child(core_halo)

    for i in range(MAX_LINK_PULSES):
        var pulse := MeshInstance3D.new()
        pulse.name = "ConduitDataPulse%d" % i
        var shape := SphereMesh.new()
        shape.radius = 0.060
        shape.height = 0.120
        shape.radial_segments = 8
        shape.rings = 4
        pulse.mesh = shape
        pulse.material_override = base_pulse_material
        add_child(pulse)
        link_pulses.append(pulse)
    set_zone("base", false)
    _draw_frame()

func _emissive(tint: Color) -> StandardMaterial3D:
    var material := StandardMaterial3D.new()
    material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    material.albedo_color = tint
    material.emission_enabled = true
    material.emission = tint
    material.cull_mode = BaseMaterial3D.CULL_DISABLED
    return material

func _batch(id: String, capacity: int, tint: Color, shape: Vector3,
        visibility_bounds: AABB) -> MultiMeshInstance3D:
    var node := MultiMeshInstance3D.new()
    node.name = id
    var base_mesh := BoxMesh.new()
    base_mesh.size = shape
    var material := _emissive(tint)
    material.vertex_color_use_as_albedo = true
    base_mesh.material = material
    var multimesh := MultiMesh.new()
    multimesh.transform_format = MultiMesh.TRANSFORM_3D
    multimesh.use_colors = true
    multimesh.mesh = base_mesh
    multimesh.instance_count = capacity
    multimesh.visible_instance_count = capacity
    node.multimesh = multimesh
    node.custom_aabb = visibility_bounds
    add_child(node)
    return node

func set_zone(next_mode: String, reduced: bool) -> void:
    mode = next_mode
    reduced_effects = reduced
    var arena: bool = next_mode in ["combat", "reward", "between", "clean"]
    var world: bool = next_mode in ["base", "hub"]
    arena_grid.visible = arena
    arena_stars.visible = arena
    scanline.visible = arena and not reduced_effects
    hub_dust.visible = world
    core_halo.visible = world
    for pulse in link_pulses:
        pulse.visible = world and not reduced_effects

func _process(delta: float) -> void:
    if not visible:
        return
    if not reduced_effects:
        clock += maxf(0.0, delta)
    _draw_frame()

func _draw_frame() -> void:
    var stars: MultiMesh = arena_stars.multimesh
    for i in range(STAR_COUNT):
        var sample: Dictionary = star_specs[i]
        var u: float = 0.0 if reduced_effects else clock
        var twinkle: float = 0.62 + 0.38 * sin(
            u * float(sample["twinkle"]) + float(sample["phase"]))
        var y: float = fposmod(float(sample["y"]) +
            u * float(sample["drift"]) + 8.0, ARENA_HEIGHT + 16.0) - 8.0
        var diameter: float = float(sample["size"]) * SCALE * maxf(0.35, twinkle) * 2.0
        stars.set_instance_transform(i, Transform3D(
            Basis().scaled(Vector3(diameter, 0.012, diameter)),
            Vector3(float(sample["x"]) * SCALE, -0.004, y * SCALE)))
        stars.set_instance_color(i, Color("#467d97").lerp(
            Color("#7ac9dc"), float(sample["depth"])))
    var motes: MultiMesh = hub_dust.multimesh
    for i in range(DUST_COUNT):
        var sample: Dictionary = dust_specs[i]
        var y: float = fposmod(float(sample["y"]) +
            clock * float(sample["speed"]), HUB_HEIGHT)
        var w: float = 0.6 + 0.4 * sin(clock * 1.5 + float(sample["phase"]))
        var size: float = float(sample["size"]) * SCALE * (0.65 + w * 0.35)
        motes.set_instance_transform(i, Transform3D(
            Basis().scaled(Vector3(size, 0.012, size)),
            Vector3(float(sample["x"]) * SCALE, 0.012, y * SCALE)))
        motes.set_instance_color(i, Color("#4f9bb5") if mode == "base" else Color("#716ba8"))
    if scanline.visible:
        scanline.position.z = fposmod(clock * 34.0 * SCALE, 7.2)
    if core_halo.visible:
        var breath: float = 1.0 if reduced_effects else 1.0 + 0.045 * sin(clock * 1.9)
        var radius: float = (0.72 if mode == "base" else 1.32) * breath
        core_halo.scale = Vector3(radius, 1.0, radius)
        core_halo.material_override = base_halo_material if mode == "base" else os_halo_material
    var targets: Array = BASE_TARGETS if mode == "base" else OS_TARGETS
    for i in range(MAX_LINK_PULSES):
        var marker: MeshInstance3D = link_pulses[i]
        marker.visible = mode in ["base", "hub"] and not reduced_effects and i < targets.size()
        if not marker.visible:
            continue
        var travel: float = fposmod(clock * 0.42 + float(i) * 0.23, 1.0)
        var point: Vector2 = HUB_CENTER.lerp(targets[i], travel)
        marker.position = Vector3(point.x * SCALE, 0.21, point.y * SCALE)
        var radius: float = 0.042 + 0.016 * sin(clock * 6.0 + float(i))
        marker.scale = Vector3.ONE * maxf(0.65, radius / 0.06)
        marker.material_override = base_pulse_material if mode == "base" else os_pulse_material
