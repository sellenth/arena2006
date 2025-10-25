@tool
extends Marker3D

@onready var start_pos := Vector3.ZERO

func _physics_process(_delta: float) -> void:
	var diff = 1 - (%WheelMesh.global_position.y - start_pos.y)
	scale.y = diff
