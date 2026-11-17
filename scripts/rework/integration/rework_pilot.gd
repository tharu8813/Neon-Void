extends Node3D
## A-owned opt-in persistent prototype: D's standalone CPU scene + E save + C cards.
## Not the default main game, and not a replacement for B's production combat.
## Never reads/writes legacy save files. A failed/ambiguous transaction blocks play.

const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Bridge = preload("res://scripts/rework/integration/mission_commit_bridge.gd")
const Offers = preload("res://scripts/rework/plugins/plugin_offer_engine.gd")

@export var slot_id: String = "integration-pilot"
@export var initial_vaccine_id: String = "pilot-vaccine"

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

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    # The reward overlay must respond while the simulation itself is paused.
    world.process_mode = Node.PROCESS_MODE_PAUSABLE
    world.set("demo_mode", false)
    _create_ui()
    world.connect("mission_start_requested", Callable(self, "_on_mission_requested"))
    world.connect("mission_returned", Callable(self, "_on_mission_returned"))
    world.connect("integration_error", Callable(self, "_on_world_error"))
    world.connect("unavailable_hardware_requested", Callable(self, "_on_unavailable"))
    var preview_combat: Node = world.get("combat") as Node
    if preview_combat != null:
        preview_combat.connect("enemy_defeated", Callable(self, "_on_enemy_defeated"))
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

func _create_ui() -> void:
    var layer := CanvasLayer.new()
    layer.name = "PilotHUD"
    layer.layer = 8
    add_child(layer)
    var outer := VBoxContainer.new()
    outer.name = "PilotStatus"
    outer.position = Vector2(24, 16)
    outer.custom_minimum_size = Vector2(480, 80)
    layer.add_child(outer)
    status_label = Label.new()
    status_label.text = "Neon Void Rework · 저장형 CPU 파일럿"
    outer.add_child(status_label)
    message_label = Label.new()
    message_label.text = "F: 상호작용 · CPU 외 하드웨어는 개발 중"
    outer.add_child(message_label)
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
    offer_panel.position = Vector2(260, 180)
    offer_panel.custom_minimum_size = Vector2(690, 330)
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
        card.custom_minimum_size = Vector2(490, 72)
        card.pressed.connect(_choose_card.bind(index))
        row.add_child(card)
        card_buttons.append(card)
        var reroll := Button.new()
        reroll.text = "재추첨"
        reroll.pressed.connect(_reroll_card.bind(index))
        row.add_child(reroll)
        reroll_buttons.append(reroll)

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
    if bool(saved.get("selection_required", false)):
        status_label.text = "백신 최종 파기 · 선택 필요"
        new_vaccine_button.visible = not frozen
        deposit_button.visible = false
        return
    var vaccine: Dictionary = saved.get("vaccine", {})
    if vaccine.is_empty():
        _halt("missing_vaccine")
        return
    status_label.text = "백신 Lv.%d · 복제 %d회 남음 · 저장 r%d" % [
        int(vaccine.get("level", 1)),
        int(vaccine.get("copies_remaining", 0)),
        int(loaded.get("revision", 0))]
    deposit_button.visible = not frozen and vaccine.get("mission") == null and not vaccine.get("carried", {}).is_empty()
    new_vaccine_button.visible = false

func _on_mission_requested(request: Dictionary) -> void:
    if frozen or bridge != null:
        return
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
    message_label.text = "CPU 의뢰 저장됨 · 전투 XP로 플러그인 획득"
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
        card_buttons[index].text = "%d. [%s] %s · Lv.%d" % [
            index + 1, str(card.get("rarity", "")),
            str(info.get("name", card.get("plugin_id", ""))),
            int(offers.levels.get(str(card.get("plugin_id", "")), 0)) + 1]
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
    offer_panel.visible = false
    get_tree().paused = false
    message_label.text = "플러그인 저장 완료: " + str(ack.get("receipt_id", ""))
    _refresh()
    _maybe_open_offer()

func _reroll_card(index: int) -> void:
    if frozen or offers == null:
        return
    var changed: Dictionary = offers.reroll_card(index)
    if not bool(changed.get("active", false)):
        _halt("plugin_reroll_failed")
        return
    _render_cards()

func _on_mission_returned(_fact: Dictionary) -> void:
    bridge = null
    offers = null
    current_run_id = ""
    offer_panel.visible = false
    get_tree().paused = false
    message_label.text = "의뢰 결과 저장됨 · 허브 복귀"
    _refresh()

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
    if frozen:
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

func _on_unavailable(region_id: String) -> void:
    message_label.text = region_id + "는 아직 플레이 가능한 구역이 아니야"

func _on_world_error(reason: String) -> void:
    _halt("world:" + reason)

func _on_reconciliation_required(reason: String, _event: Dictionary) -> void:
    _halt("E_reconciliation_required:" + reason)

func _halt(reason: String) -> void:
    frozen = true
    if message_label != null:
        message_label.text = "저장 안전 정지: " + reason
    if offer_panel != null:
        offer_panel.visible = false
    get_tree().paused = false
    if world != null and world.get("player") != null:
        var player: Node = world.get("player") as Node
        player.set("movement_enabled", false)
