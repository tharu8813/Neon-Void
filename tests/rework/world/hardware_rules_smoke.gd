extends SceneTree
## Run: godot --headless --path . --script res://tests/rework/world/hardware_rules_smoke.gd
const Environments = preload("res://scripts/rework/world/environment_rules.gd")
const Objectives = preload("res://scripts/rework/world/hardware_objectives.gd")
var failures: int = 0

func check(condition: bool, message: String) -> void:
    if not condition:
        failures += 1
        printerr("REWORK HARDWARE FAIL: ", message)

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    check(Objectives.SPECS.size() == 5, "all hardware have distinct objective specs")
    check(Environments.VALID.size() == 6, "all environment types defined")
    for region_id in Objectives.SPECS:
        var obj = Objectives.new("run-verified", region_id)
        var facts: Array = []
        obj.objective_completed.connect(func(fact: Dictionary) -> void: facts.append(fact))
        check(not obj.all_objectives_completed(), "boss initially locked " + region_id)
        for objective_id in Objectives.SPECS[region_id]:
            var rule: Dictionary = Objectives.SPECS[region_id][objective_id]
            var kind: String = rule["kind"]
            var needed: int = int(rule["required"])
            check(not obj.apply_verified_event(objective_id, "wrong_action", "wrong-"+objective_id), "wrong event rejected")
            check(not obj.apply_verified_event(objective_id, kind, "zero-"+objective_id, 0), "zero event rejected")
            check(obj.apply_verified_event(objective_id, kind, "partial-"+objective_id, needed - 1), "partial progress")
            check(not obj.apply_verified_event(objective_id, kind, "partial-"+objective_id), "duplicate receipt rejected")
            check(not bool(obj.snapshot()["progress"][objective_id]["completed"]), "still incomplete")
            check(obj.apply_verified_event(objective_id, kind, "last-"+objective_id), "complete goal")
            check(not obj.apply_verified_event(objective_id, kind, "after-"+objective_id), "completed goal ignores repeats")
        check(obj.all_objectives_completed(), "boss unlocked after all goals")
        check(facts.size() == Objectives.SPECS[region_id].size(), "exactly one completion per objective")
        check(obj.snapshot()["boss_unlocked"], "boss unlock snapshot")
        for fact in facts:
            check(fact["run_id"] == "run-verified" and fact["region_id"] == region_id, "completion identifiers")
            check(str(fact["receipt_id"]).begins_with("world:run-verified:" + region_id + ":"), "stable receipts")
    var carried: Array = [
        {"plugin_id": "protected", "learned": true},
        {"plugin_id": "ordinary_1", "learned": false},
        {"plugin_id": "ordinary_2", "learned": false},
        {"plugin_id": "unknown_status"},
        {"plugin_id": "ordinary_3", "learned": false},
        {"plugin_id": "ordinary_1", "learned": false}
    ]
    var tunnel_plan := {"environment": {"id": "tunnel_infection", "mutation_stage": 0}}
    for seed in range(100):
        var a: Dictionary = Environments.describe(tunnel_plan, carried, seed)
        var b: Dictionary = Environments.describe(tunnel_plan, carried, seed)
        check(a == b, "tunnel deterministic " + str(seed))
        var affected: Array = a["affected_plugin_ids"]
        check(affected.size() >= 1 and affected.size() <= 2, "tunnel 1-2")
        check(not affected.has("protected") and not affected.has("unknown_status"), "learned and unknown status protected")
        check(affected.size() == affected.duplicate().size(), "unique tunnel selection")
    var safe: Dictionary = Environments.describe(tunnel_plan, [{"plugin_id": "learned", "learned": true}], 42)
    check(safe["affected_plugin_ids"].is_empty(), "no eligible plugin -> none disabled")
    for stage in range(1, 6):
        var mutation: Dictionary = Environments.describe({"environment": {"id": "mutation", "mutation_stage": stage}}, [], 3)
        check(mutation["mutation_stage"] == stage and mutation["mutant_probability"] > 0, "mutation " + str(stage))
        check(mutation["mutant_plugin_slots"] >= 1 and mutation["mutant_plugin_slots"] <= 2, "mutation slots " + str(stage))
    var many: Dictionary = Environments.describe({"environment": {"id": "stack_overflow"}}, [], 42)
    check(float(many["enemy_spawn_multiplier"]) > 1.0, "stack overflow more enemies")
    check(Environments.describe({"environment": {"id": "spread"}}, [], 42)["map_scale"] == "large", "spread bigger")
    check(Environments.describe({"environment": {"id": "initial"}}, [], 42)["map_scale"] == "small", "initial smaller")
    check(Environments.describe({"environment": {"id": "normal"}}, [], 42)["map_scale"] == "standard", "normal unaffected")
    check(Environments.describe({"environment": {"id": "undefined"}}, [], 42).is_empty(), "unknown environment fails closed")
    if failures == 0:
        print("REWORK HARDWARE RULES PASS (5 objective tracks, dedup receipts, 6 environments, learned-plugin exclusion)")
    quit(0 if failures == 0 else 1)
