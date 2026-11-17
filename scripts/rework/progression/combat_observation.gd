extends RefCounted
## E-owned diagnostic-only B/D combat checkpoint contract.
## Capturing this data NEVER enables resume, retry, abandonment or settlement.
const Checkpoint = preload("res://scripts/rework/progression/mission_checkpoint.gd")
const KEY: String = "e_combat_observation_v1"
const VERSION: int = 1
const MAX_PACKET_BYTES: int = 512 * 1024

static func _keys(value: Variant, fields: Array) -> bool:
    if typeof(value) != TYPE_DICTIONARY or value.size() != fields.size():
        return false
    for field in fields:
        if not value.has(field):
            return false
    return true

static func _valid_packet(packet: Variant, tick: int, battle: bool) -> bool:
    if typeof(packet) != TYPE_DICTIONARY:
        return false
    var required: Array = ["tick", "rng_state_text"]
    if battle:
        required.append_array(["player", "enemies", "projectiles",
            "skills", "status_effects"])
    else:
        required.append_array(["player_transform", "enemy_transforms",
            "world_entities"])
    for name in required:
        if not packet.has(name):
            return false
    if packet.tick != tick or typeof(packet.rng_state_text) != TYPE_STRING \
            or packet.rng_state_text.is_empty() \
            or packet.rng_state_text.length() > 128:
        return false
    if battle:
        if typeof(packet.player) != TYPE_DICTIONARY \
                or typeof(packet.enemies) != TYPE_ARRAY \
                or typeof(packet.projectiles) != TYPE_ARRAY \
                or typeof(packet.skills) != TYPE_DICTIONARY \
                or typeof(packet.status_effects) != TYPE_ARRAY:
            return false
    elif typeof(packet.player_transform) != TYPE_DICTIONARY \
            or typeof(packet.enemy_transforms) != TYPE_ARRAY \
            or typeof(packet.world_entities) != TYPE_ARRAY:
        return false
    var serialized: String = JSON.stringify(packet, "", true, true)
    return serialized.to_utf8_buffer().size() <= MAX_PACKET_BYTES

static func _digest(b: Dictionary, d: Dictionary) -> String:
    return JSON.stringify({"b": b, "d": d}, "", true, true).sha256_text()

static func valid(mission: Dictionary) -> bool:
    var rolls: Variant = mission.get("environment_rolls", {})
    if typeof(rolls) != TYPE_DICTIONARY:
        return false
    if not rolls.has(KEY):
        return true
    var cp: Variant = rolls[KEY]
    if not _keys(cp, ["version", "region_id", "map_seed", "generator_id",
            "combat_tick", "b_version", "d_version", "b_state", "d_state",
            "state_sha256", "restore_authorized"]):
        return false
    if cp.version != VERSION or cp.restore_authorized != false \
            or typeof(cp.region_id) != TYPE_STRING \
            or not Checkpoint.HARDWARE.has(cp.region_id) \
            or not Checkpoint._whole(cp.combat_tick, 1000000000000) \
            or not Checkpoint._whole(cp.map_seed) \
            or not Checkpoint._id(cp.generator_id) \
            or not Checkpoint._id(cp.b_version) \
            or not Checkpoint._id(cp.d_version) \
            or typeof(cp.state_sha256) != TYPE_STRING:
        return false
    var world: Dictionary = rolls.get(Checkpoint.KEY, {})
    if not world.has("regions") or not world.regions.has(cp.region_id):
        return false
    var region: Dictionary = world.regions[cp.region_id]
    if region.seed != cp.map_seed or region.generator_id != cp.generator_id \
            or not _valid_packet(cp.b_state, int(cp.combat_tick), true) \
            or not _valid_packet(cp.d_state, int(cp.combat_tick), false):
        return false
    return cp.state_sha256 == _digest(cp.b_state, cp.d_state)

static func monotonic(before: Dictionary, after: Dictionary) -> bool:
    var previous: Variant = before.get("environment_rolls", {}).get(KEY, null)
    var current: Variant = after.get("environment_rolls", {}).get(KEY, null)
    if previous == null:
        return true
    if current == null or not valid(after):
        return false
    # Mission-global monotonic B/D tick. A failed save cannot erase or rewind it.
    return int(current.combat_tick) >= int(previous.combat_tick) \
        and (int(current.combat_tick) > int(previous.combat_tick) \
            or current == previous)

static func record(mission: Dictionary, region_id: String, tick: int,
        b_version: String, d_version: String, b_state: Dictionary,
        d_state: Dictionary) -> Dictionary:
    if not Checkpoint.HARDWARE.has(region_id) \
            or tick < 0 or tick > 1000000000000 \
            or not Checkpoint._id(b_version) or not Checkpoint._id(d_version) \
            or not _valid_packet(b_state, tick, true) \
            or not _valid_packet(d_state, tick, false):
        return {"ok": false, "reason": "invalid_combat_observation_packet"}
    var cp: Dictionary = mission.get("environment_rolls", {}).get(
        Checkpoint.KEY, {})
    if not cp.has("regions") or not cp.regions.has(region_id):
        return {"ok": false, "reason": "region_checkpoint_required"}
    var region: Dictionary = cp.regions[region_id]
    var previous: Variant = mission.environment_rolls.get(KEY, null)
    if previous != null and tick <= int(previous.combat_tick):
        return {"ok": false, "reason": "combat_tick_not_advanced"}
    var packet: Dictionary = {
        "version": VERSION, "region_id": region_id, "map_seed": region.seed,
        "generator_id": region.generator_id, "combat_tick": tick,
        "b_version": b_version, "d_version": d_version,
        "b_state": b_state.duplicate(true), "d_state": d_state.duplicate(true),
        "state_sha256": _digest(b_state, d_state),
        "restore_authorized": false
    }
    mission.environment_rolls[KEY] = packet
    if not valid(mission):
        return {"ok": false, "reason": "invalid_combat_observation_state"}
    return {"ok": true, "combat_tick": tick,
        "state_sha256": str(packet.state_sha256)}
