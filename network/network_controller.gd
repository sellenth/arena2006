extends Node

const DEFAULT_PORT := 45000
const PACKET_INPUT := 1
const PACKET_SNAPSHOT := 2
const PACKET_PLAYER_STATE := 3
const PACKET_WELCOME := 4
const PACKET_REMOVE_PLAYER := 5

const PEER_TIMEOUT_MSEC := 5000
const SNAPSHOT_PAYLOAD_BYTES := 4 + 12 + 16 + 12 + 12

const CarInputState := preload("res://network/car_input_state.gd")
const CarSnapshot := preload("res://network/car_snapshot.gd")

signal player_state_updated(player_id: int, snapshot: CarSnapshot)
signal player_disconnected(player_id: int)

class PeerInfo:
	var id := 0
	var peer: PacketPeerUDP
	var input_state := CarInputState.new()
	var last_snapshot: CarSnapshot
	var last_seen_msec := 0

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
var _controller_peer_id := 0
var _server_input := CarInputState.new()

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
	match role:
		Role.SERVER:
			_car.set_input_state(_server_input)
		Role.CLIENT:
			_car.set_input_state(_client_input)
		_:
			pass


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
		_controller_peer_id = 0
		print("Server listening on UDP %s" % DEFAULT_PORT)


func _start_client() -> void:
	_client_peer = PacketPeerUDP.new()
	var err = _client_peer.connect_to_host("127.0.0.1", DEFAULT_PORT)
	if err != OK:
		push_error("Failed to start UDP client (err %s)" % err)
	else:
		_client_id = 0
		_remote_player_snapshots.clear()
		print("Client connecting to 127.0.0.1:%s" % DEFAULT_PORT)


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
	if _car and _peers.size() > 0:
		var controller_state := _get_controller_input_state()
		if controller_state:
			_car.set_input_state(controller_state)
		var snapshot: CarSnapshot = _car.capture_snapshot(_tick)
		var packet := _serialize_snapshot(snapshot)
		for peer_info in _peers.values():
			if peer_info:
				peer_info.peer.put_packet(packet)

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
			var result := _deserialize_input(packet)
			if result:
				if result.tick >= peer_info.input_state.tick:
					peer_info.input_state.copy_from(result)
					if peer_id == _controller_peer_id:
						_server_input.copy_from(result)
		PACKET_PLAYER_STATE:
			var state: PlayerStateData = _deserialize_player_state(packet)
			if state and state.snapshot:
				var snapshot: CarSnapshot = state.snapshot
				peer_info.last_snapshot = snapshot
				_broadcast_player_state(peer_id, snapshot)
		_:
			pass


func _process_client() -> void:
	_tick += 1
	var local_input := _collect_local_input()
	local_input.tick = _tick
	_client_input.copy_from(local_input)

	if _car:
		_car.set_input_state(_client_input)

	if _client_peer:
		_client_peer.put_packet(_serialize_input(local_input))
		if _client_id != 0 and _car:
			var snapshot: CarSnapshot = _car.capture_snapshot(_tick)
			var state_packet := _serialize_player_state(_client_id, snapshot)
			_client_peer.put_packet(state_packet)
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
			PACKET_SNAPSHOT:
				var snapshot := _deserialize_snapshot(packet)
				if snapshot and _car:
					_car.queue_snapshot(snapshot)
			PACKET_WELCOME:
				var new_id := _deserialize_welcome(packet)
				if new_id != 0:
					_client_id = new_id
			PACKET_PLAYER_STATE:
				var remote_state: PlayerStateData = _deserialize_player_state(packet)
				if remote_state and remote_state.snapshot:
					var remote_id: int = remote_state.player_id
					if remote_id != _client_id:
						var remote_snapshot: CarSnapshot = remote_state.snapshot
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
	if _controller_peer_id == 0:
		_controller_peer_id = peer_id
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


func _broadcast_player_state(source_peer_id: int, snapshot: CarSnapshot) -> void:
	var packet := _serialize_player_state(source_peer_id, snapshot)
	for peer_id in _peers.keys():
		if peer_id == source_peer_id:
			continue
		var info: PeerInfo = _peers.get(peer_id)
		if info:
			info.peer.put_packet(packet)


func _get_controller_input_state() -> CarInputState:
	if _controller_peer_id != 0 and _peers.has(_controller_peer_id):
		_server_input.copy_from(_peers[_controller_peer_id].input_state)
		return _server_input
	return null


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
	if _controller_peer_id == peer_id:
		_controller_peer_id = _select_next_controller()
	if notify_clients and _peers.size() > 0:
		var packet := _serialize_remove_player(peer_id)
		for other in _peers.values():
			if other:
				other.peer.put_packet(packet)
	print("Client %s removed" % peer_id)


func _select_next_controller() -> int:
	for peer_id in _peers.keys():
		return peer_id
	return 0


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


func _serialize_snapshot(snapshot: CarSnapshot) -> PackedByteArray:
	var buffer := StreamPeerBuffer.new()
	buffer.big_endian = false
	buffer.put_u8(PACKET_SNAPSHOT)
	_write_snapshot_payload(buffer, snapshot)
	return buffer.data_array


func _deserialize_snapshot(packet: PackedByteArray) -> CarSnapshot:
	var buffer := StreamPeerBuffer.new()
	buffer.big_endian = false
	buffer.data_array = packet
	if buffer.get_available_bytes() < 1:
		return null
	var packet_type := buffer.get_u8()
	if packet_type != PACKET_SNAPSHOT:
		return null
	return _read_snapshot_payload(buffer)


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
