extends SceneTree
## Focused headless checks for the new rework gameplay layer:
## deterministic skill drafts, D-map blink clipping, and boss pattern pressure.
## Uses a synthetic fixture only; no save file, legacy asset or E slot is touched.
const Production = preload("res://scripts/rework/combat/production_cpu_adapter.gd")
const SkillOffer = preload("res://scripts/rework/skills/skill_offer.gd")
const SkillRegistry = preload("res://scripts/rework/skills/skill_registry.gd")
const Motor = preload("res://scripts/rework/world/preview_player_motor.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
var failures: int = 0

func check(value: bool, name: String) -> void:
    if not value:
        failures += 1
        printerr("REWORK GAMEPLAY LAYER FAIL: " + name)

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    _check_skill_draft()
    _check_boss_pressure_and_blink()
    await process_frame
    if failures == 0:
        print("REWORK GAMEPLAY LAYER PASS: deterministic skill draft, wall-safe blink, telegraphed boss patterns")
    quit(1 if failures > 0 else 0)

func _ids(candidates: Array) -> String:
    var result: Array[String] = []
    for candidate in candidates:
        result.append(str(candidate.get("id", "")))
    return "|".join(result)

func _check_skill_draft() -> void:
    check(SkillRegistry.ids().size() >= 9, "draft registry has a playable pool")
    var first: Array = SkillOffer.candidates(42, "CPU", [])
    var again: Array = SkillOffer.candidates(42, "CPU", [])
    check(first.size() == 3 and _ids(first) == _ids(again),
        "same durable seed/region/known set draws the same three cards")
    var known: Array = [str(first[0].get("id", "")), str(first[1].get("id", ""))]
    var excluding: Array = SkillOffer.candidates(42, "CPU", known)
    check(excluding.size() >= 1, "draft stays available while unknown skills remain")
    for candidate in excluding:
        check(not known.has(str(candidate.get("id", ""))),
            "already-known skill is excluded from a new draft")
    var other_region: Array = SkillOffer.candidates(42, "GPU", [])
    check(not other_region.is_empty() and _ids(other_region) != _ids(first),
        "region id participates in the deterministic draft")

func _hand_map() -> Dictionary:
    # 5x3 room with a single solid wall tile at (2,1). Floor tiles are 1.
    var tiles := PackedInt32Array([
        1, 1, 1, 1, 1,
        1, 1, 0, 1, 1,
        1, 1, 1, 1, 1
    ])
    return {"width": 5, "height": 3, "tiles": tiles,
        "world_bounds": Rect2(0, 0, 5.0 * Maps.TILE_SIZE, 3.0 * Maps.TILE_SIZE)}

func _check_boss_pressure_and_blink() -> void:
    var host := Node3D.new()
    root.add_child(host)
    var player = Motor.new()
    player.name = "SyntheticPlayer"
    host.add_child(player)
    player.global_position = Maps.cell_to_world(Vector2i(1, 1)) + Vector3.UP * 0.5
    var camera := Camera3D.new()
    host.add_child(camera)
    var battle = Production.new()
    host.add_child(battle)
    battle.balance_draft["player_stats"] = {"max_health": 50000.0, "health": 50000.0}
    var map: Dictionary = _hand_map()
    check(battle.begin_encounter(player, camera, map, "gameplay-layer-test",
        {"seed": 9}), "synthetic B encounter begins")
    check(battle.spawn_hostile({"actor_id": "boss", "boss": true,
        "role": "kernel_overseer",
        "position": Maps.cell_to_world(Vector2i(1, 0)) + Vector3.UP * 0.5}),
        "known boss role spawns from the B archetype table")

    # D-map blink clipping: a blink cannot tunnel through the (2,1) wall.
    var start: Vector3 = Maps.cell_to_world(Vector2i(1, 1))
    var finish: Vector3 = Maps.cell_to_world(Vector2i(3, 1))
    var clipped: Dictionary = battle._clip_blink_to_floor(start, finish)
    check(bool(clipped.get("blocked", false)), "blink reports a blocked wall crossing")
    var landed: Vector3 = clipped.get("position", finish)
    check(landed.x < 2.0 * Maps.TILE_SIZE + 0.01,
        "blink stops before the solid tile instead of landing inside it")

    var observed: Dictionary = {"telegraphs": 0, "patterns": 0}
    var pattern_names: Array[String] = []
    battle.combat_notice.connect(func(note: Dictionary) -> void:
        var kind: String = str(note.get("kind", ""))
        if kind == "boss_pattern_telegraph":
            observed["telegraphs"] = int(observed["telegraphs"]) + 1
        elif kind == "boss_pattern":
            observed["patterns"] = int(observed["patterns"]) + 1
            pattern_names.append(str(note.get("pattern", ""))))
    battle.set_combat_input({"move": Vector2.ZERO, "fire": false,
        "aim_world": player.global_position + Vector3.RIGHT * 6.0})
    for index in range(100):
        battle.advance_production_tick()
    check(int(observed["patterns"]) == 0 and int(observed["telegraphs"]) == 0,
        "boss stays quiet during the initial grace window")
    for index in range(60):
        battle.advance_production_tick()
    check(int(observed["telegraphs"]) >= 1,
        "boss telegraphs before its first pattern, never instant damage")
    var names: Array = pattern_names
    check(int(observed["patterns"]) == 1 and names.has("radial_ring"),
        "kernel_overseer fires its declared radial pattern once")
    check(battle.boss_pressure_snapshot().get("bosses", []).size() == 1,
        "boss pressure is exposed read-only for HUD/telemetry")

    # Skill execution data is B-owned: radial shape, mana cost, then a blink
    # that must still respect the same D wall.
    var resolved: Array = []
    battle.combat_notice.connect(func(note: Dictionary) -> void:
        if str(note.get("kind", "")) == "skill_resolved":
            resolved.append(int(note.get("projectiles", 0))))
    check(battle.learn_active_skill(SkillRegistry.definition("nv_skill_arc_pulse"),
        0, true), "draft radial skill installs in slot 1")
    battle.simulation.actors["VACCINE"]["skills"].mana = 100.0
    var projectiles_before: int = battle.simulation.projectiles.size()
    battle.set_combat_input({"move": Vector2.ZERO, "fire": false,
        "skill_slot": 0, "aim_world": player.global_position + Vector3.RIGHT * 6.0})
    battle.advance_production_tick()
    check(resolved.has(4), "radial skill resolves all four projectiles")
    check(battle.simulation.projectiles.size() >= projectiles_before + 4,
        "radial skill projectiles enter the real B projectile list")

    check(battle.learn_active_skill(SkillRegistry.definition("nv_skill_blink_step"),
        1, true), "draft blink skill installs in slot 2")
    battle.set_combat_input({"move": Vector2.ZERO, "fire": false,
        "skill_slot": 1, "aim_world": player.global_position + Vector3.RIGHT * 8.0})
    battle.advance_production_tick()
    check(player.global_position.x < 2.0 * Maps.TILE_SIZE + 0.01,
        "live blink cast is clipped by the solid D tile, not only the helper")
    host.queue_free()
