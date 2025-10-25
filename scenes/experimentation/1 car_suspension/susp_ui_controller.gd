extends Node
@export var car: RigidBody3D
@export var update_ui_vars := true
@export var use_pause_time_inputs := false

var mouse_pressed := false
var is_using_signals := false

func _ready() -> void:
	if update_ui_vars and car and car.has_signal("time_scale_changed"):
		is_using_signals = true
		car.time_scale_changed.connect(update_time_scale_label)
		car.all_force_changed.connect(update_checkbox.bind(%AllForcesCB))
		car.pull_force_changed.connect(update_checkbox.bind(%PullForcesCB))

		update_checkbox(not car.disable_pull_force, %PullForcesCB)
		update_checkbox(not car.disable_forces, %AllForcesCB)
	if not car:
		car = %Car
	if not update_ui_vars:
		$CanvasLayer.hide()


func _physics_process(_delta: float) -> void:
	if update_ui_vars and not is_using_signals and car:
		if "disable_pull_force" in car: update_checkbox(not car.disable_pull_force, %PullForcesCB)
		if "disable_forces" in car: update_checkbox(not car.disable_forces, %AllForcesCB)
		if "hand_break" in car: update_checkbox(car.hand_break, %HandBreakCB)
		if "is_slipping" in car: update_checkbox(car.is_slipping, %SlippingCB)
		update_time_scale_label(Engine.time_scale)

		# Car velocity
		%SpeedLabel.text = "Car speed: %.1f" % -car.global_basis.z.dot(car.linear_velocity)
		# Car motor
		if "accel_curve" in car:
			var vel := -car.global_basis.z.dot(car.linear_velocity)
			var ratio = vel / car.max_speed
			var ac = car.accel_curve.sample_baked(ratio)
			%MotorRatio.value = ac
			var real_accel = ac * car.acceleration
			if not car.motor_input:
				real_accel = 0
				%MotorRatio.value = 0
			%AccelLabel.text = "AccelForce: %.0f" % real_accel


func _input(event: InputEvent) -> void:
	if event.is_action_pressed("reload_scene"):
		get_tree().reload_current_scene()
	if event is InputEventMouseButton and event.button_index == 3:
		if event.pressed:
			mouse_pressed = true
			if "disable_forces" in car:
				car.disable_forces = true
				car.freeze = false
			car.gravity_scale = 0.0
		else:
			mouse_pressed = false
			if "disable_forces" in car:
				car.disable_forces = false
				car.freeze = false
			car.gravity_scale = 1.0
	if event is InputEventMouseMotion:
		if event.button_mask == 3:
			car.linear_velocity.y = -event.relative.y / 20.0
			#car.global_position.y -= event.relative.y / 50.0

	if use_pause_time_inputs:
		if event.is_action_pressed("speed_1"):
			Engine.time_scale = 1.0
			car.freeze = false
		if event.is_action_pressed("speed_2"):
			Engine.time_scale = 0.25
			car.freeze = false
		if event.is_action_pressed("speed_3"):
			Engine.time_scale = 0.1
			car.freeze = false
		if event.is_action_pressed("speed_4"):
			Engine.time_scale = 0.01
			car.freeze = false

	if event.is_action_pressed("quit"):
		get_tree().quit()

func update_time_scale_label(value: float) :
	%TimeScaleLabel.text = "Time scale: %.2f" % value
	if value >= 0.9:
		%TimeScaleLabel.hide()
	else:
		%TimeScaleLabel.show()

func update_checkbox(value: bool, checkbox: CheckBox):
	checkbox.button_pressed = value
