extends RefCounted
## Immutable catalog adapter for original 30 modules.
const PATH := "res://scripts/software/catalog.json"
const FOCUS_MULTIPLIER := 1.8
const FOCUS := {
	"CPU": ["rapid", "power", "dash2", "dashb", "cool"],
	"MEMORY": ["boom", "chain", "split", "focus", "burn"],
	"STORAGE": ["breach", "pierce", "armor", "ricochet", "regen"],
	"NETWORK": ["homing", "drone", "rear", "sustain", "mana"],
}
const BOSS_PRIORITY := {
	"CPU": ["dashb", "rapid", "power"],
	"MEMORY": ["boom", "chain", "split"],
	"STORAGE": ["breach", "pierce", "armor"],
	"NETWORK": ["homing", "drone", "rear"],
}
static var _cache: Array[Dictionary] = []

static func entries() -> Array[Dictionary]:
	if _cache.is_empty():
		var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(PATH))
		assert(parsed is Dictionary and parsed.has("entries"))
		for item in parsed["entries"]:
			_cache.append(item)
	return _cache.duplicate(true)

static func find(uid: String) -> Dictionary:
	for entry in entries():
		if entry["id"] == uid:
			return entry
	return {}

static func weight(uid: String, sector_id: String = "") -> float:
	var entry: Dictionary = find(uid)
	if entry.is_empty():
		return 0.0
	var result: float = float(entry["weight"])
	var sector: String = sector_id.to_upper()
	if FOCUS.has(sector) and uid in FOCUS[sector]:
		result *= FOCUS_MULTIPLIER
	return result

static func boss_signature(sector_id: String, levels: Dictionary) -> String:
	var sector: String = sector_id.to_upper()
	if not BOSS_PRIORITY.has(sector):
		return ""
	for uid in BOSS_PRIORITY[sector]:
		var item: Dictionary = find(uid)
		if int(levels.get(uid, 0)) < int(item["max_stack"]):
			return uid
	return ""
