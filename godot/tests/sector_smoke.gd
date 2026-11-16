extends SceneTree
const Catalog = preload("res://scripts/sectors/sector_catalog.gd")
const Campaign = preload("res://scripts/sectors/sector_campaign.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("P5 FAIL: " + message)

func kinds(plan: Dictionary) -> String:
    var result: PackedStringArray = []
    for entry in plan["spawns"]:
        result.append(str(entry["kind"]))
    return ",".join(result)

func _run() -> void:
    check(Catalog.IDS.size() == 4, "four sectors")
    check(Catalog.plan_wave("cpu", 0).is_empty(), "invalid index")
    check(Catalog.plan_wave("NOT_A_SECTOR", 1).is_empty(), "invalid sector")
    # These are independently derived from Python content.sectors.plan_sector_wave().
    check(kinds(Catalog.plan_wave("CPU", 2, 42)) == "overheater,overheater,rusher,rusher,rusher,overheater,rusher,rusher,overheater", "CPU seed42 wave2 MT parity")
    check(kinds(Catalog.plan_wave("MEMORY", 4, 42)) == "trojan,worm,splitter,splitter,trojan,rootkit,rootkit,splitter,worm", "MEMORY seed42 wave4 MT parity")
    check(kinds(Catalog.plan_wave("STORAGE", 5, 42)) == "encryptor,ransomware,ransomware,ransomware", "STORAGE seed42 boss")
    check(kinds(Catalog.plan_wave("NETWORK", 4, 42)) == "trojan,rootkit,worm,rootkit,rootkit,overheater,overheater,packet_swarm,overheater,rootkit,overheater,overheater,worm,overheater,packet_swarm,packet_swarm", "NETWORK seed42 wave4")
    check(kinds(Catalog.plan_wave("NETWORK", 5, 0)) == "zero_day,trojan,trojan,rootkit", "seed0 boss support selection")
    check(kinds(Catalog.plan_wave("MEMORY", 4, 42)) == kinds(Catalog.plan_wave("MEMORY", 4, 42)), "independent replay")

    var run = Campaign.new("os-seed-42", 42)
    check(run.enter_sector("cpu"), "enter CPU")
    var wave: Dictionary = run.start_wave()
    check(wave["wave_index"] == 1, "first wave")
    check(not run.confirm_wave(1, 0, false), "cannot clear living hostiles")
    check(not run.confirm_wave(0, 1, false), "cannot clear pending spawns")
    check(run.confirm_wave(0, 0, false), "first wave verified")
    check(not run.confirm_wave(0, 0, false), "duplicate wave denied")
    check(int(run.progress["CPU"]) == 1, "confirmed progress preserved")
    wave = run.start_wave()
    var replay: String = kinds(wave)
    run.retreat()
    check(int(run.progress["CPU"]) == 1, "retreat never deletes complete wave")
    check(run.enter_sector("CPU"), "reenter after retreat")
    wave = run.start_wave()
    check(wave["wave_index"] == 2 and kinds(wave) == replay, "retry same unfinished wave")
    run.retreat()

    var clean_events: Array = []
    run.os_cleaned.connect(func(): clean_events.append("os_cleaned"))
    var visit_order: Array[String] = ["NETWORK", "STORAGE", "MEMORY", "CPU"]
    for sid in visit_order:
        check(run.enter_sector(sid), "enter " + sid)
        while int(run.progress[sid]) < 5:
            wave = run.start_wave()
            var is_boss: bool = int(wave["wave_index"]) == 5
            if is_boss:
                check(not run.confirm_wave(0, 0, false), "boss required " + sid)
            check(run.confirm_wave(0, 0, is_boss), "clear " + sid)
            check(not run.confirm_wave(0, 0, is_boss), "no duplicate event " + sid)
        check(run.is_sector_clean(sid), "sector clean " + sid)
        run.retreat()
    check(run.is_os_clean(), "all four sectors clean in arbitrary order")
    check(clean_events.size() == 1, "OS cleaned event exactly once")
    check(not run.start_wave().size(), "no wave outside a sector")

    var cp: Dictionary = run.checkpoint()
    var restored = Campaign.from_checkpoint(cp)
    check(restored != null, "valid checkpoint")
    if restored != null:
        check(restored.is_os_clean(), "checkpoint preserves all cleared sectors")
        check(restored.active_wave == 0 and restored.active_sector.is_empty(), "checkpoint excludes active encounter")
    var malformed: Dictionary = cp.duplicate(true)
    malformed["completed_waves"]["NETWORK"] = 6
    check(Campaign.from_checkpoint(malformed) == null, "future/malformed wave fails closed")
    var status = Campaign.new("infection-test", 42)
    status.infection = 49.9
    status.tick_infection(1.0)
    check(is_equal_approx(status.integrity, 100.0), "no exposure in hub")
    status.apply_infection(0.2)
    status.tick_infection(1.0)
    check(is_equal_approx(status.integrity, 99.7), "spreading pressure")
    status.infection = 74.9
    status.apply_infection(0.2)
    status.tick_infection(1.0)
    check(is_equal_approx(status.integrity, 98.7), "critical pressure additive")
    status.purify(90.0)
    check(status.infection_stage() == 0, "purification lowers stage")
    var before: float = status.integrity
    status.tick_infection(60.0, true)
    check(is_equal_approx(status.integrity, before), "paused tick frozen")
    if failures == 0:
        print("P5 HEADLESS SECTOR PASS (seed 0/42, 4x5, retreat, boss, receipts, infection)")
    else:
        printerr("P5 HEADLESS SECTOR FAILURES: %d" % failures)
    quit(1 if failures > 0 else 0)
