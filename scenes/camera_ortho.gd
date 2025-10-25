extends Camera3D
## Needs to have inputs for
## move_forward, move_backward, move_up, move_down
## move_left, move_right, move_speed,
## toggle_mouse_capture, toggle_mouse_capture

const MOUSE_SENSITIVITY = 0.002

# The camera movement speed (tweakable using the mouse wheel).
@export var move_speed := 1.5

# Stores where the camera is wanting to go (based on pressed keys and speed modifier).
var motion := Vector3()

# Stores the effective camera velocity.
var velocity := Vector3()

@export var is_active := true
@export var start_enabled := false

func _ready() -> void:
	if start_enabled:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

func _input(event: InputEvent) -> void:
	if not is_active: return
	# Toggle mouse capture (only while the menu is not visible).
	if event is InputEventKey:
		if event.keycode == KEY_TAB and event.pressed:
			Input.mouse_mode = Input.MOUSE_MODE_VISIBLE if Input.mouse_mode == Input.MOUSE_MODE_CAPTURED else Input.MOUSE_MODE_CAPTURED


func _process(delta: float) -> void:
	if not is_active: return

	if Input.is_key_pressed(KEY_Q):
		size -= 0.1
		if Input.is_key_pressed(KEY_SHIFT):
			size -= 0.3
	elif Input.is_key_pressed(KEY_E):
		size += 0.1
		if Input.is_key_pressed(KEY_SHIFT):
			size += 0.3

	if Input.is_key_pressed(KEY_A):
		motion.z = -1
	elif Input.is_key_pressed(KEY_D):
		motion.z = 1
	else:
		motion.z = 0

	if Input.is_key_pressed(KEY_W):
		motion.y = 1
	elif Input.is_key_pressed(KEY_S):
		motion.y = -1
	else:
		motion.y = 0

	# Normalize motion
	# (prevents diagonal movement from being `sqrt(2)` times faster than straight movement).
	motion = motion.normalized()

	# Speed modifier.
	if Input.is_key_pressed(KEY_SHIFT):
		motion *= 2

	# Add motion, apply friction and velocity.
	velocity += motion * move_speed
	velocity *= 0.9
	var unscaled_delta = delta if (Engine.time_scale == 0) else (delta / Engine.time_scale);
	position += velocity * unscaled_delta
