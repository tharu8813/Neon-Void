extends RefCounted
## Synthetic tests for atomic E rare bonus, no real user saves.
## This is a provisional ONE bonus per source choice, NOT final Lv2 balance.
const Model = preload("res://scripts/rework/progression/state_model.gd")
const Rules = preload("res://scripts/rework/progression/progression_rules.gd")
const Rare = preload("res://scripts/rework/progression/rare_bonus.gd")
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")

static func _source_state() -> Dictionary:
    var state: Dictionary = Model.new_slot("rare-bonus-fixture", "rare-vaccine")
    var started: Dictionary = Rules.start_mission(state, "rare-vaccine", 1,
        "run-rare-proof", 2468)
    if not bool(started.get("ok", false)):
        return {}
    return started.state

static func _cleanup(folder: String) -> void:
    var dir: DirAccess = DirAccess.open(folder)
    if dir == null:
        return
    dir.list_dir_begin()
    var name: String = dir.get_next()
    while not name.is_empty():
        if not ["", ".", ".."].has(name):
            var file: String = folder.path_join(name)
            if dir.current_is_dir():
                _cleanup(file)
            else:
                DirAccess.remove_absolute(file)
        name = dir.get_next()
    dir.list_dir_end()
    DirAccess.remove_absolute(folder)

static func run(check: Callable) -> void:
    var first: Dictionary = _source_state()
    check.call(not first.is_empty() and Model.validate(first),
        "E source fixture valid")
    if first.is_empty():
        return
    var leveled: Dictionary = Rules.add_mission_xp(first, "rare-vaccine", 1,
        "run-rare-proof", "kill-rare-test-1", 100)
    check.call(bool(leveled.get("ok", false)), "levelup created one C offer")
    if not leveled.get("ok", false):
        return
    first = leveled.state
    var before: Dictionary = first.duplicate(true)
    var picked: Dictionary = Rules.plugin_picked(first, "rare-vaccine", 1,
        "run-rare-proof", "run-rare-proof:1:0",
        Rare.SOURCE_ID, "common", 1, "levelup", "run-rare-proof:1")
    check.call(bool(picked.get("ok", false)),
        "E selected source and rare bonus in one state transition")
    if not bool(picked.get("ok", false)):
        return
    first = picked.state
    check.call(Model.validate(first), "atomic rare grant state valid")
    var mission: Dictionary = first.vaccine.mission
    var history: Dictionary = mission.environment_rolls.get(Rare.KEY, {})
    check.call(history.size() == 1 and history.has("run-rare-proof:1:0"),
        "immutable rare grant lookup indexed by exact source receipt")
    if history.is_empty():
        return
    var grant: Dictionary = history["run-rare-proof:1:0"]
    var rare_id: String = str(grant.plugin_id)
    var canonical: Dictionary = Catalog.find_by_id(rare_id, Catalog.entries())
    check.call(str(canonical.get("rarity", "")) == "rare" and
        int(grant.level) == 1, "one canonical rare plugin at initial level")
    check.call(mission.obtained_plugins.has(Rare.SOURCE_ID) and
        mission.obtained_plugins.has(rare_id), "source and bonus are BOTH acquired")
    check.call(first.vaccine.receipts.has("run-rare-proof:1:0") and
        first.vaccine.receipts.has("run-rare-proof:1:0:rare:0"),
        "source and bonus each have unique atomic receipts")
    check.call(int(mission.pending_plugin_offers) == 0 and
        mission.claimed_offer_ids.has("run-rare-proof:1"),
        "source only consumes one C reward choice")
    var replay: Dictionary = Rules.plugin_picked(first, "rare-vaccine", 1,
        "run-rare-proof", "run-rare-proof:1:0", Rare.SOURCE_ID,
        "common", 1, "levelup", "run-rare-proof:1")
    check.call(not replay.get("ok", false) and Model.validate(first),
        "duplicate source receipt cannot pay a second bonus")
    var same: Dictionary = _source_state()
    var same_xp: Dictionary = Rules.add_mission_xp(same, "rare-vaccine", 1,
        "run-rare-proof", "kill-rare-test-1", 100)
    var same_grant: Dictionary = Rules.plugin_picked(same_xp.state, "rare-vaccine", 1,
        "run-rare-proof", "run-rare-proof:1:0", Rare.SOURCE_ID,
        "common", 1, "levelup", "run-rare-proof:1")
    check.call(bool(same_grant.get("ok", false)) and
        same_grant.state.vaccine.mission.environment_rolls[Rare.KEY] == history,
        "same mission seed + E receipt produces exactly same rare reward")
    var more_xp: Dictionary = Rules.add_mission_xp(first, "rare-vaccine", 1,
        "run-rare-proof", "kill-rare-test-2", 130)
    check.call(bool(more_xp.get("ok", false)), "second levelup accepted")
    if bool(more_xp.get("ok", false)):
        var upgraded: Dictionary = Rules.plugin_picked(more_xp.state,
            "rare-vaccine", 1, "run-rare-proof", "run-rare-proof:2:0",
            Rare.SOURCE_ID, "common", 2, "levelup", "run-rare-proof:2")
        check.call(bool(upgraded.get("ok", false)),
            "source Lv2 grants only baseline rare, extra count remains unapproved")
        if bool(upgraded.get("ok", false)):
            var after: Dictionary = upgraded.state.vaccine.mission.environment_rolls[Rare.KEY]
            check.call(after.size() == 2 and
                after["run-rare-proof:1:0"] == grant,
                "Lv2 acquisition adds one new immutable grant without editing earlier award")
            check.call(Rare.monotonic(mission, upgraded.state.vaccine.mission),
                "grant ledger monotonic for direct Store writes")
            var rolled_back: Dictionary = upgraded.state.vaccine.mission.duplicate(true)
            rolled_back.environment_rolls[Rare.KEY].erase("run-rare-proof:1:0")
            check.call(not Rare.monotonic(upgraded.state.vaccine.mission, rolled_back),
                "removal of a previously earned rare grant is rejected")

    var exhausted: Dictionary = before.duplicate(true)
    for entry in Catalog.entries():
        if str(entry.get("rarity", "")) == "rare":
            exhausted.vaccine.carried[str(entry.plugin_id)] = {
                "rarity": "rare", "level": int(entry.max_level)
            }
    check.call(Model.validate(exhausted), "fixture with all rare plugins at max valid")
    var fail: Dictionary = Rules.plugin_picked(exhausted, "rare-vaccine", 1,
        "run-rare-proof", "run-rare-proof:1:0", Rare.SOURCE_ID,
        "common", 1, "levelup", "run-rare-proof:1")
    check.call(not fail.get("ok", false)
        and str(fail.get("reason", "")) == "no_eligible_rare_candidate"
        and not exhausted.vaccine.mission.obtained_plugins.has(Rare.SOURCE_ID),
        "no eligible rare fails BEFORE spending E source receipt")

    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var slot_id: String = "rare-atomic-" + suffix
    var slot: String = Store.default_slot_path(slot_id)
    var init: Dictionary = Gateway.initialize(slot_id, "rare-atomic-vaccine")
    check.call(bool(init.get("ok", false)), "isolated rare E slot initialized")
    if not bool(init.get("ok", false)):
        return
    var identity: Dictionary = {
        "vaccine_id": "rare-atomic-vaccine", "generation": 1,
        "run_id": "run-rare-atomic"
    }
    var start: Dictionary = identity.duplicate()
    start.merge({"action":"start_mission", "seed":2468}, true)
    check.call(bool(Gateway.transact_command(slot, start).get("applied", false)),
        "E persistent transaction started rare mission")
    var xp_command: Dictionary = identity.duplicate()
    xp_command.merge({"action":"mission_xp",
        "receipt_id":"kill-rare-atomic", "amount":100}, true)
    check.call(bool(Gateway.transact_command(slot, xp_command).get("applied", false)),
        "E persistent XP created a real reward slot")
    var bonus_command: Dictionary = identity.duplicate()
    bonus_command.merge({"action":"plugin_picked",
        "receipt_id":"run-rare-atomic:1:0",
        "offer_id":"run-rare-atomic:1",
        "plugin_id":Rare.SOURCE_ID,
        "rarity":"common", "level":1,
        "offer_source":"levelup"}, true)
    var persisted: Dictionary = Gateway.transact_command(slot, bonus_command)
    check.call(bool(persisted.get("applied", false)),
        "E Store commits source + rare in a single journal transaction")
    var loaded: Dictionary = Gateway.snapshot(slot)
    check.call(bool(loaded.get("ok", false)),
        "E durable rare grant reloaded from verified checksum")
    if bool(loaded.get("ok", false)):
        var saved: Dictionary = loaded["snapshot"]
        var awards: Dictionary = saved["vaccine"]["mission"]["environment_rolls"].get(Rare.KEY, {})
        check.call(awards.size() == 1 and
            saved["vaccine"]["receipts"].has("run-rare-atomic:1:0:rare:0"),
            "durable rare and bonus receipt survive a new E read")
    var repeated: Dictionary = Gateway.transact_command(slot, bonus_command)
    check.call(not bool(repeated.get("applied", false)),
        "E does not duplicate bonus after caller retries same source receipt")
    var reloaded: Dictionary = Gateway.snapshot(slot)
    if bool(reloaded.get("ok", false)) and bool(loaded.get("ok", false)):
        check.call(int(reloaded.get("revision", -1)) == int(loaded.get("revision", -2)),
            "duplicate attempt did not advance durable state revision")
    # Crash/fault injection at the irreversible E commit boundary.
    var second_xp: Dictionary = identity.duplicate()
    second_xp.merge({"action":"mission_xp",
        "receipt_id":"kill-rare-atomic-2", "amount":130}, true)
    check.call(bool(Gateway.transact_command(slot, second_xp).get("applied", false)),
        "second real E levelup makes source Lv2 eligible")
    var second_grant: Dictionary = identity.duplicate()
    second_grant.merge({"action":"plugin_picked",
        "receipt_id":"run-rare-atomic:2:0",
        "offer_id":"run-rare-atomic:2",
        "plugin_id":Rare.SOURCE_ID,
        "rarity":"common", "level":2,
        "offer_source":"levelup"}, true)
    var rejected_before: Dictionary = Gateway.transact_command(slot, second_grant,
        -1, "after_backup")
    check.call(not bool(rejected_before.get("applied", false)),
        "simulated failure before primary cannot report a granted rare")
    var pre_state: Dictionary = Gateway.snapshot(slot)
    check.call(bool(pre_state.get("ok", false)) and
        not pre_state["snapshot"]["vaccine"]["receipts"].has(
            "run-rare-atomic:2:0") and
        not pre_state["snapshot"]["vaccine"]["receipts"].has(
            "run-rare-atomic:2:0:rare:0"),
        "before-primary abort leaves both source and rare absent")
    var uncertain: Dictionary = Gateway.transact_command(slot, second_grant,
        -1, "after_primary")
    check.call(not bool(uncertain.get("applied", false)) and
        str(uncertain.get("reason", "")) == "simulated_ack_lost",
        "E can commit while its response is deliberately lost")
    var reconciled: Dictionary = Gateway.reconcile_receipt(slot,
        "rare-atomic-vaccine", 1, "run-rare-atomic",
        "run-rare-atomic:2:0", "run-rare-atomic:2")
    check.call(bool(reconciled.get("receipt_committed", false)) and
        not bool(reconciled.get("receipt_definitely_absent", false)),
        "lost E acknowledgement resolves to committed source receipt")
    var stable: Dictionary = Gateway.snapshot(slot)
    if bool(stable.get("ok", false)):
        var vaccine_state: Dictionary = stable["snapshot"]["vaccine"]
        var grants: Dictionary = vaccine_state["mission"]["environment_rolls"].get(
            Rare.KEY, {})
        check.call(grants.size() == 2 and
            vaccine_state["receipts"].has("run-rare-atomic:2:0:rare:0") and
            int(vaccine_state["mission"]["obtained_plugins"][Rare.SOURCE_ID]["level"]) == 2,
            "after-primary crash cannot separate bonus from source or reset levels")
    else:
        check.call(false, "E should read intact primary even after lost ACK")
    var retry_after_loss: Dictionary = Gateway.transact_command(slot, second_grant)
    check.call(not bool(retry_after_loss.get("applied", false)),
        "same source cannot grant again after ambiguous success")
    var final_state: Dictionary = Gateway.snapshot(slot)
    if bool(stable.get("ok", false)) and bool(final_state.get("ok", false)):
        check.call(int(stable.get("revision", -1)) == int(final_state.get("revision", -2)),
            "duplicate after lost ACK did not advance E revision")
    _cleanup(slot)
