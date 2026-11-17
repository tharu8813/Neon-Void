extends Node3D
## A-owned opt-in persistent prototype: D's standalone CPU scene + E save + C cards.
## Not the default main game, and not a replacement for B's production combat.
## Never reads/writes legacy save files. A failed/ambiguous transaction blocks play.

const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Bridge = preload("res://scripts/rework/integration/mission_commit_bridge.gd")
const Offers = preload("res://scripts/rework/plugins/plugin_offer_engine.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const RareBonus = preload("res://scripts/rework/progression/rare_bonus.gd")
const RecoveryView = preload("res://scripts/rework/integration/pilot_recovery_view.gd")
const TacticalHUD = preload("res://scripts/rework/ui/pilot_tactical_hud.gd")
const CardStyle = preload("res://scripts/rework/ui/pilot_reward_card_style.gd")

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
var learn_panel: PanelContainer
var learn_list: VBoxContainer
var learn_checks: Array[CheckBox] = []
var pending_extraction: Dictionary = {}
var pending_reconciliation: Dictionary = {}
var recovery_panel: PanelContainer
var recovery_label: Label
var tactical_hud: PanelContainer
var _hud_elapsed: float = 0.0

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    # The reward overlay must respond while the simulation itself is paused.
    world.process_mode = Node.PROCESS_MODE_PAUSABLE
    world.set("demo_mode", false)
    _create_ui()
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
    _hud_elapsed += delta
    if _hud_elapsed < 0.15:
        return
    _hud_elapsed = 0.0
    var combat: Variant = world.get("combat")
    if combat != null and str(combat.get("mode")) == "formal":
        tactical_hud.render_combat_snapshot(combat.actor_snapshot("VACCINE"))
    else:
        tactical_hud.render_combat_snapshot({})

func _create_ui() -> void:
    var layer := CanvasLayer.new()
    layer.name = "PilotHUD"
    layer.layer = 8
    add_child(layer)
    var outer := VBoxContainer.new()
    outer.name = "PilotStatus"
    outer.position = Vector2(24, 132)
    outer.custom_minimum_size = Vector2(480, 80)
    layer.add_child(outer)
    tactical_hud = TacticalHUD.new()
    layer.add_child(tactical_hud)
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
        card.alignment = HORIZONTAL_ALIGNMENT_LEFT
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
    # Only mission start needs a bridge attached for D's preflight guard.
    # The remaining D facts are routed via A so reward learning happens
    # before the extraction transaction and its user choice is durable.
    world.call("attach_progression_bridge", null)
    if not _sync_authorized_build_from_E():
        return
    message_label.text = "CPU 의뢰 저장됨 · 전투 XP로 플러그인 획득"
    _refresh()

## A is the only owner of E transactions; D cannot create a real CPU
## map until the seed + exact generation algorithm have been durably written.
func _on_hardware_checkpoint_requested(fact: Dictionary) -> void:
    if frozen or bridge == null:
        _halt("hardware_checkpoint_without_active_E_run")
        return
    var plan: Dictionary = world.get("mission")
    var expected: Dictionary = plan.get("regions", {}).get("CPU", {})
    if str(fact.get("run_id", "")) != current_run_id \
            or str(fact.get("region_id", "")) != "CPU" \
            or str(fact.get("generator_id", "")) != Maps.GENERATOR_ID \
            or expected.is_empty() or expected != fact.get("region_plan", {}) \
            or int(fact.get("map_seed", -1)) != int(expected.get("map_seed", -2)):
        _halt("CPU_map_plan_changed_before_save")
        return
    var ack: Dictionary = bridge.checkpoint_hardware_entry(fact)
    if not bool(ack.get("ok", false)):
        _halt("CPU_map_checkpoint_not_durable")
        return
    if not bool(world.call("acknowledge_hardware_checkpoint",
            str(fact.get("receipt_id", "")), ack)):
        _halt("CPU_map_not_entered_after_durable_ack")

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

## Only an E-authenticated snapshot can authorize plugin levels for B/C.
## The B runtime is never allowed to infer a grant from an unsaved UI card.
func _sync_authorized_build_from_E() -> bool:
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    if not bool(loaded.get("ok", false)):
        _halt("authorized_loadout_snapshot_failed")
        return false
    var saved: Dictionary = loaded.get("snapshot", {})
    var vaccine: Dictionary = saved.get("vaccine", {})
    if vaccine.is_empty() or vaccine.get("mission") == null:
        _halt("authorized_loadout_mission_missing")
        return false
    var levels: Dictionary = {}
    var carried: Dictionary = vaccine.get("carried", {})
    for plugin_id in carried:
        levels[str(plugin_id)] = int(carried[plugin_id].get("level", 0))
    var obtained: Dictionary = vaccine["mission"].get("obtained_plugins", {})
    for plugin_id in obtained:
        levels[str(plugin_id)] = int(obtained[plugin_id].get("level", 0))
    var combat: Variant = world.get("combat")
    if combat == null or not bool(combat.configure_authorized_build(levels, [], [])):
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

func _on_mission_returned(_fact: Dictionary) -> void:
    bridge = null
    offers = null
    current_run_id = ""
    offer_panel.visible = false
    learn_panel.visible = false
    learn_checks.clear()
    pending_extraction.clear()
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
    get_tree().paused = false

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
    message_label.text = region_id + "는 아직 플레이 가능한 구역이 아니야"

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
    if tactical_hud != null:
        tactical_hud.show_blocked()
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
