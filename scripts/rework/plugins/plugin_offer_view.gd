extends RefCounted
## Data-only view model. A owns actual Godot Control nodes, animation, input.
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Effect = preload("res://scripts/rework/plugins/plugin_effect_runtime.gd")
const RARITY_STYLE := {
	"common":{"tone":"neutral","color":"#94A3B8","vfx":"none"},
	"rare":{"tone":"blue","color":"#4EA8FF","vfx":"edge_pulse"},
	"legendary":{"tone":"gold","color":"#F4C15D","vfx":"gold_shimmer"},
	"mythic":{"tone":"magenta","color":"#E474FF","vfx":"prismatic_glow"}
}

static func reward_screen(offers: RefCounted) -> Dictionary:
	var snapshot: Dictionary = offers.offer_snapshot()
	if not bool(snapshot.get("active",false)):
		return {"active":false,"cards":[],"rerolls_remaining":int(snapshot.get("rerolls_remaining",0))}
	var cards: Array[Dictionary] = []
	var effects = Effect.new()
	for card in snapshot.get("cards",[]):
		var uid: String = str(card.get("plugin_id",""))
		var slot: int = int(card.get("slot",-1))
		if uid.is_empty():
			cards.append({"slot":slot,"empty":true,"title":"선택 가능한 플러그인 없음","can_reroll":false})
			continue
		var entry: Dictionary = offers.get_entry(uid)
		var level: int = int(offers.levels.get(uid,0))
		var cap: int = int(entry.get("max_level",1))
		var rarity: String = str(entry.get("rarity","common"))
		var effects_by_level: Array = entry.get("level_effects",[])
		var next_text: String = ""
		if level < effects_by_level.size():
			next_text = str(effects_by_level[level].get("summary_ko",""))
		cards.append({
			"slot":slot,"empty":false,"plugin_id":uid,"name":str(entry.get("name_ko","")),
			"description":str(entry.get("description_ko","")),
			"rarity":rarity,"rarity_presentation":RARITY_STYLE.get(rarity,{}).duplicate(true),
			"current_level":level,"next_level":level+1,"max_level":cap,
			"next_level_effect":next_text,"runtime_effect":effects.effect_definition(uid),
			"learnable":Catalog.is_learnable(entry),"can_reroll":not snapshot.get("reconciliation_required",false) and snapshot.get("prepared",{}).is_empty() and int(snapshot.get("rerolls_remaining",0))>0
		})
	return {"active":true,"offer_id":snapshot.get("offer_id",""),
		"cards":cards,"rerolls_remaining":int(snapshot.get("rerolls_remaining",0)),
		"reconciliation_required":bool(snapshot.get("reconciliation_required",false)),
		"selection_pending_save":not snapshot.get("prepared",{}).is_empty(),
		"can_choose":snapshot.get("prepared",{}).is_empty() and not bool(snapshot.get("reconciliation_required",false))}
