extends RigidBody3D

@export var wheels: Array[TurnRaycastWheel]
@export var acceleration := 600.0
@export var deceleration := 200.0
@export var max_speed := 20.0
@export var accel_curve : Curve

var motor_input := 0

var hand_break := false
var is_slipping := false

@export var skid_marks: Array[GPUParticles3D]


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("jump"):
		hand_break = true
		is_slipping = true
	elif event.is_action_released("jump"):
		hand_break = false

	if event.is_action_pressed("accelerate"):
		motor_input = 1
	elif event.is_action_released("accelerate"):
		motor_input = 0

	if event.is_action_pressed("decelerate"):
		motor_input = -1
	elif event.is_action_released("decelerate"):
		motor_input = 0

@export var TURN_SPEED := 2.0
func _basic_steering_rotation(delta:float) -> void:
	var turn_input := Input.get_axis("turn_right", "turn_left") * TURN_SPEED
	if turn_input:
		$WheelFL.rotation.y = clampf($WheelFL.rotation.y + turn_input * delta, deg_to_rad(-25), deg_to_rad(25))
		$WheelFR.rotation.y = clampf($WheelFR.rotation.y + turn_input * delta, deg_to_rad(-25), deg_to_rad(25))
	else:
		$WheelFL.rotation.y = move_toward($WheelFL.rotation.y, 0, TURN_SPEED*delta)
		$WheelFR.rotation.y = move_toward($WheelFR.rotation.y, 0, TURN_SPEED*delta)

		#print($WheelFL.rotation.y)

var wheelbase := 2.6
var turn_radius := 10.8
var rear_track := 2.2
var ackerman_angle_left := 0.0
var ackerman_angle_right := 0.0
func _ackerman_steering_rotation(delta:float) -> void:
	var turn_input := Input.get_axis("turn_right", "turn_left")
	if turn_input > 0.1:
		ackerman_angle_left = atan(wheelbase / (turn_radius + rear_track / 2.0) ) * turn_input
		ackerman_angle_right = atan(wheelbase / (turn_radius - rear_track / 2.0) ) * turn_input
	elif turn_input < -0.1:
		ackerman_angle_left = atan(wheelbase / (turn_radius - rear_track / 2.0) ) * turn_input
		ackerman_angle_right = atan(wheelbase / (turn_radius + rear_track / 2.0) ) * turn_input
	else:
		ackerman_angle_left = 0
		ackerman_angle_right = 0

	#$WheelFL.rotation.y = lerp($WheelFL.rotation.y, ackerman_angle_left, TURN_SPEED * delta)
	#$WheelFR.rotation.y = lerp($WheelFR.rotation.y, ackerman_angle_right, TURN_SPEED * delta)

	$WheelFL.rotation.y = move_toward($WheelFL.rotation.y, ackerman_angle_left, TURN_SPEED*delta)
	$WheelFR.rotation.y = move_toward($WheelFR.rotation.y, ackerman_angle_right, TURN_SPEED*delta)

func _physics_process(_delta: float) -> void:
	DebugDraw.draw_arrow_ray(global_position, linear_velocity, 2.5, 0.5, Color.YELLOW)
	_basic_steering_rotation(_delta)
	#_ackerman_steering_rotation(_delta)

	var grounded := false
	var id = 0
	for wheel in wheels:
		if wheel.is_colliding():
			grounded = true
		wheel.force_raycast_update()
		_do_single_wheel_suspension(wheel)
		_do_single_wheel_acceleration(wheel)
		#_do_single_wheel_drag(wheel, id)
		#testing_impulse_single_wheel_drag(wheel, id)
		id += 1

	_fake_steering()
	if grounded:
		center_of_mass = Vector3.ZERO
	else:
		center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
		center_of_mass = Vector3.DOWN*0.5

func _get_point_velocity(point: Vector3) -> Vector3:
	return linear_velocity + angular_velocity.cross(point - global_position)

func test(ray: TurnRaycastWheel)-> void:
	if not ray.is_colliding(): return

	var steer_side_dir := ray.global_basis.x
	var tire_vel := _get_point_velocity(ray.wheel.global_position)
	var steering_x_vel := steer_side_dir.dot(tire_vel)
	var x_traction = 1.0
	var x_force = -global_basis.x * steering_x_vel * ((mass * 9.8)/4.0) * x_traction

	# F = M * dV/T
	var desired_accel = (steering_x_vel*x_traction) / get_physics_process_delta_time()
	x_force = -global_basis.x*(mass/4.0) * desired_accel

	# z force
	var f_vel = -ray.global_basis.z.dot(tire_vel)
	var wheel_traction = 0.05
	var z_force = global_basis.z * f_vel * ((mass * 9.8)/4.0) * wheel_traction# * get_physics_process_delta_time()

	ProjectSettings.get_setting("physics/3d/default_gravity")

	var force_pos := ray.wheel.global_position - global_position
	apply_force(x_force, force_pos)
	apply_force(z_force, force_pos)
	DebugDraw.draw_arrow_ray(ray.wheel.global_position, x_force/mass, 2.5, 0.5, Color.GREEN)


func testing_impulse_single_wheel_drag(ray: TurnRaycastWheel, idx:int = 0) -> void:
	if not ray.is_colliding(): return

	var steer_dir := ray.global_basis.x
	var tire_vel := _get_point_velocity(ray.wheel.global_position)
	var steering_vel := steer_dir.dot(tire_vel)
	var traction = 1.0
	if hand_break:
		traction = 0.01
	var x_force = -global_basis.x * steering_vel * ((mass * 9.8)/4.0) * traction# * get_physics_process_delta_time()

	#var desired_vel_change = steering_vel * traction
	#var desired_accel = desired_vel_change / get_physics_process_delta_time()
	#x_force = -global_basis.x*(mass/24.0) * desired_accel

	# z force
	var f_vel = -ray.global_basis.z.dot(tire_vel)
	var wheel_traction = 0.05
	var z_force = global_basis.z * f_vel * ((mass * 9.8)/4.0) * wheel_traction# * get_physics_process_delta_time()

	ProjectSettings.get_setting("physics/3d/default_gravity")

	var force_pos := ray.wheel.global_position - global_position
	apply_force(x_force, force_pos)
	apply_force(z_force, force_pos)
	DebugDraw.draw_arrow_ray(ray.wheel.global_position, x_force/mass, 2.5, 0.5, Color.GREEN)

@export var fake_steer_curve : Curve
func _fake_steering() -> void:
	var turn_input := Input.get_axis("turn_right", "turn_left")
	var car_speed := linear_velocity.dot(-global_basis.z)
	turn_input *= fake_steer_curve.sample_baked(car_speed) * 15

	apply_torque(Vector3.UP * turn_input * 100)
	var lateral_speed := linear_velocity.dot(global_basis.x)
	var longitudial_speed := linear_velocity.dot(-global_basis.z)
	var GRIP := 350.0
	var ANGULAR_GRIP := 850.0
	var force := (-global_basis.x * lateral_speed * GRIP) + (global_basis.z * longitudial_speed * 20.0)

	apply_torque(Vector3.UP * -angular_velocity.y * ANGULAR_GRIP)
	apply_central_force(force)
	DebugDraw.draw_arrow_ray(global_position, force, 2.0, 0.5, Color.PURPLE)

func _do_single_wheel_drag(ray: TurnRaycastWheel, idx:int = 0) -> void:
	if not ray.is_colliding(): return

	var steer_dir := ray.global_basis.x
	var tire_vel := _get_point_velocity(ray.wheel.global_position)
	var steering_vel := steer_dir.dot(tire_vel)

	## 'Guessing' force values
	##print("Steering_vel: ", steering_vel)
	#var vel_LS := tire_vel * ray.global_basis
	##print("Steering_vel: ", vel_LS)
	##print("\n")
#
	#var f_side := vel_LS.x * (ray.spring_strength/mass) * 3.0
	#var force_vector := (f_side * -ray.global_basis.x)
	#var force_pos_offset := ray.wheel.global_position - global_position
	#var projected_vector: Vector3 = (force_vector - ray.get_collision_normal() * force_vector.dot(ray.get_collision_normal()))
	##force_vector = projected_vector
	#apply_force(force_vector, force_pos_offset)
	##apply_force(force_vector, force_pos_offset)
	#DebugDraw.draw_arrow_ray(ray.wheel.global_position, force_vector/mass, 2.5, 0.5, Color.GREEN)
	#DebugDraw.draw_arrow_ray(ray.wheel.global_position, vel_LS, 1.5, 0.1, Color.WEB_PURPLE)
	#DebugDraw.draw_arrow_ray(ray.wheel.global_position, steer_dir * steering_vel, 2.5, 0.2, Color.VIOLET)


	var grip_fac := absf(steering_vel/tire_vel.length())
	#if hand_break:
		#grip_fac = grip_fac *2.0
	#print("%.2f" % grip_fac)

	if not hand_break and grip_fac < 0.2:
		is_slipping = false
		skid_marks[idx].emitting = false

	#print(is_slipping)
	skid_marks[idx].global_position = ray.get_collision_point() + Vector3.UP * 0.01
	skid_marks[idx].look_at(skid_marks[idx].global_position + global_basis.z)
	var GRIP_FACTOR := ray.grip_curve.sample_baked(grip_fac)
	if hand_break:
		GRIP_FACTOR = 0.01
		if not skid_marks[idx].emitting:
			skid_marks[idx].emitting = true
			print("SKID")
		#print("BREAK")
	elif is_slipping:
		#print("NOT_SLIP")
		GRIP_FACTOR = 0.1



	#print("curv: %.2f" % GRIP_FACTOR)
	var TIRE_MASS := mass/10.0
	var desired_vel_change := -steering_vel * GRIP_FACTOR
	var desired_accel := desired_vel_change / get_physics_process_delta_time()
	var force_pos_offset := ray.wheel.global_position - global_position
	var force_vector := (steer_dir * TIRE_MASS * desired_accel)
	force_vector = -steer_dir * steering_vel * (ray.spring_strength/mass) * GRIP_FACTOR
	var projected_vector: Vector3 = (force_vector - ray.get_collision_normal() * force_vector.dot(ray.get_collision_normal()))
	#force_vector = projected_vector
	apply_force(force_vector, force_pos_offset)
	DebugDraw.draw_arrow_ray(ray.wheel.global_position, force_vector/mass, 2.5, 0.5, Color.GREEN)


func _do_single_wheel_acceleration(ray: TurnRaycastWheel) -> void:
	var forward_dir := -ray.global_basis.z
	var vel := forward_dir.dot(linear_velocity)
	var wheel_surface = 2 * PI * ray.wheel_radius
	# angVel = (2 PI R)/T
	# v = angVel R
	# v =
	ray.wheel.rotate_x(-vel * get_physics_process_delta_time()/ray.wheel_radius)

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
		#elif abs(vel) > 0.1 and not motor_input:
			#var drag_force_vector = global_basis.z * deceleration * signf(vel)
			#apply_force(drag_force_vector, force_pos)
			#DebugDraw.draw_arrow_ray(contact, drag_force_vector/mass, 2.5, 0.5, Color.PURPLE)


func _do_single_wheel_suspension(ray: TurnRaycastWheel) -> void:
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
		#print("Rel_world: ", relative_vel)
		#relative_vel = (ray.prev_len - offset) / get_physics_process_delta_time()
		#print(ray.prev_len, " - ", offset, " = ", float(ray.prev_len - offset))
		#print("VelocityC: ", relative_vel, "\n")
		#ray.prev_len = offset

		var spring_damp_force := ray.spring_damping * relative_vel

		var force_vector := (spring_force - spring_damp_force) * ray.get_collision_normal()

		contact = ray.wheel.global_position
		var force_pos_offset := contact - global_position
		apply_force(force_vector, force_pos_offset)

		DebugDraw.draw_arrow_ray(contact, force_vector/mass, 2.5)
		#DebugDraw.draw_sphere(ray.get_collision_point(), 0.5)
