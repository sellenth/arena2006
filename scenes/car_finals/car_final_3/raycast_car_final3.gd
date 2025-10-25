extends RigidBody3D

@export var wheels: Array[RaycastWheelFinal3]
@export var acceleration := 600.0
@export var max_speed := 20.0
@export var accel_curve : Curve
@export var tire_turn_speed := 2.0
@export var tire_max_turn_degrees := 25

@export var skid_marks: Array[GPUParticles3D]

var motor_input := 0
var hand_break := false
var is_slipping := false


func _ready() -> void:
	for ray in wheels:
		ray.target_position.y = -(ray.rest_dist + ray.wheel_radius + ray.over_extend)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("handbreak"):
		hand_break = true
		is_slipping = true
	elif event.is_action_released("handbreak"):
		hand_break = false

	if event.is_action_pressed("accelerate"):
		motor_input = 1
	elif event.is_action_released("accelerate"):
		motor_input = 0

	if event.is_action_pressed("decelerate"):
		motor_input = -1
	elif event.is_action_released("decelerate"):
		motor_input = 0


func _basic_steering_rotation(delta: float) -> void:
	var turn_input := Input.get_axis("turn_right", "turn_left") * tire_turn_speed

	if turn_input:
		$WheelFL.rotation.y = clampf($WheelFL.rotation.y + turn_input * delta,
			deg_to_rad(-tire_max_turn_degrees), deg_to_rad(tire_max_turn_degrees))
		$WheelFR.rotation.y = clampf($WheelFR.rotation.y + turn_input * delta,
			deg_to_rad(-tire_max_turn_degrees), deg_to_rad(tire_max_turn_degrees))
	else:
		$WheelFL.rotation.y = move_toward($WheelFL.rotation.y, 0, tire_turn_speed * delta)
		$WheelFR.rotation.y = move_toward($WheelFR.rotation.y, 0, tire_turn_speed * delta)


func _physics_process(_delta: float) -> void:
	DebugDraw.draw_arrow_ray(global_position, linear_velocity, 2.5, 0.5, Color.YELLOW)
	_basic_steering_rotation(_delta)
	var grounded := false
	var id := 0
	for wheel in wheels:
		if wheel.is_colliding():
			grounded = true
		wheel.force_raycast_update()
		_do_single_wheel_suspension(wheel)
		_do_single_wheel_acceleration(wheel)
		_do_single_wheel_traction(wheel, id)
		id += 1

	if grounded:
		center_of_mass = Vector3.ZERO
	else:
		center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
		center_of_mass = Vector3.DOWN*0.5

func _get_point_velocity(point: Vector3) -> Vector3:
	return linear_velocity + angular_velocity.cross(point - global_position)


func _do_single_wheel_traction(ray: RaycastWheelFinal3, idx: int) -> void:
	if not ray.is_colliding(): return

	var steer_side_dir := ray.global_basis.x
	var tire_vel := _get_point_velocity(ray.wheel.global_position)
	var steering_x_vel := steer_side_dir.dot(tire_vel)

	var grip_factor := absf(steering_x_vel/tire_vel.length())
	var x_traction := ray.grip_curve.sample_baked(grip_factor)

	# Skid marks
	skid_marks[idx].global_position = ray.get_collision_point() + Vector3.UP * 0.01
	skid_marks[idx].look_at(skid_marks[idx].global_position + global_basis.z)

	if not hand_break and grip_factor < 0.2:
		is_slipping = false
		skid_marks[idx].emitting = false

	if hand_break:
		x_traction = 0.01
		if not skid_marks[idx].emitting:
			skid_marks[idx].emitting = true
	elif is_slipping:
		x_traction = 0.1

	var gravity: float = ProjectSettings.get_setting("physics/3d/default_gravity")
	var x_force := -steer_side_dir * steering_x_vel * x_traction * ((mass*gravity)/4.0)

	# z force traction
	var f_vel := -ray.global_basis.z.dot(tire_vel)
	var z_traction := 0.05
	var z_force := global_basis.z * f_vel * z_traction * ((mass*gravity)/4.0)

	var force_pos := ray.wheel.global_position - global_position
	apply_force(x_force, force_pos)
	apply_force(z_force, force_pos)
	DebugDraw.draw_arrow_ray(ray.wheel.global_position, x_force/mass, 2.5, 0.5, Color.GREEN)
	DebugDraw.draw_arrow_ray(ray.wheel.global_position, z_force/mass, 1.0, 0.2, Color.PURPLE)


func _do_single_wheel_acceleration(ray: RaycastWheelFinal3) -> void:
	var forward_dir := -ray.global_basis.z
	var vel := forward_dir.dot(linear_velocity)
	ray.wheel.rotate_x((-vel * get_process_delta_time())/ray.wheel_radius)

	if ray.is_colliding():
		var contact := ray.wheel.global_position
		var force_pos := contact - global_position

		if ray.is_motor and motor_input:
			var speed_ratio := vel / max_speed
			var ac := accel_curve.sample_baked(speed_ratio)
			var force_vector := forward_dir * acceleration * motor_input * ac
			#var projected_vector: Vector3 = (force_vector - ray.get_collision_normal() * force_vector.dot(ray.get_collision_normal()))
			apply_force(force_vector, force_pos)
			DebugDraw.draw_arrow_ray(contact, force_vector/mass, 2.5, 0.5, Color.RED)


func _do_single_wheel_suspension(ray: RaycastWheelFinal3) -> void:
	if ray.is_colliding():
		ray.target_position.y = -(ray.rest_dist + ray.wheel_radius + ray.over_extend)
		var contact := ray.get_collision_point()
		var spring_up_dir := ray.global_transform.basis.y
		var spring_len := ray.global_position.distance_to(contact) - ray.wheel_radius
		var offset := ray.rest_dist - spring_len

		ray.wheel.position.y = -spring_len

		var spring_force := ray.spring_strength * offset

		# damping force = damping * relative velocity
		var world_vel := _get_point_velocity(contact)
		var relative_vel := spring_up_dir.dot(world_vel)
		var spring_damp_force := ray.spring_damping * relative_vel

		var force_vector := (spring_force - spring_damp_force) * ray.get_collision_normal()

		contact = ray.wheel.global_position
		var force_pos_offset := contact - global_position
		apply_force(force_vector, force_pos_offset)

		DebugDraw.draw_arrow_ray(contact, force_vector/mass, 2.5)
		#DebugDraw.draw_sphere(ray.get_collision_point(), 0.5)
