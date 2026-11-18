extends RefCounted
## Called by D's existing cross-platform CI world-foundation test. A/B merge gate:
## on D-only base the B queue is absent, so physical contacts are tested in the
## five-hardware preview route. After B #153 integration this runs real B HP,
## DoT/CC and duplicate-hit assertions without a synthetic reward or E write.
const Router = preload("res://scripts/rework/world/world_combat_router.gd")
const Motor = preload("res://scripts/rework/world/preview_player_motor.gd")
const Contract = preload("res://scripts/rework/world/environment_hit_contract.gd")

static func run(root: Node, check: Callable) -> void:
    var host := Node3D.new()
    host.name = "DRealBHazardQueueFixture"
    root.add_child(host)
    var motor := Motor.new()
    host.add_child(motor)
    motor.global_position = Vector3(2.0, 0.5, 2.0)
    var camera := Camera3D.new()
    host.add_child(camera)
    var router := Router.new()
    host.add_child(router)
    check.call(router.begin_encounter(motor, camera, {}, "s01-d-b-hazard-proof",
        {"formal": true, "seed": 321}), "D formal router creates B-authoritative 60Hz encounter")
    if router.live == null or router.live.bridge == null             or not router.live.bridge.has_method("queue_environment_hit"):
        print("S01 D/B INTEROP PENDING: production B #153 not integrated into this branch")
        host.queue_free()
        return
    var hit_notices: Array[Dictionary] = []
    router.combat_notice.connect(func(notice: Dictionary) -> void:
        # Direct D contacts have external_event_id; subsequent B DoT ticks
        # are real damage but must not count as an additional direct hit.
        if str(notice.get("kind", "")) == "hit" and bool(
                notice.get("environment_source", false)) and not str(
                notice.get("external_event_id", "")).is_empty():
            hit_notices.append(notice.duplicate(true)))
    var initial: float = float(router.actor_snapshot("VACCINE").get("stats", {}).get("health", -1))
    for id in ["CPU", "GPU", "RAM", "SSD", "HDD"]:
        var profile: Dictionary = Contract.PROFILES[id]
        var event_id: String = id + ":" + str(profile["kind"]) + ":1"
        var fact: Dictionary = {"schema": 1, "type": "world_hazard_contact",
            "run_id": "s01-d-b-hazard-proof", "region_id": id,
            "actor_id": "VACCINE", "hazard_kind": str(profile["kind"]),
            "hazard_id": event_id, "cycle": 1, "tick": 180,
            "source_world": Vector3(2,0,2), "player_world": motor.global_position}
        var spec: Dictionary = Contract.to_b_spec(fact, "s01-d-b-hazard-proof", id)
        check.call(not spec.is_empty(), id + ": D contact maps to B contract")
        var response: Dictionary = router.queue_environment_hit(spec)
        check.call(bool(response.get("ok", false)),
            id + ": authoritative B accepted physical hazard via D router")
        var retry: Dictionary = router.queue_environment_hit(spec)
        check.call(str(retry.get("reason", "")) == "environment_contact_cooldown",
            id + ": repeat contact cannot bypass B dedup ledger")
        router.live.bridge.set_combat_input({"move": Vector2.ZERO, "fire": false})
        router.live.bridge.advance_production_tick()
    var actual_hp: float = float(router.actor_snapshot("VACCINE").get("stats", {}).get("health", -1))
    check.call(actual_hp < initial, "five D hazards cause actual B-owned HP loss")
    check.call(hit_notices.size() == 5, "five B environmental hits carry separate notices")
    for notice in hit_notices:
        check.call(str(notice.get("actor_id", "")).begins_with("environment:")
            and bool(notice.get("environment_source", false))
            and str(notice.get("external_event_id", "")).length() > 0,
            "B confirms virtual non-vamping environment actor and stable D ID")
    var statuses: Array = router.actor_snapshot("VACCINE").get(
        "status_state", {}).get("statuses", [])
    check.call(statuses.size() >= 1, "real B runtime owns hazard slow/burn status")
    host.queue_free()
