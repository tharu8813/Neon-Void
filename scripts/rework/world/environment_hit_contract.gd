extends RefCounted
## Converts trusted D physical contacts into B's queue_environment_hit contract.
## Never applied to the preview engine and never writes to E storage.
## Damage and CC are BALANCE_DRAFT and require A gameplay approval.
const PROFILES := {
    "CPU": {"kind": "core_thermal_pulse", "type": "magic", "damage": 4.0,
        "status": {"id": "cpu_thermal_slow", "duration_ticks": 90,
            "stack_mode": "refresh", "stat_modifiers": {"move_speed": -0.9}}},
    "GPU": {"kind": "coolant_overload_beam", "type": "magic", "damage": 7.0,
        "status": {"id": "gpu_beam_burn", "duration_ticks": 90,
            "stack_mode": "refresh", "dot_dps": 1.5,
            "dot_damage_type": "magic"}},
    "RAM": {"kind": "checksum_decoy_shock", "type": "magic", "damage": 5.0,
        "status": {"id": "ram_corruption_slow", "duration_ticks": 90,
            "stack_mode": "refresh", "stat_modifiers": {"move_speed": -1.1}}},
    "SSD": {"kind": "transfer_overclock_surge", "type": "magic", "damage": 5.0,
        "status": {"id": "ssd_surge_slow", "duration_ticks": 60,
            "stack_mode": "refresh", "stat_modifiers": {"move_speed": -0.8}}},
    "HDD": {"kind": "platter_arc_discharge", "type": "physical", "damage": 6.0,
        "status": {"id": "hdd_arc_slow", "duration_ticks": 120,
            "stack_mode": "refresh", "stat_modifiers": {"move_speed": -1.2}}}
}

static func to_b_spec(fact: Dictionary, expected_run: String, expected_region: String) -> Dictionary:
    if expected_run.is_empty() or not PROFILES.has(expected_region)             or int(fact.get("schema", 0)) != 1             or str(fact.get("type", "")) != "world_hazard_contact"             or str(fact.get("run_id", "")) != expected_run             or str(fact.get("region_id", "")) != expected_region             or str(fact.get("actor_id", "")) != "VACCINE":
        return {}
    var profile: Dictionary = PROFILES[expected_region]
    if str(fact.get("hazard_kind", "")) != str(profile["kind"]):
        return {}
    var hazard_id: String = str(fact.get("hazard_id", ""))
    var cycle: int = int(fact.get("cycle", -1))
    var tick: int = int(fact.get("tick", 0))
    if cycle < 0 or tick <= 0 or hazard_id.is_empty()             or not hazard_id.begins_with(expected_region + ":"):
        return {}
    var player: Vector3 = fact.get("player_world", Vector3.INF)
    var source: Vector3 = fact.get("source_world", Vector3.INF)
    if not player.is_finite() or not source.is_finite():
        return {}
    var kind: String = str(profile["kind"])
    # A stable key per physical hazard/cycle, NOT per rendered frame.
    return {
        "run_id": expected_run,
        "region_id": expected_region,
        "source_id": "%s:%s" % [expected_region, kind],
        "event_id": hazard_id, "target_id": "VACCINE",
        "damage_type": str(profile["type"]),
        "base_damage": float(profile["damage"]),
        "repeat_ticks": 0, "is_area": true, "is_dot": false,
        "status_effects": [profile["status"].duplicate(true)]
    }
