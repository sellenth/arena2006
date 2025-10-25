extends Node3D

@onready var wheel_rb: RigidBody3D = %WheelRB
@export var spring_strengh := 50.0
@export var damping_strength := 10.0

func _physics_process(_delta: float) -> void:
	if Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
		return

	var rest_pos := Vector3.ZERO
	var offset: float = rest_pos.y - wheel_rb.global_position.y
	var damping := wheel_rb.linear_velocity.y * damping_strength
	var force: Vector3 = Vector3(0, offset * spring_strengh, 0)
	force.y -= damping
	wheel_rb.apply_force(force)

	%StrengthLabel.text = "Strength: %.2f" % spring_strengh
	if damping_strength <= 0.1:
		%DampLabel.hide()
		%DampLabelForce.hide()
	else:
		%DampLabel.show()
		%DampLabelForce.show()
		%DampLabel.text = "Damping : %.2f" % damping_strength

func _input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		if event.button_mask == 1:
			wheel_rb.linear_velocity.y = -event.relative.y / 20.0
