extends RefCounted
## P3 <-> P4 adapter. No dependency on P3 file paths or common main scene.
## Call AFTER P3 player.simulate_tick() and BEFORE CombatSim.step_fixed().
static func read_player(combat: RefCounted, actor: Node) -> void:
    var state: Dictionary = actor.call("combat_snapshot")
    combat.external_invulnerability_clock = true
    combat.set_player_snapshot({
        "pos": state["logical_position"],
        "aim": state["aim_radians"],
        "hp": state["hp"], "max_hp": state["max_hp"],
        "mana": state["mana"], "max_mana": state["max_mana"],
        "radius": state["radius_px"],
        "inv": state["invulnerability_t"]
    })

static func write_player(combat: RefCounted, actor: Node) -> void:
    ## Combat owns damage/healing, skill mana and collision invulnerability.
    ## P3 owns x/y, dash and clock. Do not reassign player position here.
    var result: Dictionary = combat.get_player_snapshot()
    var hp_change: float = float(result["hp"]) - float(actor.get("hp"))
    if absf(hp_change) > 0.000001:
        actor.call("change_hp", hp_change)
    actor.set("mana", clampf(float(result["mana"]), 0.0, float(actor.get("max_mana"))))
    actor.set("invulnerability_t", maxf(float(actor.get("invulnerability_t")), float(result["inv"])))

static func try_cast(combat: RefCounted, actor: Node, skill: String) -> bool:
    read_player(combat, actor)
    var cast: bool = combat.try_skill(skill)
    if cast:
        write_player(combat, actor)
    return cast
