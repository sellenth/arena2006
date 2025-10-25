extends Node3D

@onready var w_right: Node3D = %WRight
@onready var w_left: Node3D = %WLeft
@onready var w_left_real: Node3D = %WLeftReal

@export var speed := 1.5
@onready var prev1_pos : Vector3 = w_right.global_position
@onready var prev2_pos : Vector3 = w_left.global_position
var tick_time = 0.06
var dt := 0.05
var stopped = false
func _physics_process(delta: float) -> void:
	if stopped: return

	%WRightPivot.rotate_y(-delta*speed)
	%WLeftPivot.rotate_y(-delta*speed)

	dt -= delta
	if dt <= 0:
		dt += tick_time
		DebugDraw.draw_line_thick(prev1_pos, w_right.global_position, 5.0, Color.RED, 5)
		DebugDraw.draw_line_thick(prev2_pos, w_left_real.global_position, 5.0, Color.RED, 5)
		prev1_pos = w_right.global_position
		prev2_pos = w_left_real.global_position


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action("click"):
		stopped = not stopped
	if event.is_action("quit"):
		get_tree().quit()
