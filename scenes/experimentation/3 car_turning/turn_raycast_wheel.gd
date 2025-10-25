extends RayCast3D
class_name TurnRaycastWheel

@export var spring_strength := 100.0
@export var spring_damping := 2.0
@export var rest_dist := 0.5
@export var over_extend := 0.0
@export var wheel_radius := 0.4
@export var is_motor := false

@export var grip_curve : Curve

@onready var prev_len := 0.0
@onready var prev_speed := 0.0

@onready var wheel: Node3D = get_child(0)
