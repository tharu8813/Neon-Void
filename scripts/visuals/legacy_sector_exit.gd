extends Node3D
## Frozen Python view.py::_build_sector_gate/_render_sector_gate expressed
## as inert 3D geometry. F interaction authority stays in Main.
const EXIT_POSITION := Vector2(100.0, 360.0)
const REACH: float = 112.0
const SCALE: float = 0.01
const NORMAL := Color("#327e8b")
const NEAR := Color("#78f0cf")
var frame: MeshInstance3D
var inner_bar: MeshInstance3D
var scan: MeshInstance3D
var glow_ring: MeshInstance3D
var title: Label3D
var prompt: Label3D
var animation_clock: float = 0.0
var near_exit: bool = false

func _ready() -> void:
    name = "LegacySectorExit"
    var body := BoxMesh.new()
    body.size = Vector3(0.46, 0.11, 1.40)
    frame = MeshInstance3D.new()
    frame.name = "GateFrame"
    frame.mesh = body
    frame.position = Vector3(0.47, 0.08, EXIT_POSITION.y * SCALE)
    add_child(frame)

    var bar_mesh := BoxMesh.new()
    bar_mesh.size = Vector3(0.032, 0.06, 1.00)
    inner_bar = MeshInstance3D.new()
    inner_bar.name = "GateBar"
    inner_bar.mesh = bar_mesh
    inner_bar.position = Vector3(0.47, 0.16, EXIT_POSITION.y * SCALE)
    add_child(inner_bar)

    var scan_mesh := BoxMesh.new()
    scan_mesh.size = Vector3(0.34, 0.012, 0.020)
    scan = MeshInstance3D.new()
    scan.name = "MovingScan"
    scan.mesh = scan_mesh
    scan.position = Vector3(0.47, 0.19, EXIT_POSITION.y * SCALE - 0.58)
    add_child(scan)

    var torus := TorusMesh.new()
    torus.inner_radius = 0.975
    torus.outer_radius = 1.0
    torus.rings = 40
    torus.ring_segments = 4
    glow_ring = MeshInstance3D.new()
    glow_ring.name = "NearGlow"
    glow_ring.mesh = torus
    glow_ring.material_override = _material(NEAR, false)
    glow_ring.position = Vector3(0.47, 0.025, EXIT_POSITION.y * SCALE)
    add_child(glow_ring)

    title = _label("SectorExitLabel", 0.84, 3.43, 26)
    prompt = _label("SectorExitPrompt", 0.84, 3.76, 21)
    prompt.modulate = Color("#d4fbff")
    title.text = "CPU CORE · EXIT"
    visible = false

func _material(tint: Color, glow: bool) -> StandardMaterial3D:
    var material := StandardMaterial3D.new()
    material.albedo_color = tint
    material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    material.cull_mode = BaseMaterial3D.CULL_DISABLED
    if glow:
        material.emission_enabled = true
        material.emission = tint
    return material

func _label(id: String, x: float, z: float, font_px: int) -> Label3D:
    var value := Label3D.new()
    value.name = id
    value.font_size = font_px
    value.pixel_size = 0.006
    value.position = Vector3(x, 0.22, z)
    value.rotation.x = -PI / 2.0
    value.no_depth_test = true
    value.fixed_size = false
    add_child(value)
    return value

static func can_interact(logical_position: Vector2) -> bool:
    return logical_position.distance_to(EXIT_POSITION) < REACH

func sync_gate(logical_position: Vector2, sector_name: String,
        sector_clean: bool, interact_key: String, reduced: bool,
        delta: float) -> void:
    if not visible:
        return
    animation_clock += maxf(0.0, delta)
    near_exit = can_interact(logical_position)
    var tint: Color = NEAR if near_exit else NORMAL
    frame.material_override = _material(Color("#0d2f3d") if near_exit else Color("#0a2531"), false)
    inner_bar.material_override = _material(tint, true)
    title.modulate = tint
    title.text = sector_name + " · EXIT"
    prompt.visible = near_exit
    prompt.text = "[%s] 복귀 · %s" % [interact_key.to_upper(),
        "정화 완료" if sector_clean else "현재 웨이브 재시작 / 확정 보상 유지"]
    scan.visible = not reduced
    if scan.visible:
        var scan_ratio: float = fposmod(animation_clock * 0.55, 1.0)
        scan.position.z = EXIT_POSITION.y * SCALE - 0.58 + scan_ratio * 1.16
        scan.material_override = _material(Color("#9ff5e0") if near_exit else Color("#3f8b96"), true)
    glow_ring.visible = near_exit and not reduced
    if glow_ring.visible:
        var growth: float = fposmod(animation_clock * 1.1, 1.0)
        var radius: float = (34.0 + growth * 26.0) * SCALE
        glow_ring.scale = Vector3(radius, 1.0, radius)
