extends Camera3D

@export var min_distance := 4.0
@export var max_distance := 8.0
@export var angle_v_adjust := 15.0
@export var height := 3.0

func _input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		top_level = false
		get_parent().rotate_y(-event.relative.x * 0.001)
		top_level = true

func _physics_process(_delta: float) -> void:
	var target: Vector3 = get_parent().get_parent().global_position

	var from_target := global_position - target

	## Check ranges.
	if from_target.length() < min_distance:
		from_target = from_target.normalized() * min_distance
	elif from_target.length() > max_distance:
		from_target = from_target.normalized() * max_distance

	from_target.y = height

	global_position = target + from_target

	var look_direction := global_position.direction_to(target)
	if not look_direction.is_equal_approx(Vector3.UP) and not look_direction.is_equal_approx(-Vector3.UP):
		look_at_from_position(global_position, target, Vector3.UP)
