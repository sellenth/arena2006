extends Node

const DEFAULT_PORT := 45000
const PACKET_INPUT := 1
const PACKET_PLAYER_STATE := 2
const PACKET_WELCOME := 3
const PACKET_REMOVE_PLAYER := 4

const PEER_TIMEOUT_MSEC := 5000
const SNAPSHOT_PAYLOAD_BYTES := 4 + 12 + 16 + 12 + 12

const CarInputState := preload("res://network/car_input_state.gd")
const CarSnapshot := preload("res://network/car_snapshot.gd")
const PlayerCarScene := preload("res://scenes/player_car.tscn")

signal player_state_updated(player_id: int, snapshot: CarSnapshot)
signal player_disconnected(player_id: int)

class PeerInfo:
	var id := 0
	var peer: PacketPeerUDP
	var input_state := CarInputState.new()
	var last_snapshot: CarSnapshot
	var last_seen_msec := 0
	var car: RaycastCar

	func _init(peer_id: int, peer_ref: PacketPeerUDP) -> void:
		id = peer_id
		peer = peer_ref
		last_seen_msec = Time.get_ticks_msec()

	func touch() -> void:
		last_seen_msec = Time.get_ticks_msec()


class PlayerStateData:
	var player_id := 0
	var snapshot: CarSnapshot = null

enum Role { NONE, SERVER, CLIENT }

var role: Role = Role.NONE
var _car
var _tick := 0

# Server side state
var _udp_server: UDPServer
var _peers := {}
var _next_peer_id := 1
var _server_car_parent: Node3D

# Client side state
var _client_peer: PacketPeerUDP
var _client_input := CarInputState.new()
var _client_id := 0
var _remote_player_snapshots := {}


func _ready() -> void:
	Engine.physics_ticks_per_second = 60
	role = _determine_role()

	match role:
		Role.SERVER:
			_start_server()
		Role.CLIENT:
			_start_client()
		_:
			pass

	set_physics_process(role != Role.NONE)


func register_car(car) -> void:
	_car = car
	if role == Role.CLIENT:
		_apply_client_input_to_car()


func _determine_role() -> Role:
	var args := OS.get_cmdline_args()
	var parsed_role := Role.CLIENT
	for arg in args:
		if arg == "--server":
			return Role.SERVER
		elif arg == "--client":
			parsed_role = Role.CLIENT
	return parsed_role


func _start_server() -> void:
	_udp_server = UDPServer.new()
	var err = _udp_server.listen(DEFAULT_PORT)
	if err != OK:
		push_error("Failed to bind UDP server on port %s (err %s)" % [DEFAULT_PORT, err])
	else:
		_peers.clear()
		_next_peer_id = 1
		_server_car_parent = get_tree().current_scene.get_node_or_null("AuthoritativeCars")
		if not _server_car_parent:
			_server_car_parent = get_tree().current_scene
		print("Server listening on UDP %s" % DEFAULT_PORT)


func _start_client() -> void:
	_client_peer = PacketPeerUDP.new()
	#var err = _client_peer.connect_to_host("129.212.182.9", DEFAULT_PORT)
	var err = _client_peer.connect_to_host("127.0.0.1", DEFAULT_PORT)
	if err != OK:
		push_error("Failed to start UDP client (err %s)" % err)
	else:
		_client_id = 0
		_remote_player_snapshots.clear()
		print("Client connecting to 127.0.0.1:%s" % DEFAULT_PORT)
		_apply_client_input_to_car()


func _physics_process(_delta: float) -> void:
	match role:
		Role.SERVER:
			_process_server()
		Role.CLIENT:
			_process_client()
		_:
			pass


func _process_server() -> void:
	if not _udp_server:
		return

	_udp_server.poll()
	while _udp_server.is_connection_available():
		var new_peer := _udp_server.take_connection()
		if new_peer:
			_register_peer(new_peer)

	for peer_id in _peers.keys():
		var info: PeerInfo = _peers[peer_id]
		if not info:
			continue
		while info.peer.get_available_packet_count() > 0:
			var packet := info.peer.get_packet()
			if info.peer.get_packet_error() == OK:
				_handle_server_packet(peer_id, packet)

	_tick += 1
	_update_server_cars()
	_check_peer_timeouts()


func _handle_server_packet(peer_id: int, packet: PackedByteArray) -> void:
	if packet.is_empty():
		return
	var peer_info: PeerInfo = _peers.get(peer_id)
	if not peer_info:
		return
	peer_info.touch()
	var packet_type := packet[0]
	match packet_type:
		PACKET_INPUT:
			var state := _deserialize_input(packet)
			if state and state.tick >= peer_info.input_state.tick:
				peer_info.input_state.copy_from(state)
		_:
			pass


func _update_server_cars() -> void:
	for peer_id in _peers.keys():
		var info: PeerInfo = _peers.get(peer_id)
		if not info:
			continue
		if not info.car:
			info.car = _spawn_server_car(peer_id)
		if info.car:
			info.car.set_input_state(info.input_state)

	for peer_id in _peers.keys():
		var info: PeerInfo = _peers.get(peer_id)
		if not info or not info.car:
			continue
		var snapshot: CarSnapshot = info.car.capture_snapshot(_tick)
		info.last_snapshot = snapshot
		_send_snapshot_to_all(peer_id, snapshot)


func _process_client() -> void:
	_tick += 1
	var local_input := _collect_local_input()
	local_input.tick = _tick
	_client_input.copy_from(local_input)

	if _car:
		_car.set_input_state(_client_input)

	if _client_peer:
		_client_peer.put_packet(_serialize_input(local_input))
		_poll_client_packets()


func _collect_local_input() -> CarInputState:
	var state := CarInputState.new()
	var forward := Input.get_action_strength("accelerate")
	var backward := Input.get_action_strength("decelerate")
	state.throttle = clampf(forward - backward, -1.0, 1.0)
	var right := Input.get_action_strength("turn_right")
	var left := Input.get_action_strength("turn_left")
	state.steer = clampf(left - right, -1.0, 1.0)
	state.handbrake = Input.is_action_pressed("handbreak")
	state.brake = Input.is_action_pressed("brake")
	if absf(state.throttle) > 0.1 or absf(state.steer) > 0.1 or state.handbrake or state.brake:
		print("CLIENT input tick=%s throttle=%s steer=%s hb=%s br=%s" % [
			_tick + 1,
			"%.2f" % state.throttle,
			"%.2f" % state.steer,
			state.handbrake,
			state.brake
		])
	return state


func _poll_client_packets() -> void:
	if not _client_peer:
		return

	while _client_peer.get_available_packet_count() > 0:
		var packet := _client_peer.get_packet()
		if _client_peer.get_packet_error() != OK or packet.is_empty():
			continue
		var packet_type := packet[0]
		match packet_type:
			PACKET_WELCOME:
				var new_id := _deserialize_welcome(packet)
				if new_id != 0:
					_client_id = new_id
			PACKET_PLAYER_STATE:
				var remote_state: PlayerStateData = _deserialize_player_state(packet)
				if remote_state and remote_state.snapshot:
					var remote_id: int = remote_state.player_id
					var remote_snapshot: CarSnapshot = remote_state.snapshot
					if remote_id == _client_id:
						_apply_local_snapshot(remote_snapshot)
					else:
						_remote_player_snapshots[remote_id] = remote_snapshot
						player_state_updated.emit(remote_id, remote_snapshot)
			PACKET_REMOVE_PLAYER:
				var removed_id := _deserialize_remove_player(packet)
				if removed_id != 0:
					_remote_player_snapshots.erase(removed_id)
					player_disconnected.emit(removed_id)
			_:
				continue


func _register_peer(new_peer: PacketPeerUDP) -> void:
	var peer_id := _next_peer_id
	_next_peer_id += 1
	var info := PeerInfo.new(peer_id, new_peer)
	_peers[peer_id] = info
	if role == Role.SERVER:
		info.car = _spawn_server_car(peer_id)
	print("Client connected from %s:%s assigned id=%s" % [
		new_peer.get_packet_ip(),
		new_peer.get_packet_port(),
		peer_id
	])
	new_peer.put_packet(_serialize_welcome(peer_id))
	_send_existing_player_states(peer_id)


func _send_existing_player_states(target_peer_id: int) -> void:
	var target_info: PeerInfo = _peers.get(target_peer_id)
	if not target_info:
		return
	for peer_id in _peers.keys():
		if peer_id == target_peer_id:
			continue
		var other: PeerInfo = _peers.get(peer_id)
		if other and other.last_snapshot:
			target_info.peer.put_packet(_serialize_player_state(peer_id, other.last_snapshot))


func _spawn_server_car(peer_id: int):
	if not _server_car_parent:
		return null
	var car: RaycastCar = PlayerCarScene.instantiate()
	if not car:
		return null
	car.name = "ServerCar_%s" % peer_id
	car.show_debug = false
	_server_car_parent.add_child(car)
	car.global_transform = Transform3D(Basis(), _get_spawn_position(peer_id))
	_cleanup_server_only_nodes(car)
	return car


func _get_spawn_position(peer_id: int) -> Vector3:
	var offset := peer_id * 6.0
	return Vector3(offset, 2.0, -20.0 + (peer_id % 2) * 5.0)


func _cleanup_server_only_nodes(car: Node) -> void:
	var remote := car.get_node_or_null("Car#RemoteTransform3D")
	if remote:
		remote.queue_free()
	var cam := car.get_node_or_null("Car_CameraPivot#Camera3D")
	if cam:
		cam.queue_free()
	var pivot := car.get_node_or_null("Car#CameraPivot")
	if pivot:
		pivot.queue_free()


func _send_snapshot_to_all(player_id: int, snapshot: CarSnapshot) -> void:
	var packet := _serialize_player_state(player_id, snapshot)
	for info in _peers.values():
		if info:
			info.peer.put_packet(packet)


func _check_peer_timeouts() -> void:
	if _peers.is_empty():
		return
	var now_msec := Time.get_ticks_msec()
	var to_remove: Array[int] = []
	for peer_id in _peers.keys():
		var info: PeerInfo = _peers.get(peer_id)
		if info and now_msec - info.last_seen_msec > PEER_TIMEOUT_MSEC:
			to_remove.append(peer_id)
	for peer_id in to_remove:
		_remove_peer(peer_id, true)


func _remove_peer(peer_id: int, notify_clients: bool) -> void:
	var info: PeerInfo = _peers.get(peer_id)
	if not info:
		return
	_peers.erase(peer_id)
	if info.car and is_instance_valid(info.car):
		info.car.queue_free()
	if notify_clients and _peers.size() > 0:
		var packet := _serialize_remove_player(peer_id)
		for other in _peers.values():
			if other:
				other.peer.put_packet(packet)
	print("Client %s removed" % peer_id)


func _apply_local_snapshot(snapshot: CarSnapshot) -> void:
	if _car:
		_car.queue_snapshot(snapshot)


func _apply_client_input_to_car() -> void:
	if _car:
		_car.set_input_state(_client_input)


func _serialize_input(state: CarInputState) -> PackedByteArray:
	var buffer := StreamPeerBuffer.new()
	buffer.big_endian = false
	buffer.put_u8(PACKET_INPUT)
	buffer.put_u32(state.tick)
	buffer.put_float(state.throttle)
	buffer.put_float(state.steer)
	buffer.put_u8(1 if state.handbrake else 0)
	buffer.put_u8(1 if state.brake else 0)
	return buffer.data_array


func _deserialize_input(packet: PackedByteArray) -> CarInputState:
	var buffer := StreamPeerBuffer.new()
	buffer.big_endian = false
	buffer.data_array = packet
	if buffer.get_available_bytes() < 1:
		return null
	var packet_type := buffer.get_u8()
	if packet_type != PACKET_INPUT:
		return null
	if buffer.get_available_bytes() < 4 + 4 + 4 + 1 + 1:
		return null
	var state := CarInputState.new()
	state.tick = buffer.get_u32()
	state.throttle = buffer.get_float()
	state.steer = buffer.get_float()
	state.handbrake = buffer.get_u8() == 1
	state.brake = buffer.get_u8() == 1
	return state


func _serialize_player_state(player_id: int, snapshot: CarSnapshot) -> PackedByteArray:
	var buffer := StreamPeerBuffer.new()
	buffer.big_endian = false
	buffer.put_u8(PACKET_PLAYER_STATE)
	buffer.put_u32(player_id)
	_write_snapshot_payload(buffer, snapshot)
	return buffer.data_array


func _deserialize_player_state(packet: PackedByteArray) -> PlayerStateData:
	var buffer := StreamPeerBuffer.new()
	buffer.big_endian = false
	buffer.data_array = packet
	if buffer.get_available_bytes() < 1:
		return null
	var packet_type := buffer.get_u8()
	if packet_type != PACKET_PLAYER_STATE:
		return null
	if buffer.get_available_bytes() < 4 + SNAPSHOT_PAYLOAD_BYTES:
		return null
	var data := PlayerStateData.new()
	data.player_id = buffer.get_u32()
	data.snapshot = _read_snapshot_payload(buffer)
	return data


func _serialize_welcome(peer_id: int) -> PackedByteArray:
	var buffer := StreamPeerBuffer.new()
	buffer.big_endian = false
	buffer.put_u8(PACKET_WELCOME)
	buffer.put_u32(peer_id)
	return buffer.data_array


func _deserialize_welcome(packet: PackedByteArray) -> int:
	var buffer := StreamPeerBuffer.new()
	buffer.big_endian = false
	buffer.data_array = packet
	if buffer.get_available_bytes() < 5:
		return 0
	var packet_type := buffer.get_u8()
	if packet_type != PACKET_WELCOME:
		return 0
	return buffer.get_u32()


func _serialize_remove_player(peer_id: int) -> PackedByteArray:
	var buffer := StreamPeerBuffer.new()
	buffer.big_endian = false
	buffer.put_u8(PACKET_REMOVE_PLAYER)
	buffer.put_u32(peer_id)
	return buffer.data_array


func _deserialize_remove_player(packet: PackedByteArray) -> int:
	var buffer := StreamPeerBuffer.new()
	buffer.big_endian = false
	buffer.data_array = packet
	if buffer.get_available_bytes() < 5:
		return 0
	var packet_type := buffer.get_u8()
	if packet_type != PACKET_REMOVE_PLAYER:
		return 0
	return buffer.get_u32()


func _write_snapshot_payload(buffer: StreamPeerBuffer, snapshot: CarSnapshot) -> void:
	buffer.put_u32(snapshot.tick)
	var origin := snapshot.transform.origin
	buffer.put_float(origin.x)
	buffer.put_float(origin.y)
	buffer.put_float(origin.z)
	var rotation := snapshot.transform.basis.get_rotation_quaternion()
	buffer.put_float(rotation.x)
	buffer.put_float(rotation.y)
	buffer.put_float(rotation.z)
	buffer.put_float(rotation.w)
	var lin := snapshot.linear_velocity
	buffer.put_float(lin.x)
	buffer.put_float(lin.y)
	buffer.put_float(lin.z)
	var ang := snapshot.angular_velocity
	buffer.put_float(ang.x)
	buffer.put_float(ang.y)
	buffer.put_float(ang.z)


func _read_snapshot_payload(buffer: StreamPeerBuffer) -> CarSnapshot:
	if buffer.get_available_bytes() < SNAPSHOT_PAYLOAD_BYTES:
		return null
	var snapshot := CarSnapshot.new()
	snapshot.tick = buffer.get_u32()
	var origin := Vector3(buffer.get_float(), buffer.get_float(), buffer.get_float())
	var rotation := Quaternion(buffer.get_float(), buffer.get_float(), buffer.get_float(), buffer.get_float())
	snapshot.transform = Transform3D(Basis(rotation), origin)
	snapshot.linear_velocity = Vector3(buffer.get_float(), buffer.get_float(), buffer.get_float())
	snapshot.angular_velocity = Vector3(buffer.get_float(), buffer.get_float(), buffer.get_float())
	return snapshot
