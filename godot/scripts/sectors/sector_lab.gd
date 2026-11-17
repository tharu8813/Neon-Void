extends "res://scripts/combat/combat_lab.gd"
## Isolated 3D four-sector end-to-end harness. Do not replace A's main scene.
## Reuses P4 primitive combat visuals and a temporary test-only input loop.
const Campaign = preload("res://scripts/sectors/sector_campaign.gd")
const SectorCatalog = preload("res://scripts/sectors/sector_catalog.gd")

const GATES: Dictionary = {
    "CPU": Vector2(1200, 270),
    "MEMORY": Vector2(1960, 780),
    "STORAGE": Vector2(1200, 1320),
    "NETWORK": Vector2(440, 780)
}
const STATION: Vector2 = Vector2(1200, 1050)
const STATUS: Vector2 = Vector2(1200, 780)

var campaign = Campaign.new("godot-sector-lab-42", 42)
var mode: String = "hub"
var hub_position: Vector2 = Vector2(1200, 780)
var gate_visuals: Array[Node3D] = []
var purifications_left: int = 3
var f_down: bool = false
var r_down_local: bool = false

func _ready() -> void:
    _build_scene()
    _build_hub()
    sim.player_hit.connect(_on_combat_hit)
    sim.wave_ready.connect(_on_combat_wave_ready)
    sim.active = false
    sim.player["pos"] = hub_position
    _show_gates(true)

func _build_hub() -> void:
    var ground: MeshInstance3D = MeshInstance3D.new()
    var box: BoxMesh = BoxMesh.new()
    box.size = Vector3(24.0, 0.08, 15.6)
    ground.mesh = box
    ground.position = Vector3(12.0, -0.16, 7.8)
    var material: StandardMaterial3D = StandardMaterial3D.new()
    material.albedo_color = Color(0.025, 0.035, 0.08)
    ground.material_override = material
    add_child(ground)
    ground.visible = false
    gate_visuals.append(ground)
    for id in GATES:
        var gate: MeshInstance3D = MeshInstance3D.new()
        var cylinder: CylinderMesh = CylinderMesh.new()
        cylinder.top_radius = 0.9
        cylinder.bottom_radius = 0.9
        cylinder.height = 0.1
        gate.mesh = cylinder
        gate.position = _xyz(GATES[id], 0.02)
        var gate_mat: StandardMaterial3D = StandardMaterial3D.new()
        gate_mat.albedo_color = Color(0.10, 0.61, 0.82)
        gate.material_override = gate_mat
        add_child(gate)
        gate_visuals.append(gate)
        var label_3d: Label3D = Label3D.new()
        label_3d.text = str(SectorCatalog.SECTORS[id]["name"])
        label_3d.font_size = 38
        label_3d.pixel_size = 0.006
        label_3d.billboard = BaseMaterial3D.BILLBOARD_ENABLED
        label_3d.position = _xyz(GATES[id], 0.35)
        add_child(label_3d)
        gate_visuals.append(label_3d)
    for location in [STATION, STATUS]:
        var marker: MeshInstance3D = MeshInstance3D.new()
        var shape: CylinderMesh = CylinderMesh.new()
        shape.top_radius = 0.65
        shape.bottom_radius = 0.65
        shape.height = 0.1
        marker.mesh = shape
        marker.position = _xyz(location, 0.01)
        var paint: StandardMaterial3D = StandardMaterial3D.new()
        paint.albedo_color = Color(0.2, 0.85, 0.4) if location == STATION else Color(0.95, 0.78, 0.25)
        marker.material_override = paint
        add_child(marker)
        gate_visuals.append(marker)

func _xyz(point: Vector2, height: float = 0.1) -> Vector3:
    return Vector3(point.x * SCALE, height, point.y * SCALE)

func _show_gates(visible_now: bool) -> void:
    for visual in gate_visuals:
        visual.visible = visible_now

func enter_sector_lab(id: String) -> bool:
    if mode != "hub" or not campaign.enter_sector(id):
        return false
    hub_position = sim.player["pos"]
    mode = "combat"
    sim.player["pos"] = Vector2(195.0, 360.0)
    camera.position = _xyz(Vector2(640.0, 360.0), 14.0)
    _show_gates(false)
    _start_sector_wave()
    return true

func _start_sector_wave() -> void:
    var plan: Dictionary = campaign.start_wave()
    if plan.is_empty():
        mode = "clean"
        return
    sim.begin_wave(int(plan["wave_index"]), plan["spawns"], str(plan["boss_kind"]))
    mode = "combat"

func retreat_lab() -> void:
    if mode == "hub":
        return
    sim.abort_wave()
    campaign.retreat()
    sim.player["pos"] = hub_position
    mode = "hub"
    _show_gates(true)

func _on_combat_hit(_damage: float, infection_delta: float) -> void:
    campaign.apply_infection(infection_delta)

func _on_combat_wave_ready(boss_confirmed: bool) -> void:
    if mode != "combat":
        return
    var confirmed: bool = campaign.confirm_wave(sim.enemies.size(), sim.spawn_queue.size(), boss_confirmed)
    if not confirmed:
        return
    sim.abort_wave()
    mode = "clean" if campaign.is_sector_clean(campaign.active_sector) else "between"

func _hub_use() -> void:
    var pos: Vector2 = sim.player["pos"]
    for sector_id in GATES:
        var id: String = str(sector_id)
        if pos.distance_to(GATES[id]) <= (120.0 if id == "CPU" else 115.0):
            enter_sector_lab(id)
            return
    if pos.distance_to(STATION) <= 103.0 and purifications_left > 0:
        campaign.purify(32.0)
        campaign.repair_integrity(9.0)
        purifications_left -= 1

func _physics_process(delta: float) -> void:
    var move: Vector2 = Vector2.ZERO
    if Input.is_key_pressed(KEY_A) or Input.is_key_pressed(KEY_LEFT):
        move.x -= 1.0
    if Input.is_key_pressed(KEY_D) or Input.is_key_pressed(KEY_RIGHT):
        move.x += 1.0
    if Input.is_key_pressed(KEY_W) or Input.is_key_pressed(KEY_UP):
        move.y -= 1.0
    if Input.is_key_pressed(KEY_S) or Input.is_key_pressed(KEY_DOWN):
        move.y += 1.0
    var f: bool = Input.is_key_pressed(KEY_F)
    var r: bool = Input.is_key_pressed(KEY_R)
    if mode == "hub":
        var next: Vector2 = Vector2(sim.player["pos"]) + move.normalized() * 322.0 * delta
        sim.player["pos"] = Vector2(clampf(next.x, 13.0, 2387.0), clampf(next.y, 13.0, 1547.0))
        var center: Vector2 = Vector2(clampf(next.x, 640.0, 1760.0), clampf(next.y, 360.0, 1200.0))
        camera.position = _xyz(center, 14.0)
        if f and not f_down:
            _hub_use()
    elif mode == "combat":
        var pos: Vector2 = Vector2(sim.player["pos"]) + move.normalized() * 322.0 * delta
        pos = Vector2(clampf(pos.x, 13.0, 1267.0), clampf(pos.y, 13.0, 707.0))
        var from_ray: Vector3 = camera.project_ray_origin(get_viewport().get_mouse_position())
        var ray: Vector3 = camera.project_ray_normal(get_viewport().get_mouse_position())
        var aim: float = float(sim.player["aim"])
        if absf(ray.y) > 0.00001:
            var hit: Vector3 = from_ray + ray * (-from_ray.y / ray.y)
            aim = (Vector2(hit.x, hit.z) / SCALE - pos).angle()
        sim.set_player_snapshot({"pos": pos, "aim": aim})
        var q: bool = Input.is_key_pressed(KEY_Q)
        var e: bool = Input.is_key_pressed(KEY_E)
        if q and not q_down:
            sim.try_skill("pulse")
        if e and not e_down:
            sim.try_skill("guard")
        q_down = q
        e_down = e
        sim.step_fixed()
        campaign.tick_infection(1.0 / 60.0)
        if campaign.failed:
            retreat_lab()
    else:
        if mode == "between" and f and not f_down:
            _start_sector_wave()
    if mode != "hub" and r and not r_down_local:
        retreat_lab()
    f_down = f
    r_down_local = r
    _draw_combat()
    var current: String = campaign.active_sector if not campaign.active_sector.is_empty() else "OS HUB"
    var objective: String = "WASD move | F interact with gates/station | 3 cleanses" if mode == "hub" else (
        "F next wave | R retreat" if mode == "between" else ("Sector CLEAN! R retreat" if mode == "clean" else "Q/E skills | R retreat midwave"))
    var all_clean: String = " | OS CLEAN: status terminal available (no auto extraction)" if campaign.is_os_clean() else ""
    hud.text = "P5 SECTOR LAB  /  %s  /  %s  /  Wave %d of 5\nHP %.0f  MP %.0f  Infection %.1f%%  Integrity %.1f%%  Purify %d%s\n%s" % [
        current, mode, campaign.active_wave if campaign.active_wave > 0 else (int(campaign.progress.get(current, 0))),
        float(sim.player["hp"]), float(sim.player["mana"]), campaign.infection,
        campaign.integrity, purifications_left, all_clean, objective
    ]
