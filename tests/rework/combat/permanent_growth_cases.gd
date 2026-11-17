extends RefCounted
## B owner integrated regression: E durable readback -> real 60 Hz B actor.
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Model = preload("res://scripts/rework/progression/state_model.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Growth = preload("res://scripts/rework/combat/permanent_growth.gd")
const Registry = preload("res://scripts/rework/skills/skill_registry.gd")
const Production = preload("res://scripts/rework/combat/production_cpu_adapter.gd")
const Damage = preload("res://scripts/rework/combat/damage.gd")
const Stats = preload("res://scripts/rework/combat/stats.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")

static func _near(a: float, b: float) -> bool:
    return absf(a - b) <= 0.0001

static func _erase(path: String) -> void:
    var dir: DirAccess = DirAccess.open(path)
    if dir == null:
        return
    dir.list_dir_begin()
    var item: String = dir.get_next()
    while not item.is_empty():
        var child: String = path.path_join(item)
        if dir.current_is_dir():
            _erase(child)
        else:
            DirAccess.remove_absolute(child)
        item = dir.get_next()
    dir.list_dir_end()
    DirAccess.remove_absolute(path)

static func _advance(battle: Variant, count: int, slot: int = -1) -> Array:
    var notes: Array = []
    for tick in range(count):
        var input: Dictionary = {"fire": slot == -1,
            "move": Vector2.ZERO, "aim_world": Vector3(2.0, 0.0, 0.0)}
        if slot >= 0 and tick == 0:
            input["skill_slot"] = slot
        battle.set_combat_input(input)
        notes.append_array(battle.advance_production_tick())
    return notes

static func _instantiate(root: Node, run_id: String) -> Dictionary:
    var scene := Node3D.new()
    root.add_child(scene)
    var player := CharacterBody3D.new()
    scene.add_child(player)
    player.global_position = Vector3(0, 0.5, 0)
    var camera := Camera3D.new()
    scene.add_child(camera)
    var battle = Production.new()
    scene.add_child(battle)
    var begun: bool = battle.begin_encounter(player, camera, {}, run_id)
    return {"scene": scene, "battle": battle, "started": begun}

static func run(check: Callable, root: Node) -> void:
    var matching: Dictionary = {
        "max_hp":"max_health", "attack_damage":"attack_damage",
        "ability_power":"ability_power", "armor":"armor",
        "magic_resist":"magic_resist", "move_speed":"move_speed",
        "fire_rate":"attack_speed", "projectile_speed":"projectile_speed_mult",
        "crit_chance":"crit_chance", "crit_damage":"crit_multiplier",
        "ability_haste":"skill_haste", "luck":"luck", "max_mana":"max_mana",
        "mana_regen":"mana_regen", "projectile_size":"projectile_size",
        "flat_armor_pen":"physical_pen_flat", "flat_magic_pen":"magic_pen_flat",
        "percent_armor_pen":"physical_pen_percent",
        "percent_magic_pen":"magic_pen_percent", "on_hit_vamp":"on_hit_vamp",
        "omnivamp":"omni_vamp", "tenacity":"tenacity"
    }
    check.call(Growth.INCREMENTS.size() == matching.size(),
        "all 22 E STAT_COSTS stat identifiers mapped exactly once")
    for id in matching:
        check.call(Growth.INCREMENTS.has(id)
            and str(Growth.INCREMENTS[id].get("stat", "")) == matching[id],
            "B mapping covers " + str(id))
    for invalid in [
        {"fake":1}, {"attack_damage":-1}, {"attack_damage":0.5},
        {"attack_damage":"10"}, {"attack_damage":INF}, {"attack_damage":NAN}
    ]:
        check.call(not bool(Growth.validate_allocations(invalid).get("ok", false)),
            "invalid E investment rejected before live stat mutation")
    var increments: Dictionary = {"attack_damage":4, "max_hp":2,
        "max_mana":3, "ability_haste":5, "flat_armor_pen":2,
        "crit_damage":4, "mana_regen":2, "armor":3, "fire_rate":2}
    var compiled: Dictionary = Growth.compile(Stats.make(), increments)
    check.call(bool(compiled.get("ok", false)), "GDD draft stat increments compile")
    if not bool(compiled.get("ok", false)):
        return
    var enhanced: Dictionary = compiled["stats"]
    check.call(_near(float(enhanced["attack_damage"]),6.0)
        and _near(float(enhanced["max_health"]),110.0)
        and _near(float(enhanced["max_mana"]),115.0)
        and int(enhanced["skill_haste"]) == 10
        and _near(float(enhanced["physical_pen_flat"]),1.0)
        and _near(float(enhanced["crit_multiplier"]),1.24)
        and _near(float(enhanced["attack_speed"]),2.1),
        "investment increments are actual units not percentage points confused with multipliers")
    var dummy: Dictionary = Stats.make({"armor":50.0})
    var normal: float = float(Damage.calculate(Stats.make(),
        dummy, {"damage_type":"physical","base_damage":10.0})["calculated_damage"])
    var penetration: float = float(Damage.calculate(enhanced,
        dummy, {"damage_type":"physical","base_damage":10.0})["calculated_damage"])
    check.call(penetration > normal, "E physical penetration changes B resistance calculation")
    var restored: Dictionary = Growth.compile(Stats.make(), {})
    check.call(bool(restored.get("ok", false)) and
        _near(float(restored["stats"]["attack_damage"]),5.0),
        "free respec to no investments restores baseline AD")
    check.call(float(Damage.calculate(Stats.make({"crit_multiplier":1.24}),
        Stats.make(), {"damage_type":"true","base_damage":10.0,
        "can_crit":true}, true)["calculated_damage"]) > 12.0,
        "crit damage multiplier investment is a ratio, not raw damage")
    check.call(Registry.validate_loadout(["nv_skill_arc_pulse"],
        ["nv_skill_arc_pulse", "nv_skill_arc_pulse"]).get("ok") == false,
        "duplicate skill slots rejected")
    check.call(Registry.validate_loadout([], ["nv_skill_arc_pulse"]).get("ok") == false,
        "E did not learn skill, B refuses equip")
    check.call(Registry.validate_loadout(["unknown_future_skill"],
        ["unknown_future_skill"]).get("reason") == "skill_definition_missing:unknown_future_skill",
        "unknown E skill has no invented attack payload")
    check.call(Registry.validate_loadout([], ["a","b","c","d"]).get("ok") == false,
        "more than three skills rejected")

    # E's real Store + Gateway commits each command, and B sees only readback.
    var dir: String = ProjectSettings.globalize_path(
        "user://b-progress-test-" + str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec()))
    var slot: String = dir.path_join("rework-slot-b-growth")
    var state: Dictionary = Model.new_slot("b-growth", "vaccine-b", {})
    state["vaccine"]["earned_fragments"] = 20000
    state["vaccine"]["unspent_fragments"] = 20000
    var initialized: Dictionary = Store.initialize(slot, state)
    check.call(bool(initialized.get("ok", false)), "separate E synthetic slot initialized")
    if not bool(initialized.get("ok", false)):
        return
    var receipt_index: int = 0
    var actions: Array[Dictionary] = [
        {"action":"reallocate_stats", "allocations": increments},
        {"action":"reallocate_stats", "allocations": {}},
        {"action":"reallocate_stats", "allocations": increments},
        {"action":"learn_skill","skill_id":"nv_skill_arc_pulse"},
        {"action":"learn_skill","skill_id":"nv_skill_penetrator"},
        {"action":"learn_skill","skill_id":"nv_skill_root_packet"},
        {"action":"equip_skills","equipped":[
            "nv_skill_arc_pulse","nv_skill_penetrator","nv_skill_root_packet"]},
        {"action":"start_mission","run_id":"b-growth-run","seed":911}
    ]
    for action in actions:
        receipt_index += 1
        var command: Dictionary = action.duplicate(true)
        command["vaccine_id"] = "vaccine-b"
        command["generation"] = 1
        if str(command["action"]) != "start_mission":
            command["receipt_id"] = "b:progress:" + str(receipt_index)
        var ack: Dictionary = Gateway.transact_command(slot, command)
        check.call(bool(ack.get("applied", false)),
            "E durable command ACK: " + str(command["action"]))
        if str(command["action"]) == "reallocate_stats" and (
                command["allocations"].is_empty()):
            var respec_readback: Dictionary = Gateway.snapshot(slot)
            check.call(bool(respec_readback.get("ok", false)) and
                respec_readback["snapshot"]["vaccine"]["invested_stats"].is_empty(),
                "E committed free respec clears investment before new allocation")
    var approved: Dictionary = Gateway.snapshot(slot)
    check.call(bool(approved.get("ok", false)), "B consumes E disk readback")
    if not bool(approved.get("ok", false)):
        _erase(dir)
        return

    var untrained: Dictionary = _instantiate(root, "baseline-b")
    var baseline: Variant = untrained["battle"]
    check.call(bool(untrained["started"]), "baseline live B battle begins")
    baseline.spawn_hostile({"actor_id":"E","position":Vector3(2,0,0),
        "max_health":1000.0,"move_speed":0.0,"attack_speed":0.0})
    _advance(baseline, 24)
    var hp_baseline: float = float(baseline.simulation.snapshot("E")["stats"]["health"])
    check.call(hp_baseline < 1000.0, "baseline B projectile actually hit E actor")
    untrained["scene"].queue_free()
    baseline.reset()

    var fixture: Dictionary = _instantiate(root, "b-growth-run")
    var battle: Variant = fixture["battle"]
    check.call(bool(fixture["started"]), "E-approved production B encounter begins")
    var applied: Dictionary = battle.configure_authorized_progression(approved)
    if not bool(applied.get("ok", false)):
        printerr("B E PROFILE DIAG: ", applied, " E rev=", approved.get("revision",null),
            " parse=", battle._parse_authorized_progression(approved))
    check.call(bool(applied.get("ok", false)), "E readback installed into live B player")
    var actor: Dictionary = battle.actor_snapshot()
    check.call(_near(float(actor["stats"]["attack_damage"]),6.0)
        and _near(float(actor["stats"]["health"]),110.0)
        and _near(float(actor["stats"]["max_mana"]),115.0)
        and _near(float(actor["mana"]),115.0),
        "invested AD and full spawn HP/mana visible from read-only B snapshot")
    var again: Dictionary = battle.configure_authorized_progression(approved)
    check.call(bool(again.get("ok", false))
        and _near(float(battle.actor_snapshot()["stats"]["attack_damage"]),6.0),
        "same saved E revision never double-applies investment")
    var altered: Dictionary = approved.duplicate(true)
    altered["snapshot"]["vaccine"]["invested_stats"] = {"unsupported":1}
    check.call(not bool(battle.configure_authorized_progression(altered).get("ok", false))
        and _near(float(battle.actor_snapshot()["stats"]["attack_damage"]),6.0),
        "reject invalid new investment without corrupting previously installed build")
    var skills_copy: Dictionary = battle.skill_snapshot()
    check.call(skills_copy["slots"].size() == 3 and
        str(skills_copy["slots"][2]["skill_id"]) == "nv_skill_root_packet",
        "three E-authorized skills in combat controller")
    skills_copy["slots"].clear()
    check.call(battle.skill_snapshot()["slots"].size() == 3,
        "UI copy cannot mutate B live skill slots")
    battle.spawn_hostile({"actor_id":"E","position":Vector3(2,0,0),
        "max_health":1000.0,"move_speed":0.0,"attack_speed":0.0})
    _advance(battle, 24)
    var hp_boost: float = float(battle.simulation.snapshot("E")["stats"]["health"])
    check.call(hp_boost < hp_baseline,
        "E permanent AD investment increases real B projectile damage")
    var denied_mid: Dictionary = battle.configure_authorized_progression(approved)
    check.call(str(denied_mid.get("reason","")) == "in_mission_progression_change_denied",
        "no loadout swap after first mission combat tick")
    check.call(not bool(battle.learn_active_skill({"id":"random"},
        0, true)), "unreviewed mid-mission skill injection blocked")
    var mana_before: float = float(battle.skill_snapshot()["mana"])
    var cast_notes: Array = _advance(battle, 1, 0)
    check.call(cast_notes.any(func(note: Dictionary) -> bool:
        return (str(note.get("kind","")) == "skill_cast"
            and str(note.get("skill_id","")) == "nv_skill_arc_pulse")),
        "E-approved pulse skill generates live B cast event")
    check.call(float(battle.skill_snapshot()["mana"]) < mana_before,
        "real skill consumes mana in B 60Hz controller")
    check.call(int(battle.skill_snapshot()["slots"][0]["remaining_cooldown_ticks"]) == 82,
        "10 E ability haste reduces 90-tick draft cooldown to 82 fixed ticks")
    var impact: bool = false
    for note in _advance(battle, 20, -2):
        if str(note.get("kind","")) == "skill_impact":
            impact = true
    check.call(impact, "actual B projectile hit produces public skill_impact")
    var cooldown_notes: Array = _advance(battle, 1, 0)
    check.call(cooldown_notes.any(func(note: Dictionary) -> bool:
        return (str(note.get("kind","")) == "skill_rejected"
            and str(note.get("reason","")) == "cooldown")),
        "skill cooldown rejects second real cast")
    var status_pre: Dictionary = battle.simulation.snapshot("E")["status_state"]
    _advance(battle, 1, 2)
    for tick in range(22):
        _advance(battle, 1, -2)
    var status_post: Dictionary = battle.simulation.snapshot("E")["status_state"]
    check.call(status_post.get("statuses", []).size() > status_pre.get("statuses", []).size(),
        "third registered skill applies real B enemy rooted status on hit")
    var remaining: int = int(battle.skill_snapshot()["slots"][0][
        "remaining_cooldown_ticks"])
    _advance(battle, remaining, -2)
    var recast_notes: Array = _advance(battle, 1, 0)
    check.call(recast_notes.any(func(note: Dictionary) -> bool:
        return str(note.get("kind", "")) == "skill_cast"),
        "after haste-adjusted cooldown the same real active skill casts again")
    Statuses.apply(battle.simulation.actors["VACCINE"]["status_state"],
        {"id":"test_silence","flags":["silenced"],"duration_ticks":3})
    var silenced: Array = _advance(battle, 1, 1)
    check.call(silenced.any(func(note: Dictionary) -> bool:
        return str(note.get("reason", "")) == "control_blocked"),
        "silence blocks another authorized real skill without spending mana")
    _advance(battle, 3, -2)
    var after_silence: Array = _advance(battle, 1, 1)
    check.call(after_silence.any(func(note: Dictionary) -> bool:
        return str(note.get("kind", "")) == "skill_cast"),
        "skill becomes available after silence expires")
    battle.reset()
    check.call(battle.authorized_growth_snapshot().is_empty(),
        "region reset clears previous encounter's authorized build and temporary effects")
    check.call(bool(battle.begin_encounter(
        fixture["scene"].get_child(0), fixture["scene"].get_child(1),
        {}, "b-growth-run")), "new hardware encounter starts fresh B runtime")
    check.call(bool(battle.configure_authorized_progression(approved).get("ok", false))
        and _near(float(battle.actor_snapshot()["stats"]["attack_damage"]),6.0)
        and battle.skill_snapshot()["slots"].size() == 3,
        "same E durable snapshot reapplies on new region without stacking")
    fixture["scene"].queue_free()

    var cleared: Dictionary = Gateway.snapshot(slot)
    var unchanged: bool = bool(cleared.get("ok", false))
    if unchanged:
        var saved: Dictionary = cleared["snapshot"]["vaccine"]["invested_stats"]
        unchanged = saved.size() == increments.size()
        for key in increments:
            if not saved.has(key) or float(saved.get(key, -1.0)) != float(increments[key]):
                unchanged = false
    check.call(unchanged,
        "B combat never modifies E storage or committed investments across JSON types")
    _erase(dir)
