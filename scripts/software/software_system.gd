extends RefCounted
## Standalone software + numbers. Combat and persistence owners integrate via signals.
signal software_acquired(id: String, level: int)
signal bonus_claimed(points: int)
signal offers_changed(ids: Array)

const Catalog = preload("res://scripts/software/software_catalog.gd")
const DEFAULT_STATS := {
	"speed": 322.0, "rate": 5.4, "dmg": 10.0, "bspeed": 880.0,
	"pierce": 0, "split": 0, "homing": 0.0, "explode": 0.0,
	"ricochet": 0, "crit": 0.05, "lifesteal": 0.0, "thorns": 0.0,
	"dash_max": 1, "dash_blast": 0.0, "slow": 0.0, "magnet": 82.0,
	"drone": 0, "chain": 0, "rear": 0, "armor": 4.0,
	"armor_pen": 0.0, "mana_regen": 6.0, "hp_regen": 0.0,
	"burn_chance": 0.0, "burn_dps": 0.0, "toxin_chance": 0.0,
	"toxin_dps": 0.0, "life_on_hit": 0.0, "skill_power": 1.0,
	"skill_cooldown": 1.0, "skill_efficiency": 1.0,
	"effect_duration": 1.0,
}
var levels: Dictionary = {}
var stats: Dictionary = {}
var maxhp: float = 100.0
var hp: float = 100.0
var maxmana: float = 100.0
var mana: float = 100.0
var dash_charges: int = 1
var pending_offers: Array[String] = []
var claimed_bosses: Dictionary = {}

func _init() -> void:
	reset()

func catalog_entries() -> Array[Dictionary]:
	return Catalog.entries()

func reset(base: Dictionary = {}) -> void:
	stats = DEFAULT_STATS.duplicate(true)
	levels.clear()
	pending_offers.clear()
	claimed_bosses.clear()
	for key in base.get("stats", {}):
		if stats.has(key):
			stats[key] = base["stats"][key]
	maxhp = float(base.get("maxhp", 100.0))
	maxmana = float(base.get("maxmana", 100.0))
	hp = float(base.get("hp", maxhp))
	mana = float(base.get("mana", maxmana))
	dash_charges = int(base.get("dash_charges", 1))

func apply_software(uid: String) -> bool:
	var item: Dictionary = Catalog.find(uid)
	if item.is_empty() or int(levels.get(uid, 0)) >= int(item["max_stack"]):
		return false
	levels[uid] = int(levels.get(uid, 0)) + 1
	for effect in item["effects"]:
		var key: String = str(effect["stat"])
		var op: String = str(effect["op"])
		var val: float = float(effect["value"])
		if key == "maxhp":
			maxhp += val
			hp = minf(maxhp, hp + val)
		elif key == "maxmana":
			maxmana += val
			mana = minf(maxmana, mana + val)
		else:
			var previous: float = float(stats.get(key, 0.0))
			match op:
				"add":
					stats[key] = previous + val
				"mul":
					stats[key] = previous * val
				"cap_add":
					stats[key] = minf(float(effect["cap"]), previous + val)
				_:
					push_error("Unknown software operation: " + op)
		if key == "dash_max":
			dash_charges += int(val)
	software_acquired.emit(uid, int(levels[uid]))
	return true

func restore_levels(saved: Dictionary, base: Dictionary = {}) -> bool:
	## Validate before mutating in-memory state.
	for key in saved:
		var item: Dictionary = Catalog.find(str(key))
		var count: Variant = saved[key]
		if item.is_empty() or typeof(count) != TYPE_INT or count < 0 or count > int(item["max_stack"]):
			return false
	reset(base)
	var ids: Array = saved.keys()
	ids.sort() # Mirrors legacy checkpoint replay order.
	for key in ids:
		for i in range(int(saved[key])):
			apply_software(str(key))
	return true

func roll_offers(count: int, sector_id: String, rng: RandomNumberGenerator) -> Array[String]:
	var pool: Array[Dictionary] = []
	for item in Catalog.entries():
		if int(levels.get(item["id"], 0)) < int(item["max_stack"]):
			pool.append({"id": item["id"], "weight": Catalog.weight(item["id"], sector_id)})
	var choices: Array[String] = []
	while not pool.is_empty() and choices.size() < count:
		var total: float = 0.0
		for candidate in pool:
			total += float(candidate["weight"])
		var roll: float = rng.randf() * total
		var cumulative: float = 0.0
		var selected: int = pool.size() - 1
		for i in range(pool.size()):
			cumulative += float(pool[i]["weight"])
			if roll <= cumulative:
				selected = i
				break
		choices.append(str(pool[selected]["id"]))
		pool.remove_at(selected)
	while choices.size() < count:
		choices.append("bonus")
	pending_offers = choices.duplicate()
	offers_changed.emit(choices)
	return choices

func set_pending_offers(ids: Array[String]) -> bool:
	if ids.size() != 3:
		return false
	for uid in ids:
		if uid != "bonus":
			var entry: Dictionary = Catalog.find(uid)
			if entry.is_empty() or int(levels.get(uid, 0)) >= int(entry["max_stack"]):
				return false
	pending_offers = ids.duplicate()
	offers_changed.emit(pending_offers)
	return true

func claim_offer(index: int) -> String:
	if index < 0 or index >= pending_offers.size():
		return ""
	var uid: String = pending_offers[index]
	if uid == "bonus":
		bonus_claimed.emit(250)
	elif not apply_software(uid):
		return ""
	pending_offers.clear()
	offers_changed.emit(pending_offers)
	return uid

func boss_signature(sector_id: String) -> String:
	return Catalog.boss_signature(sector_id, levels)

func award_boss_signature(sector_id: String) -> String:
	## Call only on first confirmed sector clear; caller persists clean sectors.
	var sid: String = sector_id.to_upper()
	if claimed_bosses.has(sid):
		return ""
	var uid: String = boss_signature(sid)
	claimed_bosses[sid] = true
	if not uid.is_empty():
		apply_software(uid)
	return uid

func restore_boss_claims(clean_sectors: Array[String]) -> void:
	for sid in clean_sectors:
		claimed_bosses[sid.to_upper()] = true

func snapshot() -> Dictionary:
	return {
		"levels": levels.duplicate(true), "stats": stats.duplicate(true),
		"hp": hp, "maxhp": maxhp, "mana": mana, "maxmana": maxmana,
		"dash_charges": dash_charges, "pending_offers": pending_offers.duplicate(),
	}

static func mitigated_damage(raw: float, armor: float, penetration: float = 0.0) -> float:
	if raw <= 0.0:
		return 0.0
	return raw * 100.0 / (100.0 + maxf(0.0, armor - maxf(0.0, penetration)))

static func skill_mana_cost(base: float, efficiency: float) -> float:
	return roundf(maxf(1.0, base * maxf(0.45, efficiency)) * 10.0) / 10.0

static func skill_cooldown(base: float, factor: float) -> float:
	return maxf(0.5, base * maxf(0.5, factor))
