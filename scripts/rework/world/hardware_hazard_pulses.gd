extends Node3D
## S01 D physical world contacts. B alone applies HP, barriers, DoT and CC.
## Values/timing below are BALANCE_DRAFT, not GDD-approved combat tuning.
signal environment_contact_requested(fact: Dictionary)
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const PROFILES := {
    "CPU": {"kind": "core_thermal_pulse", "period": 180, "warning": 60, "radius": 1.05},
    "SSD": {"kind": "transfer_overclock_surge", "period": 150, "warning": 55, "radius": 0.92},
    "HDD": {"kind": "platter_arc_discharge", "period": 210, "warning": 70, "radius": 1.10}
}
var run_id: String = ""
var region_id: String = ""
var geometry: Variant
var session: Variant
var ticks: int = 0
var visual: MeshInstance3D
var last_target: Vector3 = Vector3.INF

func configure(new_run: String, region: String, map_geometry: Node3D,
        objective_session: Node) -> bool:
    if new_run.is_empty() or not PROFILES.has(region) or map_geometry == null             or objective_session == null:
        return false
    run_id = new_run
    region_id = region
    geometry = map_geometry
    session = objective_session
    ticks = 0
    last_target = Vector3.INF
    var profile: Dictionary = PROFILES[region_id]
    visual = MeshInstance3D.new()
    visual.name = region_id + "WarnedHazard"
    var ring := CylinderMesh.new()
    ring.top_radius = float(profile["radius"])
    ring.bottom_radius = float(profile["radius"])
    ring.height = 0.035
    visual.mesh = ring
    var mat := StandardMaterial3D.new()
    mat.albedo_color = Color(1.0, 0.32, 0.08, 0.58)
    mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
    mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    visual.material_override = mat
    visual.visible = false
    add_child(visual)
    return true

func _active_center() -> Vector3:
    if geometry == null or session == null:
        return Vector3.INF
    if region_id == "CPU":
        if not bool(geometry.goal_activated.get("secure_core", false))                 or bool(geometry.goal_completed.get("secure_core", false)):
            return Vector3.INF
        return geometry.objective_world("secure_core")
    if region_id == "SSD":
        if not bool(session.transfer_started) or bool(
                session.objectives.progress["seal_transfer"]["completed"]):
            return Vector3.INF
        return geometry.objective_world("seal_transfer")
    if region_id == "HDD":
        if not bool(session.wave_started):
            return Vector3.INF
        for item in session.targets:
            if str(item["id"]) == "repair_platter" and not bool(item["done"]):
                return item["position"]
    return Vector3.INF

func _clear() -> void:
    ticks = 0
    last_target = Vector3.INF
    if visual != null:
        visual.visible = false

func tick_player(player_world: Vector3) -> void:
    if run_id.is_empty() or visual == null:
        return
    var center: Vector3 = _active_center()
    if not center.is_finite():
        _clear()
        return
    if last_target != center:
        ticks = 0
        last_target = center
    var profile: Dictionary = PROFILES[region_id]
    ticks += 1
    var period: int = int(profile["period"])
    var phase: int = ticks % period
    visual.position = center + Vector3.UP * 0.13
    # Visible warning begins before the strike, never after it.
    visual.visible = phase >= period - int(profile["warning"]) and phase != 0
    if phase != 0:
        return
    # Circular telegraph and hit check share the exact floor-XZ radius.
    if not geometry.can_walk(player_world):
        return
    var offset := Vector2(player_world.x - center.x, player_world.z - center.z)
    if offset.length() > float(profile["radius"]):
        return
    # Do not hit through a solid procedural maze wall.
    if not _clear_floor_sight(center, player_world):
        return
    var cycle: int = ticks / period
    var source: String = region_id + ":" + str(profile["kind"])
    environment_contact_requested.emit({
        "schema": 1, "type": "world_hazard_contact",
        "run_id": run_id, "region_id": region_id, "actor_id": "VACCINE",
        "hazard_kind": str(profile["kind"]),
        "hazard_id": "%s:%d" % [source, cycle],
        "tick": ticks, "cycle": cycle, "source_world": center,
        "player_world": player_world, "contact_radius": float(profile["radius"])
    })

func _clear_floor_sight(start: Vector3, finish: Vector3) -> bool:
    var a := Vector2(start.x, start.z)
    var b := Vector2(finish.x, finish.z)
    var segments: int = maxi(1, ceili(a.distance_to(b) / (Maps.TILE_SIZE * 0.25)))
    for i in range(segments + 1):
        var at: Vector2 = a.lerp(b, float(i) / float(segments))
        if not Maps.is_floor(geometry.map_plan, Vector2i(
                floori(at.x / Maps.TILE_SIZE), floori(at.y / Maps.TILE_SIZE))):
            return false
    return true
