extends RefCounted
## S01 D-only preflight for E-approved D-procedural-v1 maps.
## This is read-only: never regenerates a stored map, changes a seed, or repairs
## an invalid save. It catches inaccessible/overlapping F targets before draw.
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const Catalog = preload("res://scripts/rework/world/hardware_catalog.gd")

static func inspect(plan: Dictionary) -> Dictionary:
    var issues: Array[String] = []
    var id: String = str(plan.get("region_id", ""))
    var width: int = int(plan.get("width", 0))
    var height: int = int(plan.get("height", 0))
    var tiles: PackedInt32Array = plan.get("tiles", PackedInt32Array())
    if int(plan.get("schema", -1)) != 1 or not Catalog.has_hardware(id):
        issues.append("invalid_region_or_schema")
    if width < 5 or height < 5 or width > 255 or height > 255 or tiles.size() != width * height:
        issues.append("invalid_tile_dimensions")
    if absf(float(plan.get("tile_size", 0.0)) - Maps.TILE_SIZE) > 0.00001:
        issues.append("incompatible_tile_size")
    if not issues.is_empty():
        return {"ok": false, "issues": issues}
    var spawn: Vector2i = plan.get("spawn", Vector2i(-1, -1))
    var boss: Vector2i = plan.get("boss", Vector2i(-1, -1))
    var extraction: Vector2i = plan.get("extraction", Vector2i(-1, -1))
    if not Maps.is_floor(plan, spawn):
        issues.append("spawn_not_walkable")
    if not Maps.is_floor(plan, boss) or boss == spawn:
        issues.append("boss_not_accessible")
    if not Maps.is_floor(plan, extraction):
        issues.append("extraction_not_walkable")
    if not issues.is_empty():
        return {"ok": false, "issues": issues}
    var distances: PackedInt32Array = Maps.distance_field(plan, spawn)
    if distances[boss.y * width + boss.x] < 1:
        issues.append("boss_disconnected")
    if distances[extraction.y * width + extraction.x] < 0:
        issues.append("extraction_disconnected")
    if not Maps.all_floor_reachable(plan):
        issues.append("disconnected_floor")
    # No blocking objective pedestals are allowed. The player radius (0.30m)
    # fits through a 1.2m corridor, and all interaction pads use floor centres.
    var wanted: Array = Catalog.objectives(id)
    var objectives: Array = plan.get("objectives", [])
    if objectives.size() != wanted.size():
        issues.append("objective_count_mismatch")
    var used_ids: Dictionary = {}
    var used_cells: Dictionary = {}
    for item in objectives:
        if not item is Dictionary:
            issues.append("invalid_objective_payload")
            continue
        var goal_id: String = str(item.get("id", ""))
        var cell: Vector2i = item.get("cell", Vector2i(-1, -1))
        if not wanted.has(goal_id) or used_ids.has(goal_id):
            issues.append("duplicate_or_unknown_objective:" + goal_id)
        used_ids[goal_id] = true
        if used_cells.has(cell) or cell == spawn or cell == boss or cell == extraction:
            issues.append("objective_overlaps_reserved_cell:" + goal_id)
        used_cells[cell] = true
        if not Maps.is_floor(plan, cell):
            issues.append("objective_in_wall:" + goal_id)
            continue
        if distances[cell.y * width + cell.x] < 0:
            issues.append("objective_disconnected:" + goal_id)
        var world: Vector3 = item.get("world", Vector3(INF, INF, INF))
        if not world.is_finite() or world.distance_to(Maps.cell_to_world(cell)) > 0.001:
            issues.append("objective_marker_not_on_walkable_cell:" + goal_id)
    return {
        "ok": issues.is_empty(), "issues": issues,
        "region_id": id, "reachable_floor_cells": distances.size(),
        "objective_count": objectives.size(),
        "generator_id": Maps.GENERATOR_ID
    }
