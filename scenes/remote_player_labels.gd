extends Node3D

const CarSnapshot := preload("res://network/car_snapshot.gd")

@export var label_color := Color(0.95, 0.86, 0.3)
@export var label_height := 2.5
@export var label_pixel_size := 0.01

var _network: Node
var _labels: Dictionary[int, Label3D] = {}


func _ready() -> void:
	_network = get_tree().root.get_node_or_null("/root/NetworkController")
	if not _network:
		return
	if _network.has_signal("player_state_updated"):
		_network.player_state_updated.connect(_on_player_state_updated)
	if _network.has_signal("player_disconnected"):
		_network.player_disconnected.connect(_on_player_disconnected)


func _on_player_state_updated(player_id: int, snapshot: CarSnapshot) -> void:
	if not snapshot:
		return
	var label: Label3D = _labels.get(player_id, null)
	if not label:
		label = _create_label(player_id)
		_labels[player_id] = label
		add_child(label)
	label.global_position = snapshot.transform.origin + Vector3.UP * label_height


func _on_player_disconnected(player_id: int) -> void:
	var label: Label3D = _labels.get(player_id, null)
	if not label:
		return
	_labels.erase(player_id)
	label.queue_free()


func _create_label(player_id: int) -> Label3D:
	var label := Label3D.new()
	label.name = "RemoteLabel_%s" % player_id
	label.text = "Player %s" % player_id
	label.pixel_size = label_pixel_size
	label.modulate = label_color
	label.outline_modulate = Color.BLACK
	label.outline_size = 4
	label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	label.no_depth_test = true
	return label
