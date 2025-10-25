extends Camera3D
## Needs to have inputs for
## move_forward, move_backward, move_up, move_down
## move_left, move_right, move_speed,
## toggle_mouse_capture, toggle_mouse_capture

const MOUSE_SENSITIVITY = 0.002

# The camera movement speed (tweakable using the mouse wheel).
@export var move_speed := 1.5
@export var shift_mult := 2.5

# Stores where the camera is wanting to go (based on pressed keys and speed modifier).
var motion := Vector3()

# Stores the effective camera velocity.
var velocity := Vector3()

@export var is_active := true
@export var start_enabled := false
var mouse_pressed := false

func _ready() -> void:
	if start_enabled:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED


func _input(event: InputEvent) -> void:

	if event.is_action_pressed("toggle_wireframe"): ## X
		if get_viewport().debug_draw == Viewport.DEBUG_DRAW_WIREFRAME:
			get_viewport().debug_draw = Viewport.DEBUG_DRAW_DISABLED
		else:
			RenderingServer.set_debug_generate_wireframes(true)
			get_viewport().debug_draw = Viewport.DEBUG_DRAW_WIREFRAME
	if event.is_action_pressed("change_camera"):
		if is_active:
			is_active = false
			%OrthoCamera.is_active = true
			current = false
			%OrthoCamera.current = true
		else:
			is_active = true
			%OrthoCamera.is_active = false
			current = true
			%OrthoCamera.current = false


	if event is InputEventMouseButton and event.button_index == 3:
		if event.pressed:
			mouse_pressed = true
		else:
			mouse_pressed = false

	if mouse_pressed: return
	if not is_active: return

	# Toggle mouse capture (only while the menu is not visible).
	if event is InputEventKey:
		if event.keycode == KEY_TAB and event.pressed:
			Input.mouse_mode = Input.MOUSE_MODE_VISIBLE if Input.mouse_mode == Input.MOUSE_MODE_CAPTURED else Input.MOUSE_MODE_CAPTURED
	# Mouse look (effective only if the mouse is captured).
	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		# Horizontal mouse look.
		rotation.y -= event.relative.x * MOUSE_SENSITIVITY
		# Vertical mouse look, clamped to -90..90 degrees.
		rotation.x = clamp(rotation.x - event.relative.y * MOUSE_SENSITIVITY, deg_to_rad(-90), deg_to_rad(90))


	if Input.is_key_pressed(KEY_ESCAPE):
		get_tree().quit()


func _process(delta: float) -> void:
	if not is_active: return

	if Input.is_key_pressed(KEY_W):
		motion.z = -1
	elif Input.is_key_pressed(KEY_S):
		motion.z = 1
	else:
		motion.z = 0

	if Input.is_key_pressed(KEY_A):
		motion.x = -1
	elif Input.is_key_pressed(KEY_D):
		motion.x = 1
	else:
		motion.x = 0

	if Input.is_key_pressed(KEY_E):
		motion.y = 1
	elif Input.is_key_pressed(KEY_Q):
		motion.y = -1
	else:
		motion.y = 0

	# Normalize motion
	# (prevents diagonal movement from being `sqrt(2)` times faster than straight movement).
	motion = motion.normalized()

	# Speed modifier.
	if Input.is_key_pressed(KEY_SHIFT):
		motion *= shift_mult

	# Rotate the motion based on the camera angle.
	motion = motion \
		.rotated(Vector3(0, 1, 0), rotation.y) \
		.rotated(Vector3(1, 0, 0), cos(rotation.y) * rotation.x) \
		.rotated(Vector3(0, 0, 1), -sin(rotation.y) * rotation.x)

	# Add motion, apply friction and velocity.
	velocity += motion * move_speed
	velocity *= 0.9
	var unscaled_delta = delta if (Engine.time_scale == 0) else (delta / Engine.time_scale);
	position += velocity * unscaled_delta
