extends RefCounted
## Stable, separate RNG streams avoid changing difficulty when a map/VFX roll changes.
const Catalog = preload("res://scripts/rework/world/hardware_catalog.gd")
const DIFFICULTIES := ["easy", "normal", "hard"]
const ENVIRONMENTS := ["normal", "mutation", "stack_overflow", "tunnel_infection", "spread", "initial"]

static func stream_seed(seed: int, region_id: String, stream: String) -> int:
    var result: int = posmod(seed, 2147483646) + 1
    for code in (region_id + "|" + stream).to_utf8_buffer():
        result = (result * 131 + int(code)) % 2147483647
    return maxi(1, result)

static func _rng(seed: int, region_id: String, stream: String) -> RandomNumberGenerator:
    var rng := RandomNumberGenerator.new()
    rng.seed = stream_seed(seed, region_id, stream)
    return rng

static func create_mission(seed: int, previous_mutation_missions: int = 0) -> Dictionary:
    var regions: Dictionary = {}
    var easy_exists: bool = false
    for id in Catalog.IDS:
        var difficulty_rng := _rng(seed, id, "difficulty.v1")
        var difficulty: String = DIFFICULTIES[difficulty_rng.randi_range(0, DIFFICULTIES.size() - 1)]
        if difficulty == "easy":
            easy_exists = true
        var environment_rng := _rng(seed, id, "environment.v1")
        var environment_id: String = ENVIRONMENTS[environment_rng.randi_range(0, ENVIRONMENTS.size() - 1)]
        var stage: int = 0
        if environment_id == "mutation":
            # [draft] History changes stage severity, NOT mutation appearance frequency.
            var stage_rng := _rng(seed, id, "mutation_stage.v1")
            stage = stage_rng.randi_range(1, mini(5, 1 + maxi(0, previous_mutation_missions) / 3))
        var catalog_entry: Dictionary = Catalog.get_hardware(id)
        regions[id] = {
            "id": id, "difficulty": difficulty,
            "environment": {"id": environment_id, "mutation_stage": stage},
            "map_seed": stream_seed(seed, id, "map.v1"),
            "encounter_seed": stream_seed(seed, id, "combat.v1"),
            "objective_kind": catalog_entry["objective_kind"],
            "objective_ids": catalog_entry["objective_ids"].duplicate(),
            "enemy_roles": catalog_entry["enemy_roles"].duplicate(),
            "boss_role": catalog_entry["boss_role"]
        }
    if not easy_exists:
        var guaranteed_rng := _rng(seed, "MISSION", "forced_easy.v1")
        var easy_id: String = Catalog.IDS[guaranteed_rng.randi_range(0, Catalog.IDS.size() - 1)]
        regions[easy_id]["difficulty"] = "easy"
    return {"schema": 1, "seed": seed, "regions": regions, "hardware_order": Catalog.ids()}

static func count_easy(mission: Dictionary) -> int:
    var count: int = 0
    for region_id in Catalog.IDS:
        if mission.get("regions", {}).get(region_id, {}).get("difficulty", "") == "easy":
            count += 1
    return count
