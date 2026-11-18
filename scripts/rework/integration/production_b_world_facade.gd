extends Node3D
## A-owned adaptation of the reviewed B #119 production CPU adapter to D #116's
## formal world router. No independent HP, damage, reward or duplicate proc ledger.
##
## B/D sources in their owner PRs remain unchanged. This only runs in the
## opt-in A integrated validation pilot; it does NOT change legacy main.tscn.
signal enemy_defeated(actor_id: String, receipt_id: String)
signal player_died()
signal health_updated(health: float, maximum: float)
signal combat_notice(notice: Dictionary)
signal integration_failed(reason: String)
signal encounter_authorization_requested(run_id: String)

const Production = preload("res://scripts/rework/combat/production_cpu_adapter.gd")
var bridge: Variant = null
var seed: int = 42
var last_error: String = ""
var require_e_authorization: bool = false
var _encounter_authorized: bool = false
var enemies: Dictionary:
    get:
        if bridge == null:
            return {}
        # D preview exposes each CharacterBody under the "node" key.
        # B production owns the same physical body under "body".
        # Return a READ-ONLY view; B remains the authoritative actor owner.
        var view: Dictionary = {}
        for id in bridge.enemies:
            var info: Dictionary = bridge.enemies[id].duplicate()
            if not info.has("node") and info.has("body"):
                info["node"] = info["body"]
            view[id] = info
        return view
var map_plan: Dictionary:
    get:
        return bridge.map_plan if bridge != null else {}

func _ready() -> void:
    bridge = Production.new()
    bridge.name = "B119ProductionCPU"
    add_child(bridge)
    bridge.enemy_defeated.connect(func(id: String, receipt: String) -> void:
        enemy_defeated.emit(id, receipt))
    bridge.player_died.connect(func() -> void: player_died.emit())
    bridge.health_updated.connect(func(hp: float, maximum: float) -> void:
        health_updated.emit(hp, maximum))
    bridge.combat_notice.connect(func(note: Dictionary) -> void:
        combat_notice.emit(note.duplicate(true)))
    bridge.plugin_failure.connect(func(reason: String) -> void:
        last_error = "C_B_plugin:" + reason
        integration_failed.emit(last_error))

func begin_encounter(player_node: CharacterBody3D, camera_node: Camera3D,
        world_map: Dictionary, new_run_id: String, config: Dictionary = {}) -> bool:
    if bridge == null:
        integration_failed.emit("B_production_adapter_missing")
        return false
    seed = int(config.get("seed", 42))
    var ready: bool = bool(bridge.begin_encounter(
        player_node, camera_node, world_map, new_run_id, config))
    if not ready:
        last_error = "B_production_begin_rejected"
        integration_failed.emit(last_error)
        return false
    # Opt-in A pilot requires the E-owned growth+skills AND C-level build to be
    # acknowledged BEFORE the first B simulation tick. D starts B encounters
    # on every new motherboard/hardware scene, including returns from a zone.
    _encounter_authorized = false
    if require_e_authorization:
        encounter_authorization_requested.emit(new_run_id)
        if not _encounter_authorized:
            last_error = "B_formal_encounter_missing_E_authorization"
            bridge.reset()
            integration_failed.emit(last_error)
            return false
    return true

func reset() -> void:
    if bridge != null:
        bridge.reset()
    _encounter_authorized = false
    last_error = ""

func confirm_encounter_authorized(run_id: String, revision: int) -> bool:
    # This A-only handshake is not a substitute for E. The pilot must call it
    # ONLY after its complete E snapshot checks, B growth ACK and C-level ACK.
    if not require_e_authorization or bridge == null or not bridge.running \
            or bridge.run_id != run_id or bridge.simulation.fixed_tick != 0:
        return false
    var growth: Dictionary = authorized_growth_snapshot()
    if int(growth.get("revision", -1)) != revision or revision < 1:
        return false
    _encounter_authorized = true
    return true

func spawn_hostile(info: Dictionary) -> bool:
    return bridge != null and bool(bridge.spawn_hostile(info))

## Forward the COMPLETE E Gateway.snapshot() packet, not a UI draft.
## This A public facade is the sole entry to B-approved growth and skills.
func configure_authorized_progression(loaded: Dictionary) -> Dictionary:
    if bridge == null:
        last_error = "B_growth_adapter_unavailable"
        integration_failed.emit(last_error)
        return {"ok": false, "reason": last_error}
    var approved: Dictionary = bridge.configure_authorized_progression(loaded)
    if not bool(approved.get("ok", false)):
        last_error = "B_growth_rejected:" + str(approved.get("reason", "unknown"))
        integration_failed.emit(last_error)
    return approved.duplicate(true)

func authorized_growth_snapshot() -> Dictionary:
    return bridge.authorized_growth_snapshot() if bridge != null else {}

## Mid-mission skill discovery. Only a FRESH E snapshot whose revision is newer
## than the one B installed may pass; stats/HP/rewards are untouched.
func apply_authorized_skill_loadout(loaded: Dictionary) -> Dictionary:
    if bridge == null:
        return {"ok": false, "reason": "B_growth_adapter_unavailable"}
    var approved: Dictionary = bridge.apply_authorized_skill_loadout(loaded)
    if not bool(approved.get("ok", false)):
        last_error = "B_skill_loadout_rejected:" + str(approved.get("reason", "unknown"))
        integration_failed.emit(last_error)
    return approved.duplicate(true)

## Field recovery after D has an E ACK for a world fact. BALANCE_DRAFT ratios,
## never a reward claim and never a save write.
func recover_player(health_ratio: float, mana_ratio: float) -> Dictionary:
    if bridge == null:
        return {"ok": false, "reason": "B_growth_adapter_unavailable"}
    return bridge.recover_player(health_ratio, mana_ratio)

func skill_snapshot() -> Dictionary:
    return bridge.skill_snapshot() if bridge != null else {}

func configure_authorized_build(levels: Dictionary, instances: Array,
        skills: Array = []) -> bool:
    if bridge == null or bridge.run_id.is_empty():
        return false
    # Once E approved growth, direct skill-definition injection would
    # bypass the real E equipped skills. Reject it rather than double-equip.
    if not skills.is_empty() and not bridge.authorized_growth_snapshot().is_empty():
        integration_failed.emit("direct_skills_forbidden_after_E_authorization")
        return false
    # B #119 owns C-driven execution through c_effect_adapter. Injecting the
    # older D numeric instances as well would double-apply proc damage.
    # Until both owners approve a single combined representation, fail closed.
    if not instances.is_empty():
        integration_failed.emit("numeric_plugin_instances_need_ownership_review")
        return false
    if not bridge.configure_plugins(levels, seed):
        integration_failed.emit("B_C_authorized_plugin_rejected")
        return false
    for i in range(skills.size()):
        if i >= 3 or not skills[i] is Dictionary:
            integration_failed.emit("invalid_authorized_skill_payload")
            return false
        if not bool(bridge.learn_active_skill(skills[i], i, true)):
            integration_failed.emit("B_authorized_skill_rejected")
            return false
    return true

func upgrade_plugin_after_ack(plugin_id: String, level: int) -> bool:
    return bridge != null and bool(bridge.upgrade_plugin_after_ack(plugin_id, level))

func actor_snapshot(actor_id: String) -> Dictionary:
    if bridge == null:
        return {}
    # B now exports effective stats, shield and skill_state through its
    # supported public read-only adapter. Never bypass with simulation internals.
    return bridge.actor_snapshot(actor_id)

func advance_test_tick(commands: Dictionary) -> Array[Dictionary]:
    ## Deterministic headless D regression adapter, NOT a damage/XP shortcut.
    ## Every hit goes through B's real projectile physics, HP and event ledger.
    if bridge == null or not bridge.running:
        return []
    if bridge.player != null and is_instance_valid(bridge.player):
        bridge.simulation.set_actor_position("VACCINE", bridge.player.global_position)
    bridge.set_combat_input(commands.get("VACCINE", {}))
    for actor_id in commands:
        if str(actor_id) != "VACCINE":
            bridge.set_input(str(actor_id), commands[actor_id])
    return bridge.advance_production_tick()

func diagnostics() -> Dictionary:
    if bridge == null:
        return {"ready": false}
    return bridge.diagnostics()
