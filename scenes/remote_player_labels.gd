extends Node3D

const CarSnapshot := preload("res://network/car_snapshot.gd")

@export var remote_car_scene: PackedScene = preload("res://scenes/remote_car_proxy.tscn")
@export var label_color := Color(0.95, 0.86, 0.3)
@export var label_height := 2.5
@export var label_pixel_size := 0.01
@export var placeholder_color := Color(0.2, 0.6, 0.9)

class RemotePlayerView:
	var root: Node3D
	var label: Label3D

var _network: Node
var _views: Dictionary[int, RemotePlayerView] = {}


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
	var view: RemotePlayerView = _views.get(player_id, null)
	if not view:
		view = _create_view(player_id)
		_views[player_id] = view
	view.root.global_transform = snapshot.transform


func _on_player_disconnected(player_id: int) -> void:
	var view: RemotePlayerView = _views.get(player_id, null)
	if not view:
		return
	_views.erase(player_id)
	if is_instance_valid(view.root):
		view.root.queue_free()


func _create_view(player_id: int) -> RemotePlayerView:
	var view := RemotePlayerView.new()
	view.root = _instantiate_remote_car()
	view.root.name = "RemotePlayer_%s" % player_id
	add_child(view.root)
	view.label = _create_label(player_id)
	view.label.position = Vector3(0, label_height, 0)
	view.root.add_child(view.label)
	return view


func _instantiate_remote_car() -> Node3D:
	if remote_car_scene:
		var inst := remote_car_scene.instantiate()
		if inst is Node3D:
			return inst
	var placeholder := Node3D.new()
	var mesh := MeshInstance3D.new()
	var box := BoxMesh.new()
	box.size = Vector3(2, 0.5, 4)
	mesh.mesh = box
	var mat := StandardMaterial3D.new()
	mat.albedo_color = placeholder_color
	mesh.material_override = mat
	mesh.position = Vector3(0, 0.25, 0)
	placeholder.add_child(mesh)
	return placeholder


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
