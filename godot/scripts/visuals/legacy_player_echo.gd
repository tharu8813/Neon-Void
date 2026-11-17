extends Node3D
## Source Python step_player creates a translucent dash trail. The 3D
## equivalent reuses the exact actor mesh without physics/collision nodes.
const MAX_ECHOES: int = 14
const ECHO_LIFETIME: float = 0.28
const ECHO_INTERVAL: float = 0.028
var pool: Array[MeshInstance3D] = []
var ages: PackedFloat32Array = PackedFloat32Array()
var materials: Array[StandardMaterial3D] = []
var next_slot: int = 0
var sample_clock: float = 0.0

func _ready() -> void:
    for i in range(MAX_ECHOES):
        var ghost := MeshInstance3D.new()
        ghost.name = "DashAfterimage%d" % i
        ghost.visible = false
        ghost.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
        var surface := StandardMaterial3D.new()
        surface.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
        surface.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
        surface.cull_mode = BaseMaterial3D.CULL_DISABLED
        surface.albedo_color = Color(0.13, 0.63, 0.79, 0.0)
        surface.emission_enabled = true
        surface.emission = Color("#1b93a8")
        surface.emission_energy_multiplier = 0.24
        ghost.material_override = surface
        add_child(ghost)
        pool.append(ghost)
        materials.append(surface)
        ages.append(0.0)

func bind_mesh(actor_mesh: Mesh) -> void:
    for node in pool:
        node.mesh = actor_mesh

func sync_echo(location: Vector3, yaw: float, dash_active: bool,
        reduced_effects: bool, delta: float) -> void:
    if reduced_effects:
        clear_echoes()
        return
    var dt: float = maxf(0.0, delta)
    for i in range(MAX_ECHOES):
        if ages[i] <= 0.0:
            pool[i].visible = false
            continue
        ages[i] = maxf(0.0, ages[i] - dt)
        pool[i].visible = ages[i] > 0.0
        if pool[i].visible:
            var tint: Color = Color("#3ba7bf")
            tint.a = 0.27 * ages[i] / ECHO_LIFETIME
            materials[i].albedo_color = tint
            var scale_factor: float = 0.87 + 0.13 * ages[i] / ECHO_LIFETIME
            pool[i].scale = Vector3.ONE * scale_factor
    if not dash_active:
        sample_clock = 0.0
        return
    sample_clock += dt
    if sample_clock < ECHO_INTERVAL:
        return
    sample_clock = fposmod(sample_clock, ECHO_INTERVAL)
    var index: int = next_slot
    next_slot = (next_slot + 1) % MAX_ECHOES
    ages[index] = ECHO_LIFETIME
    pool[index].position = location
    pool[index].rotation.y = yaw
    pool[index].scale = Vector3.ONE
    pool[index].visible = true
    materials[index].albedo_color = Color(0.13, 0.63, 0.79, 0.27)

func clear_echoes() -> void:
    sample_clock = 0.0
    for i in range(MAX_ECHOES):
        ages[i] = 0.0
        pool[i].visible = false
