extends SceneTree
## Synthetic pure-domain tests. No real user save paths or Python imports.
const State = preload("res://scripts/rework/progression/state_model.gd")
const Rules = preload("res://scripts/rework/progression/progression_rules.gd")
const Balance = preload("res://scripts/rework/progression/balance_config.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(condition: bool, note: String) -> void:
    if not condition:
        failures += 1
        printerr("REWORK E PROGRESSION FAIL: " + note)

func accepted(previous: Dictionary, outcome: Dictionary, note: String) -> Dictionary:
    check(outcome.get("ok", false), note + ": " + str(outcome.get("reason", "")))
    return outcome.get("state", previous)

func _run() -> void:
    var s: Dictionary = State.new_slot("domain-test", "vaccine-A")
    check(State.validate(s), "fresh v1 state")
    check(State.valid_id("run-a:1:0"), "C colon-delimited card receipts accepted")
    check(not State.valid_slot_id("invalid:filename"), "unsafe filesystem slot rejected")
    check(Balance.needed_xp(1, false) == 100, "mission level XP draft")
    check(Balance.needed_xp(2, false) == 130, "mission level XP slope")
    check(Balance.needed_xp(1, true) == 180, "vaccine XP draft")
    check(Balance.death_retention_percent(5) == 70, "five-clean retention capped 70 percent")
    check(not Balance.valid_config({"unapproved_rule": 1}), "unknown balance setting rejected")

    s = accepted(s, Rules.start_mission(s, "vaccine-A", 1, "run-1", 111), "mission 1")
    s = accepted(s, Rules.add_mission_xp(s, "vaccine-A", 1, "run-1", "kill-001", 100),
        "mission XP")
    check(s.vaccine.mission.mission_level == 2 and s.vaccine.level == 1,
        "mission and vaccine levels separated")
    check(s.vaccine.mission.pending_plugin_offers == 1, "level-up queues one offer")
    check(not Rules.add_mission_xp(s, "vaccine-A", 1, "run-1", "kill-001", 100).ok,
        "duplicate kill XP denied")
    s = accepted(s, Rules.plugin_picked(s, "vaccine-A", 1, "run-1", "choice-A",
        "plugin_alpha", "common", 1), "first plugin")
    s = accepted(s, Rules.plugin_picked(s, "vaccine-A", 1, "run-1", "choice-A2",
        "plugin_alpha", "common", 3, "event"), "level-up keeps latest level")
    s = accepted(s, Rules.plugin_picked(s, "vaccine-A", 1, "run-1", "choice-B",
        "plugin_beta", "rare", 2, "hardware"), "rare plugin")
    s = accepted(s, Rules.plugin_picked(s, "vaccine-A", 1, "run-1", "run-1:4:0",
        "plugin_gamma", "legendary", 1, "event", "run-1:4"), "legendary mission-only")
    check(not Rules.plugin_picked(s, "vaccine-A", 1, "run-1", "run-1:4:1",
        "plugin_delta", "common", 1, "event", "run-1:4").ok,
        "two different card receipts cannot claim same offer twice")
    check(not Rules.extract(s, "vaccine-A", 1, "run-1",
        "extract-too-early", ["plugin_alpha"]).ok, "cannot extract before cleaning")
    s = accepted(s, Rules.objective_completed(s, "vaccine-A", 1, "run-1",
        "objective-1", "CPU.objective-1"), "objective reward")
    s = accepted(s, Rules.hardware_cleaned(s, "vaccine-A", 1, "run-1",
        "world:run-1:CPU:clean", "CPU"), "hardware clean XP")
    check(not Rules.hardware_cleaned(s, "vaccine-A", 1, "run-1",
        "clean-cpu-again", "CPU").ok, "same hardware cannot double-pay")
    check(not Rules.extract(s, "vaccine-A", 1, "run-1",
        "extract-legend", ["plugin_gamma"]).ok, "legendary permanent learning rejected")
    check(not Rules.extract(s, "vaccine-A", 1, "run-1",
        "extract-three", ["plugin_alpha", "plugin_beta", "plugin_gamma"]).ok,
        "learning limited to two")
    s = accepted(s, Rules.extract(s, "vaccine-A", 1, "run-1", "extract-ok",
        ["plugin_alpha", "plugin_beta"]), "successful extraction")
    check(s.vaccine.total_xp == 135 and s.vaccine.mission == null, "clean and objective XP banked")
    check(s.vaccine.carried.plugin_alpha.level == 3, "learned plugin level preserved")
    check(s.vaccine.archive.is_empty(), "successful return does not auto-deposit")
    check(not Rules.extract(s, "vaccine-A", 1, "run-1", "extract-ok",
        ["plugin_alpha"]).ok, "duplicate extraction denied after mission closes")

    s = accepted(s, Rules.archive_plugin(s, "vaccine-A", 1, "plugin_alpha",
        "deposit-alpha"), "manual deposit")
    check(s.vaccine.archive.has("plugin_alpha") and not s.vaccine.carried.has("plugin_alpha"),
        "archive move is not duplication")
    check(not Rules.archive_plugin(s, "vaccine-A", 1, "plugin_alpha",
        "deposit-alpha-2").ok, "archive duplicate rejected")
    s = accepted(s, Rules.withdraw_plugin(s, "vaccine-A", 1, "plugin_alpha",
        "withdraw-alpha"), "withdraw removes original")
    check(not s.vaccine.archive.has("plugin_alpha") and s.vaccine.carried.has("plugin_alpha"),
        "deployed plugin removes archive original")
    s = accepted(s, Rules.archive_plugin(s, "vaccine-A", 1, "plugin_alpha",
        "re-deposit-alpha"), "manual re-deposit")
    s = accepted(s, Rules.learn_skill(s, "vaccine-A", 1, "learn-skill",
        "pulse"), "learn persistent skill")
    s = accepted(s, Rules.equip_skills(s, "vaccine-A", 1, "equip-skill",
        ["pulse"]), "equip known skill")

    # Deaths 1-3 preserve banked XP and archived plugins, but erase carried.
    s = accepted(s, Rules.start_mission(s, "vaccine-A", 1, "run-2", 222),
        "new mission level 1")
    check(s.vaccine.mission.mission_level == 1 and s.vaccine.mission.seed != 111,
        "fresh mission level and seed")
    check(not Rules.equip_skills(s, "vaccine-A", 1, "illegal-midmission-equip",
        ["pulse"]).ok, "ordinary skill swap blocked during mission")
    s = accepted(s, Rules.learn_skill(s, "vaccine-A", 1, "new-skill-mission",
        "barrier", ["pulse", "barrier"]), "discovered skill may equip immediately")
    check(s.vaccine.equipped_skills.has("barrier"),
        "first discovery equips new skill in same atomic transition")
    s = accepted(s, Rules.add_pending_reward(s, "vaccine-A", 1, "run-2",
        "cache-1", 100, 100), "uncommitted point cache")
    s = accepted(s, Rules.die(s, "vaccine-A", 1, "run-2", "death-1"), "first clone")
    check(s.vaccine.copies_remaining == 2 and s.vaccine.total_xp == 155,
        "first death retains 20 XP and uses one copy")
    check(s.vaccine.unspent_fragments == 20 and s.vaccine.archive.has("plugin_alpha"),
        "death retains fraction of pending points and stored plugin")
    check(s.vaccine.carried.is_empty() and s.vaccine.known_skills.has("pulse"),
        "death loses carried plugin, preserves skill")
    check(not Rules.start_mission(s, "vaccine-A", 1, "run-2", 333).ok,
        "closed run ID cannot be replayed")

    s = accepted(s, Rules.start_mission(s, "vaccine-A", 1, "run-3", 333), "run 3")
    s = accepted(s, Rules.hardware_cleaned(s, "vaccine-A", 1, "run-3",
        "cpu-3", "CPU"), "run3 hardware clean")
    s = accepted(s, Rules.add_pending_reward(s, "vaccine-A", 1, "run-3",
        "bonus-3", 100, 100), "run3 pending")
    s = accepted(s, Rules.die(s, "vaccine-A", 1, "run-3", "death-2"), "second clone")
    check(s.vaccine.copies_remaining == 1 and s.vaccine.total_xp == 221,
        "second death 30 percent of 220 XP = 66, level persists")
    check(s.vaccine.level == 2 and s.vaccine.unspent_fragments == 150,
        "vaccine level-up adds 100 fragments on top of 20+30")
    s = accepted(s, Rules.reallocate_stats(s, "vaccine-A", 1,
        "allocate", {"max_hp": 1}), "free stat point allocation")
    check(s.vaccine.unspent_fragments == 50, "one-point stat cost charged")
    s = accepted(s, Rules.reallocate_stats(s, "vaccine-A", 1,
        "reset", {}), "free hub respec")
    check(s.vaccine.unspent_fragments == 150, "full point refund on respec")

    s = accepted(s, Rules.start_mission(s, "vaccine-A", 1, "run-4", 444), "run 4")
    s = accepted(s, Rules.die(s, "vaccine-A", 1, "run-4", "death-3"), "third clone")
    check(s.vaccine.copies_remaining == 0 and s.vaccine.alive,
        "three deaths leave living vaccine with zero copies")
    s = accepted(s, Rules.start_mission(s, "vaccine-A", 1, "run-5", 555), "fourth death run")
    s = accepted(s, Rules.add_pending_reward(s, "vaccine-A", 1, "run-5",
        "pending-before-final", 100, 100), "final pending")
    s = accepted(s, Rules.die(s, "vaccine-A", 1, "run-5", "death-4"), "fourth death destroys")
    check(s.vaccine == null and s.selection_required and State.validate(s),
        "fourth death clears ALL growth and returns to vaccine select")
    check(not Rules.choose_new_vaccine(s, "").ok, "invalid next vaccine denied")
    s = accepted(s, Rules.choose_new_vaccine(s, "vaccine-B"), "new vaccine selected")
    check(s.generation == 2 and s.vaccine.copies_remaining == 3         and s.vaccine.level == 1 and s.vaccine.archive.is_empty(),
        "new vaccine has fresh independent generation and lifetime")
    check(not Rules.start_mission(s, "vaccine-A", 1, "old-callback", 42).ok,
        "stale event from destroyed generation rejected")

    # Full-clean extraction receives 5 * 120 + 220 bonus exactly once.
    var full: Dictionary = State.new_slot("full-clean", "full-vaccine")
    full = accepted(full, Rules.start_mission(full, "full-vaccine", 1,
        "full-run", 123), "full run")
    for hardware in State.HARDWARE:
        full = accepted(full, Rules.hardware_cleaned(full, "full-vaccine", 1,
            "full-run", "clean-" + hardware, hardware), "clean " + hardware)
    full = accepted(full, Rules.extract(full, "full-vaccine", 1,
        "full-run", "full-extract", []), "full extract")
    check(full.vaccine.total_xp == 820 and full.vaccine.level == 4,
        "full-clean XP bonus 220 and vaccine progression")
    check(full.vaccine.unspent_fragments == 300,
        "vaccine levels 2,3,4 each grant one point")

    if failures == 0:
        print("REWORK E PROGRESSION PASS: synthetic transition suite")
    quit(1 if failures > 0 else 0)
