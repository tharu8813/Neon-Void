extends SceneTree
## Headless model, reward and inventory smoke. Does not assert DPI or visual parity.
const System = preload("res://scripts/software/software_system.gd")
const Catalog = preload("res://scripts/software/software_catalog.gd")
const Preview = preload("res://scripts/ui/software_preview.gd")

func _initialize() -> void:
	call_deferred("_run")

func _fail(message: String) -> void:
	printerr("P6 FAIL: " + message)
	quit(1)

func _run() -> void:
	var all: Array[Dictionary] = Catalog.entries()
	if all.size() != 30:
		_fail("expected 30 catalog entries")
		return
	var ids: Dictionary = {}
	for item in all:
		var uid: String = str(item["id"])
		if ids.has(uid) or int(item["max_stack"]) < 1:
			_fail("duplicate / invalid " + uid)
			return
		ids[uid] = true
	var system = System.new()
	if not system.apply_software("power") or not is_equal_approx(float(system.stats["dmg"]), 14.0):
		_fail("power damage")
		return
	if not system.apply_software("rapid") or not is_equal_approx(float(system.stats["rate"]), 5.4 * 1.18):
		_fail("rate multiplier")
		return
	system.reset()
	for i in range(3):
		system.apply_software("dash2")
	if system.apply_software("dash2") or int(system.stats["dash_max"]) != 4 or system.dash_charges != 4:
		_fail("dash max stacks")
		return
	system.reset()
	for i in range(3):
		system.apply_software("burn")
		system.apply_software("toxin")
	if not is_equal_approx(float(system.stats["burn_chance"]), 0.54) or not is_equal_approx(float(system.stats["toxin_chance"]), 0.48):
		_fail("DoT stacking")
		return
	system.reset()
	var rng := RandomNumberGenerator.new()
	rng.seed = 42
	var offers: Array[String] = system.roll_offers(3, "CPU", rng)
	if offers.size() != 3 or offers[0] == offers[1] or offers[0] == offers[2] or offers[1] == offers[2]:
		_fail("3 unique cards")
		return
	if system.claim_offer(1) != offers[1] or not system.pending_offers.is_empty() or system.claim_offer(0) != "":
		_fail("single claim")
		return
	system.reset()
	if system.boss_signature("CPU") != "dashb" or system.award_boss_signature("CPU") != "dashb" or system.award_boss_signature("CPU") != "":
		_fail("boss reward once")
		return
	var maxed: Dictionary = {}
	for item in all:
		maxed[item["id"]] = int(item["max_stack"])
	if not system.restore_levels(maxed):
		_fail("valid restored stacks")
		return
	var fill: Array[String] = system.roll_offers(3, "MEMORY", rng)
	if fill != ["bonus", "bonus", "bonus"] or system.claim_offer(0) != "bonus":
		_fail("all capped -> bonus")
		return
	if system.restore_levels({"power": 99}):
		_fail("invalid snapshot accepted")
		return
	if not is_equal_approx(System.mitigated_damage(100.0, 100.0), 50.0):
		_fail("armor parity")
		return
	# P1 stat previews must match real effects and leave live software immutable.
	var model: RefCounted = System.new()
	var before: Dictionary = model.stats.duplicate(true)
	var add_preview: Array[String] = Preview.effect_lines("power", model.stats, model.maxhp, model.maxmana)
	var mult_preview: Array[String] = Preview.effect_lines("rapid", model.stats, model.maxhp, model.maxmana)
	var cap_preview: Array[String] = Preview.effect_lines("burn", {
		"burn_chance": 0.70, "burn_dps": 5.0}, model.maxhp, model.maxmana)
	var heal_preview: Array[String] = Preview.effect_lines("vamp", model.stats, model.maxhp, model.maxmana)
	if add_preview.is_empty() or not add_preview[0].contains("10.0 → 14.0"):
		_fail("P1 additive software forecast")
		return
	if mult_preview.is_empty() or not mult_preview[0].contains("5.4/s → 6.4/s"):
		_fail("P1 multiplicative software forecast")
		return
	if cap_preview.size() != 2 or not cap_preview[0].contains("70% → 75%"):
		_fail("P1 capped percent forecast")
		return
	if heal_preview.is_empty() or not heal_preview[0].contains("0.0 → 0.3"):
		_fail("P1 effective on-kill healing forecast")
		return
	if before != model.stats or not model.levels.is_empty():
		_fail("P1 read-only software preview mutated game stats")
		return
	if not Preview.owned_complement("power", {"rapid": 2}).contains("Lv.2"):
		_fail("P1 actual installed module complement")
		return
	if Preview.owned_complement("power", {}).length() != 0:
		_fail("P1 should never claim a synergy without its module")
		return
	var offer_scene: PackedScene = load("res://scenes/ui/software_offer.tscn")
	var panel: Control = offer_scene.instantiate() as Control
	root.add_child(panel)
	var picked: Array[int] = []
	panel.card_selected.connect(func(index: int) -> void: picked.append(index))
	var sample_offers: Array[String] = ["rapid", "power", "bonus"]
	panel.set_offers(sample_offers, {"rapid": 2}, {
		"rate": 7.0, "dmg": 16.0}, 126.0, 100.0)
	if panel._cards.size() != 3 or not panel._cards[1].text.contains("16.0 → 20.0") \
			or not panel._cards[1].text.contains("과열 방출 Lv.2") \
			or not panel._cards[2].text.contains("점수 +250"):
		_fail("P1 reward cards display authoritative stat delta, combo and bonus")
		return
	panel._on_pick(2)
	if not panel.visible or picked != [2]:
		_fail("reward UI event")
		return
	var loadout: Control = load("res://scenes/ui/software_loadout.tscn").instantiate() as Control
	root.add_child(loadout)
	loadout.set_levels({"power": 2})
	if loadout.get_child_count() == 0:
		_fail("inventory screen")
		return
	print("P6 SOFTWARE SMOKE PASS")
	quit(0)
