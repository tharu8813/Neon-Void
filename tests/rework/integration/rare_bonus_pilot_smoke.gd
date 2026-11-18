extends SceneTree
## End-to-end opt-in pilot: one common source reward atomically grants an
## eligible rare through E, then A applies BOTH in B/C without a second draw.
## Synthetic unique v1 slot only, no real user or legacy save touched.

const PilotScene = preload("res://scenes/rework/integration/rework_pilot.tscn")
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Offers = preload("res://scripts/rework/plugins/plugin_offer_engine.gd")
const Rare = preload("res://scripts/rework/progression/rare_bonus.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A RARE PILOT FAIL: " + message)

func _clean(path: String) -> void:
    var dir: DirAccess = DirAccess.open(path)
    if dir == null:
        return
    dir.list_dir_begin()
    var name: String = dir.get_next()
    while not name.is_empty():
        var child: String = path.path_join(name)
        if dir.current_is_dir():
            _clean(child)
        else:
            DirAccess.remove_absolute(child)
        name = dir.get_next()
    dir.list_dir_end()
    DirAccess.remove_absolute(path)

func _run() -> void:
    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var slot_id: String = "rare-pilot-" + suffix
    var slot_path: String = Store.default_slot_path(slot_id)
    var pilot: Node3D = PilotScene.instantiate()
    pilot.set("slot_id", slot_id)
    pilot.set("initial_vaccine_id", "rare-pilot-vaccine")
    root.add_child(pilot)
    await process_frame
    check(not bool(pilot.get("frozen")), "new isolated pilot E slot initialized")
    pilot.call("_on_mission_requested", {"seed": 42})
    check(not bool(pilot.get("frozen")), "E mission starts before the B world")
    var source_entry: Dictionary = Catalog.find_by_id(Rare.SOURCE_ID, Catalog.entries())
    check(not source_entry.is_empty(), "C canonical random rare source exists")
    var draft: Dictionary = Catalog.odds_settings()
    draft["rarity_weights"] = {
        "common": 100.0, "rare": 0.0,
        "legendary": 0.0, "mythic": 0.0
    }
    var candidates: Array[Dictionary] = [source_entry]
    var offer: Variant = Offers.new(candidates, draft)
    check(bool(offer.begin_mission(42, {}, str(pilot.get("current_run_id")))),
        "deterministic forced source reward valid for isolated fixture")
    pilot.set("offers", offer)
    for index in range(13):
        pilot.call("_on_enemy_defeated",
            "rare-test-enemy-" + str(index),
            "rare-test-kill-" + str(index))
    check(paused and (pilot.get("offer_panel") as Control).visible,
        "C source card shown only after durable E offer checkpoint")
    var snapshot: Dictionary = Gateway.snapshot(slot_path)
    check(bool(snapshot.get("ok", false)) and
        snapshot["snapshot"]["vaccine"]["mission"]["environment_rolls"]
            .has("e_checkpoint_v1"),
        "E verified exact source card and draw before the user chooses")
    pilot.call("_choose_card", 0)
    check(not bool(pilot.get("frozen")) and not paused,
        "A atomic source + rare bonus accepted and combat resumed")
    snapshot = Gateway.snapshot(slot_path)
    check(bool(snapshot.get("ok", false)), "E bonus source+rare persisted")
    if bool(snapshot.get("ok", false)):
        var vaccine: Dictionary = snapshot["snapshot"]["vaccine"]
        var mission: Dictionary = vaccine["mission"]
        var history: Dictionary = mission["environment_rolls"].get(Rare.KEY, {})
        check(history.size() == 1, "precisely one bonus, no optimistic second draw")
        if not history.is_empty():
            var source_receipt: String = str(history.keys()[0])
            var bonus: Dictionary = history[source_receipt]
            var bonus_id: String = str(bonus.get("plugin_id", ""))
            var bonus_level: int = int(bonus.get("level", 0))
            check(bonus_id != Rare.SOURCE_ID and bonus.get("rarity") == "rare",
                "source card receives one distinct real rare plugin")
            check(vaccine["receipts"].has(source_receipt) and
                vaccine["receipts"].has(str(bonus.get("bonus_receipt", ""))),
                "same E journal transaction stored both receipts")
            check(mission["obtained_plugins"].size() == 2 and
                int(mission["obtained_plugins"][bonus_id]["level"]) == bonus_level,
                "E knows both source and awarded rare before UI acknowledgement")
            var combat_router: Variant = (pilot.get_node("World") as Node3D).get("combat")
            var effects: Variant = combat_router.get("live").get("bridge").effects
            var actual_levels: Dictionary = effects.active_levels()
            check(int(actual_levels.get(Rare.SOURCE_ID, 0)) == 1 and
                int(actual_levels.get(bonus_id, 0)) == bonus_level,
                "B/C active real combat levels equal E durable rare receipt")
            check(int(offer.levels.get(bonus_id, 0)) == bonus_level,
                "C future three-card options reflect E bonus level, no over-cap")
    paused = false
    root.remove_child(pilot)
    pilot.queue_free()
    await process_frame
    _clean(slot_path)
    if failures == 0:
        print("REWORK A RARE PILOT PASS: E atomic source+rare receipts, B/C ACK-gated synchronization")
    quit(1 if failures > 0 else 0)
