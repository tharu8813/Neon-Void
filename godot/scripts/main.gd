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
const Bindings = preload("res://scripts/input/legacy_bindings.gd")
const Migration = preload("res://scripts/persistence/migration_service.gd")
const HubGeometry = preload("res://scripts/world/hub_geometry.gd")
const ActorShapes = preload("res://scripts/visuals/legacy_actor_shapes.gd")
const EnemyVisual = preload("res://scripts/visuals/legacy_enemy_visual.gd")
const EnemyCatalog = preload("res://scripts/enemies/enemy_catalog.gd")
const CombatFX = preload("res://scripts/visuals/legacy_combat_fx.gd")
const LegacyAudio = preload("res://scripts/audio/legacy_audio.gd")
const LegacyExit = preload("res://scripts/visuals/legacy_sector_exit.gd")
const WORLD_SCALE: float = 0.01
const HUB_SPAWN: Vector2 = Vector2(1200.0, 780.0)
# Frozen Python world/session.py BASE_NODES. Use the same 2D pixel coordinates and
# interaction radii. The Godot hub is not an extra start/selection menu.
const BASE_TERMINALS := {
    "deploy": {"position": Vector2(1580.0, 780.0), "radius": 112.0, "label": "OS DEPLOYMENT"},
    "database": {"position": Vector2(820.0, 610.0), "radius": 94.0, "label": "ANTIVIRUS DATABASE"},
    "diagnostics": {"position": Vector2(820.0, 1000.0), "radius": 94.0, "label": "DIAGNOSTICS"},
    "settings": {"position": Vector2(1540.0, 1030.0), "radius": 94.0, "label": "SETTINGS TERMINAL"}
}
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
var mode: String = "base"
var paused: bool = true
var purify_uses: int = 3
var hub_position: Vector2 = HUB_SPAWN
var hub_marker_groups: Dictionary = {}
var _base_menu_dismiss_resume: bool = false
var enemy_visuals: Dictionary = {}
var avatar_material: StandardMaterial3D
var avatar_hit_material: StandardMaterial3D
var avatar_aura: MeshInstance3D
var avatar_dash: MeshInstance3D
var visual_fx: Node3D
var legacy_audio: Node
var sector_exit: Node3D
var _enemy_hp_previous: Dictionary = {}
var combo_count: int = 0
var combo_window: float = 0.0
var _last_friendly_cooldown: float = 0.0
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
var _pending_boss_receipts: bool = false
var selected_archived_software: String = ""
var _import_dialog: FileDialog = null

func _ready() -> void:
    assert(camera_3d.projection == Camera3D.PROJECTION_ORTHOGONAL)
    assert(arena_floor.mesh != null and player_visual.mesh != null)
    _setup_player_visual()
    visual_fx = CombatFX.new()
    visual_fx.name = "CombatFX"
    add_child(visual_fx)
    legacy_audio = LegacyAudio.new()
    legacy_audio.name = "LegacyAudio"
    add_child(legacy_audio)
    sector_exit = LegacyExit.new()
    sector_exit.name = "LegacySectorExit"
    add_child(sector_exit)
    actor.set("auto_tick", false)
    actor.call("set_input_source", controls)
    actor.set("camera_source", camera_3d)
    actor.set("simulation_enabled", true)
    actor.get_node("PlayerVisual").visible = false
    actor.call("set_logical_position", HUB_SPAWN)
    camera_3d.set("follow_target", actor)
    camera_3d.set("hub_mode", true)
    camera_3d.call("follow_logical", HUB_SPAWN)
    controls.connect("system_pressed", _on_system_pressed)
    actor.connect("skill_requested", _on_skill_requested)
    actor.connect("dash_started", _on_dash_visual)
    actor.connect("interact_requested", _on_interact)
    sim.connect("player_hit", _on_player_hit)
    sim.connect("enemy_died", _on_enemy_died)
    sim.connect("wave_ready", _on_wave_ready)
    ui.connect("action_requested", _on_menu_action)
    ui.connect("software_card_requested", _on_card_selected)
    ui.connect("pause_requested", _pause_gameplay)
    ui.connect("resume_requested", _resume_gameplay)
    ui.connect("menu_dismissed", _on_menu_dismissed)
    ui.connect("binding_change_requested", _on_binding_change)
    ui.connect("setting_change_requested", _on_setting_change)
    # Headless scene imports and smoke tests must never initialize real user data.
    if DisplayServer.get_name() == "headless":
        persistence_enabled = false
    if persistence_enabled:
        save_slot = SaveBridge.safe_slot_path()
        if DirAccess.dir_exists_absolute(save_slot):
            var boot: Dictionary = SaveBridge.ensure_slot(save_slot)
            if boot.get("ok", false):
                saved_state = boot["state"]
                var cp: Variant = saved_state.get("os_checkpoint.json")
                checkpoint_revision = int(cp.revision) if cp != null else 0
            else:
                last_error = "저장 슬롯 접근 실패. 원본은 변경되지 않았습니다: " + str(boot.get("reason", "unknown"))
        else:
            # Never create a blank save on application launch: leave the
            # default destination free for explicit legacy import.
            saved_state = SaveStore.fresh_state()
        if not saved_state.is_empty():
            controls.call("apply_bindings", saved_state["settings.json"]["bindings"])
            _apply_runtime_settings(saved_state["settings.json"])
    _make_hub_markers()
    _set_world(false)
    _update_visuals()
    _update_hud()
    # Python's normal launch starts in the freely walkable antivirus base.
    # No new slot is created and no legacy JSON is touched at application boot.
    _resume_gameplay()
    if not last_error.is_empty():
        _pause_gameplay()
        ui.call("show_error", last_error)

func start_new_run() -> bool:
    if mode not in ["base", "dead"]:
        return false
    if persistence_enabled:
        if not last_error.is_empty():
            ui.call("show_error", last_error)
            return false
        if not DirAccess.dir_exists_absolute(save_slot):
            var initialized: Dictionary = SaveBridge.ensure_slot(save_slot)
            if not initialized.get("ok", false):
                ui.call("show_error", "새 저장 슬롯을 생성하지 못했습니다. 기존 데이터는 유지됩니다.")
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
    # Unix seconds + a process-local counter is NOT unique after restart.
    # A secure 96-bit token prevents a new OS reusing an already-closed run ID
    # and accidentally skipping profile history or duplicating reward receipts.
    var run_token: PackedByteArray = Crypto.new().generate_random_bytes(12)
    if run_token.size() != 12:
        ui.call("show_error", "안전한 OS 실행 ID를 생성하지 못했습니다.")
        return false
    var unique_run_id: String = "godot_%d_%s" % [
        int(Time.get_unix_time_from_system()), run_token.hex_encode()]
    if persistence_enabled and saved_state["antivirus_db.json"]["closed_runs"].has(unique_run_id):
        ui.call("show_error", "OS 실행 ID 충돌로 새 배포를 중단했습니다.")
        return false
    campaign = Campaign.new(unique_run_id, 42)
    sim = Combat.new(42)
    sim.connect("player_hit", _on_player_hit)
    sim.connect("enemy_died", _on_enemy_died)
    sim.connect("wave_ready", _on_wave_ready)
    software.reset(_deployed_base())
    if persistence_enabled and not selected_archived_software.is_empty():
        var start: Dictionary = Progression.starting_software(
            saved_state["antivirus_db.json"], selected_archived_software)
        if not start.get("ok", false) or not software.restore_levels(start["levels"], _deployed_base()):
            ui.call("show_error", "선택된 영구 소프트웨어를 검증하지 못했습니다.")
            return false
    _pending_wave = false
    _pending_offers.clear()
    purify_uses = 3
    score = 0
    combo_count = 0
    combo_window = 0.0
    _enemy_hp_previous.clear()
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
            mode = "base"
            actor.call("set_logical_position", HUB_SPAWN)
            actor.set("velocity_px", Vector2.ZERO)
            _set_world(false)
            _pause_gameplay()
            ui.call("show_error", "OS 최초 체크포인트 저장 실패. 배포를 취소했습니다.")
            return false
    _resume_gameplay()
    _update_visuals()
    _update_hud()
    return true

func _set_world(combat_world: bool) -> void:
    arena_floor.visible = combat_world
    sector_exit.visible = combat_world
    hub_floor.visible = not combat_world
    gate_container.visible = not combat_world
    _sync_hub_markers()
    if visual_fx != null:
        # Hub purification feedback is rendered as an inert transient ring.
        visual_fx.visible = combat_world or mode == "hub"
        if mode == "base":
            visual_fx.call("clear_battle")
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
    _last_friendly_cooldown = float(sim.player.get("fire_cd", 0.0))
    _enemy_hp_previous.clear()
    mode = "combat"
    legacy_audio.call("play_effect", "wave")
    _resume_gameplay()
    return true

func retreat_to_hub() -> void:
    if mode in ["base", "title", "hub", "dead", "reward"] or _pending_wave or _pending_boss_receipts:
        return
    # The wave in progress is intentionally abandoned. Confirmed waves,
    # infection and resources must be on disk before changing the world.
    if not _save_hub_checkpoint():
        _pause_gameplay()
        ui.call("show_error", "허브 복귀 체크포인트를 저장하지 못했습니다. R로 재시도하세요.")
        return
    sim.abort_wave()
    campaign.retreat()
    actor.call("set_logical_position", hub_position)
    actor.set("velocity_px", Vector2.ZERO)
    mode = "hub"
    _set_world(false)
    _resume_gameplay()

func _physics_process(delta: float) -> void:
    if paused or mode not in ["base", "hub", "combat", "between", "clean"]:
        return
    actor.call("simulate_tick", delta,
        controls.call("movement_vector"),
        controls.call("consume_dash_request"))
    var pointer: Vector2 = get_viewport().get_mouse_position()
    controls.call("set_pointer_logical", pointer)
    if bool(controls.get("pointer_valid")):
        actor.call("set_aim", controls.get("pointer_logical"), camera_3d.call("camera_top_left"))
    camera_3d.call("follow_logical", actor.get("logical_position"))
    if combo_window > 0.0:
        combo_window = maxf(0.0, combo_window - 1.0 / 60.0)
        if combo_window <= 0.0:
            combo_count = 0
    if mode == "combat":
        PlayerBridge.read_player(sim, actor)
        sim.set_player_snapshot({"stats": software.stats})
        var shots_before: int = sim.enemy_bullets.size()
        var cooldown_before: float = float(sim.player.get("fire_cd", 0.0))
        sim.step_fixed()
        # Observe C state transitions. Do NOT insert audio events into C's
        # deterministic event list or alter any combat timing.
        var cooldown_after: float = float(sim.player.get("fire_cd", 0.0))
        if cooldown_after > cooldown_before + 0.001:
            legacy_audio.call("play_effect", "shoot", 0.045)
        if sim.enemy_bullets.size() > shots_before:
            legacy_audio.call("play_effect", "eshoot", 0.08)
        _last_friendly_cooldown = cooldown_after
        PlayerBridge.write_player(sim, actor)
        campaign.tick_infection(1.0 / 60.0)
        if float(actor.get("hp")) <= 0.0:
            _game_over()
        elif campaign.failed:
            _close_failed_run("abandoned")
    if mode != "base":
        run_frames += 1
    _update_visuals()
    hud_frames += 1
    if hud_frames >= 6:
        hud_frames = 0
        _update_hud()

func _on_wave_ready(boss_defeated: bool) -> void:
    if mode != "combat" or _pending_wave:
        return
    _pending_wave = true
    _pending_boss = boss_defeated
    var sid: String = str(campaign.active_sector)
    var reward_owner: RefCounted = software
    if int(campaign.active_wave) == 5:
        # The boss signature must be saved in the same checkpoint as the
        # three final reward cards before live state changes.
        reward_owner = Software.new()
        if not reward_owner.restore_levels(software.levels, _deployed_base()):
            _pause_gameplay()
            ui.call("show_error", "보스 보상 적용 전에 소프트웨어 상태 검증에 실패했습니다.")
            return
        reward_owner.award_boss_signature(sid)
    _pending_offers = reward_owner.roll_offers(3, sid, sim.rng)
    _try_confirm_wave()

func _try_confirm_wave() -> void:
    if not _pending_wave:
        return
    var sid: String = str(campaign.active_sector)
    var wave: int = int(campaign.active_wave)
    if persistence_enabled:
        # E's atomic transaction commits wave + XP + pending reward first.
        PlayerBridge.write_player(sim, actor)
        var preview_software: RefCounted = software
        if wave == 5:
            preview_software = Software.new()
            if not preview_software.restore_levels(software.levels, _deployed_base()):
                _pause_gameplay()
                ui.call("show_error", "보스 보상 상태를 안전하게 생성하지 못했습니다.")
                return
            preview_software.award_boss_signature(sid)
            preview_software.set_pending_offers(_pending_offers)
        var future: Dictionary = SaveBridge.make_checkpoint(campaign, actor, sim,
            preview_software, "upgrade", hub_position, _pending_offers, purify_uses,
            score, run_kills, run_boss_kills, deployed_level, run_frames, wave)
        var commit: Dictionary = SaveBridge.persist_wave(save_slot, future,
            checkpoint_revision, str(campaign.run_id), sid, wave)
        if not commit.get("applied", false) or not _reload_save():
            _pause_gameplay()
            ui.call("show_error", "웨이브는 아직 완료되지 않았습니다. 저장 실패, R로 재시도: " +
                str(commit.get("reason", "readback_failed")))
            return
    if not campaign.confirm_wave(sim.enemies.size(), sim.spawn_queue.size(), _pending_boss):
        _pause_gameplay()
        ui.call("show_error", "검증된 전투 결과와 기록이 불일치합니다. 진행을 중단했습니다.")
        return
    _pending_wave = false
    sim.abort_wave()
    if campaign.is_sector_clean(sid):
        software.award_boss_signature(sid)
    software.set_pending_offers(_pending_offers)
    _apply_stats()
    _pending_boss_receipts = persistence_enabled and campaign.is_sector_clean(sid)
    _present_wave_reward()

func _present_wave_reward() -> void:
    if _pending_boss_receipts:
        var sid: String = str(campaign.active_sector)
        var outcome: Dictionary = SaveBridge.persist_boss_and_sector_rewards(
            save_slot, str(campaign.run_id), sid, campaign.is_os_clean())
        if not outcome.get("ok", false):
            _pause_gameplay()
            ui.call("show_error", "보스/구역 영구 보상 저장 실패. R로 안전하게 재시도: " +
                str(outcome.get("reason", "unknown")))
            return
        if not _reload_save():
            _pause_gameplay()
            ui.call("show_error", "보상 저장 후 데이터 재검증 실패. 진행을 중단했습니다.")
            return
        _pending_boss_receipts = false
    mode = "reward"
    _pause_gameplay()
    ui.call("show_reward_cards", _pending_offers, software.levels)
    legacy_audio.call("play_effect", "up")
    _update_hud()

func _on_card_selected(index: int) -> void:
    if mode != "reward":
        return
    if persistence_enabled:
        # Work on a clone. The displayed reward remains untouched until the
        # corresponding software levels and choice are saved.
        var candidate: RefCounted = Software.new()
        if not candidate.restore_levels(software.levels, _deployed_base()) or not candidate.set_pending_offers(software.pending_offers):
            ui.call("show_error", "소프트웨어 상태를 복원할 수 없습니다.")
            return
        var intended: String = candidate.claim_offer(index)
        if intended.is_empty():
            return
        var scene_after: String = "hub" if campaign.is_sector_clean(str(campaign.active_sector)) else "play"
        var new_score: int = score + (250 if intended == "bonus" else 0)
        var snapshot: Dictionary = SaveBridge.make_checkpoint(campaign, actor, sim,
            candidate, scene_after, hub_position, [], purify_uses, new_score,
            run_kills, run_boss_kills, deployed_level, run_frames)
        var saved: Dictionary = SaveBridge.persist_checkpoint(save_slot, snapshot, checkpoint_revision)
        if not saved.get("applied", false) or not _reload_save():
            ui.call("show_error", "선택은 보류됐습니다. 저장 실패 후 다시 선택하세요: " +
                str(saved.get("reason", "readback_failed")))
            return
    var chosen: String = software.claim_offer(index)
    if chosen.is_empty():
        return
    if chosen == "bonus":
        score += 250
    legacy_audio.call("play_effect", "pick")
    _pending_offers.clear()
    _apply_stats()
    ui.call("complete_reward_selection")
    mode = "clean" if campaign.is_sector_clean(campaign.active_sector) else "between"
    _resume_gameplay()

func _on_player_hit(_damage: float, infection_delta: float) -> void:
    campaign.apply_infection(infection_delta)
    legacy_audio.call("play_effect", "hurt")
    visual_fx.call("add_burst", actor.get("logical_position"), "hurt")
    if float(actor.get("hp")) / maxf(1.0, float(actor.get("max_hp"))) < 0.35:
        legacy_audio.call("play_effect", "lowhp", 2.0)

func _on_enemy_died(_enemy_id: int, kind: String) -> void:
    run_kills += 1
    score += 10
    var boss_kill: bool = EnemyCatalog.is_boss(kind)
    combo_count = combo_count + 1 if combo_window > 0.0 else 1
    combo_window = 2.6
    if combo_count % 10 == 0:
        legacy_audio.call("play_effect", "combo", 0.15)
    legacy_audio.call("play_effect", "boom" if boss_kill else "kill", 0.06)
    if enemy_visuals.has(_enemy_id):
        var death_visual: Node3D = enemy_visuals[_enemy_id]
        var at: Vector3 = death_visual.position
        visual_fx.call("add_burst", Vector2(at.x / WORLD_SCALE, at.z / WORLD_SCALE),
            "boss" if boss_kill else "kill")
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

func _on_dash_visual(_direction: Vector2) -> void:
    if paused or mode not in ["base", "hub", "combat", "between", "clean"]:
        return
    legacy_audio.call("play_effect", "dash", 0.08)
    if mode == "combat":
        visual_fx.call("add_burst", actor.get("logical_position"), "dash")

func _on_skill_requested(skill: String) -> void:
    if mode != "combat" or paused:
        return
    if PlayerBridge.try_cast(sim, actor, skill):
        legacy_audio.call("play_effect", "hshot" if skill == "pulse" else "phase")
        if skill == "pulse":
            visual_fx.call("add_burst", actor.get("logical_position"), "pulse")

func nearest_base_terminal(point: Vector2) -> String:
    # Match Python WorldSession.nearest: nearest reachable node, not the first
    # dictionary entry. F must never interact with an out-of-range terminal.
    var nearest: String = ""
    var nearest_distance: float = INF
    for id in BASE_TERMINALS:
        var data: Dictionary = BASE_TERMINALS[id]
        var distance: float = point.distance_to(data["position"])
        if distance <= float(data["radius"]) and distance < nearest_distance:
            nearest_distance = distance
            nearest = str(id)
    return nearest

func _on_interact() -> void:
    if paused:
        return
    # Python game.py::_handle_key requires F within 112 logical pixels of
    # CPU_EXIT(100,360). A remote F must never abandon a running sector.
    if mode in ["combat", "between", "clean"] and LegacyExit.can_interact(actor.get("logical_position")):
        retreat_to_hub()
        return
    if mode == "between":
        start_next_wave()
    elif mode == "base":
        var terminal: String = nearest_base_terminal(actor.get("logical_position"))
        if terminal.is_empty():
            return
        _base_menu_dismiss_resume = true
        _pause_gameplay()
        match terminal:
            "deploy":
                var pending: Variant = saved_state.get("os_checkpoint.json")
                ui.call("open_menu", "checkpoint" if pending != null else "deployment")
            "database":
                ui.call("open_menu", "database")
            "diagnostics":
                ui.call("open_menu", "profile")
            "settings":
                ui.call("open_menu", "settings")
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
    var old_infection: float = float(campaign.infection)
    var old_integrity: float = float(campaign.integrity)
    # Legacy game.py::interact_hub keeps charges if both recovery values
    # are zero. Avoid an ineffective transaction and permanent charge loss.
    if old_infection <= 0.0 and old_integrity >= 100.0:
        return false
    campaign.purify(32.0)
    campaign.repair_integrity(9.0)
    purify_uses -= 1
    if not _save_hub_checkpoint():
        # Never consume a purification charge if E could not commit it.
        campaign.infection = old_infection
        campaign.integrity = old_integrity
        purify_uses += 1
        _pause_gameplay()
        ui.call("show_error", "정화 기록 저장 실패. 정화 횟수와 감염도를 되돌렸습니다.")
        return false
    visual_fx.call("add_burst", PURIFY_STATION, "purify")
    legacy_audio.call("play_effect", "up")
    _update_hud()
    return true

func _on_system_pressed(key: String) -> void:
    # Python handles M and display shortcuts outside gameplay pause logic.
    if key == "m":
        var current: Dictionary = saved_state.get("settings.json", SaveStore.fresh_state()["settings.json"])
        _on_setting_change("muted", not bool(current.get("muted", false)))
        return
    if key == "f11" or key == "borderless":
        var options: Dictionary = saved_state.get("settings.json", SaveStore.fresh_state()["settings.json"])
        var current_mode: String = str(options.get("display_mode", "windowed"))
        var target_mode: String = "borderless" if key == "borderless" else "fullscreen"
        _on_setting_change("display_mode", "windowed" if current_mode == target_mode else target_mode)
        return
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
            extract_software(str(payload.get("id", "")))
        "select_archived":
            select_archived_software(str(payload.get("id", "")))
        "import_legacy":
            begin_legacy_import()
        "resume_checkpoint":
            if not resume_checkpoint():
                ui.call("show_error", "저장된 OS를 안전하게 복구할 수 없었습니다.")
        "discard_checkpoint":
            abandon_checkpoint()
        "return_base":
            return_to_base()
        _:
            ui.call("show_error", "현재 연결되지 않은 명령: " + action)

func _on_binding_change(action: String, key: String) -> void:
    var candidate_bindings: Dictionary = Bindings.rebound(controls.get("bindings"), action, key)
    if candidate_bindings.is_empty():
        ui.call("show_error", "지원하지 않거나 중복된 키 설정입니다.")
        return
    var options: Dictionary = saved_state.get("settings.json", SaveStore.fresh_state()["settings.json"]).duplicate(true)
    options["bindings"] = candidate_bindings
    if not _commit_options(options):
        ui.call("show_error", "키 변경을 저장할 수 없어 이전 키 설정을 유지합니다.")
        return
    controls.call("apply_bindings", candidate_bindings)
    _update_hud()

func _on_setting_change(key: String, value: Variant) -> void:
    var options: Dictionary = saved_state.get("settings.json", SaveStore.fresh_state()["settings.json"]).duplicate(true)
    if key == "resolution" and value is Dictionary:
        options["window_width"] = int(value.get("window_width", options["window_width"]))
        options["window_height"] = int(value.get("window_height", options["window_height"]))
    elif key in ["volume", "muted", "display_mode", "show_fps", "reduced_effects"]:
        options[key] = value
    else:
        ui.call("show_error", "허용되지 않은 설정 항목입니다.")
        return
    if not _commit_options(options):
        ui.call("show_error", "설정 변경을 안전하게 저장하지 못했습니다.")
        return
    _apply_runtime_settings(options)
    _update_hud()

func _pause_gameplay() -> void:
    paused = true
    # ESC/P and O also open modal screens in the antivirus base. Closing them
    # must restore free movement unless startup discovered unsafe save data.
    if mode == "base" and last_error.is_empty():
        _base_menu_dismiss_resume = true
    controls.call("set_enabled", false)
    ui.call("set_gameplay_active", false)

func _on_menu_dismissed() -> void:
    # Closing a base terminal's modal returns to the world. Programmatic
    # close_menu() from resume/deploy must not recurse into this callback.
    if _base_menu_dismiss_resume and mode == "base":
        _base_menu_dismiss_resume = false
        _resume_gameplay()

func _resume_gameplay() -> void:
    if mode in ["title", "dead", "reward"]:
        return
    _base_menu_dismiss_resume = false
    paused = false
    ui.call("close_menu")
    ui.call("set_gameplay_active", true)
    controls.call("set_enabled", true)

func _game_over() -> void:
    _close_failed_run("dead")

func _close_failed_run(status: String) -> bool:
    if status not in ["dead", "abandoned"]:
        return false
    _pause_gameplay()
    if persistence_enabled:
        var closed: Dictionary = _finalize_saved_run(status)
        if not closed.get("applied", false) or not _reload_save():
            ui.call("show_error", "실패한 OS 종료 기록을 저장하지 못했습니다. 사용자 데이터를 유지하고 진행을 중단했습니다: " +
                str(closed.get("reason", "readback_failed")))
            return false
    sim.abort_wave()
    mode = "dead"
    ui.call("open_menu", "death")
    _update_hud()
    return true

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
    var database: Dictionary = saved_state.get("antivirus_db.json", {})
    var db_xp: Dictionary = Progression.xp_progress(database) if not database.is_empty() else {}
    var profile: Dictionary = saved_state.get("profile.json", {})
    var settings: Dictionary = saved_state.get("settings.json", {})
    var checkpoint: Variant = saved_state.get("os_checkpoint.json")
    var cp_name: String = ""
    if checkpoint != null:
        cp_name = str(checkpoint.campaign.run_id)
    return {
        "hp": actor.get("hp"), "maxhp": actor.get("max_hp"),
        "mana": actor.get("mana"), "maxmana": actor.get("max_mana"),
        "dash_charges": actor.get("charges"), "dash_max": actor.get("dash_max"),
        "charge_t": actor.get("charge_t"), "dash_cd": actor.get("dash_cd"),
        "guard_t": state.get("guard", 0.0),
        "pulse_cd": state.get("pulse_cd", 0.0), "guard_cd": state.get("guard_cd", 0.0),
        "boss_hp": boss_hp, "boss_maxhp": boss_maxhp, "boss_name": boss_name,
        "sector": "ANTIVIRUS HUB" if mode == "base" else (campaign.active_sector if not campaign.active_sector.is_empty() else "OS HUB"),
        "wave": campaign.active_wave,
        "status": mode, "nearby": _world_nearby_prompt(),
        "infection": campaign.infection, "integrity": campaign.integrity,
        "software_levels": software.levels.duplicate(true),
        "stats": software.stats.duplicate(true),
        "purify_uses": purify_uses,
        "os_clean": campaign.is_os_clean(),
        "sector_progress": campaign.progress.duplicate(true),
        "settings": settings if not settings.is_empty() else {"reduced_effects": false, "show_fps": true},
        "fps": Engine.get_frames_per_second(), "show_fps": bool(settings.get("show_fps", true)),
        "score": score, "run_kills": run_kills,
        "checkpoint_valid": checkpoint != null,
        "checkpoint_summary": cp_name,
        "import_available": persistence_enabled and not save_slot.is_empty() and not DirAccess.dir_exists_absolute(save_slot),
        "database": {
            "antivirus_level": int(database.get("antivirus_level", 1)),
            "xp": int(db_xp.get("current", 0)),
            "xp_to_next": int(db_xp.get("required", 100)),
            "software_archive": database.get("software_archive", {}),
            "clean_runs": database.get("processed_extractions", {}).size(),
        },
        "profile": profile,
        "extraction_options": software.levels.keys(),
        "selected_archived_software": selected_archived_software,
    }

func _world_nearby_prompt() -> String:
    # Visual/UI hint only. Interaction authority stays in _on_interact and
    # frozen B/C collision/reach checks, not in this optional text.
    var point: Vector2 = actor.get("logical_position")
    if mode == "base":
        var terminal: String = nearest_base_terminal(point)
        return str(BASE_TERMINALS[terminal]["label"]) if not terminal.is_empty() else ""
    if mode != "hub":
        return ""
    for sid in GATES:
        if point.distance_to(GATES[sid]) <= 115.0:
            return str(SectorCatalog.SECTORS[sid]["name"])
    if point.distance_to(PURIFY_STATION) <= 103.0:
        return "PURIFICATION STATION"
    if point.distance_to(STATUS_STATION) <= 95.0:
        return "철수 단말기" if campaign.is_os_clean() else "SYSTEM STATUS"
    return ""

func _update_hud() -> void:
    ui.call("update_game_state", _hud_snapshot())

func _make_hub_markers() -> void:
    var os_group := Node3D.new()
    os_group.name = "OSStations"
    gate_container.add_child(os_group)
    hub_marker_groups["hub"] = os_group
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
        os_group.add_child(marker)
        var tag: Label3D = Label3D.new()
        tag.text = str(SectorCatalog.SECTORS[id]["name"]) if GATES.has(id) else str(id)
        tag.font_size = 32
        tag.pixel_size = 0.006
        tag.position = Vector3(p.x * WORLD_SCALE, 0.45, p.y * WORLD_SCALE)
        os_group.add_child(tag)
    var base_group := Node3D.new()
    base_group.name = "AntivirusBaseTerminals"
    gate_container.add_child(base_group)
    hub_marker_groups["base"] = base_group
    for id in BASE_TERMINALS:
        var data: Dictionary = BASE_TERMINALS[id]
        var position_2d: Vector2 = data["position"]
        var platform := MeshInstance3D.new()
        var platform_shape := CylinderMesh.new()
        platform_shape.top_radius = 0.60
        platform_shape.bottom_radius = 0.60
        platform_shape.height = 0.12
        platform.mesh = platform_shape
        var platform_color := StandardMaterial3D.new()
        platform_color.albedo_color = Color("#164659")
        platform_color.emission_enabled = true
        platform_color.emission = Color("#0e6270")
        platform.material_override = platform_color
        platform.position = Vector3(position_2d.x * WORLD_SCALE, 0.06, position_2d.y * WORLD_SCALE)
        base_group.add_child(platform)
        var terminal_label := Label3D.new()
        terminal_label.text = str(data["label"])
        terminal_label.font_size = 32
        terminal_label.pixel_size = 0.006
        terminal_label.position = Vector3(position_2d.x * WORLD_SCALE, 0.45, position_2d.y * WORLD_SCALE)
        base_group.add_child(terminal_label)
    var base_destinations: Array[Vector2] = []
    for id in BASE_TERMINALS:
        base_destinations.append(BASE_TERMINALS[id]["position"])
    HubGeometry.build(base_group, true, base_destinations)
    var os_destinations: Array[Vector2] = []
    for id in GATES:
        os_destinations.append(GATES[id])
    os_destinations.append(PURIFY_STATION)
    os_destinations.append(STATUS_STATION)
    HubGeometry.build(os_group, false, os_destinations)
    _sync_hub_markers()

func _sync_hub_markers() -> void:
    for id in hub_marker_groups:
        var group: Node3D = hub_marker_groups[id]
        group.visible = mode == str(id)

func _setup_player_visual() -> void:
    # Use Python draw_player hull in top-down XZ. Keep B's hidden collision
    # scene untouched; its original radius and aiming rules remain authoritative.
    player_visual.mesh = ActorShapes.mesh_for("player", 13.0)
    avatar_material = ActorShapes.lit_material(ActorShapes.color_for("player"))
    avatar_hit_material = ActorShapes.lit_material(Color("#6b8f9c"))
    player_visual.material_override = avatar_material
    avatar_aura = MeshInstance3D.new()
    avatar_aura.name = "IdentityRing"
    avatar_aura.mesh = ActorShapes.ring_mesh(0.25)
    avatar_aura.material_override = ActorShapes.lit_material(Color("#237a92"))
    avatar_aura.position.y = -0.032
    player_visual.add_child(avatar_aura)
    avatar_dash = MeshInstance3D.new()
    avatar_dash.name = "DashRing"
    avatar_dash.mesh = ActorShapes.ring_mesh(0.31, 0.030)
    avatar_dash.material_override = ActorShapes.lit_material(Color("#6ff2ff"))
    avatar_dash.position.y = -0.030
    avatar_dash.visible = false
    player_visual.add_child(avatar_dash)

func _make_enemy_visual(kind: String, boss: bool, radius: float) -> Node3D:
    # The boss flag must agree with C's catalog; never invent a different
    # render-only enemy identity from a generic boss/sphere fallback.
    assert(ActorShapes.COLORS.has(kind), "Missing original Python enemy color")
    var visual: Node3D = EnemyVisual.new()
    visual.call("configure", kind, radius)
    assert(bool(visual.get("boss")) == boss, "C boss identity mismatch")
    enemy_container.add_child(visual)
    return visual

func _update_visuals() -> void:
    var p: Vector3 = actor.global_position
    player_visual.position = p + Vector3(0.0, 0.13, 0.0)
    player_visual.rotation.y = -float(actor.get("aim_radians"))
    player_visual.material_override = avatar_hit_material if float(actor.get("invulnerability_t")) > 0.0 \
        and int(run_frames / 3) % 2 == 0 else avatar_material
    avatar_dash.visible = float(actor.get("dash_t")) > 0.0
    var active_keys: Dictionary = {}
    var settings: Dictionary = saved_state.get("settings.json", {})
    var reduced_motion: bool = bool(settings.get("reduced_effects", false))
    if mode == "combat":
        for enemy in sim.enemies:
            var id: int = int(enemy["id"])
            active_keys[id] = true
            var visual: Node3D = enemy_visuals.get(id)
            if visual == null:
                visual = _make_enemy_visual(str(enemy["kind"]), bool(enemy["boss"]), float(enemy["radius"]))
                enemy_visuals[id] = visual
            var pos: Vector2 = enemy["pos"]
            visual.position = Vector3(pos.x * WORLD_SCALE, 0.115, pos.y * WORLD_SCALE)
            visual.call("sync_state", enemy, reduced_motion)
    for id in enemy_visuals.keys():
        if not active_keys.has(id):
            enemy_visuals[id].queue_free()
            enemy_visuals.erase(id)
    var locations: PackedVector2Array = PackedVector2Array()
    var tints: PackedColorArray = PackedColorArray()
    if mode == "combat":
        for bullet in sim.bullets:
            locations.append(bullet["pos"])
            # Original Python C_BULLET (#ccff4d), not P9 placeholder cyan.
            tints.append(Color("#ccff4d"))
        for bullet in sim.enemy_bullets:
            locations.append(bullet["pos"])
            tints.append(Color("#ff9fbb"))
    projectiles.call("set_projectiles", locations, tints)
    if visual_fx != null:
        if mode == "combat":
            var reduced: bool = bool(saved_state.get("settings.json", {}).get("reduced_effects", false))
            visual_fx.call("sync_battle", sim.bullets, sim.enemy_bullets,
                sim.telegraphs, sim.enemies, reduced)
        elif visual_fx.visible:
            visual_fx.call("clear_battle")

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

func _input(event: InputEvent) -> void:
    # While the input dispatcher is disabled after a save failure, R retries
    # ONLY the exact pending transaction. Never roll new reward cards.
    if event is InputEventKey:
        var key_event: InputEventKey = event
        if key_event.pressed and not key_event.echo and key_event.keycode == KEY_R:
            if _pending_wave:
                get_viewport().set_input_as_handled()
                _try_confirm_wave()
            elif _pending_boss_receipts:
                get_viewport().set_input_as_handled()
                _present_wave_reward()

func resume_checkpoint() -> bool:
    if not persistence_enabled or save_slot.is_empty() or not _reload_save():
        return false
    var cp: Variant = saved_state.get("os_checkpoint.json")
    if cp == null:
        return false
    if saved_state["antivirus_db.json"]["closed_runs"].has(str(cp.campaign.run_id)):
        return false
    var recovered: RefCounted = SaveBridge.replay_campaign(saved_state)
    if recovered == null:
        return false
    var base: Dictionary = Progression.base_stats_for_level(int(cp.deployed_level))
    if base.is_empty():
        return false
    var next_software: RefCounted = Software.new()
    # JSON.parse represents integer levels as numeric floating-point variants.
    # The E validator has already checked exact integrality and bounds;
    # convert to concrete TYPE_INT before passing them to D's strict API.
    var restored_levels: Dictionary = {}
    for uid in cp.software_levels:
        restored_levels[uid] = int(cp.software_levels[uid])
    if not next_software.restore_levels(restored_levels, {
        "maxhp": float(base["maxhp"]), "maxmana": float(base["maxmana"]),
        "stats": {"dmg": float(base["dmg"]), "armor": float(base["armor"])}}):
        return false
    var finished: Array[String] = []
    for sid in recovered.progress:
        if int(recovered.progress[sid]) == 5:
            finished.append(str(sid))
    next_software.restore_boss_claims(finished)
    var cards: Array[String] = []
    if str(cp.scene) == "upgrade":
        for uid in cp.pending_cards:
            cards.append(str(uid))
        if not next_software.set_pending_offers(cards):
            return false
    software = next_software
    deployed_level = int(cp.deployed_level)
    campaign = recovered
    sim = Combat.new(int(campaign.seed_value))
    sim.connect("player_hit", _on_player_hit)
    sim.connect("enemy_died", _on_enemy_died)
    sim.connect("wave_ready", _on_wave_ready)
    run_kills = int(cp.run_kills)
    run_boss_kills = int(cp.run_boss_kills)
    run_frames = int(cp.elapsed_frames)
    score = int(cp.score)
    purify_uses = int(cp.purify_uses)
    hub_position = Vector2(float(cp.hub_position[0]), float(cp.hub_position[1]))
    actor.set("simulation_enabled", true)
    actor.set("hp", float(cp.player.hp))
    actor.set("mana", float(cp.player.mana))
    _apply_stats()
    actor.set("charges", int(cp.player.charges))
    sim.set_player_snapshot({
        "fire_cd": float(cp.player.fire_cd),
        "pulse_cd": float(cp.player.pulse_cd),
        "guard_cd": float(cp.player.guard_cd),
        "guard": float(cp.player.guard_t),
    })
    _pending_wave = false
    _pending_boss_receipts = false
    _pending_offers.clear()
    if str(cp.scene) == "hub":
        mode = "hub"
        actor.call("set_logical_position", Vector2(float(cp.player_position[0]), float(cp.player_position[1])))
        _set_world(false)
        _resume_gameplay()
    elif str(cp.scene) in ["play", "upgrade"]:
        var sector_id: String = str(cp.sector).to_upper()
        if not campaign.enter_sector(sector_id):
            return false
        actor.call("set_logical_position", Vector2(float(cp.player_position[0]), float(cp.player_position[1])))
        _set_world(true)
        if str(cp.scene) == "upgrade":
            _pending_offers = cards.duplicate()
            _pending_boss_receipts = campaign.is_sector_clean(sector_id)
            if _pending_boss_receipts:
                var receipt_result: Dictionary = SaveBridge.persist_boss_and_sector_rewards(
                    save_slot, str(campaign.run_id), sector_id, campaign.is_os_clean())
                if not receipt_result.get("ok", false) or not _reload_save():
                    _pause_gameplay()
                    ui.call("show_error", "보스/구역 영구 보상 복구 실패. R로 재시도하세요.")
                    return false
                _pending_boss_receipts = false
            mode = "reward"
            _pause_gameplay()
            ui.call("show_reward_cards", cards, software.levels)
        else:
            mode = "between"
            _resume_gameplay()
    else:
        mode = "dead"
        _pause_gameplay()
        ui.call("open_menu", "death")
    _update_visuals()
    _update_hud()
    return true

func abandon_checkpoint() -> bool:
    # Called only from the explicit 'new OS' action on the checkpoint menu.
    if not persistence_enabled or not _reload_save():
        return false
    var cp: Variant = saved_state.get("os_checkpoint.json")
    if cp == null:
        return false
    var run_id: String = str(cp.campaign.run_id)
    if saved_state["antivirus_db.json"]["closed_runs"].has(run_id):
        # Compatibility recovery for old two-step terminal saves. The new
        # single-transaction finalizer never creates this half-closed state.
        var previous: Dictionary = CheckpointRules.clear_after_run(save_slot, checkpoint_revision)
        if not previous.get("applied", false) or not _reload_save():
            ui.call("show_error", "이전 방식의 종료 체크포인트를 정리하지 못했습니다.")
            return false
    else:
        var closed: Dictionary = _finalize_saved_run("abandoned")
        if not closed.get("applied", false) or not _reload_save():
            ui.call("show_error", "기존 OS를 안전하게 종료하지 못했습니다: " +
                str(closed.get("reason", "readback_failed")))
            return false
    return_to_base()
    return true

func extract_software(uid: String) -> bool:
    if not persistence_enabled or mode != "hub" or not campaign.is_os_clean():
        ui.call("show_error", "OS CLEAN 완료 및 안전한 영구 저장이 필요합니다.")
        return false
    var archived: Dictionary = _finalize_saved_run("extracted", uid)
    if not archived.get("applied", false) or not _reload_save():
        ui.call("show_error", "영구 소프트웨어 보존과 기록을 안전하게 완료하지 못했습니다: " +
            str(archived.get("reason", "readback_failed")))
        return false
    return_to_base()
    return true

func return_to_base() -> void:
    # Called only after E has committed the terminal receipt or after an
    # already-closed death. Never erase or synthesize checkpoint/XP state.
    sim.abort_wave()
    mode = "base"
    actor.set("simulation_enabled", true)
    actor.call("set_logical_position", HUB_SPAWN)
    actor.set("velocity_px", Vector2.ZERO)
    _set_world(false)
    _resume_gameplay()
    _update_visuals()
    _update_hud()

func _commit_options(candidate: Dictionary) -> bool:
    var validation: Dictionary = SaveBridge.Validator.convert("settings.json", candidate)
    if not validation.get("ok", false):
        return false
    if not persistence_enabled:
        saved_state["settings.json"] = validation["value"]
        return true
    if not DirAccess.dir_exists_absolute(save_slot):
        var initialized: Dictionary = SaveBridge.ensure_slot(save_slot)
        if not initialized.get("ok", false):
            return false
    var before: Dictionary = SaveStore.read(save_slot)
    if not before.get("ok", false):
        return false
    var result: Dictionary = SaveStore.persist_document(save_slot, "settings.json",
        validation["value"], int(before["revision"]))
    if not result.get("applied", false) and result.get("reason", "") != "unchanged":
        return false
    return _reload_save()

func _apply_runtime_settings(settings: Dictionary) -> void:
    if legacy_audio != null:
        legacy_audio.call("apply_settings", float(settings.get("volume", 0.9)),
            bool(settings.get("muted", false)))
    if DisplayServer.get_name() == "headless":
        return
    if AudioServer.get_bus_count() > 0:
        AudioServer.set_bus_mute(0, bool(settings.get("muted", false)))
        AudioServer.set_bus_volume_db(0, linear_to_db(maxf(0.001, float(settings.get("volume", 0.9)))))
    var display_mode: String = str(settings.get("display_mode", "windowed"))
    match display_mode:
        "fullscreen":
            DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN)
        "borderless":
            DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN)
        _:
            DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
            DisplayServer.window_set_size(Vector2i(
                int(settings.get("window_width", 1280)), int(settings.get("window_height", 720))))

func select_archived_software(uid: String) -> bool:
    if not uid.is_empty():
        var db: Dictionary = saved_state.get("antivirus_db.json", {})
        if db.is_empty() or not Progression.starting_software(db, uid).get("ok", false):
            ui.call("show_error", "영구 보유하지 않은 소프트웨어는 장착할 수 없습니다.")
            return false
    selected_archived_software = uid
    _update_hud()
    return true

func begin_legacy_import() -> bool:
    if not persistence_enabled or save_slot.is_empty() or mode != "base":
        return false
    if DirAccess.dir_exists_absolute(save_slot) or FileAccess.file_exists(save_slot):
        ui.call("show_error", "기존 Godot 저장 슬롯이 이미 존재합니다. 안전을 위해 가져오기로 덮어쓸 수 없습니다.")
        return false
    if _import_dialog != null:
        return false
    _import_dialog = FileDialog.new()
    _import_dialog.access = FileDialog.ACCESS_FILESYSTEM
    _import_dialog.file_mode = FileDialog.FILE_MODE_OPEN_DIR
    _import_dialog.title = "기존 Python NeonVoid 저장 폴더 선택 (원본 절대 변경 안 함)"
    _import_dialog.dir_selected.connect(_legacy_folder_selected)
    _import_dialog.canceled.connect(_close_import_dialog)
    add_child(_import_dialog)
    if OS.get_name() == "Windows":
        var suggested: String = OS.get_environment("LOCALAPPDATA").path_join("NeonVoid")
        if DirAccess.dir_exists_absolute(suggested):
            _import_dialog.current_dir = suggested
    _import_dialog.popup_centered_ratio(0.72)
    return true

func _close_import_dialog() -> void:
    if is_instance_valid(_import_dialog):
        _import_dialog.queue_free()
    _import_dialog = null

func _legacy_folder_selected(folder: String) -> void:
    _close_import_dialog()
    if DirAccess.dir_exists_absolute(save_slot) or FileAccess.file_exists(save_slot):
        ui.call("show_error", "다른 프로세스가 저장 슬롯을 생성했습니다. 이관을 중단했습니다.")
        return
    # Explicit folder selection is the import consent. E copies and hashes
    # original bytes read-only, then atomically creates a separate Godot slot.
    var imported: Dictionary = Migration.import_legacy(folder, save_slot, true)
    if not imported.get("ok", false) or not _reload_save():
        ui.call("show_error", "이관하지 않았습니다. 원본 보존: " + str(imported.get("reason", "readback_failed")))
        return
    selected_archived_software = ""
    controls.call("apply_bindings", saved_state["settings.json"]["bindings"])
    _apply_runtime_settings(saved_state["settings.json"])
    _update_hud()
    ui.call("open_menu", "deployment")

func _save_hub_checkpoint() -> bool:
    # A side-effect-free new snapshot is validated and committed as a single
    # E transaction before this caller mutates C's active-sector state.
    if not persistence_enabled:
        return true
    if save_slot.is_empty() or not _reload_save():
        return false
    var existing: Variant = saved_state.get("os_checkpoint.json")
    if existing == null or str(existing.campaign.run_id) != str(campaign.run_id):
        return false
    var next: Dictionary = SaveBridge.make_checkpoint(campaign, actor, sim, software,
        "hub", hub_position, [], purify_uses, score, run_kills,
        run_boss_kills, deployed_level, run_frames)
    var result: Dictionary = SaveBridge.persist_checkpoint(save_slot, next, checkpoint_revision)
    return result.get("applied", false) and _reload_save()

func _finalize_saved_run(status: String, selected_id: String = "") -> Dictionary:
    var highest_wave: int = int(campaign.active_wave)
    for sid in campaign.progress:
        highest_wave = maxi(highest_wave, int(campaign.progress[sid]))
    return SaveBridge.finalize_run(save_slot, str(campaign.run_id), status,
        software.levels.duplicate(true), selected_id, score, run_kills,
        run_boss_kills, 0, run_frames, highest_wave)
