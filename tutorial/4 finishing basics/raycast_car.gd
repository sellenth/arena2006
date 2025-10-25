extends RigidBody3D
class_name RaycastCar

const CarInputState := preload("res://network/car_input_state.gd")
const CarSnapshot := preload("res://network/car_snapshot.gd")

@export var wheels: Array[RaycastWheel]
@export var acceleration := 600.0
@export var max_speed := 20.0
@export var accel_curve : Curve
@export var tire_turn_speed := 2.0
@export var tire_max_turn_degrees := 25

@export var skid_marks: Array[GPUParticles3D]
@export var show_debug := false

@onready var total_wheels := wheels.size()

var motor_input := 0.0
var hand_break := false
var is_slipping := false

var _input_state := CarInputState.new()
var _brake_pressed := false
var _pending_snapshot: CarSnapshot


func _ready() -> void:
	var network := get_tree().root.get_node_or_null("/root/NetworkController")
	if network and network.has_method("register_car"):
		network.register_car(self)

func _get_point_velocity(point: Vector3) -> Vector3:
	return linear_velocity + angular_velocity.cross(point - global_position)

func set_input_state(state: CarInputState) -> void:
	_input_state.copy_from(state)
	motor_input = clampf(_input_state.throttle, -1.0, 1.0)
	hand_break = _input_state.handbrake
	_brake_pressed = _input_state.brake
	if _input_state.handbrake:
		is_slipping = true


func capture_snapshot(tick: int) -> CarSnapshot:
	var snapshot := CarSnapshot.new()
	snapshot.tick = tick
	snapshot.transform = global_transform
	snapshot.linear_velocity = linear_velocity
	snapshot.angular_velocity = angular_velocity
	return snapshot


func queue_snapshot(snapshot: CarSnapshot) -> void:
	_pending_snapshot = snapshot

func _basic_steering_rotation(wheel: RaycastWheel, delta: float) -> void:
	if not wheel.is_steer: return

	var turn_input := _input_state.steer * tire_turn_speed
	if turn_input:
		wheel.rotation.y = clampf(wheel.rotation.y + turn_input * delta,
			deg_to_rad(-tire_max_turn_degrees), deg_to_rad(tire_max_turn_degrees))
	else:
		wheel.rotation.y = move_toward(wheel.rotation.y, 0, tire_turn_speed * delta)


func _physics_process(delta: float) -> void:
	if show_debug: DebugDraw.draw_arrow_ray(global_position, linear_velocity, 2.5, 0.5, Color.GREEN)

	var id := 0
	var grounded := false
	for wheel in wheels:
		wheel.apply_wheel_physics(self)
		_basic_steering_rotation(wheel, delta)

		wheel.is_braking = _brake_pressed

		# Skid marks
		skid_marks[id].global_position = wheel.get_collision_point() + Vector3.UP * 0.01
		skid_marks[id].look_at(skid_marks[id].global_position + global_basis.z)

		if not hand_break and wheel.grip_factor < 0.2:
			is_slipping = false
			skid_marks[id].emitting = false

		if hand_break and not skid_marks[id].emitting:
			skid_marks[id].emitting = true

		if wheel.is_colliding():
			grounded = true

		id += 1

	if grounded:
		center_of_mass = Vector3.ZERO
	else:
		center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
		center_of_mass = Vector3.DOWN*0.5

func _integrate_forces(state: PhysicsDirectBodyState3D) -> void:
	if _pending_snapshot:
		state.transform = _pending_snapshot.transform
		state.linear_velocity = _pending_snapshot.linear_velocity
		state.angular_velocity = _pending_snapshot.angular_velocity
		_pending_snapshot = null
