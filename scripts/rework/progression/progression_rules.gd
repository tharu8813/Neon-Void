extends RefCounted
## Deterministic pure state transitions; caller MUST durably commit before showing rewards.
## All commands require the current vaccine generation, guarding stale callbacks after destruction.
const State = preload("res://scripts/rework/progression/state_model.gd")
const Balance = preload("res://scripts/rework/progression/balance_config.gd")

static func _reject(reason: String) -> Dictionary:
    return {"ok": false, "reason": reason}

static func _prepare(state: Dictionary, vaccine_id: String, generation: int,
        run_id: String = "") -> Dictionary:
    if not State.validate(state):
        return _reject("invalid_state")
    if state.vaccine == null or state.generation != generation             or state.vaccine.vaccine_id != vaccine_id:
        return _reject("vaccine_not_current")
    if not run_id.is_empty():
        if state.vaccine.mission == null or state.vaccine.mission.run_id != run_id:
            return _reject("run_not_active")
    return {"ok": true, "state": state.duplicate(true)}

static func _finish(state: Dictionary, event_name: String) -> Dictionary:
    if not State.validate(state):
        return _reject("invalid_transition")
    return {"ok": true, "state": state, "event": event_name}

static func _accept_receipt(vaccine: Dictionary, receipt_id: String) -> bool:
    if not State.valid_id(receipt_id) or vaccine.receipts.has(receipt_id):
        return false
    vaccine.receipts[receipt_id] = true
    return true

static func _mission_xp(mission: Dictionary, reward: int, config: Dictionary) -> bool:
    if reward < 0 or reward > 1000000000000             or int(mission.mission_xp) + reward > State.MAX_EXACT_INT:
        return false
    mission.mission_xp += reward
    while mission.mission_xp >= Balance.needed_xp(int(mission.mission_level), false, config):
        var requirement: int = Balance.needed_xp(int(mission.mission_level), false, config)
        if requirement < 1 or mission.mission_level >= 1000000:
            return false
        mission.mission_xp -= requirement
        mission.mission_level += 1
        mission.pending_plugin_offers += 1
    return true

static func _vaccine_xp(vaccine: Dictionary, reward: int, config: Dictionary) -> bool:
    if reward < 0 or reward > 1000000000000             or int(vaccine.total_xp) + reward > State.MAX_EXACT_INT             or int(vaccine.xp) + reward > State.MAX_EXACT_INT:
        return false
    vaccine.xp += reward
    vaccine.total_xp += reward
    while vaccine.xp >= Balance.needed_xp(int(vaccine.level), true, config):
        var requirement: int = Balance.needed_xp(int(vaccine.level), true, config)
        if requirement < 1 or vaccine.level >= 1000000:
            return false
        vaccine.xp -= requirement
        vaccine.level += 1
        var fragments: int = Balance.get_number("fragments_per_vaccine_level", config)
        vaccine.earned_fragments += fragments
        vaccine.unspent_fragments += fragments
    return true

static func start_mission(state: Dictionary, vaccine_id: String, generation: int,
        run_id: String, seed: int) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation)
    if not prepared.ok:
        return prepared
    if not State.valid_id(run_id) or seed < 0 or seed > State.MAX_EXACT_INT:
        return _reject("invalid_run")
    var output: Dictionary = prepared.state
    var v: Dictionary = output.vaccine
    if v.mission != null or v.closed_runs.has(run_id):
        return _reject("run_already_known")
    v.mission = State.new_mission(run_id, seed, output.balance)
    return _finish(output, "mission_started")

static func add_mission_xp(state: Dictionary, vaccine_id: String, generation: int,
        run_id: String, receipt_id: String, amount: int) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation, run_id)
    if not prepared.ok:
        return prepared
    var output: Dictionary = prepared.state
    if not _accept_receipt(output.vaccine, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    if not _mission_xp(output.vaccine.mission, amount, output.balance):
        return _reject("xp_overflow")
    return _finish(output, "mission_xp_awarded")

static func add_pending_reward(state: Dictionary, vaccine_id: String, generation: int,
        run_id: String, receipt_id: String, vaccine_xp: int,
        point_fragments: int) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation, run_id)
    if not prepared.ok:
        return prepared
    if vaccine_xp < 0 or point_fragments < 0 or vaccine_xp > 1000000000000             or point_fragments > 1000000000000:
        return _reject("invalid_reward")
    var output: Dictionary = prepared.state
    if not _accept_receipt(output.vaccine, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    var m: Dictionary = output.vaccine.mission
    if int(m.uncommitted_vaccine_xp) + vaccine_xp > 1000000000000             or int(m.uncommitted_point_fragments) + point_fragments > 1000000000000:
        return _reject("reward_overflow")
    m.uncommitted_vaccine_xp += vaccine_xp
    m.uncommitted_point_fragments += point_fragments
    return _finish(output, "pending_reward_recorded")

static func objective_completed(state: Dictionary, vaccine_id: String, generation: int,
        run_id: String, receipt_id: String, objective_id: String) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation, run_id)
    if not prepared.ok:
        return prepared
    var output: Dictionary = prepared.state
    var m: Dictionary = output.vaccine.mission
    if not State.valid_id(objective_id) or m.objective_ids.has(objective_id):
        return _reject("duplicate_objective")
    if not _accept_receipt(output.vaccine, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    m.objective_ids.append(objective_id)
    m.uncommitted_vaccine_xp += Balance.get_number("objective_vaccine_xp", output.balance)
    return _finish(output, "objective_completed")

static func hardware_cleaned(state: Dictionary, vaccine_id: String, generation: int,
        run_id: String, receipt_id: String, hardware_id: String) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation, run_id)
    if not prepared.ok:
        return prepared
    var output: Dictionary = prepared.state
    var m: Dictionary = output.vaccine.mission
    if not State.HARDWARE.has(hardware_id) or m.hardware_cleaned_ids.has(hardware_id):
        return _reject("hardware_not_eligible")
    if not _accept_receipt(output.vaccine, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    m.hardware_cleaned_ids.append(hardware_id)
    m.uncommitted_vaccine_xp += Balance.get_number("hardware_vaccine_xp", output.balance)
    return _finish(output, "hardware_cleaned")

static func plugin_picked(state: Dictionary, vaccine_id: String, generation: int,
        run_id: String, receipt_id: String, plugin_id: String, rarity: String,
        level: int, offer_source: String = "levelup") -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation, run_id)
    if not prepared.ok:
        return prepared
    if not State.valid_id(plugin_id) or not State.RARITIES.has(rarity)             or level < 1 or level > 1000000             or not ["levelup", "hardware", "event"].has(offer_source):
        return _reject("invalid_plugin")
    var output: Dictionary = prepared.state
    var m: Dictionary = output.vaccine.mission
    if offer_source == "levelup" and m.pending_plugin_offers < 1:
        return _reject("no_levelup_offer")
    if m.obtained_plugins.has(plugin_id):
        var old: Dictionary = m.obtained_plugins[plugin_id]
        if old.rarity != rarity or level <= int(old.level):
            return _reject("invalid_plugin_level")
    if not _accept_receipt(output.vaccine, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    m.obtained_plugins[plugin_id] = {"rarity": rarity, "level": level}
    if offer_source == "levelup":
        m.pending_plugin_offers -= 1
    return _finish(output, "plugin_picked")

static func extract(state: Dictionary, vaccine_id: String, generation: int,
        run_id: String, receipt_id: String, learn_plugin_ids: Array) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation, run_id)
    if not prepared.ok:
        return prepared
    var output: Dictionary = prepared.state
    var v: Dictionary = output.vaccine
    var m: Dictionary = v.mission
    if m.hardware_cleaned_ids.is_empty():
        return _reject("hardware_required")
    if learn_plugin_ids.size() > 2 or not State._valid_ids(learn_plugin_ids, 2):
        return _reject("invalid_learning_selection")
    for plugin_id in learn_plugin_ids:
        if not m.obtained_plugins.has(plugin_id):
            return _reject("plugin_not_obtained")
        if not ["common", "rare"].has(m.obtained_plugins[plugin_id].rarity):
            return _reject("rarity_not_learnable")
        # Existing learned-instance synthesis/recovery against 2-new quota is [미확정].
        # Fail closed rather than duplicate or silently merge.
        if v.archive.has(plugin_id) or v.carried.has(plugin_id):
            return _reject("already_learned_policy_pending")
    if not _accept_receipt(v, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    var bonus: int = 0
    if m.hardware_cleaned_ids.size() == 5:
        bonus = Balance.get_number("all_clean_extraction_bonus_xp", output.balance)
    if not _vaccine_xp(v, int(m.uncommitted_vaccine_xp) + bonus, output.balance):
        return _reject("xp_overflow")
    if int(v.earned_fragments) + int(m.uncommitted_point_fragments) > State.MAX_EXACT_INT:
        return _reject("point_overflow")
    v.earned_fragments += m.uncommitted_point_fragments
    v.unspent_fragments += m.uncommitted_point_fragments
    for plugin_id in learn_plugin_ids:
        v.carried[plugin_id] = m.obtained_plugins[plugin_id].duplicate(true)
    v.closed_runs[run_id] = "extracted"
    v.mission = null
    return _finish(output, "extracted_to_hub")

static func die(state: Dictionary, vaccine_id: String, generation: int,
        run_id: String, receipt_id: String) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation, run_id)
    if not prepared.ok:
        return prepared
    var output: Dictionary = prepared.state
    var v: Dictionary = output.vaccine
    var m: Dictionary = v.mission
    if not _accept_receipt(v, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    if v.copies_remaining == 0:
        # The fourth death destroys the entire vaccine, including uncommitted awards.
        output.vaccine = null
        output.selection_required = true
        return _finish(output, "vaccine_destroyed_select_screen")
    var retention: int = Balance.death_retention_percent(m.hardware_cleaned_ids.size(), output.balance)
    var kept_xp: int = int(floor(float(m.uncommitted_vaccine_xp) * float(retention) / 100.0))
    var kept_points: int = int(floor(float(m.uncommitted_point_fragments) * float(retention) / 100.0))
    if not _vaccine_xp(v, kept_xp, output.balance)             or int(v.earned_fragments) + kept_points > State.MAX_EXACT_INT:
        return _reject("death_reward_overflow")
    v.earned_fragments += kept_points
    v.unspent_fragments += kept_points
    v.copies_remaining -= 1
    v.carried.clear()
    v.closed_runs[run_id] = "dead"
    v.mission = null
    return _finish(output, "cloned_to_hub")

static func archive_plugin(state: Dictionary, vaccine_id: String, generation: int,
        plugin_id: String, receipt_id: String) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation)
    if not prepared.ok:
        return prepared
    var output: Dictionary = prepared.state
    var v: Dictionary = output.vaccine
    if v.mission != null or not v.carried.has(plugin_id) or v.archive.has(plugin_id):
        return _reject("plugin_not_available_at_hub")
    if not _accept_receipt(v, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    v.archive[plugin_id] = v.carried[plugin_id]
    v.carried.erase(plugin_id)
    return _finish(output, "plugin_deposited")

static func withdraw_plugin(state: Dictionary, vaccine_id: String, generation: int,
        plugin_id: String, receipt_id: String) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation)
    if not prepared.ok:
        return prepared
    var output: Dictionary = prepared.state
    var v: Dictionary = output.vaccine
    if v.mission != null or not v.archive.has(plugin_id) or v.carried.has(plugin_id):
        return _reject("plugin_not_archived")
    if not _accept_receipt(v, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    v.carried[plugin_id] = v.archive[plugin_id]
    v.archive.erase(plugin_id)
    return _finish(output, "plugin_withdrawn")

static func reallocate_stats(state: Dictionary, vaccine_id: String, generation: int,
        receipt_id: String, allocations: Dictionary) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation)
    if not prepared.ok:
        return prepared
    var output: Dictionary = prepared.state
    var v: Dictionary = output.vaccine
    var cost: int = Balance.stat_cost_fragments(allocations, output.balance)
    if v.mission != null or cost < 0 or cost > int(v.earned_fragments):
        return _reject("invalid_allocation")
    if not _accept_receipt(v, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    v.invested_stats = allocations.duplicate(true)
    v.unspent_fragments = int(v.earned_fragments) - cost
    return _finish(output, "stats_reallocated")

static func learn_skill(state: Dictionary, vaccine_id: String, generation: int,
        receipt_id: String, skill_id: String, equip_on_discovery: Array = []) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation)
    if not prepared.ok:
        return prepared
    var output: Dictionary = prepared.state
    var v: Dictionary = output.vaccine
    if not State.valid_id(skill_id) or v.known_skills.has(skill_id):
        return _reject("skill_already_known_or_invalid")
    if not State._valid_ids(equip_on_discovery, 3):
        return _reject("invalid_discovery_loadout")
    for equipped_skill in equip_on_discovery:
        if equipped_skill != skill_id and not v.known_skills.has(equipped_skill):
            return _reject("unknown_skill")
    if not _accept_receipt(v, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    v.known_skills.append(skill_id)
    if not equip_on_discovery.is_empty():
        v.equipped_skills = equip_on_discovery.duplicate()
    return _finish(output, "skill_learned")

static func equip_skills(state: Dictionary, vaccine_id: String, generation: int,
        receipt_id: String, equipped: Array) -> Dictionary:
    var prepared: Dictionary = _prepare(state, vaccine_id, generation)
    if not prepared.ok:
        return prepared
    var output: Dictionary = prepared.state
    var v: Dictionary = output.vaccine
    # Mid-mission loadout swaps are only possible in the same transaction as
    # discovering a NEW skill (learn_skill.equip_on_discovery).
    if v.mission != null:
        return _reject("hub_only_except_skill_discovery")
    if not State._valid_ids(equipped, 3):
        return _reject("invalid_skill_loadout")
    for skill in equipped:
        if not v.known_skills.has(skill):
            return _reject("unknown_skill")
    if not _accept_receipt(v, receipt_id):
        return _reject("duplicate_or_invalid_receipt")
    v.equipped_skills = equipped.duplicate()
    return _finish(output, "skills_equipped")

static func choose_new_vaccine(state: Dictionary, new_vaccine_id: String) -> Dictionary:
    if not State.validate(state):
        return _reject("invalid_state")
    if not state.selection_required or not State.valid_id(new_vaccine_id)             or state.generation >= 1000000000:
        return _reject("not_at_vaccine_selection")
    var output: Dictionary = state.duplicate(true)
    output.generation += 1
    output.vaccine = State.new_vaccine(new_vaccine_id)
    output.selection_required = false
    return _finish(output, "new_vaccine_selected")
