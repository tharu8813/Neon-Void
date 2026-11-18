extends SceneTree
## Pure cross-team A↔C↔D↔E contract smoke; NO gameplay scene or actual user save.
## Run: godot --headless --path . --script res://tests/rework/integration/mission_commit_bridge_smoke.gd
const Bridge = preload("res://scripts/rework/integration/mission_commit_bridge.gd")
const Model = preload("res://scripts/rework/progression/state_model.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const World = preload("res://scripts/rework/world/extraction_state.gd")
const WorldCatalog = preload("res://scripts/rework/world/hardware_catalog.gd")
const PluginCatalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Offers = preload("res://scripts/rework/plugins/plugin_offer_engine.gd")
const ShieldCases = preload("res://tests/rework/integration/provisional_shield_contract_cases.gd")
const RareBonusCases = preload("res://tests/rework/integration/rare_bonus_cases.gd")

var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A INTEGRATION FAIL: " + message)

func _delete_synthetic_tree(folder: String) -> void:
    var dir: DirAccess = DirAccess.open(folder)
    if dir == null:
        return
    dir.list_dir_begin()
    var child: String = dir.get_next()
    while not child.is_empty():
        if child != "." and child != "..":
            var location: String = folder.path_join(child)
            if dir.current_is_dir():
                _delete_synthetic_tree(location)
            else:
                DirAccess.remove_absolute(location)
        child = dir.get_next()
    dir.list_dir_end()
    DirAccess.remove_absolute(folder)

func _run() -> void:
    ShieldCases.run(Callable(self, "check"))
    RareBonusCases.run(Callable(self, "check"))
    if failures == 0:
        print("REWORK A RARE BONUS PASS: one E atomic source+rare receipt, deterministic eligible pool, replay denied")
    if failures == 0:
        print("REWORK A SHIELDS PROPOSAL PASS: periodic/skill/dash real B shielding, HP-only after depletion")
    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var slot_id: String = "a-contract-" + suffix
    var synthetic_parent: String = ProjectSettings.globalize_path("user://rework-a-test-" + suffix)
    var slot_path: String = synthetic_parent.path_join("rework-slot-" + slot_id)
    var initialized: Dictionary = Store.initialize(
        slot_path, Model.new_slot(slot_id, "vaccine-contract"))
    check(initialized.get("ok", false), "create isolated synthetic store")
    if not initialized.get("ok", false):
        _delete_synthetic_tree(synthetic_parent)
        quit(1)
        return

    var bridge = Bridge.new(slot_path, "run-contract", "vaccine-contract", 1)
    var ack: Dictionary = bridge.begin_mission(92417)
    check(ack.get("ok", false), "D mission start persisted before world publication")
    check(bridge.record_mission_xp("run-contract:kill-1", 100).get("ok", false),
        "B kill XP creates mission level-up via E transaction")
    var before_offer: Dictionary = Gateway.snapshot(slot_path)
    check(before_offer.get("ok", false)
        and before_offer["snapshot"]["vaccine"]["mission"]["pending_plugin_offers"] == 1,
        "E mission level 2 queues exactly one plugin reward")

    # Override only odds for this deterministic integration test, NOT real balance.
    var draft_odds: Dictionary = PluginCatalog.odds_settings()
    draft_odds["rarity_weights"] = {
        "common": 100.0, "rare": 0.0, "legendary": 0.0, "mythic": 0.0
    }
    var empty_catalog: Array[Dictionary] = []
    var offers = Offers.new(empty_catalog, draft_odds)
    check(offers.begin_mission(92417, {}, "run-contract"), "C catalog starts with durable run ID")
    var cards: Dictionary = offers.roll_offer({
        "cleared_hardware": 0, "difficulty_tier": 0, "luck_points": 0.0})
    check(cards.get("active", false), "C generated a 3-card selection")
    var selected: Dictionary = offers.prepare_card(0)
    check(not selected.is_empty() and selected.get("rarity", "") == "common",
        "deterministic common plugin prepared")
    if selected.is_empty():
        _delete_synthetic_tree(synthetic_parent)
        quit(1)
        return
    check(int(offers.levels.get(str(selected["plugin_id"]), 0)) == 0,
        "prepare alone must never grant a plugin")
    var committed_choice: Dictionary = bridge.choose_and_commit_plugin(offers, 0)
    check(committed_choice.get("ok", false)
        and str(committed_choice.get("receipt_id", "")) == str(selected["receipt_id"]),
        "E durably accepts prepared choice before C confirms it")
    check(int(offers.levels.get(str(selected["plugin_id"]), 0)) == int(selected["level"]),
        "C inventory advances only after E durable acknowledgement")
    var after_choice: Dictionary = Gateway.snapshot(slot_path)
    check(after_choice.get("ok", false)
        and after_choice["snapshot"]["vaccine"]["mission"]["pending_plugin_offers"] == 0,
        "E consumed one level-up offer")
    check(after_choice["snapshot"]["vaccine"]["mission"]["obtained_plugins"].has(selected["plugin_id"]),
        "E remembers C's chosen plugin level")

    var world = World.new("run-contract")
    var facts: Array[Dictionary] = []
    world.world_fact.connect(func(fact: Dictionary) -> void: facts.append(fact.duplicate(true)))
    for objective_id in WorldCatalog.objectives("CPU"):
        check(world.confirm_objective("CPU", str(objective_id),
            "validated:CPU:" + str(objective_id)), "D objective verified")
    check(world.confirm_boss_defeated("CPU", "validated:CPU:boss"),
        "D boss gated by objective completion")
    var published: int = 0
    for fact in facts:
        var committed: Dictionary = bridge.handle_world_fact(fact)
        check(committed.get("ok", false), "D fact written to E: " + str(fact.get("type", "")))
        if not committed.get("ignored", false):
            published += 1
    check(published == WorldCatalog.objectives("CPU").size() + 1,
        "each objective and clean event recorded, boss not double-paid")
    var after_clean: Dictionary = Gateway.snapshot(slot_path)
    check(after_clean.get("ok", false)
        and after_clean["snapshot"]["vaccine"]["mission"]["hardware_cleaned_ids"].has("CPU"),
        "CPU cleanse persisted and readable after restart")

    var extraction: Dictionary = world.request_extraction("CPU")
    check(extraction.get("outcome", "") == "success"
        and extraction.get("cleared_count", 0) == 1
        and not extraction.get("full_clear", true), "D partial extraction fact")
    check(bridge.commit_terminal_fact(extraction, [selected["plugin_id"]]).get("ok", false),
        "E durable extraction and one common plugin learned")
    var saved: Dictionary = Gateway.snapshot(slot_path)
    check(saved.get("ok", false), "save reopened successfully after extraction")
    if saved.get("ok", false):
        var vaccine: Dictionary = saved["snapshot"]["vaccine"]
        check(vaccine["mission"] == null and vaccine["total_xp"] > 0,
            "success banks vaccine XP and ends mission")
        check(vaccine["carried"].has(selected["plugin_id"])
            and vaccine["archive"].is_empty(),
            "learned plugin stays carried until manual deposit")

    var before_duplicate_revision: int = int(saved.get("revision", -1))
    var duplicate: Dictionary = bridge.commit_terminal_fact(extraction, [selected["plugin_id"]])
    check(not duplicate.get("ok", false) and bridge.frozen,
        "duplicate terminal receipt freezes UI rather than paying twice")
    var after_duplicate: Dictionary = Gateway.snapshot(slot_path)
    check(after_duplicate.get("revision", -2) == before_duplicate_revision,
        "duplicate terminal fact cannot advance save revision")

    var deposit: Dictionary = Gateway.transact_command(slot_path, {
        "action": "archive_plugin", "vaccine_id": "vaccine-contract", "generation": 1,
        "receipt_id": "contract:manual-deposit", "plugin_id": selected["plugin_id"]})
    check(deposit.get("applied", false), "hub manual database deposit persisted")
    var final_state: Dictionary = Gateway.snapshot(slot_path)
    if final_state.get("ok", false):
        check(final_state["snapshot"]["vaccine"]["archive"].has(selected["plugin_id"])
            and not final_state["snapshot"]["vaccine"]["carried"].has(selected["plugin_id"]),
            "manual deposit moves, never duplicates plugin")

    _delete_synthetic_tree(synthetic_parent)
    if failures == 0:
        print("REWORK A INTEGRATION PASS: C reward -> E receipt; D facts -> E; partial extraction -> save/archive; duplicate freeze")
    quit(1 if failures > 0 else 0)
