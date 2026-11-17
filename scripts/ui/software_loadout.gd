extends PanelContainer
## Standalone paginated inventory, no save-file access.
const Catalog = preload("res://scripts/software/software_catalog.gd")
var page: int = 0
var page_size: int = 8
var _levels: Dictionary = {}
var _entries: Array[Dictionary] = []
var _body: VBoxContainer

func _ready() -> void:
	custom_minimum_size = Vector2(600, 400)
	_body = VBoxContainer.new()
	add_child(_body)
	_refresh()

func set_levels(levels: Dictionary) -> void:
	_levels = levels.duplicate(true)
	_entries.clear()
	for entry in Catalog.entries():
		if int(_levels.get(entry["id"], 0)) > 0:
			_entries.append(entry)
	_entries.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		var al: int = int(_levels[a["id"]])
		var bl: int = int(_levels[b["id"]])
		if al == bl:
			return str(a["id"]) < str(b["id"])
		return al > bl)
	page = 0
	if is_node_ready():
		_refresh()

func _refresh() -> void:
	if _body == null:
		return
	for child in _body.get_children():
		child.queue_free()
	var title := Label.new()
	title.text = "설치된 소프트웨어  /  %d종" % _entries.size()
	title.add_theme_font_size_override("font_size", 24)
	_body.add_child(title)
	var start: int = page * page_size
	for i in range(start, mini(start + page_size, _entries.size())):
		var item: Dictionary = _entries[i]
		var line := Label.new()
		line.text = "%s  Lv.%d/%d  ·  %s" % [item["name"], int(_levels[item["id"]]), int(item["max_stack"]), Catalog.description_for_display(str(item["id"]))]
		line.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		_body.add_child(line)
	var pages: int = maxi(1, ceili(float(_entries.size()) / float(page_size)))
	var nav := HBoxContainer.new()
	_body.add_child(nav)
	var prev := Button.new()
	prev.text = "◀"
	prev.disabled = page == 0
	prev.pressed.connect(_move_page.bind(-1))
	nav.add_child(prev)
	var counter := Label.new()
	counter.text = "%d / %d" % [page + 1, pages]
	nav.add_child(counter)
	var next := Button.new()
	next.text = "▶"
	next.disabled = page >= pages - 1
	next.pressed.connect(_move_page.bind(1))
	nav.add_child(next)

func _move_page(direction: int) -> void:
	page = clampi(page + direction, 0, maxi(0, ceili(float(_entries.size()) / float(page_size)) - 1))
	_refresh()
