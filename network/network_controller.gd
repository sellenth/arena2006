extends Node

const DEFAULT_PORT := 45000
const PACKET_INPUT := 1
const PACKET_SNAPSHOT := 2

const CarInputState := preload("res://network/car_input_state.gd")
const CarSnapshot := preload("res://network/car_snapshot.gd")

enum Role { NONE, SERVER, CLIENT }

var role: Role = Role.NONE
var _car
var _tick := 0

# Server side state
var _udp_server: UDPServer
var _server_peer: PacketPeerUDP
var _server_input := CarInputState.new()

# Client side state
var _client_peer: PacketPeerUDP
var _client_input := CarInputState.new()


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
		print("Server listening on UDP %s" % DEFAULT_PORT)


func _start_client() -> void:
	_client_peer = PacketPeerUDP.new()
	var err = _client_peer.connect_to_host("127.0.0.1", DEFAULT_PORT)
	if err != OK:
		push_error("Failed to start UDP client (err %s)" % err)
	else:
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
			print("Client connected from %s:%s" % [new_peer.get_packet_ip(), new_peer.get_packet_port()])
			_server_peer = new_peer

	if _server_peer:
		while _server_peer.get_available_packet_count() > 0:
			var packet := _server_peer.get_packet()
			if _server_peer.get_packet_error() == OK:
				_handle_server_packet(packet)

	_tick += 1
	if _car:
		_car.set_input_state(_server_input)
		if _server_peer:
			var snapshot: CarSnapshot = _car.capture_snapshot(_tick)
			var packet := _serialize_snapshot(snapshot)
			_server_peer.put_packet(packet)


func _handle_server_packet(packet: PackedByteArray) -> void:
	if packet.is_empty():
		return
	var result := _deserialize_input(packet)
	if result:
		if result.tick >= _server_input.tick:
			_server_input.copy_from(result)


func _process_client() -> void:
	_tick += 1
	var local_input := _collect_local_input()
	local_input.tick = _tick
	_client_input.copy_from(local_input)

	if _car:
		_car.set_input_state(_client_input)

	if _client_peer:
		var packet := _serialize_input(local_input)
		_client_peer.put_packet(packet)
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
	return state


func _poll_client_packets() -> void:
	if not _client_peer:
		return

	while _client_peer.get_available_packet_count() > 0:
		var packet := _client_peer.get_packet()
		if _client_peer.get_packet_error() != OK or packet.is_empty():
			continue
		var snapshot := _deserialize_snapshot(packet)
		if snapshot and _car:
			_car.queue_snapshot(snapshot)


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
	if buffer.get_available_bytes() < 4 + 12 + 16 + 12 + 12:
		return null
	var snapshot := CarSnapshot.new()
	snapshot.tick = buffer.get_u32()
	var origin := Vector3(buffer.get_float(), buffer.get_float(), buffer.get_float())
	var rotation := Quaternion(buffer.get_float(), buffer.get_float(), buffer.get_float(), buffer.get_float())
	snapshot.transform = Transform3D(Basis(rotation), origin)
	snapshot.linear_velocity = Vector3(buffer.get_float(), buffer.get_float(), buffer.get_float())
	snapshot.angular_velocity = Vector3(buffer.get_float(), buffer.get_float(), buffer.get_float())
	return snapshot
