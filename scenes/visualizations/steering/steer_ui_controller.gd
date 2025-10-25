extends Node3D

@onready var velocity_marker: Marker3D = %VelocityMarker
@onready var w_right: Node3D = %WRight
@onready var w_left: Node3D = %WLeft

func _physics_process(delta: float) -> void:
	var turn_input := Input.get_axis("turn_right", "turn_left") * 0.5
	w_left.rotate_y(turn_input*delta)
	w_right.rotate_y(turn_input*delta)


	var turn_speed := Input.get_axis("accelerate", "decelerate")
	velocity_marker.global_position.x += turn_speed*delta

	var vel := velocity_marker.global_position - global_position
	var steer_dir := w_right.global_basis.x
	var tire_vel := vel # We don't use car torque here, so same?
	var steering_vel := steer_dir.dot(tire_vel)

	var x_force := -w_right.global_basis.x * steering_vel
	DebugDraw.draw_arrow_ray(w_right.global_position+Vector3.UP, x_force, 1.5, 0.1, Color.RED)
	#var x_force2 := -w_left.global_basis.x * w_left.global_basis.x.dot(tire_vel)
	#DebugDraw.draw_arrow_ray(w_left.global_position+Vector3.UP, x_force2, 2.0, 0.2, Color.RED)

	var z_force := -w_left.global_basis.z * w_left.global_basis.z.dot(tire_vel) * 0.2
	DebugDraw.draw_arrow_ray(w_right.global_position+Vector3.UP, z_force, 1.0, 0.1, Color.BLUE)

	DebugDraw.draw_arrow_ray(global_position+Vector3.UP, vel, 1.5, 0.1, Color.YELLOW)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action("quit"):
		get_tree().quit()
