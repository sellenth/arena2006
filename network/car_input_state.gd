extends RefCounted
class_name CarInputState

var tick := 0
var throttle := 0.0
var steer := 0.0
var handbrake := false
var brake := false


func copy_from(other: CarInputState) -> void:
	tick = other.tick
	throttle = other.throttle
	steer = other.steer
	handbrake = other.handbrake
	brake = other.brake


func reset() -> void:
	tick = 0
	throttle = 0.0
	steer = 0.0
	handbrake = false
	brake = false
