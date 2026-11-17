extends RefCounted
## Boundary adapter for C's metadata. B intentionally does not invent catalog
## effects or parse Korean level summaries as executable balance.
## C/A must provide explicit effect_commands (versioned data) to activate.
const MATCHED_EVENTS: Dictionary = {
    "projectile_hit": "on_hit",
    "damage_dealt": "on_hit",
    "skill_damage": "on_hit",
    "skill_cast": "skill_cast",
    "on_hit_proc_count": "on_hit",
    "health_threshold": "damage_taken",
    "periodic": "periodic",
    "mobility_or_reveal": "dash",
    "dash": "dash",
    "dash_or_blink": "dash",
    "kill": "kill",
    "passive_and_damage_taken": "damage_taken",
    "on_hit_proc": "on_hit",
    "on_hit_proc_or_kill": "on_hit",
    "basic_attack": "basic_attack"
}
static func requests_for(owned_plugins: Array, event: Dictionary, counters: Dictionary = {}) -> Array[Dictionary]:
    var result: Array[Dictionary] = []
    var actor: String = str(event.get("actor_id", ""))
    var tags: Array = event.get("source_tags", [])
    var trigger_kind: String = str(event.get("trigger_kind", ""))
    var event_kind: String = str(event.get("kind", ""))
    for instance in owned_plugins:
        if not (instance is Dictionary):
            continue
        var item: Dictionary = instance
        var plugin_id: String = str(item.get("plugin_id", ""))
        var trigger: Dictionary = item.get("trigger", {})
        var catalog_kind: String = str(trigger.get("event", ""))
        var expected: String = str(MATCHED_EVENTS.get(catalog_kind, ""))
        if expected != trigger_kind and not (
                catalog_kind == "on_hit_proc_or_kill" and trigger_kind == "kill"):
            continue
        if catalog_kind in ["on_hit_proc", "on_hit_proc_or_kill"] and (
                trigger_kind == "on_hit" and not tags.has("proc_damage")):
            continue
        if catalog_kind == "health_threshold":
            var threshold: float = float(trigger.get("health_below_fraction", -1.0))
            if threshold < 0.0 or float(event.get("health_fraction", 1.0)) > threshold:
                continue
        if trigger_kind == "on_hit" and event_kind != "damage":
            continue
        if trigger_kind == "on_hit" and float(event.get("actual_damage", 0.0)) <= 0.0:
            continue
        if catalog_kind == "projectile_hit" and not tags.has("projectile_hit"):
            continue
        if catalog_kind == "skill_damage" and not tags.has("skill_damage"):
            continue
        var allowed_tags: Array = trigger.get("source_tags", [])
        if not allowed_tags.is_empty():
            var found: bool = false
            for tag in allowed_tags:
                if tags.has(tag):
                    found = true
                    break
            if not found:
                continue
        if catalog_kind == "on_hit_proc_count":
            # Do not infer a count from localized description text. C supplies
            # machine-readable trigger_after_hits when this effect is executable.
            var needed: int = int(item.get("trigger_after_hits", 0))
            if needed <= 0:
                continue
            var key: String = actor + "|" + plugin_id
            var counted: int = int(counters.get(key, 0)) + 1
            counters[key] = counted if counted < needed else 0
            if counted < needed:
                continue
        var permissions: Dictionary = item.get("proc_permissions", {})
        var downstream: Array = []
        if bool(permissions.get("can_trigger_on_hit_effects", false)):
            downstream = permissions.get("allowed_proc_triggers", []).duplicate()
        var commands: Array = item.get("effect_commands", [])
        for index in range(commands.size()):
            if not (commands[index] is Dictionary):
                continue
            var command: Dictionary = commands[index]
            var action: String = str(command.get("kind", ""))
            if not ["damage", "status", "spawn_projectile", "heal", "stat_modifier"].has(action):
                continue
            var payload: Dictionary = {
                "kind": action,
                "actor_id": actor,
                "target_id": str(event.get("target_id", "")),
                "attack_id": str(event.get("attack_id", "")),
                "trigger_kind": trigger_kind,
                "source_tags": ["plugin", "proc_damage"] if action == "damage" else ["plugin"],
                "allowed_proc_triggers": downstream.duplicate(),
                "on_hit_damage_efficiency": float(
                    permissions.get("on_hit_damage_efficiency", 1.0)),
                "command": command.duplicate(true),
                "proc_damage_efficiency": float(
                    event.get("on_hit_damage_efficiency", 1.0))
            }
            var scope: String = str(item.get("cooldown_scope", "none"))
            payload["cooldown_scope"] = scope
            if scope == "per_actor" or scope == "global":
                payload["icd_scope_key"] = actor if scope == "per_actor" else "global"
            var seconds: Variant = item.get("internal_cooldown_seconds", null)
            var ticks: int = maxi(0, ceili(float(seconds) * 60.0)) if seconds != null else 0
            result.append({
                "effect_id": plugin_id + ":" + str(index),
                "event": payload, "icd_ticks": ticks
            })
    return result

static func passive_offsets(owned_plugins: Array) -> Dictionary:
    ## C supplies numeric passive_stat_modifiers; summaries are never parsed.
    var merged: Dictionary = {}
    for instance in owned_plugins:
        if not (instance is Dictionary):
            continue
        var values: Dictionary = instance.get("passive_stat_modifiers", {})
        for key in values:
            merged[key] = float(merged.get(key, 0.0)) + float(values[key])
    return merged
