extends RayCast3D
class_name RaycastWheelEndBasic

@export_group("Wheel properties")
@export var spring_strength := 100.0
@export var spring_damping := 2.0
@export var rest_dist := 0.5
@export var over_extend := 0.0
@export var wheel_radius := 0.4
@export var z_traction := 0.05

@export_category("Motor")
@export var is_motor := false
@export var is_steer := false
@export var grip_curve : Curve

@export_category("Debug")
@export var show_debug := false
@export var shape_cast :ShapeCast3D

@onready var wheel: Node3D = get_child(0)

var engine_force := 0.0
var grip_factor  := 0.0


func _ready() -> void:
	target_position.y = -(rest_dist + wheel_radius + over_extend)
	if shape_cast:
		shape_cast.target_position.x = -(rest_dist + wheel_radius + over_extend)
		shape_cast.add_exception(get_parent())


func apply_wheel_physics(car: EndBasicCar) -> void:
	force_raycast_update()
	if shape_cast: shape_cast.force_shapecast_update()
	target_position.y = -(rest_dist + wheel_radius + over_extend)

	## Rotates wheel visuals
	var forward_dir   := -global_basis.z
	var vel           := forward_dir.dot(car.linear_velocity)
	wheel.rotate_x( (-vel * get_physics_process_delta_time()) / wheel_radius )

	if not is_colliding(): return

	var contact       := get_collision_point()
	var spring_len    := global_position.distance_to(contact) - wheel_radius
	if shape_cast:
		spring_len = shape_cast.get_closest_collision_safe_fraction() * -shape_cast.target_position.x
	var offset        := rest_dist - spring_len

	wheel.position.y = -spring_len # Local y position of the wheel
	contact = wheel.global_position
	var force_pos     := contact - car.global_position

	## Spring forces
	var spring_force  := spring_strength * offset
	var tire_vel      := car._get_point_velocity(contact) # Center of the wheel
	var spring_damp_f := spring_damping * global_basis.y.dot(tire_vel)

	var y_force       := (spring_force - spring_damp_f) * get_collision_normal()

	## Acceleration
	if is_motor and car.motor_input:
		var speed_ratio := vel / car.max_speed
		var ac := car.accel_curve.sample_baked(speed_ratio)
		var accel_force := forward_dir * car.acceleration * car.motor_input * ac
		car.apply_force(accel_force, force_pos)
		if show_debug: DebugDraw.draw_arrow_ray(contact, accel_force/car.mass, 2.5, 0.5, Color.RED)

	## Tire X traction (Steering)
	var steering_x_vel := global_basis.x.dot(tire_vel)

	grip_factor        = absf(steering_x_vel/tire_vel.length())
	var x_traction     := grip_curve.sample_baked(grip_factor)

	if not car.hand_break and grip_factor < 0.2:
		car.is_slipping = false
	if car.hand_break:
		x_traction = 0.01
	elif car.is_slipping:
		x_traction = 0.1

	var gravity        := -car.get_gravity().y
	var x_force        := -global_basis.x * steering_x_vel * x_traction * ((car.mass * gravity)/car.total_wheels)


	## Tire Z traction (Longidutinasl)
	var f_vel          := forward_dir.dot(tire_vel)
	var z_friction     := z_traction
	var z_force        := global_basis.z * f_vel * z_friction * ((car.mass * gravity)/car.total_wheels)

	car.apply_force(y_force, force_pos)
	car.apply_force(x_force, force_pos)
	car.apply_force(z_force, force_pos)

	if show_debug: DebugDraw.draw_arrow_ray(contact, y_force/car.mass, 2.5)
	if show_debug: DebugDraw.draw_arrow_ray(contact, x_force/car.mass, 1.5, 0.2, Color.YELLOW)

	if is_steer: ## Temp tests
		var slip_angle := Vector3.FORWARD.signed_angle_to(Vector3.FORWARD, Vector3.UP)
		slip_angle = (-car.global_basis.z).signed_angle_to(-global_basis.z, global_basis.y)
		#print(rad_to_deg(absf(slip_angle)))
