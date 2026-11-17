extends RefCounted
## D-owned new five-hardware catalogue. Objectives/enemies/bosses are [draft v0.1],
## and are not a replacement for the legacy four-sector campaign.
const IDS := ["CPU", "GPU", "RAM", "SSD", "HDD"]
const HARDWARE := {
    "CPU": {
        "label": "CPU", "objective_kind": "core_defense",
        "objective_ids": ["secure_core", "stop_intruders"],
        "enemy_roles": ["process_rusher", "overheater"], "boss_role": "kernel_overseer",
        "design_note": "Hold a computation core while removing hostile processes."
    },
    "GPU": {
        "label": "GPU", "objective_kind": "overload_control",
        "objective_ids": ["disable_emitter", "stabilize_coolant"],
        "enemy_roles": ["beam_caster", "hazard_warden"], "boss_role": "shader_colossus",
        "design_note": "Avoid projected beams and suppress overload."
    },
    "RAM": {
        "label": "RAM", "objective_kind": "memory_cleanse",
        "objective_ids": ["purge_corruption", "verify_memory"],
        "enemy_roles": ["mirror", "duplicator"], "boss_role": "memory_phantom",
        "design_note": "Find and cleanse corrupted memory among replicas."
    },
    "SSD": {
        "label": "SSD", "objective_kind": "data_capture",
        "objective_ids": ["capture_node", "seal_transfer"],
        "enemy_roles": ["rapid_runner", "data_sentry"], "boss_role": "flash_daemon",
        "design_note": "Use mobility to secure high-speed data nodes."
    },
    "HDD": {
        "label": "HDD", "objective_kind": "sector_repair",
        "objective_ids": ["repair_platter", "restore_index"],
        "enemy_roles": ["fragment", "sector_guardian"], "boss_role": "disk_leviathan",
        "design_note": "Explore a large disk and repair its damaged sectors."
    }
}

static func ids() -> Array:
    return IDS.duplicate()

static func has_hardware(region_id: String) -> bool:
    return HARDWARE.has(region_id)

static func get_hardware(region_id: String) -> Dictionary:
    if not has_hardware(region_id):
        return {}
    return HARDWARE[region_id].duplicate(true)

static func objectives(region_id: String) -> Array:
    return get_hardware(region_id).get("objective_ids", [])
