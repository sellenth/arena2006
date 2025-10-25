extends RigidBody3D

@export var wheels: Array[AccelWheels]
@export var spring_strength := 100.0
@export var spring_damping := 2.0
@export var rest_dist := 0.5
@export var wheel_radius := 0.4

@export_group("Aceleration")
@export var acceleration := 800.0
@export var max_speed := 20.0
@export var deceleration := 20.0

@export var accel_curve: Curve

var disable_forces := false

var MOTOR_ON := false
var motor_input := 0

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("jump"):
		#linear_velocity.y = 10
		set_physics_process(false)
		apply_central_impulse(Vector3.UP * 250)
		await get_tree().create_timer(0.3).timeout
		set_physics_process(true)
	if event.is_action_pressed("accelerate"):
		MOTOR_ON = true
		motor_input = 1
	elif event.is_action_released("accelerate"):
		MOTOR_ON = false

	if event.is_action_pressed("decelerate"):
		MOTOR_ON = true
		motor_input = -1
	elif event.is_action_released("decelerate"):
		MOTOR_ON = false


func _physics_process(_delta: float) -> void:
	center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
	%CenterOfMass.position = center_of_mass
	var grounded = false
	for wheel in wheels:
		if wheel.is_colliding():
			grounded = true
			_do_single_wheel_suspension(wheel)
		_do_single_wheel_acceleration(wheel)
	if grounded:
		pass
		center_of_mass = Vector3.ZERO
		#linear_damp = 1
		#angular_damp = 1
	else:
		pass
		center_of_mass = Vector3.DOWN*1
		#linear_damp = 0
		#angular_damp = 0

func _get_point_velocity(point: Vector3) -> Vector3:
	return linear_velocity + angular_velocity.cross(point - global_position + center_of_mass)


func _do_single_wheel_acceleration(suspension_ray: AccelWheels) -> void:
	var foward_dir := -suspension_ray.global_basis.z
	var vel := foward_dir.dot(linear_velocity)
	var wheel:Node3D = suspension_ray.get_node("Wheel")
	wheel.rotate_x(-2 * vel * get_process_delta_time())
	if suspension_ray.is_colliding() and suspension_ray.position.z > 0:
		var speed_ratio := vel / max_speed
		var ac := accel_curve.sample_baked(speed_ratio) * motor_input
		#print("speed_ratio: ", speed_ratio)
		#print("accel: ", ac)

		var drag = suspension_ray.get_collider().get_meta("drag", false)

		var contact := suspension_ray.get_collision_point()
		contact = suspension_ray.wheel.global_position
		var force_vector := foward_dir * (ac * acceleration)
		var force_pos_offset := contact - global_position
		#var projected_vector: Vector3 = (force_vector - suspension_ray.get_collision_normal() * force_vector.dot(suspension_ray.get_collision_normal()))
		if MOTOR_ON:
			apply_force(force_vector, force_pos_offset)
			force_vector = (force_vector/(50))
			DebugDraw.draw_arrow_ray(contact, force_vector, 2.5, 0.5, Color.GREEN)
			#apply_force(projected_vector*2, force_pos_offset)
			#projected_vector = (projected_vector/(50))
			#DebugDraw.draw_arrow_ray(contact, projected_vector, 2.5, 0.5, Color.BLACK)
		# Drag
		if abs(vel) > 0.05:
			force_vector = global_basis.z * deceleration * signf(vel)
			if drag:
				force_vector = force_vector * drag * abs(speed_ratio)
			apply_force(force_vector, force_pos_offset)
			force_vector = (force_vector/(50))
			DebugDraw.draw_arrow_ray(contact, force_vector, 2.5, 0.3, Color.BLUE_VIOLET)


func _do_single_wheel_acceleration_basic(suspension_ray: AccelWheels) -> void:
	var foward_dir := -suspension_ray.global_basis.z
	var vel := foward_dir.dot(linear_velocity)
	var wheel:Node3D = suspension_ray.get_node("Wheel")
	wheel.rotate_x(-2 * vel * get_process_delta_time())
	if not MOTOR_ON: return
	if suspension_ray.is_colliding() and suspension_ray.position.z > 0:
		#var speed := linear_velocity * global_basis
		#var foward_speed := -speed.z
		#print(suspension_ray.name," | InvTransformSpeed: %.3f" % foward_speed)
		#print(suspension_ray.name," | DotProduct       : %.3f" % vel)
		if vel > max_speed:
			foward_dir *= 0.1
		var contact := suspension_ray.get_collision_point()
		contact = suspension_ray.wheel.global_position
		var force_vector := foward_dir * acceleration
		var force_pos_offset := contact - global_position
		apply_force(force_vector, force_pos_offset)
		DebugDraw.draw_arrow_ray(contact, force_vector, 2.5, 0.5, Color.GREEN)

		#wheel.rotate_object_local(Vector3.UP, 0.1)
#func _do_single_wheel_acceleration(suspension_ray: RayCast3D) -> void:
	#var foward_dir := -suspension_ray.global_basis.z
	#var vel := foward_dir.dot(linear_velocity)
	#var wheel:Node3D = suspension_ray.get_node("Wheel")
	#wheel.rotate_x(-2 * vel * get_process_delta_time())
	#if not MOTOR_ON: return
	#if suspension_ray.is_colliding() and suspension_ray.position.z > 0:
		#var speed := linear_velocity * global_basis
		#var foward_speed := -speed.z
		##print(suspension_ray.name," | InvTransformSpeed: %.3f" % foward_speed)
		##print(suspension_ray.name," | DotProduct       : %.3f" % vel)
		#if vel > max_speed:
			#foward_dir *= 0.25
		#var contact := suspension_ray.get_collision_point()
		#var force_vector := foward_dir * acceleration
		#var force_pos_offset := contact - global_position
		#apply_force(force_vector, force_pos_offset)
		#DebugDraw.draw_arrow_ray(contact, force_vector, 2.5, 0.5, Color.GREEN)
#
		##wheel.rotate_object_local(Vector3.UP, 0.1)


func _do_single_wheel_suspension(suspension_ray: AccelWheels) -> void:
	suspension_ray.target_position.y = -(suspension_ray.rest_dist + suspension_ray.wheel_radius)
	var contact := suspension_ray.get_collision_point()
	var spring_up_dir := suspension_ray.global_basis.y
	var spring_len := suspension_ray.global_position.distance_to(contact) - suspension_ray.wheel_radius
	var offset := suspension_ray.rest_dist - spring_len

	suspension_ray.wheel.position.y = -spring_len
	#suspension_ray.wheel.position.y = move_toward(suspension_ray.wheel.position.y, -spring_len, 15*get_process_delta_time())

	var spring_force := suspension_ray.spring_strength * offset

	# damping force = damping * relative velocity
	var world_vel := _get_point_velocity(contact)
	var relative_vel := spring_up_dir.dot(world_vel)
	var spring_damp_force := suspension_ray.spring_damping * relative_vel

	var force_vector := (spring_force - spring_damp_force) * spring_up_dir


	contact = suspension_ray.wheel.global_position
	var force_pos_offset := contact - global_position

	if not disable_forces:
		apply_force(force_vector, force_pos_offset)
	force_vector = (force_vector/(50))
	DebugDraw.draw_arrow_ray(contact, force_vector, 2.5)
