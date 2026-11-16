extends Node3D
## A-owned integration boundary. Source gameplay mechanics remain in B/C/D.
## The legacy Python project and user saves are never opened here.
const Combat = preload("res://scripts/combat/combat_sim.gd")
const Campaign = preload("res://scripts/sectors/sector_campaign.gd")
const Software = preload("res://scripts/software/software_system.gd")
const PlayerBridge = preload("res://scripts/combat/player_bridge.gd")
const SectorCatalog = preload("res://scripts/sectors/sector_catalog.gd")
const SaveBridge = preload("res://scripts/integration/save_bridge.gd")
const SaveStore = preload("res://scripts/persistence/save_store.gd")
const Progression = preload("res://scripts/progression/antivirus_progression.gd")
const CheckpointRules = preload("res://scripts/progression/checkpoint_rules.gd")
const WORLD_SCALE: float = 0.01
const HUB_SPAWN: Vector2 = Vector2(1200.0, 780.0)
const ARENA_SPAWN: Vector2 = Vector2(195.0, 360.0)
const GATES := {
    "CPU": Vector2(1200.0, 270.0),
    "MEMORY": Vector2(1960.0, 780.0),
    "STORAGE": Vector2(1200.0, 1320.0),
    "NETWORK": Vector2(440.0, 780.0)
}
const PURIFY_STATION: Vector2 = Vector2(1200.0, 1050.0)
const STATUS_STATION: Vector2 = Vector2(1200.0, 780.0)

@onready var camera_3d: Camera3D = $Camera3D
@onready var player_visual: MeshInstance3D = $PlayerVisual
@onready var arena_floor: MeshInstance3D = $ArenaFloor
@onready var hub_floor: MeshInstance3D = $HubFloor
@onready var actor: Node3D = $LegacyPlayer
@onready var controls: Node = $InputController
@onready var ui: CanvasLayer = $UIShell
@onready var projectiles: MultiMeshInstance3D = $ProjectileBatch
@onready var enemy_container: Node3D = $EnemyVisuals
@onready var gate_container: Node3D = $GateVisuals

var sim: RefCounted = Combat.new(42)
var campaign: RefCounted = Campaign.new("preview_not_deployed", 42)
var software: RefCounted = Software.new()
var mode: String = "title"
var paused: bool = true
var purify_uses: int = 3
var hub_position: Vector2 = HUB_SPAWN
var enemy_visuals: Dictionary = {}
var hud_frames: int = 0
var score: int = 0
var run_kills: int = 0
var run_boss_kills: int = 0
var run_id_counter: int = 0
var last_error: String = ""
@export var persistence_enabled: bool = true
var save_slot: String = ""
var saved_state: Dictionary = {}
var checkpoint_revision: int = 0
var deployed_level: int = 1
var run_frames: int = 0
var _pending_wave: bool = false
var _pending_boss: bool = false
var _pending_offers: Array[String] = []

func _ready() -> void:
    assert(camera_3d.projection == Camera3D.PROJECTION_ORTHOGONAL)
    assert(arena_floor.mesh != null and player_visual.mesh != null)
    actor.set("auto_tick", false)
    actor.call("set_input_source", controls)
    actor.set("camera_source", camera_3d)
    actor.set("simulation_enabled", false)
    actor.get_node("PlayerVisual").visible = false
    actor.call("set_logical_position", HUB_SPAWN)
    camera_3d.set("follow_target", actor)
    camera_3d.set("hub_mode", true)
    camera_3d.call("follow_logical", HUB_SPAWN)
    controls.connect("system_pressed", _on_system_pressed)
    actor.connect("skill_requested", _on_skill_requested)
    actor.connect("interact_requested", _on_interact)
    sim.connect("player_hit", _on_player_hit)
    sim.connect("enemy_died", _on_enemy_died)
    sim.connect("wave_ready", _on_wave_ready)
    ui.connect("action_requested", _on_menu_action)
    ui.connect("software_card_requested", _on_card_selected)
    ui.connect("pause_requested", _pause_gameplay)
    ui.connect("resume_requested", _resume_gameplay)
    ui.connect("binding_change_requested", _on_binding_change)
    ui.connect("setting_change_requested", _on_setting_change)
    # Headless scene imports and smoke tests must never initialize real user data.
    if DisplayServer.get_name() == "headless":
        persistence_enabled = false
    if persistence_enabled:
        save_slot = SaveBridge.safe_slot_path()
        var boot: Dictionary = SaveBridge.ensure_slot(save_slot)
        if boot.get("ok", false):
            saved_state = boot["state"]
            var cp: Variant = saved_state.get("os_checkpoint.json")
            checkpoint_revision = int(cp.revision) if cp != null else 0
        else:
            last_error = "저장 슬롯 접근 실패. 원본은 변경되지 않았습니다: " + str(boot.get("reason", "unknown"))
    _make_hub_markers()
    _set_world(false)
    _update_visuals()
    _update_hud()
    ui.call("set_gameplay_active", false)
    ui.call("open_menu", "title")
    controls.call("set_enabled", false)
    if not last_error.is_empty():
        ui.call("show_error", last_error)

func start_new_run() -> bool:
    if mode != "title" and mode != "dead":
        return false
    if persistence_enabled:
        if not last_error.is_empty():
            ui.call("show_error", last_error)
            return false
        var current: Dictionary = SaveStore.read(save_slot)
        if not current.get("ok", false):
            ui.call("show_error", "저장 슬롯 확인에 실패해 배포를 중단했습니다.")
            return false
        saved_state = current["state"]
        if saved_state.get("os_checkpoint.json") != null:
            ui.call("show_error", "기존 OS 진행이 남아 있습니다. 먼저 이어하기 또는 명시적 폐기를 선택하세요.")
            return false
        deployed_level = int(saved_state["antivirus_db.json"]["antivirus_level"])
    else:
        deployed_level = 1
    run_id_counter += 1
    campaign = Campaign.new("godot_preview_%d_%d" % [Time.get_unix_time_from_system(), run_id_counter], 42)
    sim = Combat.new(42)
    sim.connect("player_hit", _on_player_hit)
    sim.connect("enemy_died", _on_enemy_died)
    sim.connect("wave_ready", _on_wave_ready)
    software.reset(_deployed_base())
    _pending_wave = false
    _pending_offers.clear()
    purify_uses = 3
    score = 0
    run_kills = 0
    run_boss_kills = 0
    run_frames = 0
    hub_position = HUB_SPAWN
    mode = "hub"
    actor.set("hub_mode", true)
    actor.set("simulation_enabled", true)
    actor.call("set_logical_position", HUB_SPAWN)
    actor.set("velocity_px", Vector2.ZERO)
    actor.set("hp", software.maxhp)
    actor.set("mana", software.maxmana)
    _apply_stats()
    _set_world(false)
    if persistence_enabled:
        var first: Dictionary = SaveBridge.make_checkpoint(campaign, actor, sim, software,
            "hub", hub_position, [], purify_uses, score, run_kills,
            run_boss_kills, deployed_level, run_frames)
        var initial: Dictionary = SaveBridge.persist_checkpoint(save_slot, first, 0)
        if not initial.get("applied", false) or not _reload_save():
            mode = "title"
            actor.set("simulation_enabled", false)
            _pause_gameplay()
            ui.call("show_error", "OS 최초 체크포인트 저장 실패. 배포를 취소했습니다.")
            return false
    _resume_gameplay()
    _update_visuals()
    _update_hud()
    return true

func _set_world(combat_world: bool) -> void:
    arena_floor.visible = combat_world
    hub_floor.visible = not combat_world
    gate_container.visible = not combat_world
    camera_3d.set("hub_mode", not combat_world)
    actor.set("hub_mode", not combat_world)
    camera_3d.call("follow_logical", actor.get("logical_position"))

func enter_sector(sector_id: String) -> bool:
    var sid: String = SectorCatalog.normalize(sector_id)
    if mode != "hub" or sid.is_empty() or campaign.is_sector_clean(sid):
        return false
    if not campaign.enter_sector(sid):
        return false
    hub_position = actor.get("logical_position")
    actor.call("set_logical_position", ARENA_SPAWN)
    actor.set("velocity_px", Vector2.ZERO)
    _set_world(true)
    return start_next_wave()

func start_next_wave() -> bool:
    if mode != "hub" and mode != "between":
        return false
    var plan: Dictionary = campaign.start_wave()
    if plan.is_empty():
        return false
    sim.begin_wave(int(plan["wave_index"]), plan["spawns"], str(plan["boss_kind"]))
    mode = "combat"
    _resume_gameplay()
    return true

func retreat_to_hub() -> void:
    if mode == "title" or mode == "hub":
        return
    sim.abort_wave()
    campaign.retreat()
    actor.call("set_logical_position", hub_position)
    actor.set("velocity_px", Vector2.ZERO)
    mode = "hub"
    _set_world(false)
    _resume_gameplay()

func _physics_process(delta: float) -> void:
    if paused or mode not in ["hub", "combat", "between", "clean"]:
        return
    actor.call("simulate_tick", delta,
        controls.call("movement_vector"),
        controls.call("consume_dash_request"))
    var pointer: Vector2 = get_viewport().get_mouse_position()
    controls.call("set_pointer_logical", pointer)
    if bool(controls.get("pointer_valid")):
        actor.call("set_aim", controls.get("pointer_logical"), camera_3d.call("camera_top_left"))
    camera_3d.call("follow_logical", actor.get("logical_position"))
    if mode == "combat":
        PlayerBridge.read_player(sim, actor)
        sim.set_player_snapshot({"stats": software.stats})
        sim.step_fixed()
        PlayerBridge.write_player(sim, actor)
        campaign.tick_infection(1.0 / 60.0)
        if float(actor.get("hp")) <= 0.0:
            _game_over()
        elif campaign.failed:
            ui.call("show_error", "OS 무결성이 0이 되었습니다. 안전한 철수 처리가 필요합니다.")
            _pause_gameplay()
    run_frames += 1
    _update_visuals()
    hud_frames += 1
    if hud_frames >= 6:
        hud_frames = 0
        _update_hud()

func _on_wave_ready(boss_defeated: bool) -> void:
    if mode != "combat":
        return
    if not campaign.confirm_wave(sim.enemies.size(), sim.spawn_queue.size(), boss_defeated):
        return
    var sid: String = campaign.active_sector
    sim.abort_wave()
    if campaign.is_sector_clean(sid):
        software.award_boss_signature(sid)
    _apply_stats()
    var offers: Array[String] = software.roll_offers(3, sid, sim.rng)
    mode = "reward"
    _pause_gameplay()
    ui.call("show_reward_cards", offers, software.levels)
    _update_hud()

func _on_card_selected(index: int) -> void:
    if mode != "reward":
        return
    var chosen: String = software.claim_offer(index)
    if chosen.is_empty():
        return
    if chosen == "bonus":
        score += 250
    _apply_stats()
    ui.call("complete_reward_selection")
    mode = "clean" if campaign.is_sector_clean(campaign.active_sector) else "between"
    _resume_gameplay()

func _on_player_hit(_damage: float, infection_delta: float) -> void:
    campaign.apply_infection(infection_delta)

func _on_enemy_died(_enemy_id: int, kind: String) -> void:
    run_kills += 1
    score += 10
    if SectorCatalog.SECTORS.has(campaign.active_sector):
        if kind == str(SectorCatalog.SECTORS[campaign.active_sector]["boss"]):
            run_boss_kills += 1

func _apply_stats() -> void:
    actor.set("speed_px", float(software.stats["speed"]))
    actor.set("max_hp", software.maxhp)
    actor.set("max_mana", software.maxmana)
    actor.set("dash_max", int(software.stats["dash_max"]))
    actor.set("charges", mini(int(actor.get("charges")), int(actor.get("dash_max"))))
    actor.set("hp", minf(float(actor.get("hp")), software.maxhp))
    actor.set("mana", minf(float(actor.get("mana")), software.maxmana))
    sim.set_player_snapshot({"stats": software.stats, "max_hp": software.maxhp, "max_mana": software.maxmana})

func _on_skill_requested(skill: String) -> void:
    if mode != "combat" or paused:
        return
    PlayerBridge.try_cast(sim, actor, skill)

func _on_interact() -> void:
    if paused:
        return
    if mode == "between":
        start_next_wave()
    elif mode == "clean":
        retreat_to_hub()
    elif mode == "hub":
        var p: Vector2 = actor.get("logical_position")
        for sid in GATES:
            if p.distance_to(GATES[sid]) <= 115.0:
                enter_sector(str(sid))
                return
        if p.distance_to(PURIFY_STATION) <= 103.0:
            purify()
        elif p.distance_to(STATUS_STATION) <= 95.0:
            ui.call("open_menu", "os_clean" if campaign.is_os_clean() else "diagnostics")
            _pause_gameplay()

func purify() -> bool:
    if mode != "hub" or purify_uses <= 0:
        return false
    campaign.purify(32.0)
    campaign.repair_integrity(9.0)
    purify_uses -= 1
    _update_hud()
    return true

func _on_system_pressed(key: String) -> void:
    if paused:
        return
    match key:
        "r":
            if mode != "hub":
                retreat_to_hub()
        "tab":
            ui.call("open_menu", "loadout")
            _pause_gameplay()
        "o":
            ui.call("open_menu", "settings")
            _pause_gameplay()

func _on_menu_action(action: String, payload: Dictionary) -> void:
    match action:
        "deploy":
            start_new_run()
        "enter_sector":
            _resume_gameplay()
            enter_sector(str(payload.get("sector_id", "")))
        "return_hub":
            retreat_to_hub()
        "purify":
            if purify():
                ui.call("close_menu")
                _resume_gameplay()
        "extract_software":
            ui.call("show_error", "저장 이관과 영구 보상 트랜잭션 검증 전에는 철수를 잠갔습니다.")
            _pause_gameplay()
        "resume_checkpoint":
            ui.call("show_error", "체크포인트 복구는 저장 통합 검증 후 활성화됩니다.")
        _:
            ui.call("show_error", "현재 연결되지 않은 명령: " + action)

func _on_binding_change(action: String, key: String) -> void:
    if not controls.call("rebind_action", action, key):
        ui.call("show_error", "지원하지 않거나 중복된 키 설정입니다.")

func _on_setting_change(key: String, value: Variant) -> void:
    if key == "show_fps" or key == "reduced_effects":
        var snapshot: Dictionary = _hud_snapshot()
        snapshot["settings"][key] = value
        ui.call("update_game_state", snapshot)

func _pause_gameplay() -> void:
    paused = true
    controls.call("set_enabled", false)
    ui.call("set_gameplay_active", false)

func _resume_gameplay() -> void:
    if mode in ["title", "dead", "reward"]:
        return
    paused = false
    ui.call("close_menu")
    ui.call("set_gameplay_active", true)
    controls.call("set_enabled", true)

func _game_over() -> void:
    sim.abort_wave()
    mode = "dead"
    _pause_gameplay()
    ui.call("open_menu", "death")

func _hud_snapshot() -> Dictionary:
    var state: Dictionary = sim.get_player_snapshot()
    var boss_hp: float = 0.0
    var boss_maxhp: float = 0.0
    var boss_name: String = ""
    for enemy in sim.enemies:
        if bool(enemy.get("boss", false)):
            boss_hp = float(enemy["hp"])
            boss_maxhp = float(enemy["max_hp"])
            boss_name = str(enemy["kind"])
            break
    return {
        "hp": actor.get("hp"), "maxhp": actor.get("max_hp"),
        "mana": actor.get("mana"), "maxmana": actor.get("max_mana"),
        "dash_charges": actor.get("charges"), "dash_max": actor.get("dash_max"),
        "pulse_cd": state.get("pulse_cd", 0.0), "guard_cd": state.get("guard_cd", 0.0),
        "boss_hp": boss_hp, "boss_maxhp": boss_maxhp, "boss_name": boss_name,
        "sector": campaign.active_sector if not campaign.active_sector.is_empty() else "OS HUB",
        "wave": campaign.active_wave,
        "status": mode,
        "infection": campaign.infection, "integrity": campaign.integrity,
        "software_levels": software.levels.duplicate(true),
        "stats": software.stats.duplicate(true),
        "purify_uses": purify_uses,
        "os_clean": campaign.is_os_clean(),
        "sector_progress": campaign.progress.duplicate(true),
        "settings": {"reduced_effects": false, "show_fps": true},
        "fps": Engine.get_frames_per_second(), "show_fps": true,
        "score": score, "run_kills": run_kills,
        "checkpoint_valid": false, "database": {"antivirus_level": 1, "xp": 0, "xp_to_next": 100},
        "extraction_options": software.levels.keys(),
    }

func _update_hud() -> void:
    ui.call("update_game_state", _hud_snapshot())

func _make_hub_markers() -> void:
    var markers: Dictionary = GATES.duplicate(true)
    markers["PURIFY"] = PURIFY_STATION
    markers["STATUS"] = STATUS_STATION
    for id in markers:
        var marker: MeshInstance3D = MeshInstance3D.new()
        var shape: CylinderMesh = CylinderMesh.new()
        shape.top_radius = 0.78
        shape.bottom_radius = 0.78
        shape.height = 0.12
        marker.mesh = shape
        var material: StandardMaterial3D = StandardMaterial3D.new()
        material.albedo_color = Color(0.15, 0.75, 0.92) if GATES.has(id) else Color(0.24, 0.92, 0.42)
        marker.material_override = material
        var p: Vector2 = markers[id]
        marker.position = Vector3(p.x * WORLD_SCALE, 0.06, p.y * WORLD_SCALE)
        gate_container.add_child(marker)
        var tag: Label3D = Label3D.new()
        tag.text = str(SectorCatalog.SECTORS[id]["name"]) if GATES.has(id) else str(id)
        tag.font_size = 32
        tag.pixel_size = 0.006
        tag.position = Vector3(p.x * WORLD_SCALE, 0.45, p.y * WORLD_SCALE)
        gate_container.add_child(tag)

func _make_enemy_visual(kind: String, boss: bool, radius: float) -> MeshInstance3D:
    var visual: MeshInstance3D = MeshInstance3D.new()
    var sphere: SphereMesh = SphereMesh.new()
    sphere.radius = maxf(0.06, radius * WORLD_SCALE)
    sphere.height = 2.0 * sphere.radius
    visual.mesh = sphere
    var material: StandardMaterial3D = StandardMaterial3D.new()
    material.albedo_color = Color(1.0, 0.29, 0.20) if boss else Color(0.72, 0.32, 0.90)
    material.roughness = 0.32
    material.metallic = 0.23
    visual.material_override = material
    enemy_container.add_child(visual)
    return visual

func _update_visuals() -> void:
    var p: Vector3 = actor.global_position
    player_visual.position = p + Vector3(0.0, 0.13, 0.0)
    var active_keys: Dictionary = {}
    if mode == "combat":
        for enemy in sim.enemies:
            var id: int = int(enemy["id"])
            active_keys[id] = true
            var visual: MeshInstance3D = enemy_visuals.get(id)
            if visual == null:
                visual = _make_enemy_visual(str(enemy["kind"]), bool(enemy["boss"]), float(enemy["radius"]))
                enemy_visuals[id] = visual
            var pos: Vector2 = enemy["pos"]
            visual.position = Vector3(pos.x * WORLD_SCALE, 0.13, pos.y * WORLD_SCALE)
    for id in enemy_visuals.keys():
        if not active_keys.has(id):
            enemy_visuals[id].queue_free()
            enemy_visuals.erase(id)
    var locations: PackedVector2Array = PackedVector2Array()
    var tints: PackedColorArray = PackedColorArray()
    if mode == "combat":
        for bullet in sim.bullets:
            locations.append(bullet["pos"])
            tints.append(Color(0.3, 0.9, 1.0))
        for bullet in sim.enemy_bullets:
            locations.append(bullet["pos"])
            tints.append(Color(1.0, 0.43, 0.23))
    projectiles.call("set_projectiles", locations, tints)

func _deployed_base() -> Dictionary:
    var base: Dictionary = Progression.base_stats_for_level(deployed_level)
    return {"maxhp": float(base.get("maxhp", 100.0)),
        "maxmana": float(base.get("maxmana", 100.0)),
        "stats": {"dmg": float(base.get("dmg", 10.0)), "armor": float(base.get("armor", 4.0))}}

func _reload_save() -> bool:
    var fresh: Dictionary = SaveStore.read(save_slot)
    if not fresh.get("ok", false):
        return false
    saved_state = fresh["state"]
    var cp: Variant = saved_state.get("os_checkpoint.json")
    checkpoint_revision = int(cp.revision) if cp != null else 0
    return true
