extends Node3D
## Reinstates Python game.py::burst's short-lived motes and source-colored
## impact/death particles. C gameplay RNG and all hitboxes remain untouched.
const SCALE: float = 0.01
const MAX_PARTICLES: int = 380
var particles: Array[Dictionary] = []
var cloud: MultiMeshInstance3D
var rng: RandomNumberGenerator = RandomNumberGenerator.new()
var reduced_effects: bool = false

func _ready() -> void:
    rng.seed = 119204
    cloud = MultiMeshInstance3D.new()
    cloud.name = "InstancedImpactParticles"
    var sprite := BoxMesh.new()
    sprite.size = Vector3.ONE
    var paint := StandardMaterial3D.new()
    paint.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    paint.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
    paint.vertex_color_use_as_albedo = true
    paint.cull_mode = BaseMaterial3D.CULL_DISABLED
    sprite.material = paint
    var batch := MultiMesh.new()
    batch.transform_format = MultiMesh.TRANSFORM_3D
    batch.use_colors = true
    batch.mesh = sprite
    batch.instance_count = MAX_PARTICLES
    batch.visible_instance_count = 0
    cloud.multimesh = batch
    cloud.custom_aabb = AABB(Vector3(-0.9, 0.0, -0.9),
        Vector3(14.5, 1.4, 9.2))
    add_child(cloud)

func set_reduced_effects(enabled: bool) -> void:
    reduced_effects = enabled
    if enabled:
        clear_particles()

func emit_sparks(logical: Vector2, kind: String = "impact") -> void:
    if reduced_effects:
        return
    var n: int = 4
    var speed: float = 105.0
    var duration: float = 0.24
    var radius: float = 1.8
    var tint: Color = Color("#ccff4d")
    match kind:
        "critical":
            n = 10
            speed = 165.0
            duration = 0.37
            radius = 3.0
            tint = Color("#fff0a6")
        "kill":
            n = 14
            speed = 190.0
            duration = 0.40
            radius = 2.4
            tint = Color("#ff9fbb")
        "boss":
            n = 38
            speed = 290.0
            duration = 0.53
            radius = 3.8
            tint = Color("#ff5cf0")
        "explosion":
            n = 17
            speed = 230.0
            duration = 0.35
            radius = 2.8
            tint = Color("#ffd166")
        "dash":
            n = 7
            speed = 85.0
            duration = 0.30
            radius = 4.0
            tint = Color("#1b93a8")
        "pulse":
            n = 24
            speed = 270.0
            duration = 0.42
            radius = 2.3
            tint = Color("#6ff2ff")
        "purify":
            n = 30
            speed = 200.0
            duration = 0.50
            radius = 2.9
            tint = Color("#3ff5a0")
    var remaining: int = mini(n, MAX_PARTICLES - particles.size())
    for i in range(remaining):
        var angle: float = rng.randf_range(0.0, TAU)
        var magnitude: float = rng.randf_range(speed * 0.35, speed)
        var life: float = rng.randf_range(duration * 0.55, duration)
        particles.append({
            "pos": logical,
            "vel": Vector2.RIGHT.rotated(angle) * magnitude,
            "life": life,
            "total": life,
            "radius": rng.randf_range(0.9, radius),
            "color": tint,
        })

func _process(delta: float) -> void:
    if cloud == null:
        return
    var dt: float = maxf(0.0, delta)
    var kept: Array[Dictionary] = []
    for particle in particles:
        var life: float = float(particle["life"]) - dt
        if life <= 0.0:
            continue
        particle["life"] = life
        var vel: Vector2 = particle["vel"]
        particle["pos"] = Vector2(particle["pos"]) + vel * dt
        particle["vel"] = vel * maxf(0.0, 1.0 - 2.6 * dt)
        kept.append(particle)
    particles = kept
    var batch: MultiMesh = cloud.multimesh
    var count: int = mini(MAX_PARTICLES, particles.size())
    for i in range(count):
        var sample: Dictionary = particles[i]
        var ratio: float = clampf(float(sample["life"]) / maxf(0.01, float(sample["total"])), 0.0, 1.0)
        var p: Vector2 = sample["pos"]
        var d: float = float(sample["radius"]) * SCALE * (0.25 + 0.75 * ratio) * 2.0
        batch.set_instance_transform(i, Transform3D(
            Basis().scaled(Vector3(d, 0.018, d)),
            Vector3(p.x * SCALE, 0.19, p.y * SCALE)))
        var color: Color = sample["color"]
        color.a = ratio * 0.90
        batch.set_instance_color(i, color)
    batch.visible_instance_count = count

func clear_particles() -> void:
    particles.clear()
    if cloud != null:
        cloud.multimesh.visible_instance_count = 0
