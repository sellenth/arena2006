extends RigidBody3D
@export var wheels: Array[RayCast3D]
@export var use_wheels := true
@export var disable_pull_force := false
@export var disable_forces := false


func get_point_velocity (point :Vector3)->Vector3:
	return linear_velocity + angular_velocity.cross(point - global_transform.origin)

func _physics_process(_delta: float) -> void:
	for wheel in wheels:
		_single_wheel_suspension(wheel)
		_single_wheel_acceleration(wheel)
		_single_wheel_steering(wheel, _delta)
		pass
	pass


func _single_wheel_suspension(suspension_ray: RayCast3D) -> void:
	if suspension_ray.is_colliding():
		var contact := suspension_ray.get_collision_point() # Raycast hit point
		var spring_up_dir := suspension_ray.global_transform.basis.y # The top direction along the wheel
		var rest_dist := suspension_ray.target_position.length()/2.0 # Half the raycast is the rest distance
		var spring_hit_distance := suspension_ray.global_position.distance_to(contact)
		if use_wheels:
			spring_hit_distance -= 0.4 # Wheel radius
		var offset := rest_dist - spring_hit_distance # How different from the rest
		#offset = clampf(offset, suspension_ray.target_position.y/2.0, -suspension_ray.target_position.y/2.0)

		var wheel: Node3D = suspension_ray.get_node("Wheel")

		var world_vel := get_point_velocity(wheel.global_position)
		var vel := spring_up_dir.dot(world_vel)
		var spring_damper := 2
		var spring_strength := 100.0

		if disable_pull_force and offset < 0:
			return

		##              spring force            -   spring damper force
		var force := (offset * spring_strength) - (vel * spring_damper)
		var force_vector := spring_up_dir * force
		var force_position_offset := wheel.global_position - global_position
		if not disable_forces:
			apply_force(force_vector, force_position_offset)
		# Set visual wheel pos
		wheel.position.y = -(spring_hit_distance)
		%UiController.get_node("%OffsetLabel").text = "Offset: %.3f" % offset

		force_vector = (force_vector / spring_strength) * 10.0
		DebugDraw.draw_arrow_ray(wheel.global_position, force_vector, 2.0, 0.2)


var MOTOR_ON := false
func _single_wheel_acceleration(suspension_ray: RayCast3D) -> void:
	if not suspension_ray.is_colliding():
		return
	if MOTOR_ON and suspension_ray.position.z > 0: ## positive z is back wheels
		var steering_dir = -suspension_ray.global_transform.basis.z
		var force_aceleration_vector = 10 * steering_dir
		var wheel: Node3D = suspension_ray.get_node("Wheel")
		var force_position := wheel.global_position - global_position
		if not disable_forces:
			apply_force(force_aceleration_vector, force_position)


func _single_wheel_steering(suspension_ray: RayCast3D, delta: float) -> void:
	if not suspension_ray.is_colliding():
		return
	###### Tire Drag
	# apply force which is the oposite of the tire force sideways
	# Can use lookup curve for traction over tire velocity
	var wheel: Node3D = suspension_ray.get_node("Wheel")
	var tireWordVel := get_point_velocity(wheel.global_position)

	# dir is now the red vector pointing away from the wheel
	var steering_dir = suspension_ray.global_transform.basis.x
	#print(steering_dir)
	#if suspension_ray.position.x < 0:
		#steering_dir = -steering_dir
	# tireWordVel stays the same
	# Get velocity magnitude in the spring away dir
	var steering_vel = steering_dir.dot(tireWordVel)


	# The change in velocity desired
	# 0-1 | 0 is no grip/change, 1 is full traction
	var grip_factor = 0.8
	var desiredVelChange = -steering_vel * grip_factor

	# a = (v-v0)/t
	var desired_aceleration = desiredVelChange / delta


	# F = m.a
	var tire_mass = 0.4
	var force_steering_vector = steering_dir * tire_mass * desired_aceleration
	var force_position := wheel.global_position - global_position
	if not disable_forces:
		apply_force(force_steering_vector, force_position)

	if not force_steering_vector.is_zero_approx():
		DebugDraw.draw_arrow_ray(wheel.global_position, 5*force_steering_vector, 2.0, 0., Color.RED)

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("accelerate"):
		MOTOR_ON = true
	elif event.is_action_released("accelerate"):
		MOTOR_ON = false
	if event.is_action_pressed("jump"):
		#linear_velocity.y = 10
		set_physics_process(false)
		apply_central_impulse(Vector3.UP * 5)
		await get_tree().create_timer(0.3).timeout
		set_physics_process(true)

	if event.is_action_pressed("toggle_pull_forces"): ## Z
		disable_pull_force = not disable_pull_force
		freeze = false
	if event.is_action_pressed("toggle_all_forces"): ## C
		disable_forces = not disable_forces
		freeze = false

	if event.is_action_pressed("speed_1"):
		Engine.time_scale = 1.0
		freeze = false
	if event.is_action_pressed("speed_2"):
		Engine.time_scale = 0.25
		freeze = false
	if event.is_action_pressed("speed_3"):
		Engine.time_scale = 0.1
		freeze = false

	if event.is_action_pressed("quit"):
		get_tree().quit()
