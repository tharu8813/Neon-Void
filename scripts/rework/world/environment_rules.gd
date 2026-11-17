extends RefCounted
## D's deterministic environment *descriptors*. B/C implement live combat/plugin effects.
## Tunable numbers below are [draft v0.1]; no effect is silently applied to saves.
const VALID := ["normal", "mutation", "stack_overflow", "tunnel_infection", "spread", "initial"]

static func describe(region_plan: Dictionary, carried_plugins: Array, seed: int) -> Dictionary:
    var environment: Dictionary = region_plan.get("environment", {})
    var effect_id: String = str(environment.get("id", "normal"))
    if not VALID.has(effect_id):
        return {}
    var stage: int = clampi(int(environment.get("mutation_stage", 0)), 1, 5) if effect_id == "mutation" else 0
    var description: Dictionary = {
        "schema": 1, "environment_id": effect_id, "mutation_stage": stage,
        "enemy_spawn_multiplier": 1.0, "mutant_probability": 0.0,
        "mutant_stat_multiplier": 1.0, "mutant_plugin_slots": 0,
        "tunnel_plugin_mode": "none", "affected_plugin_ids": [],
        "map_scale": "standard"
    }
    match effect_id:
        "mutation":
            description["mutant_probability"] = minf(0.45, 0.05 + float(stage) * 0.075)
            description["mutant_stat_multiplier"] = 1.0 + float(stage) * 0.07
            description["mutant_plugin_slots"] = 1 if stage < 4 else 2
        "stack_overflow":
            description["enemy_spawn_multiplier"] = 1.6
        "tunnel_infection":
            # Fail closed: unknown learning status is NOT eligible for corruption.
            var eligible: Array[String] = []
            for plugin in carried_plugins:
                if not plugin is Dictionary:
                    continue
                if not plugin.has("learned") or bool(plugin["learned"]):
                    continue
                var id: String = str(plugin.get("plugin_id", ""))
                if not id.is_empty() and not eligible.has(id):
                    eligible.append(id)
            var rng := RandomNumberGenerator.new()
            rng.seed = seed
            for i in range(eligible.size() - 1, 0, -1):
                var j: int = rng.randi_range(0, i)
                var temp: String = eligible[i]
                eligible[i] = eligible[j]
                eligible[j] = temp
            var target_count: int = mini(eligible.size(), rng.randi_range(1, 2))
            var selected: Array[String] = []
            for i in range(target_count):
                selected.append(eligible[i])
            description["tunnel_plugin_mode"] = "suspend_until_exit" # [draft], C/E approval required
            description["affected_plugin_ids"] = selected
        "spread":
            description["map_scale"] = "large"
        "initial":
            description["map_scale"] = "small"
    return description

static func valid_mutant_roll(effect: Dictionary, rng: RandomNumberGenerator) -> bool:
    return rng.randf() < float(effect.get("mutant_probability", 0.0))
