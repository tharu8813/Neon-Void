extends RefCounted
## Read-only reward-card forecast. Never modifies live software, combat or saves.
const Catalog = preload("res://scripts/software/software_catalog.gd")
const Software = preload("res://scripts/software/software_system.gd")
const LABELS := {
	"rate": "연사", "dmg": "피해", "pierce": "관통", "split": "분열", "homing": "유도",
	"explode": "폭발", "crit": "치명타", "lifesteal": "처치 회복", "thorns": "반사",
	"dash_max": "대시", "dash_blast": "대시 폭발", "slow": "둔화", "magnet": "자석",
	"drone": "드론", "chain": "연쇄", "rear": "후방탄", "maxhp": "최대 체력",
	"ricochet": "도탄", "armor": "방어력", "armor_pen": "방어 관통",
	"maxmana": "최대 마나", "mana_regen": "마나 회복", "burn_chance": "화상 확률",
	"burn_dps": "화상 피해/s", "toxin_chance": "중독 확률",
	"toxin_dps": "중독 피해/s", "life_on_hit": "적중 회복",
	"skill_power": "스킬 위력", "skill_cooldown": "스킬 재사용",
	"skill_efficiency": "마나 소모", "hp_regen": "체력 회복",
	"effect_duration": "효과 지속"
}
const PERCENT_STATS := ["crit", "burn_chance", "toxin_chance", "slow", "skill_power",
	"skill_cooldown", "skill_efficiency", "effect_duration"]
const INTEGER_STATS := ["pierce", "split", "dash_max", "drone", "chain", "rear", "ricochet"]
const COMPLEMENTS := {
	"rapid": ["power", "crit", "vamp"], "power": ["rapid", "pierce", "split"],
	"crit": ["rapid", "boom", "chain"], "vamp": ["rapid", "armor", "regen"],
	"boom": ["split", "chain", "power"], "burn": ["rapid", "duration", "toxin"],
	"toxin": ["rapid", "duration", "burn"], "dash2": ["dashb", "armor"],
	"dashb": ["dash2", "cool"], "guard": ["armor"],
	"armor": ["regen", "vamp", "hull"], "breach": ["pierce", "rapid"],
	"chain": ["boom", "rapid"], "drone": ["homing", "power"],
	"mana": ["cool", "sustain"], "cool": ["mana", "focus", "duration"]
}

static func _display_number(stat: String, number: float) -> String:
	if stat == "lifesteal":
		number *= 0.5 # Legacy kill recovery is lifesteal * 0.5.
	if stat in PERCENT_STATS:
		return "%.0f%%" % (number * 100.0)
	if stat in INTEGER_STATS:
		return "%d" % int(roundf(number))
	if stat == "rate" or stat == "mana_regen" or stat == "hp_regen":
		return "%.1f/s" % number
	return "%.1f" % number

static func effect_lines(uid: String, stats: Dictionary, maxhp: float,
		maxmana: float) -> Array[String]:
	if uid == "bonus":
		return ["선택 시 점수 +250"]
	var entry: Dictionary = Catalog.find(uid)
	if entry.is_empty():
		return []
	var result: Array[String] = []
	for effect in entry["effects"]:
		var key: String = str(effect["stat"])
		var previous: float = float(stats.get(key, Software.DEFAULT_STATS.get(key, 0.0)))
		if key == "maxhp":
			previous = maxhp
		elif key == "maxmana":
			previous = maxmana
		var after: float = previous
		match str(effect["op"]):
			"add":
				after += float(effect["value"])
			"mul":
				after *= float(effect["value"])
			"cap_add":
				after = minf(float(effect["cap"]), previous + float(effect["value"]))
			_:
				continue
		result.append("%s %s → %s" % [str(LABELS.get(key, key)),
			_display_number(key, previous), _display_number(key, after)])
	return result

static func owned_complement(uid: String, levels: Dictionary) -> String:
	for candidate in COMPLEMENTS.get(uid, []):
		if int(levels.get(candidate, 0)) > 0:
			var data: Dictionary = Catalog.find(candidate)
			if not data.is_empty():
				# A build suggestion only: no hidden synergy multiplier exists.
				return "조합 참고 · 보유: %s Lv.%d" % [data["name"], int(levels[candidate])]
	return ""
