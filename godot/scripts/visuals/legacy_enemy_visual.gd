extends Node3D
## Visual-only view over a C-owned enemy Dictionary. No damage/collision/AI.
const Shapes = preload("res://scripts/visuals/legacy_actor_shapes.gd")
const BossCatalog = preload("res://scripts/enemies/enemy_catalog.gd")
var enemy_kind: String = ""
var boss: bool = false
var radius_px: float = 13.0
var hull: MeshInstance3D
var inset: MeshInstance3D
var outline: MeshInstance3D
var warning_ring: MeshInstance3D
var orbit: Node3D
var beacon: MeshInstance3D
var normal_material: StandardMaterial3D
var hurt_material: StandardMaterial3D
var vulnerable_material: StandardMaterial3D
var dot_material: StandardMaterial3D
var previous_hp: float = -1.0
var hit_frames: int = 0

func configure(kind: String, logical_radius: float) -> void:
    assert(Shapes.COLORS.has(kind), "Never substitute a generic unknown enemy")
    enemy_kind = kind
    boss = BossCatalog.is_boss(kind)
    radius_px = logical_radius
    normal_material = Shapes.lit_material(Shapes.color_for(kind))
    hurt_material = Shapes.lit_material(Color.WHITE)
    vulnerable_material = Shapes.lit_material(Color("#89ffd1"))
    dot_material = Shapes.lit_material(Color("#ff8a3d"))

    hull = MeshInstance3D.new()
    hull.name = "Silhouette"
    hull.mesh = Shapes.mesh_for(kind, logical_radius)
    hull.material_override = normal_material
    add_child(hull)

    outline = MeshInstance3D.new()
    outline.name = "IdentityRing"
    outline.mesh = Shapes.ring_mesh((logical_radius + (14.0 if boss else 5.0)) * Shapes.PX_TO_WORLD)
    outline.material_override = Shapes.lit_material(Color("#ff5cf0") if boss else Shapes.color_for(kind).darkened(0.38))
    outline.position.y = -0.025
    add_child(outline)

    if boss:
        inset = MeshInstance3D.new()
        inset.name = "CounterRotatingCore"
        inset.mesh = Shapes.mesh_for(kind, logical_radius * 0.62)
        inset.material_override = Shapes.lit_material(Color("#5c1058"))
        inset.position.y = 0.045
        add_child(inset)

        orbit = Node3D.new()
        orbit.name = "Orbit"
        add_child(orbit)
        beacon = MeshInstance3D.new()
        beacon.name = "OrbitBeacon"
        var light := SphereMesh.new()
        light.radius = 0.044
        light.height = 0.088
        light.radial_segments = 8
        light.rings = 4
        beacon.mesh = light
        beacon.material_override = Shapes.lit_material(Shapes.color_for(kind).lightened(0.25))
        beacon.position = Vector3((logical_radius + 14.0) * Shapes.PX_TO_WORLD, 0.045, 0.0)
        orbit.add_child(beacon)

    warning_ring = MeshInstance3D.new()
    warning_ring.name = "StatusRing"
    warning_ring.mesh = Shapes.ring_mesh((logical_radius + (10.0 if kind == "rusher" else 7.0)) * Shapes.PX_TO_WORLD, 0.03)
    warning_ring.material_override = Shapes.lit_material(Color("#ff8a3d") if kind == "rusher" else Color("#ffd166"))
    warning_ring.position.y = 0.004
    warning_ring.visible = false
    add_child(warning_ring)

func sync_state(data: Dictionary, reduced_motion: bool = false) -> void:
    # C enemy state is read-only. Visual animation must never modify data.
    var elapsed: float = float(data.get("elapsed", 0.0))
    var angle: float = float(data.get("angle", 0.0))
    var hp: float = float(data.get("hp", 0.0))
    if previous_hp >= 0.0 and hp < previous_hp:
        hit_frames = 6
    previous_hp = hp
    if hit_frames > 0:
        hit_frames -= 1

    var spinning: float = 0.0
    if not reduced_motion:
        if boss:
            spinning = elapsed * 0.9
        elif enemy_kind == "shooter":
            spinning = elapsed * 0.8
        elif enemy_kind == "splitter":
            spinning = elapsed * 0.6
        elif enemy_kind not in ["drifter", "mini", "rusher"]:
            spinning = elapsed * 0.4
    hull.rotation.y = -(angle if enemy_kind in ["drifter", "mini", "rusher"] else spinning)
    if boss:
        inset.rotation.y = spinning * 1.6
        orbit.rotation.y = -spinning * 1.3

    var has_burn: bool = false
    for status in data.get("statuses", []):
        if status is Dictionary and str(status.get("kind", "")) == "burn":
            has_burn = true
            break
    var vulnerable: bool = float(data.get("vulnerable", 0.0)) > 0.0
    if hit_frames > 0:
        hull.material_override = hurt_material
    elif vulnerable:
        hull.material_override = vulnerable_material
    elif has_burn:
        hull.material_override = dot_material
    else:
        hull.material_override = normal_material
    warning_ring.visible = (enemy_kind == "rusher" and int(data.get("state", 0)) == 1) \
        or float(data.get("guard", 0.0)) > 0.0 or vulnerable
    if vulnerable:
        warning_ring.material_override = vulnerable_material
