extends RefCounted
## Deterministic, non-persisted active-skill draft.
##
## E stores only the chosen skill ID and receipt. The 3-card draft itself is
## derived from durable data (mission seed + region + already learned skills),
## so an interrupted mission can never desync a volatile offer table and no new
## E schema is required. GDD leaves the skill pool itself [미확정], therefore the
## candidate list must never be treated as an approved final balance table.
const Registry = preload("res://scripts/rework/skills/skill_registry.gd")

static func _seed_for(seed_value: int, region_id: String) -> int:
    ## Explicit portable character mixing, not String.hash().
    var result: int = posmod(seed_value, 2147483646) + 1
    for code in ("skill-draft|" + region_id).to_utf8_buffer():
        result = (result * 131 + int(code)) % 2147483647
    return maxi(1, result)

static func candidates(seed_value: int, region_id: String, known: Variant,
        count: int = 3) -> Array[Dictionary]:
    var learned: Dictionary = {}
    if known is Array:
        for uid in known:
            if uid is String:
                learned[str(uid)] = true
    var pool: Array[String] = []
    for skill_id in Registry.ids():
        if not learned.has(skill_id):
            pool.append(skill_id)
    if pool.is_empty():
        return []
    var rng := RandomNumberGenerator.new()
    rng.seed = _seed_for(seed_value, region_id)
    # Fisher-Yates keeps every candidate at most once and is stable for a
    # given durable seed/region/known-set combination.
    for index in range(pool.size() - 1, 0, -1):
        var other: int = rng.randi_range(0, index)
        var swap: String = pool[index]
        pool[index] = pool[other]
        pool[other] = swap
    var result: Array[Dictionary] = []
    var take: int = mini(maxi(1, count), pool.size())
    for index in range(take):
        var definition: Dictionary = Registry.definition(pool[index])
        if definition.is_empty():
            continue
        result.append(definition)
    return result
