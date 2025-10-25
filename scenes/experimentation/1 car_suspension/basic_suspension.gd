extends RigidBody3D
@export var wheels: Array[RayCast3D]

@export var use_wheels := true
@export var disable_pull_force := false
@export var disable_suspension := false
@export var disable_forces := false


func get_point_velocity (point :Vector3)->Vector3:
	return linear_velocity + angular_velocity.cross(point - global_transform.origin)

func _physics_process(_delta: float) -> void:
	for wheel in wheels:
		if disable_suspension:
			break
		_single_wheel_suspension(wheel)
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
		offset = clampf(offset, suspension_ray.target_position.y/2.0, -suspension_ray.target_position.y/2.0)

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
		%OffsetLabel.text = "Offset: %.3f" % offset

		force_vector = (force_vector / spring_strength) * 10.0
		DebugDraw.draw_arrow_ray(wheel.global_position, force_vector, 2.0, 0.2)


func _input(event: InputEvent) -> void:
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
		disable_suspension = not disable_suspension
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
#
#func _do_wheel_suspension() -> void:
	#if wheel_ray.is_colliding():
		#var spring_direction := wheel.global_basis.y
		#var ray_distance := wheel_ray.get_collision_point().distance_to(wheel_ray.global_position)
		#var offset := rest_dist - ray_distance
		#var wheel_world_vel := get_point_velocity(wheel.global_position)
		#var vel := spring_direction.dot(wheel_world_vel)
		#var force := (offset * 100) - (vel * 10)
		#var force_offset := wheel.global_position - global_position
		#apply_force(spring_direction * force, force_offset)
