extends CharacterBody3D
## D-owned standalone playtest motor only. A connects production input/B dash.
## Physics world units/second. Does not author combat, skill or save rules.
@export var walk_speed: float = 5.0
@export var movement_enabled: bool = true

func _physics_process(_delta: float) -> void:
    if not movement_enabled:
        velocity = Vector3.ZERO
        move_and_slide()
        return
    var dir := Vector2.ZERO
    if Input.is_key_pressed(KEY_A) or Input.is_key_pressed(KEY_LEFT):
        dir.x -= 1.0
    if Input.is_key_pressed(KEY_D) or Input.is_key_pressed(KEY_RIGHT):
        dir.x += 1.0
    if Input.is_key_pressed(KEY_W) or Input.is_key_pressed(KEY_UP):
        dir.y -= 1.0
    if Input.is_key_pressed(KEY_S) or Input.is_key_pressed(KEY_DOWN):
        dir.y += 1.0
    dir = dir.normalized()
    velocity = Vector3(dir.x * walk_speed, 0.0, dir.y * walk_speed)
    move_and_slide()
