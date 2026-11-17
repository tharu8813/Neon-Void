extends Node3D
## Standalone P4 QA scene. Only primitive 3D geometry and test-only WASD input.
## A's main scene and B's player/input files are intentionally untouched.
const Combat = preload("res://scripts/combat/combat_sim.gd")
const Catalog = preload("res://scripts/enemies/enemy_catalog.gd")
const SCALE: float = 0.01
var sim = Combat.new(42)
var camera: Camera3D
var hud: Label
var visual_player: MeshInstance3D
var objects: Dictionary = {}
var q_down: bool = false
var e_down: bool = false
var r_down: bool = false

func _ready() -> void:
    _build_scene()
    _begin_wave()

func _build_scene() -> void:
    var light: DirectionalLight3D = DirectionalLight3D.new()
    light.rotation_degrees = Vector3(-55.0, -25.0, 10.0)
    light.light_energy = 1.25
    add_child(light)
    var floor_mesh: BoxMesh = BoxMesh.new()
    floor_mesh.size = Vector3(12.8, 0.1, 7.2)
    var floor_actor: MeshInstance3D = MeshInstance3D.new()
    floor_actor.mesh = floor_mesh
    floor_actor.position = Vector3(6.4, -0.12, 3.6)
    var floor_material: StandardMaterial3D = StandardMaterial3D.new()
    floor_material.albedo_color = Color(0.025, 0.05, 0.11)
    floor_material.metallic = 0.2
    floor_actor.material_override = floor_material
    add_child(floor_actor)
    camera = Camera3D.new()
    camera.name = "CombatLabCamera"
    camera.projection = Camera3D.PROJECTION_ORTHOGONAL
    camera.keep_aspect = Camera3D.KEEP_HEIGHT
    camera.size = 7.2
    camera.position = Vector3(6.4, 14.0, 3.6)
    camera.rotation_degrees = Vector3(-90.0, 0.0, 0.0)
    camera.current = true
    add_child(camera)
    visual_player = _make_orb(Color(0.27, 0.94, 0.94), 0.13)
    var overlay: CanvasLayer = CanvasLayer.new()
    add_child(overlay)
    hud = Label.new()
    hud.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
    hud.position = Vector2(20.0, 15.0)
    hud.add_theme_font_size_override("font_size", 21)
    overlay.add_child(hud)

func _make_orb(color: Color, radius: float) -> MeshInstance3D:
    var orb: MeshInstance3D = MeshInstance3D.new()
    var sphere: SphereMesh = SphereMesh.new()
    sphere.radius = radius
    sphere.height = radius * 2.0
    orb.mesh = sphere
    var material: StandardMaterial3D = StandardMaterial3D.new()
    material.albedo_color = color
    material.metallic = 0.38
    material.roughness = 0.28
    orb.material_override = material
    add_child(orb)
    return orb

func _begin_wave() -> void:
    ## CPU wave 1 seed=42: eight overheaters at 0.4+0.605*n seconds.
    var scheduled: Array = []
    for i in range(8):
        scheduled.append({"at": 0.4 + 0.605 * float(i), "kind": "overheater"})
    sim.begin_wave(1, scheduled)
    for object in objects.values():
        if is_instance_valid(object):
            object.queue_free()
    objects.clear()

func _physics_process(delta: float) -> void:
    ## This independent harness runs at 60 physics FPS; B remains authoritative
    ## for final movement, dash and keybindings when worlds are integrated.
    var move: Vector2 = Vector2.ZERO
    if Input.is_key_pressed(KEY_A) or Input.is_key_pressed(KEY_LEFT):
        move.x -= 1.0
    if Input.is_key_pressed(KEY_D) or Input.is_key_pressed(KEY_RIGHT):
        move.x += 1.0
    if Input.is_key_pressed(KEY_W) or Input.is_key_pressed(KEY_UP):
        move.y -= 1.0
    if Input.is_key_pressed(KEY_S) or Input.is_key_pressed(KEY_DOWN):
        move.y += 1.0
    var player_pos: Vector2 = sim.player["pos"]
    player_pos += move.normalized() * 322.0 * delta
    player_pos = Vector2(clampf(player_pos.x, 13.0, 1267.0), clampf(player_pos.y, 13.0, 707.0))
    var from_ray: Vector3 = camera.project_ray_origin(get_viewport().get_mouse_position())
    var ray: Vector3 = camera.project_ray_normal(get_viewport().get_mouse_position())
    var aim: float = float(sim.player["aim"])
    if absf(ray.y) > 0.00001:
        var point: Vector3 = from_ray + ray * (-from_ray.y / ray.y)
        aim = (Vector2(point.x, point.z) / SCALE - player_pos).angle()
    sim.set_player_snapshot({"pos": player_pos, "aim": aim})
    var q: bool = Input.is_key_pressed(KEY_Q)
    var e: bool = Input.is_key_pressed(KEY_E)
    var r: bool = Input.is_key_pressed(KEY_R)
    if q and not q_down:
        sim.try_skill("pulse")
    if e and not e_down:
        sim.try_skill("guard")
    if r and not r_down:
        _begin_wave()
    q_down = q
    e_down = e
    r_down = r
    sim.step_fixed()
    _draw_combat()

func _draw_combat() -> void:
    var pos: Vector2 = sim.player["pos"]
    visual_player.position = Vector3(pos.x * SCALE, 0.15, pos.y * SCALE)
    var live: Dictionary = {}
    for enemy in sim.enemies:
        var key: String = "enemy-%d" % int(enemy["id"])
        live[key] = true
        var orb: MeshInstance3D = objects.get(key)
        if orb == null:
            orb = _make_orb(Color(1.0, 0.3, 0.25) if bool(enemy["boss"]) else Color(0.73, 0.37, 0.9), float(enemy["radius"]) * SCALE)
            objects[key] = orb
        var p: Vector2 = enemy["pos"]
        orb.position = Vector3(p.x * SCALE, 0.1, p.y * SCALE)
    for i in range(sim.bullets.size()):
        var key: String = "bullet-%d" % i
        live[key] = true
        var orb: MeshInstance3D = objects.get(key)
        if orb == null:
            orb = _make_orb(Color(0.22, 0.9, 1.0), 0.04)
            objects[key] = orb
        var p: Vector2 = sim.bullets[i]["pos"]
        orb.position = Vector3(p.x * SCALE, 0.1, p.y * SCALE)
    for i in range(sim.enemy_bullets.size()):
        var key: String = "hostile-%d" % i
        live[key] = true
        var orb: MeshInstance3D = objects.get(key)
        if orb == null:
            orb = _make_orb(Color(1.0, 0.52, 0.18), 0.05)
            objects[key] = orb
        var p: Vector2 = sim.enemy_bullets[i]["pos"]
        orb.position = Vector3(p.x * SCALE, 0.1, p.y * SCALE)
    for key in objects.keys():
        if not live.has(key):
            if is_instance_valid(objects[key]):
                objects[key].queue_free()
            objects.erase(key)
    var ready: String = " WAVE CLEAR! Press R to retry" if sim.wave_ready_sent else ""
    hud.text = "P4 3D COMBAT LAB / seed 42 | HP %.0f | MP %.0f | Enemies %d | Bullets %d%s\nWASD/Arrows move  ·  Mouse aim  ·  Q pulse  ·  E guard  ·  R reset" % [
        float(sim.player["hp"]), float(sim.player["mana"]), sim.enemies.size(), sim.enemy_bullets.size(), ready
    ]
