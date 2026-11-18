extends Node3D
## A-owned opt-in rework pilot: five D hardware scenes + E-acknowledged B/C gameplay.
## Not the default main game, and not a replacement for B's production combat.
## Never reads/writes legacy save files. A failed/ambiguous transaction blocks play.

const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Bridge = preload("res://scripts/rework/integration/mission_commit_bridge.gd")
const Offers = preload("res://scripts/rework/plugins/plugin_offer_engine.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const RareBonus = preload("res://scripts/rework/progression/rare_bonus.gd")
const RecoveryView = preload("res://scripts/rework/integration/pilot_recovery_view.gd")
const Balance = preload("res://scripts/rework/progression/balance_config.gd")
const TacticalHUD = preload("res://scripts/rework/ui/pilot_tactical_hud.gd")
const CardStyle = preload("res://scripts/rework/ui/pilot_reward_card_style.gd")
const HubManagement = preload("res://scripts/rework/ui/hub_management_panel.gd")
const MissionTracker = preload("res://scripts/rework/ui/pilot_mission_tracker.gd")
const MissionResult = preload("res://scripts/rework/ui/pilot_mission_result.gd")
const StatusHUD = preload("res://scripts/rework/ui/pilot_status_effects.gd")
const SkillBar = preload("res://scripts/rework/ui/pilot_skill_bar.gd")
const SkillRegistry = preload("res://scripts/rework/skills/skill_registry.gd")
const QA_SKILL_IDS: Array[String] = [
    "nv_skill_arc_pulse", "nv_skill_penetrator", "nv_skill_root_packet"]

@export var slot_id: String = "integration-pilot"
@export var initial_vaccine_id: String = "pilot-vaccine"
## Opt-in QA scene only. Real saved E transactions; never enabled in release pilot.
@export var qa_skill_tools_enabled: bool = false

@onready var world: Node3D = $World

var slot_path: String = ""
var bridge: RefCounted
var offers: RefCounted
var current_run_id: String = ""
var frozen: bool = false
var message_label: Label
var status_label: Label
var offer_panel: PanelContainer
var card_buttons: Array[Button] = []
var reroll_buttons: Array[Button] = []
var deposit_button: Button
var new_vaccine_button: Button
var learn_panel: PanelContainer
var learn_list: VBoxContainer
var learn_checks: Array[CheckBox] = []
var pending_extraction: Dictionary = {}
var pending_reconciliation: Dictionary = {}
var recovery_panel: PanelContainer
var recovery_label: Label
var tactical_hud: PanelContainer
var hub_panel: PanelContainer
var hub_button: Button
var qa_skill_button: Button
var mission_tracker: PanelContainer
var result_panel: PanelContainer
var status_effects: PanelContainer
var skill_bar: PanelContainer
var status_column: VBoxContainer
var _hud_elapsed: float = 0.0
var _combat_detail_elapsed: float = 0.0

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    # The reward overlay must respond while the simulation itself is paused.
    world.process_mode = Node.PROCESS_MODE_PAUSABLE
    world.set("demo_mode", false)
    _create_ui()
    get_viewport().size_changed.connect(_update_ui_geometry)
    _update_ui_geometry()
    world.connect("mission_start_requested", Callable(self, "_on_mission_requested"))
    world.connect("hardware_checkpoint_requested", Callable(self, "_on_hardware_checkpoint_requested"))
    world.connect("mission_returned", Callable(self, "_on_mission_returned"))
    world.connect("integration_error", Callable(self, "_on_world_error"))
    # A handles D's durability requests so extraction can present a 0-2
    # learnable-plugin selection BEFORE E commits the one-shot receipt.
    world.connect("world_fact_requested", Callable(self, "_on_world_fact_requested"))
    world.connect("extraction_commit_requested", Callable(self, "_on_extraction_requested"))
    world.connect("death_commit_requested", Callable(self, "_on_death_requested"))
    world.connect("unavailable_hardware_requested", Callable(self, "_on_unavailable"))
    var preview_combat: Node = world.get("combat") as Node
    if preview_combat != null:
        preview_combat.connect("enemy_defeated", Callable(self, "_on_enemy_defeated"))
        var live: Variant = preview_combat.get("live")
        if live != null:
            live.require_e_authorization = true
            live.encounter_authorization_requested.connect(
                _on_encounter_authorization_requested)
    slot_path = Store.default_slot_path(slot_id)
    if slot_path.is_empty():
        _halt("invalid_pilot_slot")
        return
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        # Only a truly absent directory may be initialized. Never overwrite
        # a corrupt, interrupted, locked or existing slot.
        if DirAccess.dir_exists_absolute(slot_path):
            _halt("existing_slot_unreadable:" + str(loaded.get("reason", "unknown")))
            return
        var created: Dictionary = Gateway.initialize(slot_id, initial_vaccine_id)
        if not bool(created.get("ok", false)):
            _halt("cannot_create_slot:" + str(created.get("reason", "unknown")))
            return
        loaded = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("cannot_read_slot:" + str(loaded.get("reason", "unknown")))
        return
    _accept_saved_state(loaded)
    _refresh()

func _process(delta: float) -> void:
    if tactical_hud == null or world == null:
        return
    if frozen:
        if status_effects != null:
            status_effects.clear_view()
        if skill_bar != null:
            skill_bar.clear_view()
        return
    _hud_elapsed += delta
    if _hud_elapsed < 0.15:
        return
    _hud_elapsed = 0.0
    var combat: Variant = world.get("combat")
    var actor: Dictionary = {}
    if combat != null and str(combat.get("mode")) == "formal":
        actor = combat.actor_snapshot("VACCINE")
    tactical_hud.render_combat_snapshot(actor)
    if status_effects != null:
        status_effects.render_actor(actor)
    if skill_bar != null:
        skill_bar.render_actor(actor)
    # Do not rebuild dozens of Control rows at 60 Hz or reset scroll/focus.
    if hub_panel != null and hub_panel.visible and hub_panel.tabs.current_tab == 4:
        _combat_detail_elapsed += 0.15
        if _combat_detail_elapsed >= 0.5:
            _combat_detail_elapsed = 0.0
            hub_panel.show_live_combat(actor)
    else:
        _combat_detail_elapsed = 0.5
    if mission_tracker != null:
        mission_tracker.render_world(world)

func _update_ui_geometry() -> void:
    if get_viewport() == null or tactical_hud == null:
        return
    var vp: Vector2 = get_viewport().get_visible_rect().size
    tactical_hud.update_layout()
    if status_effects != null:
        status_effects.update_layout()
    if skill_bar != null:
        skill_bar.update_layout()
    if mission_tracker != null:
        mission_tracker.update_layout()
    if hub_panel != null:
        hub_panel._layout_to_viewport()
    if result_panel != null:
        result_panel.update_layout()
    if status_column != null:
        status_column.custom_minimum_size.x = minf(480.0, maxf(240.0, vp.x - 36.0))
    # Responsive overlays for windowed, 4:3, 16:9 and ultrawide layouts.
    if offer_panel != null:
        var offer_width: float = minf(720.0, maxf(300.0, vp.x - 24.0))
        var offer_height: float = minf(420.0, maxf(200.0, vp.y - 24.0))
        offer_panel.custom_minimum_size = Vector2(offer_width, offer_height)
        offer_panel.position = ((vp - offer_panel.custom_minimum_size) * 0.5).max(Vector2.ZERO)
        for card in card_buttons:
            card.custom_minimum_size.x = maxf(210.0, offer_width - 175.0)
    if learn_panel != null:
        var learn_size := Vector2(minf(600.0, maxf(280.0, vp.x - 24.0)),
            minf(280.0, maxf(180.0, vp.y - 24.0)))
        learn_panel.custom_minimum_size = learn_size
        learn_panel.position = ((vp - learn_size) * 0.5).max(Vector2.ZERO)
    if recovery_panel != null:
        var recover_width: float = minf(570.0, maxf(300.0, vp.x - 24.0))
        var recover_size := Vector2(recover_width, minf(300.0, maxf(200.0, vp.y - 24.0)))
        recovery_panel.custom_minimum_size = recover_size
        recovery_panel.position = ((vp - recover_size) * 0.5).max(Vector2.ZERO)
        if recovery_label != null:
            recovery_label.custom_minimum_size.x = maxf(250.0, recover_width - 40.0)

func _unhandled_input(event: InputEvent) -> void:
    if not (event is InputEventKey) or not event.pressed or event.echo:
        return
    if event.keycode == KEY_TAB:
        _toggle_management()
        get_viewport().set_input_as_handled()
    elif event.keycode == KEY_ESCAPE and result_panel != null and result_panel.visible:
        result_panel.call("_dismiss")
        get_viewport().set_input_as_handled()
    elif event.keycode == KEY_ESCAPE and hub_panel != null and hub_panel.visible:
        _close_management()
        get_viewport().set_input_as_handled()

func _create_ui() -> void:
    var layer := CanvasLayer.new()
    layer.name = "PilotHUD"
    layer.layer = 8
    add_child(layer)
    var outer := VBoxContainer.new()
    outer.name = "PilotStatus"
    status_column = outer
    outer.position = Vector2(24, 140)
    outer.custom_minimum_size = Vector2(480, 80)
    layer.add_child(outer)
    tactical_hud = TacticalHUD.new()
    layer.add_child(tactical_hud)
    status_effects = StatusHUD.new()
    layer.add_child(status_effects)
    skill_bar = SkillBar.new()
    layer.add_child(skill_bar)
    mission_tracker = MissionTracker.new()
    layer.add_child(mission_tracker)
    hub_panel = HubManagement.new()
    layer.add_child(hub_panel)
    result_panel = MissionResult.new()
    layer.add_child(result_panel)
    result_panel.dismissed.connect(_on_result_dismissed)
    hub_panel.close_requested.connect(_close_management)
    hub_panel.allocate_requested.connect(_on_hub_allocate)
    hub_panel.plugin_move_requested.connect(_on_hub_plugin_move)
    hub_panel.equip_requested.connect(_on_hub_equip_skills)
    status_label = Label.new()
    status_label.text = "Neon Void Rework · 저장형 하드웨어 파일럿"
    outer.add_child(status_label)
    message_label = Label.new()
    message_label.text = "F: 상호작용 · 메인보드에서 CPU/GPU/RAM/SSD/HDD 선택"
    outer.add_child(message_label)
    hub_button = Button.new()
    hub_button.name = "OpenHubManagement"
    hub_button.text = "백신·능력치·플러그인·스킬 관리  [Tab]"
    hub_button.pressed.connect(_toggle_management)
    outer.add_child(hub_button)
    if qa_skill_tools_enabled:
        qa_skill_button = Button.new()
        qa_skill_button.name = "QAInstallAuthorizedSkills"
        qa_skill_button.text = "QA 전용 · E 저장으로 테스트 스킬 3종 장착"
        qa_skill_button.tooltip_text = "리워크 QA 슬롯 전용. 정식 보상 정책과 무관한 임시 스킬 3종"
        qa_skill_button.pressed.connect(_qa_install_authorized_skills)
        outer.add_child(qa_skill_button)
    deposit_button = Button.new()
    deposit_button.text = "허브 소지 플러그인 수동 보관"
    deposit_button.pressed.connect(_deposit_carried)
    outer.add_child(deposit_button)
    new_vaccine_button = Button.new()
    new_vaccine_button.text = "파기된 백신 대신 새 백신 생성"
    new_vaccine_button.visible = false
    new_vaccine_button.pressed.connect(_choose_new_vaccine)
    outer.add_child(new_vaccine_button)

    offer_panel = PanelContainer.new()
    offer_panel.name = "PluginChoice"
    offer_panel.visible = false
    offer_panel.position = Vector2(260, 150)
    offer_panel.custom_minimum_size = Vector2(720, 420)
    layer.add_child(offer_panel)
    var panel_column := VBoxContainer.new()
    offer_panel.add_child(panel_column)
    var heading := Label.new()
    heading.text = "플러그인 선택 · 3장 중 하나"
    panel_column.add_child(heading)
    for index in range(3):
        var row := HBoxContainer.new()
        panel_column.add_child(row)
        var card := Button.new()
        card.custom_minimum_size = Vector2(520, 106)
        card.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
        card.pressed.connect(_choose_card.bind(index))
        row.add_child(card)
        card_buttons.append(card)
        var reroll := Button.new()
        reroll.text = "재추첨"
        reroll.pressed.connect(_reroll_card.bind(index))
        row.add_child(reroll)
        reroll_buttons.append(reroll)

    learn_panel = PanelContainer.new()
    learn_panel.name = "ExtractionLearning"
    learn_panel.position = Vector2(280, 200)
    learn_panel.custom_minimum_size = Vector2(600, 280)
    learn_panel.visible = false
    layer.add_child(learn_panel)
    var learn_column := VBoxContainer.new()
    learn_panel.add_child(learn_column)
    var learn_title := Label.new()
    learn_title.text = "철수 보상: 이번 의뢰 플러그인 최대 2개 학습 (선택 가능)"
    learn_column.add_child(learn_title)
    learn_list = VBoxContainer.new()
    learn_column.add_child(learn_list)
    var finish_button := Button.new()
    finish_button.text = "선택 확정 및 안전 철수"
    finish_button.pressed.connect(_finish_extraction)
    learn_column.add_child(finish_button)

    # An incident inspector stays responsive even while world ticks are paused.
    # It offers NO resume, retry, abort, restore or overwrite action.
    recovery_panel = PanelContainer.new()
    recovery_panel.name = "RecoveryInspection"
    recovery_panel.position = Vector2(680, 16)
    recovery_panel.custom_minimum_size = Vector2(570, 300)
    recovery_panel.visible = false
    layer.add_child(recovery_panel)
    var recovery_column := VBoxContainer.new()
    recovery_panel.add_child(recovery_column)
    var recovery_heading := Label.new()
    recovery_heading.text = "저장 안전 진단 · 읽기 전용"
    recovery_column.add_child(recovery_heading)
    recovery_label = Label.new()
    recovery_label.custom_minimum_size = Vector2(530, 220)
    recovery_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
    recovery_column.add_child(recovery_label)
    var recovery_refresh := Button.new()
    recovery_refresh.text = "저장 상태 다시 확인 (읽기 전용)"
    recovery_refresh.pressed.connect(_show_recovery_report)
    recovery_column.add_child(recovery_refresh)

func _accept_saved_state(loaded: Dictionary) -> void:
    var saved: Dictionary = loaded.get("snapshot", {})
    if not bool(saved.get("selection_required", false)):
        var vaccine: Variant = saved.get("vaccine")
        if typeof(vaccine) != TYPE_DICTIONARY:
            _halt("missing_vaccine")
            return
        if vaccine.get("mission") != null:
            # Resuming a half-finished procedural run is not yet approved.
            _halt("active_mission_requires_offline_reconciliation")
            return
    _refresh()

func _refresh() -> void:
    if slot_path.is_empty():
        return
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("save_requires_reconciliation:" + str(loaded.get("reason", "unknown")))
        return
    var saved: Dictionary = loaded.get("snapshot", {})
    if tactical_hud != null:
        tactical_hud.render_saved_snapshot(loaded)
        if frozen:
            tactical_hud.show_blocked()
    if mission_tracker != null:
        mission_tracker.show_saved_snapshot(loaded)
        mission_tracker.render_world(world)
    if hub_panel != null and hub_panel.visible:
        hub_panel.show_saved(loaded, not frozen and str(world.get("current_mode")) == "hub")
    if bool(saved.get("selection_required", false)):
        status_label.text = "백신 최종 파기 · 선택 필요"
        new_vaccine_button.visible = not frozen
        deposit_button.visible = false
        if hub_button != null:
            hub_button.disabled = true
        return
    var vaccine: Dictionary = saved.get("vaccine", {})
    if vaccine.is_empty():
        _halt("missing_vaccine")
        return
    status_label.text = "%s백신 Lv.%d · 복제 %d회 남음 · 저장 r%d" % [
        "리워크 QA 전용 · " if qa_skill_tools_enabled else "",
        int(vaccine.get("level", 1)),
        int(vaccine.get("copies_remaining", 0)),
        int(loaded.get("revision", 0))]
    if qa_skill_button != null:
        var ready_skills: bool = vaccine.get("equipped_skills", []) == QA_SKILL_IDS
        qa_skill_button.visible = qa_skill_tools_enabled and (
            str(world.get("current_mode")) == "hub")
        qa_skill_button.disabled = frozen or bridge != null or ready_skills
        qa_skill_button.text = "QA 스킬 3종 · E 저장 완료" if ready_skills else \
            "QA 전용 · E 저장으로 테스트 스킬 3종 장착"
    # Per-plugin DB UI replaces accidental bulk transfers; preserve test API.
    deposit_button.visible = false
    hub_button.disabled = frozen
    new_vaccine_button.visible = false

## QA-only helper: seed provisional B test skills using E's normal
## revision-guarded durable transactions. No direct edits to saves, B actors
## or owner modules. Can only be invoked in the isolated QA scene's hub.
func _qa_install_authorized_skills() -> void:
    if not qa_skill_tools_enabled or frozen or bridge != null \
            or str(world.get("current_mode")) != "hub" \
            or (result_panel != null and result_panel.visible):
        return
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("QA_E_skill_snapshot_unavailable")
        return
    var saved: Dictionary = loaded.get("snapshot", {})
    var vaccine: Dictionary = saved.get("vaccine", {})
    if bool(saved.get("selection_required", false)) or vaccine.is_empty() \
            or vaccine.get("alive") != true or vaccine.get("mission") != null:
        _halt("QA_skills_require_valid_idle_E_vaccine")
        return
    for skill_id in QA_SKILL_IDS:
        if SkillRegistry.definition(skill_id).is_empty():
            _halt("QA_B_skill_registry_missing:" + skill_id)
            return
        if vaccine.get("known_skills", []).has(skill_id):
            continue
        var expected: int = int(loaded.get("revision", -1))
        var saved_ack: Dictionary = Gateway.transact_command(slot_path, {
            "action": "learn_skill",
            "vaccine_id": str(vaccine["vaccine_id"]),
            "generation": int(saved["generation"]),
            "receipt_id": "qa:learn:" + skill_id + ":" + str(expected),
            "skill_id": skill_id
        }, expected)
        if not bool(saved_ack.get("applied", false)):
            _halt("QA_skill_E_learn_rejected:" + str(
                saved_ack.get("reason", "unknown")))
            return
        loaded = Gateway.snapshot(slot_path)
        if not bool(loaded.get("ok", false)) \
                or int(loaded.get("revision", -1)) <= expected:
            _halt("QA_skill_E_readback_failed:" + skill_id)
            return
        saved = loaded["snapshot"]
        vaccine = saved.get("vaccine", {})
        if not vaccine.get("known_skills", []).has(skill_id):
            _halt("QA_skill_E_learn_receipt_unconfirmed:" + skill_id)
            return
    if vaccine.get("equipped_skills", []) != QA_SKILL_IDS:
        var expected: int = int(loaded.get("revision", -1))
        var equipped_ack: Dictionary = Gateway.transact_command(slot_path, {
            "action": "equip_skills",
            "vaccine_id": str(vaccine["vaccine_id"]),
            "generation": int(saved["generation"]),
            "receipt_id": "qa:equip:" + str(expected),
            "equipped": QA_SKILL_IDS.duplicate()
        }, expected)
        if not bool(equipped_ack.get("applied", false)):
            _halt("QA_skill_E_equip_rejected:" + str(
                equipped_ack.get("reason", "unknown")))
            return
        loaded = Gateway.snapshot(slot_path)
        if not bool(loaded.get("ok", false)) \
                or int(loaded.get("revision", -1)) <= expected:
            _halt("QA_skill_E_equip_readback_failed")
            return
        vaccine = loaded.get("snapshot", {}).get("vaccine", {})
        if vaccine.get("equipped_skills", []) != QA_SKILL_IDS:
            _halt("QA_skill_E_equip_unconfirmed")
            return
    message_label.text = "QA 스킬 3종 E 저장 확인 r%d · 출전 후 Q/E/C 테스트 가능" % [
        int(loaded["revision"])]
    _refresh()

## A owns UI-to-E writes: only a fresh, verified hub revision can authorize them.
## Receipt IDs include expected revision; unknown ACK freezes instead of retrying.
func _toggle_management() -> void:
    if frozen or hub_panel == null or offer_panel.visible or learn_panel.visible             or (result_panel != null and result_panel.visible):
        return
    if hub_panel.visible:
        _close_management()
        return
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("management_snapshot_unavailable")
        return
    if bool(loaded.get("snapshot", {}).get("selection_required", false)):
        return
    hub_panel.show_saved(loaded, str(world.get("current_mode")) == "hub")
    hub_panel.visible = true
    get_tree().paused = true

func _close_management() -> void:
    if hub_panel != null:
        hub_panel.visible = false
    if not frozen and not offer_panel.visible and not learn_panel.visible:
        get_tree().paused = false

func _commit_hub_action(action: String, payload: Dictionary, expected: int) -> void:
    if frozen or hub_panel == null or not hub_panel.visible \
            or str(world.get("current_mode")) != "hub" or bridge != null:
        return
    if not ["reallocate_stats", "archive_plugin", "withdraw_plugin", "equip_skills"].has(action):
        return
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("hub_snapshot_unavailable")
        return
    var state: Dictionary = loaded.get("snapshot", {})
    var current: Variant = state.get("vaccine")
    if bool(state.get("selection_required", false)) or typeof(current) != TYPE_DICTIONARY:
        _halt("hub_action_requires_active_vaccine")
        return
    var vaccine: Dictionary = current
    if vaccine.get("mission") != null:
        _halt("hub_action_requires_no_active_mission")
        return
    if int(loaded.get("revision", -1)) != expected:
        hub_panel.show_saved(loaded, true)
        var label: Label = hub_panel.get("hint") as Label
        if label != null:
            label.text = "저장 변경을 발견했어. 현재 데이터를 다시 확인해 줘."
        return
    var receipt: String = "hub:%s:%d" % [action, expected]
    if payload.has("plugin_id"):
        receipt += ":" + str(payload["plugin_id"])
    var command: Dictionary = {
        "action": action, "vaccine_id": str(vaccine.get("vaccine_id", "")),
        "generation": int(state.get("generation", 0)),
        "receipt_id": receipt
    }
    for key in payload:
        if command.has(key):
            _halt("hub_reserved_command_field")
            return
        command[key] = payload[key]
    var ack: Dictionary = Gateway.transact_command(slot_path, command, expected)
    if not bool(ack.get("applied", false)):
        _halt("hub_E_ack_uncertain:" + str(ack.get("reason", "unknown")))
        return
    var confirmed: Dictionary = Gateway.snapshot(slot_path)
    if not bool(confirmed.get("ok", false)) \
            or int(confirmed.get("revision", 0)) <= expected:
        _halt("hub_E_readback_failed")
        return
    hub_panel.show_saved(confirmed, true)
    message_label.text = "변경 사항을 E 저장소에 안전하게 기록했어"
    _refresh()

func _on_hub_allocate(allocations: Dictionary, expected: int) -> void:
    if frozen or hub_panel == null or not hub_panel.visible:
        return
    var current: Dictionary = Gateway.snapshot(slot_path)
    if not bool(current.get("ok", false)):
        _halt("stat_snapshot_unavailable")
        return
    var vaccine: Dictionary = current.get("snapshot", {}).get("vaccine", {})
    if vaccine.is_empty():
        return
    var balance: Dictionary = current.get("snapshot", {}).get("balance", {})
    var cost: int = Balance.stat_cost_fragments(allocations, balance)
    if cost < 0 or cost > int(vaccine.get("earned_fragments", 0)):
        return
    _commit_hub_action("reallocate_stats",
        {"allocations": allocations.duplicate(true)}, expected)

func _on_hub_plugin_move(action: String, plugin_id: String, expected: int) -> void:
    if ["archive_plugin", "withdraw_plugin"].has(action) and not plugin_id.is_empty():
        _commit_hub_action(action, {"plugin_id": plugin_id}, expected)

func _on_hub_equip_skills(equipped: Array, expected: int) -> void:
    if equipped.size() > 3:
        return
    var seen: Dictionary = {}
    for id in equipped:
        if not (id is String) or str(id).is_empty() or seen.has(id):
            return
        seen[id] = true
    _commit_hub_action("equip_skills",
        {"equipped": equipped.duplicate(true)}, expected)

func _on_mission_requested(request: Dictionary) -> void:
    if frozen or bridge != null or (result_panel != null and result_panel.visible):
        return
    if hub_panel != null and hub_panel.visible:
        _close_management()
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("mission_snapshot_unavailable")
        return
    var saved: Dictionary = loaded.get("snapshot", {})
    if bool(saved.get("selection_required", false)) or saved.get("vaccine") == null:
        _halt("vaccine_selection_required")
        return
    var vaccine: Dictionary = saved["vaccine"]
    if vaccine.get("mission") != null:
        _halt("active_mission_unresolved")
        return
    var seed: int = int(request.get("seed", -1))
    if seed < 0:
        _halt("invalid_world_seed")
        return
    var generation: int = int(saved["generation"])
    current_run_id = "pilot:%d:%d" % [int(loaded["revision"]), Time.get_ticks_usec()]
    bridge = Bridge.new(slot_path, current_run_id, str(vaccine["vaccine_id"]), generation)
    bridge.reconciliation_required.connect(_on_reconciliation_required)
    var started: Dictionary = bridge.begin_mission(seed)
    if not bool(started.get("ok", false)):
        _halt("mission_save_start_rejected")
        return
    offers = Offers.new()
    var initial_levels: Dictionary = {}
    for plugin_id in vaccine.get("carried", {}):
        initial_levels[plugin_id] = int(vaccine["carried"][plugin_id]["level"])
    if not offers.begin_mission(seed, initial_levels, current_run_id):
        _halt("cannot_begin_plugin_roll")
        return
    world.call("attach_progression_bridge", bridge)
    if not bool(world.call("confirm_mission_started", current_run_id, seed)):
        _halt("world_failed_after_E_mission_commit")
        return
    # Only mission start needs a bridge attached for D's preflight guard.
    # The remaining D facts are routed via A so reward learning happens
    # before the extraction transaction and its user choice is durable.
    world.call("attach_progression_bridge", null)
    # B's pre-tick facade handshake already applied the exact E profile
    # synchronously as D created the motherboard encounter.
    if frozen:
        return
    message_label.text = "의뢰 저장 완료 · 메인보드에서 하드웨어 선택"
    _refresh()

## A is the only owner of E transactions. All supported hardware maps
## require immutable region/generator/seed checkpoint BEFORE D draws geometry.
## The E approval is scoped to the actual selected run and its D mission plan.
func _on_hardware_checkpoint_requested(fact: Dictionary) -> void:
    if frozen or bridge == null:
        _halt("hardware_checkpoint_without_active_E_run")
        return
    var region: String = str(fact.get("region_id", ""))
    var plan: Dictionary = world.get("mission")
    var expected: Dictionary = plan.get("regions", {}).get(region, {})
    var receipt: String = str(fact.get("receipt_id", ""))
    if not ["CPU", "GPU", "RAM", "SSD", "HDD"].has(region) \
            or str(fact.get("run_id", "")) != current_run_id \
            or receipt != current_run_id + ":" + region + ":map-checkpoint" \
            or str(fact.get("generator_id", "")) != Maps.GENERATOR_ID \
            or expected.is_empty() or str(expected.get("id", "")) != region \
            or expected != fact.get("region_plan", {}) \
            or int(fact.get("map_seed", -1)) != int(expected.get("map_seed", -2)):
        _halt("hardware_map_plan_changed_before_save")
        return
    var ack: Dictionary = bridge.checkpoint_hardware_entry(fact)
    if not bool(ack.get("ok", false)):
        _halt("hardware_map_checkpoint_not_durable")
        return
    if not bool(world.call("acknowledge_hardware_checkpoint", receipt, ack)):
        _halt("hardware_map_not_entered_after_durable_ack")
        return
    # D creates a new B actor during the E-approved map transition.
    # The opt-in facade handshake synchronously re-authorized B/C from E
    # before this D world call returned, without awarding anything.
    if frozen:
        return
    _refresh()

func _on_enemy_defeated(actor_id: String, receipt: String) -> void:
    if frozen or bridge == null:
        return
    if current_run_id.is_empty():
        return
    var amount: int = 8
    if actor_id.to_lower().contains("boss"):
        amount = 120
    var result: Dictionary = bridge.record_mission_xp("kill:" + receipt, amount)
    if not bool(result.get("ok", false)):
        _halt("combat_xp_persistence_failed")
        return
    _maybe_open_offer()
    _refresh()

func _on_encounter_authorization_requested(run_id: String) -> void:
    # This runs SYNCHRONOUSLY inside D -> A facade -> B begin_encounter,
    # before the first B tick on motherboard, return or any hardware scene.
    if frozen or bridge == null or run_id != current_run_id:
        _halt("B_encounter_missing_matching_E_mission")
        return
    if not _sync_authorized_build_from_E():
        return
    var live: Variant = world.get("combat").get("live")
    var authorized: Dictionary = live.authorized_growth_snapshot()
    if not bool(live.confirm_encounter_authorized(
            run_id, int(authorized.get("revision", -1)))):
        _halt("B_encounter_approval_handshake_failed")

## E → A → B: both permanent growth/3 learned skills AND C plugin levels
## must be authorized from the SAME verified E revision before the B 60Hz
## encounter's first physics tick. Every hardware transition resets B, so
## this function must be called again after the E map checkpoint and D entry.
func _sync_authorized_build_from_E() -> bool:
    if frozen or bridge == null or bool(bridge.frozen):
        return false
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("authorized_loadout_snapshot_failed")
        return false
    var saved: Dictionary = loaded.get("snapshot", {})
    var raw_vaccine: Variant = saved.get("vaccine", null)
    if bool(saved.get("selection_required", false)) or not (raw_vaccine is Dictionary):
        _halt("authorized_loadout_vaccine_missing")
        return false
    var vaccine: Dictionary = raw_vaccine
    var mission_value: Variant = vaccine.get("mission", null)
    if not (mission_value is Dictionary):
        _halt("authorized_loadout_mission_missing")
        return false
    var mission: Dictionary = mission_value
    if current_run_id.is_empty() or str(mission.get("run_id", "")) != current_run_id \
            or str(world.get("current_run_id")) != current_run_id \
            or str(bridge.run_id) != current_run_id \
            or str(vaccine.get("vaccine_id", "")) != str(bridge.vaccine_id) \
            or int(saved.get("generation", -1)) != int(bridge.generation) \
            or int(loaded.get("revision", -1)) < 1:
        _halt("E_B_progression_identity_diverged")
        return false
    var combat: Variant = world.get("combat")
    if combat == null or str(combat.get("mode")) != "formal":
        _halt("B_growth_requires_formal_world")
        return false
    var live: Variant = combat.get("live")
    if live == null or live.bridge == null or live.bridge.run_id != current_run_id:
        _halt("B_growth_encounter_mismatch")
        return false
    # B's one-shot public method checks the E payload, player baseline and
    # learned/equipped registry IDs. A NEVER constructs a substitute profile.
    var growth_ack: Dictionary = live.configure_authorized_progression(loaded)
    if not bool(growth_ack.get("ok", false)) \
            or int(growth_ack.get("revision", -1)) != int(loaded["revision"]) \
            or str(growth_ack.get("vaccine_id", "")) != str(bridge.vaccine_id) \
            or int(growth_ack.get("generation", -1)) != int(bridge.generation):
        _halt("B_E_growth_ack_rejected:" + str(growth_ack.get("reason", "identity_mismatch")))
        return false
    var levels: Dictionary = {}
    var carried: Dictionary = vaccine.get("carried", {})
    for plugin_id in carried:
        levels[str(plugin_id)] = int(carried[plugin_id].get("level", 0))
    var obtained: Dictionary = mission.get("obtained_plugins", {})
    for plugin_id in obtained:
        levels[str(plugin_id)] = int(obtained[plugin_id].get("level", 0))
    # Existing C effect bridge is still authoritative. A never duplicates
    # C numeric plugin instances or injects ad-hoc skill definitions.
    if not bool(combat.configure_authorized_build(levels, [], [])):
        _halt("B_C_initial_authorized_build_rejected")
        return false
    return true

## C draws its offer first in volatile memory. E #118 must atomically
## checkpoint that exact offer, RNG cursor and reroll state before it is
## ever shown. Any ambiguous failure freezes rather than re-drawing.
func _checkpoint_current_offer() -> bool:
    if offers == null or bridge == null:
        _halt("offer_checkpoint_missing_engine")
        return false
    var state: Dictionary = offers.export_mission_state()
    var offer: Dictionary = offers.offer_snapshot()
    var receipt: Dictionary = bridge.checkpoint_offer(
        offer, str(state.get("rng_state", "")))
    if not bool(receipt.get("ok", false)):
        _halt("offer_checkpoint_not_durable")
        return false
    return true

func _maybe_open_offer() -> void:
    if frozen or offers == null or offer_panel.visible:
        return
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("reward_snapshot_unavailable")
        return
    var vaccine: Dictionary = loaded["snapshot"].get("vaccine", {})
    if vaccine.get("mission") == null:
        return
    var mission: Dictionary = vaccine["mission"]
    if int(mission.get("pending_plugin_offers", 0)) <= 0:
        return
    var offer: Dictionary = offers.roll_offer({
        "cleared_hardware": mission.get("hardware_cleaned_ids", []).size(),
        "difficulty_tier": 0, "luck_points": 0.0
    })
    if not bool(offer.get("active", false)):
        _halt("no_plugin_candidates")
        return
    if not _checkpoint_current_offer():
        return
    offer_panel.visible = true
    get_tree().paused = true
    _render_cards()

func _render_cards() -> void:
    if offers == null:
        return
    var offer: Dictionary = offers.offer_snapshot()
    var cards: Array = offer.get("cards", [])
    if cards.size() != 3:
        _halt("invalid_offer_cards")
        return
    for index in range(3):
        var card: Dictionary = cards[index]
        var info: Dictionary = offers.get_entry(str(card.get("plugin_id", "")))
        var rarity: String = str(card.get("rarity", ""))
        var level: int = int(offers.levels.get(str(card.get("plugin_id", "")), 0))
        card_buttons[index].text = CardStyle.card_label(card, info, level)
        for variant in ["normal", "hover", "pressed", "disabled"]:
            card_buttons[index].add_theme_stylebox_override(
                variant, CardStyle.card_style(rarity, variant))
        card_buttons[index].add_theme_color_override(
            "font_color", CardStyle.rarity_color(rarity))
        card_buttons[index].disabled = bool(card.get("empty", false)) or frozen
        reroll_buttons[index].disabled = frozen or int(offer.get("rerolls_remaining", 0)) <= 0
        reroll_buttons[index].text = "재추첨 (%d)" % int(offer.get("rerolls_remaining", 0))

func _choose_card(index: int) -> void:
    if frozen or offers == null or bridge == null:
        return
    var ack: Dictionary = bridge.choose_and_commit_plugin(offers, index)
    if not bool(ack.get("ok", false)):
        _halt("plugin_commit_or_confirmation_failed")
        return
    var confirmed_plugin: Dictionary = ack.get("plugin", {})
    var combat: Variant = world.get("combat")
    var bonus: Dictionary = {}
    if str(confirmed_plugin.get("plugin_id", "")) == RareBonus.SOURCE_ID:
        # Source and bonus are a SINGLE durable E transaction. Never infer
        # a rare from C's pending command or independently roll on the client.
        var loaded: Dictionary = Gateway.snapshot(slot_path)
        if not bool(loaded.get("ok", false)):
            _halt("atomic_rare_bonus_E_snapshot_unreadable")
            return
        var snapshot: Dictionary = loaded.get("snapshot", {})
        var vaccine: Dictionary = snapshot.get("vaccine", {})
        var mission: Dictionary = vaccine.get("mission", {})
        bonus = mission.get("environment_rolls", {}).get(
            RareBonus.KEY, {}).get(str(ack.get("receipt_id", "")), {})
        if bonus.is_empty() or not vaccine.get("receipts", {}).has(
                str(bonus.get("bonus_receipt", ""))):
            _halt("atomic_rare_bonus_receipt_missing")
            return
        var bonus_id: String = str(bonus.get("plugin_id", ""))
        var bonus_level: int = int(bonus.get("level", 0))
        var authoritative: Dictionary = mission.get("obtained_plugins", {}).get(
            bonus_id, {})
        if str(bonus.get("rarity", "")) != "rare" \
                or bonus_id.is_empty() or int(authoritative.get("level", 0)) != bonus_level \
                or int(offers.levels.get(bonus_id, 0)) + 1 != bonus_level:
            _halt("atomic_rare_bonus_E_C_level_divergence")
            return
    # Only AFTER E confirms source + possible rare reward do we mutate B/C.
    if combat == null or not bool(combat.upgrade_plugin_after_ack(
            str(confirmed_plugin.get("plugin_id", "")),
            int(confirmed_plugin.get("level", 0)))):
        _halt("B_C_effect_sync_failed_after_E_commit")
        return
    var bonus_message: String = ""
    if not bonus.is_empty():
        var bonus_id: String = str(bonus.get("plugin_id", ""))
        var bonus_level: int = int(bonus.get("level", 0))
        if not bool(combat.upgrade_plugin_after_ack(bonus_id, bonus_level)):
            _halt("B_C_rare_bonus_sync_failed_after_E_commit")
            return
        # The offer engine tracks candidate levels separately from B combat.
        # This is an E-verified level, never an optimistic UI award.
        offers.levels[bonus_id] = bonus_level
        bonus_message = " · 레어 보너스: " + str(offers.get_entry(bonus_id).get(
            "name_ko", bonus_id))
    offer_panel.visible = false
    get_tree().paused = false
    message_label.text = "플러그인 저장 완료" + bonus_message
    _refresh()
    _maybe_open_offer()

func _reroll_card(index: int) -> void:
    if frozen or offers == null:
        return
    var changed: Dictionary = offers.reroll_card(index)
    if not bool(changed.get("active", false)):
        _halt("plugin_reroll_failed")
        return
    if not _checkpoint_current_offer():
        return
    _render_cards()

func _on_mission_returned(fact: Dictionary) -> void:
    bridge = null
    offers = null
    current_run_id = ""
    offer_panel.visible = false
    learn_panel.visible = false
    learn_checks.clear()
    pending_extraction.clear()
    if hub_panel != null:
        hub_panel.visible = false
    message_label.text = "의뢰 결과 영구 저장됨 · 허브 복귀"
    _refresh()
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)) or result_panel == null             or not bool(result_panel.show_verified(fact, loaded)):
        _halt("terminal_outcome_receipt_unverified")
        return
    get_tree().paused = true

func _on_result_dismissed() -> void:
    if not frozen and not offer_panel.visible and not learn_panel.visible             and (hub_panel == null or not hub_panel.visible):
        get_tree().paused = false

func _deposit_carried() -> void:
    if frozen:
        return
    var snapshot: Dictionary = Gateway.snapshot(slot_path)
    if not bool(snapshot.get("ok", false)):
        _halt("archive_snapshot_unavailable")
        return
    var saved: Dictionary = snapshot["snapshot"]
    var vaccine: Dictionary = saved.get("vaccine", {})
    if vaccine.get("mission") != null:
        return
    var carried: Dictionary = vaccine.get("carried", {})
    for plugin_id in carried:
        var receipt: String = "archive:%d:%s" % [int(snapshot["revision"]), str(plugin_id)]
        var ack: Dictionary = Gateway.transact_command(slot_path, {
            "action": "archive_plugin", "vaccine_id": vaccine["vaccine_id"],
            "generation": int(saved["generation"]), "plugin_id": str(plugin_id),
            "receipt_id": receipt
        })
        if not bool(ack.get("applied", false)):
            _halt("archive_commit_rejected:" + str(ack.get("reason", "unknown")))
            return
    message_label.text = "선택한 소지 플러그인을 데이터베이스에 보관했어"
    _refresh()

func _choose_new_vaccine() -> void:
    if frozen or (result_panel != null and result_panel.visible):
        return
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("selection_snapshot_unavailable")
        return
    var saved: Dictionary = loaded["snapshot"]
    if not bool(saved.get("selection_required", false)):
        return
    var next_id: String = "pilot-vaccine-%d" % (int(saved["generation"]) + 1)
    var ack: Dictionary = Gateway.transact_command(slot_path, {
        "action": "choose_new_vaccine", "new_vaccine_id": next_id
    })
    if not bool(ack.get("applied", false)):
        _halt("vaccine_selection_failed")
        return
    _refresh()

func _on_world_fact_requested(fact: Dictionary) -> void:
    if frozen or bridge == null:
        return
    if str(fact.get("type", "")) == "boss_defeated":
        return
    var ack: Dictionary = bridge.handle_world_fact(fact)
    if not bool(ack.get("ok", false)):
        _halt("hardware_objective_save_failed")
        return
    if not bool(world.call("acknowledge_world_fact",
            str(fact.get("receipt_id", "")), ack)):
        _halt("hardware_objective_ack_failed")

func _on_extraction_requested(fact: Dictionary) -> void:
    if frozen or bridge == null:
        return
    if not pending_extraction.is_empty():
        _halt("overlapping_extraction")
        return
    if str(fact.get("run_id", "")) != current_run_id:
        _halt("extraction_run_mismatch")
        return
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("cannot_read_extraction_choices")
        return
    var vaccine: Dictionary = loaded["snapshot"].get("vaccine", {})
    var mission: Variant = vaccine.get("mission")
    if typeof(mission) != TYPE_DICTIONARY:
        _halt("no_active_mission_for_extraction")
        return
    learn_checks.clear()
    for child in learn_list.get_children():
        learn_list.remove_child(child)
        child.queue_free()
    var obtained: Dictionary = mission.get("obtained_plugins", {})
    var archive: Dictionary = vaccine.get("archive", {})
    var carried: Dictionary = vaccine.get("carried", {})
    for plugin_id in obtained:
        var info: Dictionary = obtained[plugin_id]
        if not ["common", "rare"].has(str(info.get("rarity", ""))):
            continue
        # Previously learned instances are deliberately excluded until the
        # unapproved re-learning policy is specified (E currently rejects).
        if archive.has(plugin_id) or carried.has(plugin_id):
            continue
        var choice := CheckBox.new()
        choice.text = "[%s] %s · Lv.%d" % [str(info["rarity"]), str(plugin_id), int(info["level"])]
        choice.set_meta("plugin_id", str(plugin_id))
        learn_list.add_child(choice)
        learn_checks.append(choice)
    if learn_checks.is_empty():
        var note := Label.new()
        note.text = "학습 가능한 신규 일반·레어 플러그인이 없어. 보상 없이 철수 가능."
        learn_list.add_child(note)
    pending_extraction = fact.duplicate(true)
    learn_panel.visible = true
    get_tree().paused = true

func _finish_extraction() -> void:
    if frozen or bridge == null or pending_extraction.is_empty():
        return
    var chosen: Array = []
    for checkbox in learn_checks:
        if checkbox.button_pressed:
            chosen.append(str(checkbox.get_meta("plugin_id", "")))
    if chosen.size() > 2:
        message_label.text = "학습할 플러그인은 최대 2개까지 선택할 수 있어"
        return
    var ack: Dictionary = bridge.commit_terminal_fact(pending_extraction, chosen)
    if not bool(ack.get("ok", false)):
        _halt("extraction_save_failed")
        return
    if not bool(world.call("acknowledge_extraction", ack)):
        _halt("extraction_world_ack_failed")
        return
    learn_panel.visible = false
    get_tree().paused = result_panel != null and result_panel.visible

func _on_death_requested(fact: Dictionary) -> void:
    if frozen or bridge == null:
        return
    var ack: Dictionary = bridge.commit_terminal_fact(fact)
    if not bool(ack.get("ok", false)):
        _halt("death_save_failed")
        return
    if not bool(world.call("acknowledge_death", ack)):
        _halt("death_world_ack_failed")

func _on_unavailable(region_id: String) -> void:
    message_label.text = region_id + "는 아직 제작 중이야"

func _on_world_error(reason: String) -> void:
    _halt("world:" + reason)

func _on_reconciliation_required(reason: String, event: Dictionary) -> void:
    pending_reconciliation = event.duplicate(true)
    _halt("E_reconciliation_required:" + reason)

func _show_recovery_report() -> void:
    if recovery_panel == null or recovery_label == null:
        return
    var report: Dictionary = RecoveryView.inspect(slot_path, pending_reconciliation)
    recovery_label.text = str(report.get("text", "저장 상태 검증 불가"))
    recovery_panel.visible = true

func _halt(reason: String) -> void:
    frozen = true
    if hub_panel != null:
        hub_panel.visible = false
    if result_panel != null:
        result_panel.visible = false
    if tactical_hud != null:
        tactical_hud.show_blocked()
    if status_effects != null:
        status_effects.clear_view()
    if skill_bar != null:
        skill_bar.clear_view()
    if mission_tracker != null:
        mission_tracker.show_blocked()
    if message_label != null:
        message_label.text = "저장 안전 정지: " + reason
    if offer_panel != null:
        offer_panel.visible = false
    if learn_panel != null:
        learn_panel.visible = false
    # A failed/ambiguous E commit freezes ALL gameplay ticks, not merely
    # movement. Keep the A CanvasLayer responsive for recovery instructions.
    get_tree().paused = true
    if world != null and world.get("player") != null:
        var player: Node = world.get("player") as Node
        player.set("movement_enabled", false)
    _show_recovery_report()
