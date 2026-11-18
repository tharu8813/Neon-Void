extends SceneTree
## A-only end-to-end D plan -> A bridge -> E durable map seed contract.
## Synthetic isolated slot, never real player saves or old Python JSON files.
const Bridge = preload("res://scripts/rework/integration/mission_commit_bridge.gd")
const Planner = preload("res://scripts/rework/world/mission_planner.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const Model = preload("res://scripts/rework/progression/state_model.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const HARDWARE := ["CPU", "GPU", "RAM", "SSD", "HDD"]
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, label: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A HARDWARE ACK FAIL: " + label)

func _clean(path: String) -> void:
    var dir: DirAccess = DirAccess.open(path)
    if dir == null:
        return
    dir.list_dir_begin()
    var filename: String = dir.get_next()
    while not filename.is_empty():
        var full: String = path.path_join(filename)
        if dir.current_is_dir():
            _clean(full)
        else:
            DirAccess.remove_absolute(full)
        filename = dir.get_next()
    dir.list_dir_end()
    DirAccess.remove_absolute(path)

func _fact(run_id: String, region: String, plan: Dictionary) -> Dictionary:
    return {
        "schema": 1, "type": "hardware_checkpoint_requested",
        "run_id": run_id, "region_id": region,
        "map_seed": int(plan.get("map_seed", 0)),
        "generator_id": Maps.GENERATOR_ID,
        "region_plan": plan.duplicate(true),
        "receipt_id": run_id + ":" + region + ":map-checkpoint"
    }

func _run() -> void:
    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var root_dir: String = ProjectSettings.globalize_path("user://rework-a-hardware-" + suffix)
    var slot: String = root_dir.path_join("rework-slot-hardware")
    check(DirAccess.make_dir_recursive_absolute(root_dir) == OK, "fixture folder")
    var initialized: Dictionary = Store.initialize(slot,
        Model.new_slot("hardware", "vaccine-hardware"))
    check(bool(initialized.get("ok", false)), "isolated E state created")
    if not bool(initialized.get("ok", false)):
        _clean(root_dir)
        quit(1)
        return
    var run_id: String = "hardware-run"
    var bridge = Bridge.new(slot, run_id, "vaccine-hardware", 1)
    check(bool(bridge.begin_mission(42).get("ok", false)),
        "E mission start committed before hardware gates")
    var plans: Dictionary = Planner.create_mission(42).get("regions", {})
    check(plans.size() == 5, "D generated five hardware plans")
    for region in HARDWARE:
        var plan: Dictionary = plans.get(region, {})
        check(not plan.is_empty() and str(plan.get("id", "")) == region,
            "D region plan identity: " + region)
        var ack: Dictionary = bridge.checkpoint_hardware_entry(
            _fact(run_id, region, plan))
        check(bool(ack.get("ok", false)) and not bridge.frozen
            and str(ack.get("receipt_id", "")) == run_id + ":" + region + ":map-checkpoint"
            and int(ack.get("revision", 0)) >= 3,
            "E ACK precedes " + region + " geometry creation")
    var state: Dictionary = Gateway.snapshot(slot)
    check(bool(state.get("ok", false)), "E authoritative read succeeds")
    if bool(state.get("ok", false)):
        var mission: Dictionary = state["snapshot"]["vaccine"]["mission"]
        var checkpoint: Dictionary = mission.get("environment_rolls", {}).get(
            "e_checkpoint_v1", {})
        var persisted: Dictionary = checkpoint.get("regions", {})
        check(persisted.size() == 5, "all five E region checkpoints persisted")
        for region in HARDWARE:
            var entry: Dictionary = persisted.get(region, {})
            check(int(entry.get("seed", -1)) == int(plans[region]["map_seed"])
                and str(entry.get("generator_id", "")) == Maps.GENERATOR_ID
                and int(mission["generated_map_seeds"].get(region, -1))
                    == int(plans[region]["map_seed"]),
                "E seed and generator exactly match D plan: " + region)
    var revision: int = int(state.get("revision", -1))
    var malformed: Dictionary = _fact(run_id, "GPU", plans["GPU"])
    malformed["region_plan"]["id"] = "CPU"
    var reject = Bridge.new(slot, run_id, "vaccine-hardware", 1)
    check(not bool(reject.checkpoint_hardware_entry(malformed).get("ok", false))
        and reject.frozen, "cross-region plan substitution fails closed")
    var wrong_run: Dictionary = _fact("other-run", "RAM", plans["RAM"])
    reject = Bridge.new(slot, run_id, "vaccine-hardware", 1)
    check(not bool(reject.checkpoint_hardware_entry(wrong_run).get("ok", false))
        and reject.frozen, "cross-run receipt or plan cannot be saved")
    var wrong_id: Dictionary = _fact(run_id, "SSD", plans["SSD"])
    wrong_id["receipt_id"] = "fake:receipt"
    reject = Bridge.new(slot, run_id, "vaccine-hardware", 1)
    check(not bool(reject.checkpoint_hardware_entry(wrong_id).get("ok", false))
        and reject.frozen, "noncanonical receipt fails closed")
    var replay = Bridge.new(slot, run_id, "vaccine-hardware", 1)
    check(not bool(replay.checkpoint_hardware_entry(
        _fact(run_id, "CPU", plans["CPU"])).get("ok", false)) and replay.frozen,
        "duplicate CPU map receipt cannot advance revision")
    check(int(Gateway.snapshot(slot).get("revision", -2)) == revision,
        "all invalid and duplicate commands leave E revision unchanged")
    _clean(root_dir)
    if failures == 0:
        print("REWORK A HARDWARE ACK PASS: 5 immutable E checkpoints; plan/run/replay guards")
    quit(1 if failures > 0 else 0)
