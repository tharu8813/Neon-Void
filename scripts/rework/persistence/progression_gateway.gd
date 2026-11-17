extends RefCounted
## Public integration boundary for C/D/A. All commands are committed or rejected
## atomically with their receipts. Never mutate a UI snapshot and assume it was saved.
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Model = preload("res://scripts/rework/progression/state_model.gd")
const Rules = preload("res://scripts/rework/progression/progression_rules.gd")

static func initialize(slot_id: String, vaccine_id: String, config: Dictionary = {}) -> Dictionary:
    var path: String = Store.default_slot_path(slot_id)
    if path.is_empty():
        return {"ok": false, "reason": "invalid_slot_id"}
    return Store.initialize(path, Model.new_slot(slot_id, vaccine_id, config))

static func snapshot(slot_path: String) -> Dictionary:
    ## Only read-only copies should be passed to UI consumers.
    var loaded: Dictionary = Store.read(slot_path)
    if not loaded.ok:
        return loaded
    return {"ok": true, "revision": loaded.revision,
        "snapshot": loaded.state.duplicate(true)}

static func transact_command(slot_path: String, command: Dictionary,
        expected_revision: int = -1, fault_at: String = "") -> Dictionary:
    if not Model.valid_id(command.get("action")):
        return {"applied": false, "reason": "invalid_action"}
    # Revision is an optimistic UI guard; -1 lets independently delivered
    # C/D events serialize on the filesystem lock and receipt ledger instead.
    return Store.transact(slot_path,
        func(state: Dictionary) -> Dictionary: return dispatch(state, command),
        expected_revision, fault_at)

static func dispatch(state: Dictionary, command: Dictionary) -> Dictionary:
    var action: String = str(command.get("action", ""))
    if action == "choose_new_vaccine":
        return Rules.choose_new_vaccine(state, str(command.get("new_vaccine_id", "")))
    if not Model.natural(command.get("generation"), 1000000000):
        return {"ok": false, "reason": "invalid_generation"}
    var vaccine_id: String = str(command.get("vaccine_id", ""))
    var generation: int = int(command.generation)
    var run_id: String = str(command.get("run_id", ""))
    var receipt_id: String = str(command.get("receipt_id", ""))
    match action:
        "start_mission":
            if not Model.natural(command.get("seed")):
                return {"ok": false, "reason": "invalid_seed"}
            return Rules.start_mission(state, vaccine_id, generation, run_id, int(command.seed))
        "mission_xp":
            if not Model.natural(command.get("amount")):
                return {"ok": false, "reason": "invalid_xp"}
            return Rules.add_mission_xp(state, vaccine_id, generation, run_id,
                receipt_id, int(command.amount))
        "pending_reward":
            if not Model.natural(command.get("xp")) or not Model.natural(command.get("fragments")):
                return {"ok": false, "reason": "invalid_reward"}
            return Rules.add_pending_reward(state, vaccine_id, generation, run_id,
                receipt_id, int(command.xp), int(command.fragments))
        "objective_completed":
            return Rules.objective_completed(state, vaccine_id, generation,
                run_id, receipt_id, str(command.get("objective_id", "")))
        "hardware_cleaned":
            return Rules.hardware_cleaned(state, vaccine_id, generation,
                run_id, receipt_id, str(command.get("hardware_id", "")))
        "plugin_picked":
            if not Model.natural(command.get("level"), 1000000):
                return {"ok": false, "reason": "invalid_plugin_level"}
            return Rules.plugin_picked(state, vaccine_id, generation, run_id,
                receipt_id, str(command.get("plugin_id", "")),
                str(command.get("rarity", "")), int(command.level),
                str(command.get("offer_source", "levelup")),\n                str(command.get("offer_id", "")))
        "extract":
            if typeof(command.get("learn_plugin_ids")) != TYPE_ARRAY:
                return {"ok": false, "reason": "invalid_learning_selection"}
            return Rules.extract(state, vaccine_id, generation, run_id,
                receipt_id, command.learn_plugin_ids)
        "die":
            return Rules.die(state, vaccine_id, generation, run_id, receipt_id)
        "archive_plugin":
            return Rules.archive_plugin(state, vaccine_id, generation,
                str(command.get("plugin_id", "")), receipt_id)
        "withdraw_plugin":
            return Rules.withdraw_plugin(state, vaccine_id, generation,
                str(command.get("plugin_id", "")), receipt_id)
        "reallocate_stats":
            if typeof(command.get("allocations")) != TYPE_DICTIONARY:
                return {"ok": false, "reason": "invalid_allocation"}
            return Rules.reallocate_stats(state, vaccine_id, generation,
                receipt_id, command.allocations)
        "learn_skill":
            if typeof(command.get("equip_on_discovery", [])) != TYPE_ARRAY:
                return {"ok": false, "reason": "invalid_discovery_loadout"}
            return Rules.learn_skill(state, vaccine_id, generation,
                receipt_id, str(command.get("skill_id", "")),
                command.get("equip_on_discovery", []))
        "equip_skills":
            if typeof(command.get("equipped")) != TYPE_ARRAY:
                return {"ok": false, "reason": "invalid_skills"}
            return Rules.equip_skills(state, vaccine_id, generation,
                receipt_id, command.equipped)
        _:
            return {"ok": false, "reason": "unknown_command"}
