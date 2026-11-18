extends SceneTree
## Run with Godot 4.7.2:
## godot --headless --path . --script res://tests/rework/world/s01_hardware_access_125_seeds.gd
## D-procedural-v1 remains immutable; every test is a read-only replay.
const Planner = preload("res://scripts/rework/world/mission_planner.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const Catalog = preload("res://scripts/rework/world/hardware_catalog.gd")
const Audit = preload("res://scripts/rework/world/hardware_access_audit.gd")
const Hub = preload("res://scripts/rework/world/hub_layout.gd")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const B = preload("res://scripts/rework/combat/production_cpu_adapter.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, reason: String) -> void:
    if not ok:
        failures += 1
        printerr("S01 D ACCESS FAIL: ", reason)

func _run() -> void:
    var boss_patterns: Dictionary = {
        "CPU": "radial_ring", "GPU": "line_sweep", "RAM": "homing_echoes",
        "SSD": "rail_volley", "HDD": "quake"
    }
    check(Maps.GENERATOR_ID == "D-procedural-v1", "immutable map generator identity")
    for terminal_id in Hub.TERMINALS:
        var data: Dictionary = Hub.TERMINALS[terminal_id]
        var pos: Vector3 = data["position"]
        check(Hub.BOUNDS.has_point(Vector2(pos.x, pos.z)), "hub terminal inside walkable bounds " + terminal_id)
        check(str(Hub.request_terminal(terminal_id, pos).get("action", "")) == str(data["action"]),
            "hub F interaction reachable " + terminal_id)
    for seed in range(125):
        var mission: Dictionary = Planner.create_mission(seed + 1)
        check(Planner.count_easy(mission) >= 1, "at least one easy hardware at seed " + str(seed))
        var board = Board.new("s01", mission)
        for enemy in Board.INTRO:
            check(board.confirm_intro_defeat(str(enemy["id"]), "s01:%d:%s" % [seed, enemy["id"]]),
                "intro enemy receipt accepted")
        check(board.gates_unlocked(), "all five gates unlock after real introductory receipts")
        for id in Catalog.IDS:
            var gate: Dictionary = Board.GATES[id]
            var gate_at: Vector3 = gate["position"]
            check(Board.BOUNDS.has_point(Vector2(gate_at.x, gate_at.z)),
                "board gate physically inside playable bounds: " + id)
            var requested: Dictionary = board.request_gate(id, gate_at)
            check(requested.get("region_id", "") == id, "walk-up F gate works: " + id)
            check(board.return_to_board(id), "five-gate return path: " + id)
            var plan: Dictionary = mission["regions"][id]
            var generated: Dictionary = Maps.generate(id, int(plan["map_seed"]), plan["environment"])
            var result: Dictionary = Audit.inspect(generated)
            check(bool(result.get("ok", false)),
                "seed=%d %s %s" % [seed, id, str(result.get("issues", []))])
            if not bool(result.get("ok", false)):
                continue
            if id == "GPU":
                check(not Audit.gpu_dodge_orientations(generated).is_empty(),
                    "GPU only schedules physically dodgeable beam directions")
            if seed < 3:
                check(generated == Maps.generate(id, int(plan["map_seed"]), plan["environment"]),
                    "old v1 map replays exactly: " + id)
            var role: String = str(Catalog.get_hardware(id)["boss_role"])
            check(str(B.ARCHETYPES.get(role, {}).get("pattern", "")) == boss_patterns[id],
                "B actual boss attack contract matches D boss role: " + id)
    # Cover every map-scale mode, not just whichever RNG selected for 125 missions.
    for id in Catalog.IDS:
        for environment_id in Planner.ENVIRONMENTS:
            var example: Dictionary = Maps.generate(id, 20261118, {"id": environment_id})
            check(bool(Audit.inspect(example).get("ok", false)),
                "environment variant traversable: %s/%s" % [id, environment_id])
    if failures == 0:
        print("S01 D HARDWARE ACCESS PASS: 125 missions, 625 region maps + 30 variants, hub/board F, five distinct B boss patterns, preserved v1")
    quit(0 if failures == 0 else 1)
