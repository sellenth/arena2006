extends Node2D
@onready var wheel: MeshInstance3D = %WheelMesh
@onready var spring_root: Node3D = %SpringRoot
@onready var camera: Camera3D = %Camera3D
@onready var label: Label3D = %Label3D


func _draw() -> void:
	var wheel_pos := camera.unproject_position(wheel.global_position)
	var root_pos := camera.unproject_position(spring_root.global_position)
	var rest_position := camera.unproject_position(Vector3.ZERO)
	const LINE_WIDTH = 3
	draw_line(root_pos, root_pos + Vector2(-250, 0), Color.RED, LINE_WIDTH, true)
	draw_line(root_pos + Vector2(-250, 0), root_pos + Vector2(-250, 0) + Vector2(0, wheel_pos.y - root_pos.y), Color.RED, LINE_WIDTH)
	draw_line(wheel_pos, wheel_pos + Vector2(-250, 0), Color.RED, LINE_WIDTH)

	# Offset arrow
	draw_line(wheel_pos + Vector2(-150, 0), rest_position + Vector2(-150, 0), Color.BLUE, LINE_WIDTH)
	var y_mult := 1 if wheel_pos.y > rest_position.y else -1
	_draw_arrow(rest_position + Vector2(-150, 0), y_mult)

	# Horizontal line
	draw_line(Vector2(-500, rest_position.y), Vector2(3000, rest_position.y), Color.ROSY_BROWN, 1.5)

	# Zero is rest position for this
	var diff_to_rest := Vector3.ZERO.y - wheel.global_position.y
	label.text = "Offset: %.2f" % [diff_to_rest]

func _draw_arrow(a_pos:Vector2, y_mult:float = 1) -> void:
	var arrow1 := Vector2(-10, 15 * y_mult)
	var arrow2 := Vector2(10, 15 * y_mult)

	draw_line(a_pos, a_pos+arrow1,Color.BLUE, 3)
	draw_line(a_pos, a_pos+arrow2,Color.BLUE, 3)


func _process(_delta: float) -> void:
	queue_redraw()
