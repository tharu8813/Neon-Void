extends RefCounted
## Rework-only metadata loader. No legacy IDs or persistence side effects.
const CATALOG_PATH := "res://data/rework/plugins/catalog.v1.json"
const ODDS_PATH := "res://data/rework/plugins/odds.proposal.v1.json"
const RARITIES := ["common", "rare", "legendary", "mythic"]

static func entries() -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	var raw: Variant = JSON.parse_string(FileAccess.get_file_as_string(CATALOG_PATH))
	if not (raw is Dictionary) or not (raw.get("entries", null) is Array):
		push_error("Invalid rework plugin catalog JSON")
		return result
	for item in raw["entries"]:
		if item is Dictionary:
			result.append(item.duplicate(true))
	return result

static func odds_settings() -> Dictionary:
	var raw: Variant = JSON.parse_string(FileAccess.get_file_as_string(ODDS_PATH))
	if not (raw is Dictionary):
		push_error("Invalid rework plugin odds JSON")
		return {}
	return raw.duplicate(true)

static func find_by_id(plugin_id: String, source: Array[Dictionary]) -> Dictionary:
	for item in source:
		if str(item.get("plugin_id", "")) == plugin_id:
			return item.duplicate(true)
	return {}

static func is_learnable(entry: Dictionary) -> bool:
	return str(entry.get("rarity", "")) in ["common", "rare"] and bool(entry.get("learnable", false))

static func validate(source: Array[Dictionary]) -> Array[String]:
	var errors: Array[String] = []
	var seen: Dictionary = {}
	for entry in source:
		var uid: String = str(entry.get("plugin_id", ""))
		if uid.is_empty() or seen.has(uid):
			errors.append("missing or duplicate plugin_id: " + uid)
			continue
		seen[uid] = true
		var rarity: String = str(entry.get("rarity", ""))
		if not RARITIES.has(rarity):
			errors.append(uid + ": unsupported rarity")
		if not entry.has("name_ko") or not entry.has("description_ko") or not entry.has("acquisition_condition"):
			errors.append(uid + ": missing identity or acquisition metadata")
		var max_level: int = int(entry.get("max_level", 0))
		if max_level < 1:
			errors.append(uid + ": max_level must be positive")
		var effects: Variant = entry.get("level_effects", [])
		if not (effects is Array) or effects.size() != max_level:
			errors.append(uid + ": incomplete level_effects")
		else:
			for index in range(effects.size()):
				if not (effects[index] is Dictionary) or int(effects[index].get("level", -1)) != index + 1:
					errors.append(uid + ": invalid level_effect at " + str(index + 1))
		if not (entry.get("trigger", null) is Dictionary) or not (entry.get("proc_permissions", null) is Dictionary):
			errors.append(uid + ": missing combat interface descriptors")
		if bool(entry.get("learnable", false)) != (rarity in ["common", "rare"]):
			errors.append(uid + ": learnability must follow GDD rarity restriction")
	return errors
