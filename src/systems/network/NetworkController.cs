using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

public partial class NetworkController : Node
{
	private const int DefaultPort = 45000;
	private const int PeerTimeoutMsec = 5000;

	[Signal] public delegate void PlayerStateUpdatedEventHandler(int playerId, PlayerStateSnapshot snapshot);
	[Signal] public delegate void PlayerDisconnectedEventHandler(int playerId);
	[Signal] public delegate void VehicleStateUpdatedEventHandler(int vehicleId, VehicleStateSnapshot snapshot);
	[Signal] public delegate void VehicleDespawnedEventHandler(int vehicleId);

	private partial class PeerInfo : GodotObject
	{
		public int Id { get; set; }
		public PacketPeerUdp Peer { get; set; }
		public CarInputState CarInputState { get; set; } = new CarInputState();
		public PlayerInputState PlayerInputState { get; set; } = new PlayerInputState();
		public CarSnapshot LastCarSnapshot { get; set; }
		public PlayerSnapshot LastPlayerSnapshot { get; set; }
		public long LastSeenMsec { get; set; }
		public PlayerCharacter PlayerCharacter { get; set; }
		public PlayerMode Mode { get; set; } = PlayerMode.Foot;
		public Transform3D PlayerRestTransform { get; set; } = Transform3D.Identity;
		public int ControlledVehicleId { get; set; }

		public PeerInfo(int peerId, PacketPeerUdp peerRef)
		{
			Id = peerId;
			Peer = peerRef;
			LastSeenMsec = (long)Time.GetTicksMsec();
		}

		public void Touch()
		{
			LastSeenMsec = (long)Time.GetTicksMsec();
		}
	}

	private partial class VehicleInfo : GodotObject
	{
		public int Id { get; set; }
		public RaycastCar Car { get; set; }
		public VehicleSeat DriverSeat { get; set; }
		public int OccupantPeerId { get; set; }
		public VehicleStateSnapshot LastSnapshot { get; set; }

		public ulong InstanceId => Car?.GetInstanceId() ?? 0;
	}

private readonly struct PlayerPredictionSample
{
	public int Tick { get; }
	public Transform3D Transform { get; }
	public Vector3 Velocity { get; }

	public PlayerPredictionSample(int tick, Transform3D transform, Vector3 velocity)
	{
		Tick = tick;
		Transform = transform;
		Velocity = velocity;
	}
}

	private NetworkRole _role = NetworkRole.None;
	private RaycastCar _car;
	private PlayerCharacter _playerCharacter;
	private int _tick = 0;

	private UdpServer _udpServer;
	private Godot.Collections.Dictionary<int, PeerInfo> _peers = new Godot.Collections.Dictionary<int, PeerInfo>();
	private int _nextPeerId = 1;
	private Node3D _serverCarParent;
	private Node3D _serverPlayerParent;

	private PacketPeerUdp _clientPeer;
	private CarInputState _clientInput = new CarInputState();
	private PlayerInputState _clientPlayerInput = new PlayerInputState();
	private int _clientId = 0;
	private int _clientVehicleId = 0;
	private int _lastAcknowledgedPlayerInputTick = 0;
	private Godot.Collections.Dictionary<int, PlayerStateSnapshot> _remotePlayerSnapshots = new Godot.Collections.Dictionary<int, PlayerStateSnapshot>();
	public PlayerMode CurrentClientMode { get; private set; } = PlayerMode.Foot;
	public event Action<PlayerMode> ClientModeChanged;
	public event Action<RaycastCar> LocalCarChanged;
	public bool IsServer => _role == NetworkRole.Server;
	public bool IsClient => _role == NetworkRole.Client;
	public int ClientPeerId => _clientId;
	public RaycastCar LocalCar => _car;

	private PackedScene _playerCharacterScene;
	private readonly System.Collections.Generic.Dictionary<int, VehicleInfo> _serverVehicles = new System.Collections.Generic.Dictionary<int, VehicleInfo>();
	private readonly System.Collections.Generic.Dictionary<ulong, int> _vehicleIdByInstance = new System.Collections.Generic.Dictionary<ulong, int>();
	private readonly System.Collections.Generic.List<VehicleStateSnapshot> _vehicleSnapshotBuffer = new System.Collections.Generic.List<VehicleStateSnapshot>();
	private readonly System.Collections.Generic.List<PlayerPredictionSample> _playerPredictionHistory = new System.Collections.Generic.List<PlayerPredictionSample>();
	private int _nextVehicleId = 1;
	private bool _respawnPointsCached = false;
	private bool _hasFallbackSpawn = false;
	private Transform3D _fallbackSpawnTransform = Transform3D.Identity;
	private TestInputMode _testInputMode = TestInputMode.None;
	private int _testInputStartTick = -1;
	private const int MaxPredictionHistory = 256;
	private const float PlayerSnapDistance = 2.5f;
	private const float PlayerSmallCorrectionBlend = 0.25f;

	public override void _Ready()
	{
		_playerCharacterScene = GD.Load<PackedScene>("res://src/entities/player/player_character.tscn");
		Engine.PhysicsTicksPerSecond = 60;
		_role = CmdLineArgsManager.GetNetworkRole();
		InitializeTestInputScript();

		switch (_role)
		{
			case NetworkRole.Server:
				StartServer();
				break;
			case NetworkRole.Client:
				StartClient();
				break;
		}

		SetPhysicsProcess(_role != NetworkRole.None);
	}

	private void UpdateLocalCarReference(RaycastCar car)
	{
		if (_car == car)
			return;

		_car = car;
		LocalCarChanged?.Invoke(_car);
	}

	public void RegisterLocalPlayerCar(RaycastCar car)
	{
		if (car == null)
			return;

		GD.Print($"NetworkController: RegisterLocalPlayerCar role={_role}, car={car.Name}");
		UpdateLocalCarReference(car);
		if (_role == NetworkRole.Client)
		{
			_car?.ConfigureForLocalDriver(false);
			ApplyClientInputToCar();
		}
	}

	public void RegisterAuthoritativeVehicle(RaycastCar car)
	{
		if (_role != NetworkRole.Server || car == null)
			return;

		var id = _nextVehicleId++;
		var info = new VehicleInfo
		{
			Id = id,
			Car = car,
			DriverSeat = FindDriverSeat(car),
			OccupantPeerId = 0
		};

		_serverVehicles[id] = info;
		_vehicleIdByInstance[car.GetInstanceId()] = id;
		car.TreeExiting += () => OnServerVehicleExiting(id);
		GD.Print($"NetworkController: Server vehicle registered id={id} name={car.Name}");
	}

	public void RegisterPlayerCharacter(PlayerCharacter player)
	{
		GD.Print($"NetworkController: RegisterPlayerCharacter called, role={_role}, player={player?.Name ?? "null"}");
		_playerCharacter = player;
		if (_role == NetworkRole.Client)
		{
			player.ConfigureAuthority(true);
			player.SetCameraActive(CurrentClientMode == PlayerMode.Foot);
			ApplyClientInputToPlayer();
		}
	}

	public void AttachLocalVehicle(int vehicleId, RaycastCar car)
	{
		if (!IsClient || car == null)
			return;

		_clientVehicleId = vehicleId;
		UpdateLocalCarReference(car);
		_car?.ConfigureForLocalDriver(true);
		GD.Print($"NetworkController: Local client now controls vehicle {vehicleId}");
	}

	public void DetachLocalVehicle(int vehicleId, RaycastCar car)
	{
		if (!IsClient)
			return;

		if (_clientVehicleId == vehicleId && _car != null && car == _car)
		{
			_car.ConfigureForLocalDriver(false);
			UpdateLocalCarReference(null);
		}

		if (_clientVehicleId == vehicleId)
			_clientVehicleId = 0;
	}


	private void StartServer()
	{
		_udpServer = new UdpServer();
		var err = _udpServer.Listen(DefaultPort);
		if (err != Error.Ok)
		{
			GD.PushError($"Failed to bind UDP server on port {DefaultPort} (err {err})");
		}
		else
		{
			_peers.Clear();
			_nextPeerId = 1;
			_serverCarParent = GetTree().CurrentScene.GetNodeOrNull<Node3D>("AuthoritativeCars");
			if (_serverCarParent == null)
				_serverCarParent = GetTree().CurrentScene as Node3D;
			_serverPlayerParent = GetTree().CurrentScene.GetNodeOrNull<Node3D>("AuthoritativePlayers");
			if (_serverPlayerParent == null)
				_serverPlayerParent = _serverCarParent;
			GD.Print($"Server listening on UDP {DefaultPort}");
			GD.Print("TEST_EVENT: SERVER_STARTED");
		}
	}

	private void StartClient()
	{
		_clientPeer = new PacketPeerUdp();
		var clientIp = CmdLineArgsManager.GetClientIp();
		var err = _clientPeer.ConnectToHost(clientIp, DefaultPort);
		if (err != Error.Ok)
		{
			GD.PushError($"Failed to start UDP client (err {err})");
		}
		else
		{
			_clientId = 0;
			_remotePlayerSnapshots.Clear();
			_playerPredictionHistory.Clear();
			_lastAcknowledgedPlayerInputTick = 0;
			CurrentClientMode = PlayerMode.Foot;
			GD.Print($"Client connecting to {clientIp}:{DefaultPort}");
			ApplyClientInputToCar();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("quit") || Input.IsKeyPressed(Key.Escape))
		{
			GetTree().Quit();
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		switch (_role)
		{
			case NetworkRole.Server:
				ProcessServer();
				break;
			case NetworkRole.Client:
				ProcessClient();
				break;
		}
	}

	private void ProcessServer()
	{
		if (_udpServer == null) return;

		_udpServer.Poll();
		while (_udpServer.IsConnectionAvailable())
		{
			var newPeer = _udpServer.TakeConnection();
			if (newPeer != null)
				RegisterPeer(newPeer);
		}

		foreach (var peerId in _peers.Keys.ToList())
		{
			var info = _peers[peerId];
			if (info == null) continue;
			while (info.Peer.GetAvailablePacketCount() > 0)
			{
				var packet = info.Peer.GetPacket();
				if (info.Peer.GetPacketError() == Error.Ok)
					HandleServerPacket(peerId, packet);
			}
		}

		_tick++;
		UpdateServerPlayers();
		CheckPeerTimeouts();
		BroadcastVehicleStates();
	}

	private void HandleServerPacket(int peerId, byte[] packet)
	{
		if (packet.Length == 0) return;
		if (!_peers.ContainsKey(peerId)) return;
		var peerInfo = _peers[peerId];
		peerInfo.Touch();
		var packetType = packet[0];
		switch (packetType)
		{
			case NetworkSerializer.PacketCarInput:
				var carState = NetworkSerializer.DeserializeCarInput(packet);
				if (carState != null && carState.Tick >= peerInfo.CarInputState.Tick)
					peerInfo.CarInputState.CopyFrom(carState);
				break;
			case NetworkSerializer.PacketPlayerInput:
				var playerState = NetworkSerializer.DeserializePlayerInput(packet);
				if (playerState != null && playerState.Tick >= peerInfo.PlayerInputState.Tick)
					peerInfo.PlayerInputState.CopyFrom(playerState);
				break;
		}
	}

	private void UpdateServerPlayers()
	{
		foreach (var peerId in _peers.Keys.ToList())
		{
			if (!_peers.TryGetValue(peerId, out var info) || info == null)
				continue;

			EnsureServerEntities(peerId, info);

			switch (info.Mode)
			{
				case PlayerMode.Vehicle:
					var vehicle = GetVehicleInfo(info.ControlledVehicleId);
					if (info.CarInputState.VehicleId != info.ControlledVehicleId)
						info.CarInputState.VehicleId = info.ControlledVehicleId;
					if (vehicle?.Car != null)
					{
						vehicle.Car.SetInputState(info.CarInputState);
						if (info.CarInputState.Interact)
						{
							TryExitVehicle(info);
							info.CarInputState.Interact = false;
						}
						if (info.CarInputState.Respawn)
						{
							vehicle.Car.Respawn();
							info.CarInputState.Respawn = false;
						}
					}
					else
					{
						info.Mode = PlayerMode.Foot;
					}

					if (info.PlayerCharacter != null)
					{
						info.PlayerCharacter.SetWorldActive(false);
					}
					break;
				case PlayerMode.Foot:
					if (info.PlayerCharacter != null)
					{
						info.PlayerCharacter.SetWorldActive(true);
						info.PlayerCharacter.ConfigureAuthority(true);
						info.PlayerCharacter.SetInputState(info.PlayerInputState);
						if (info.PlayerInputState.Interact)
						{
							TryEnterVehicle(info);
							info.PlayerInputState.Interact = false;
						}
					}
					break;
			}
		}

		foreach (var peerId in _peers.Keys.ToList())
		{
			if (!_peers.TryGetValue(peerId, out var info) || info == null)
				continue;

			var vehicleInfo = GetVehicleInfo(info.ControlledVehicleId);
			var carSnapshot = vehicleInfo?.Car?.CaptureSnapshot(_tick);
			var playerSnapshot = info.PlayerCharacter?.CaptureSnapshot(_tick);
			info.LastCarSnapshot = carSnapshot;
			info.LastPlayerSnapshot = playerSnapshot;

			var snapshot = new PlayerStateSnapshot
			{
				Tick = _tick,
				Mode = info.Mode,
				VehicleId = info.ControlledVehicleId,
				LastProcessedInputTick = info.PlayerInputState?.Tick ?? 0,
				CarSnapshot = carSnapshot,
				PlayerSnapshot = playerSnapshot
			};
			SendSnapshotToAll(peerId, snapshot);
		}
	}

	private void EnsureServerEntities(int peerId, PeerInfo info)
	{
		if (info.PlayerCharacter == null)
		{
			info.PlayerCharacter = SpawnServerPlayerCharacter(peerId, null);
		}
	}

	private bool TryEnterVehicle(PeerInfo info)
	{
		if (info == null || info.PlayerCharacter == null)
			return false;

		var vehicle = FindAvailableVehicle(info.PlayerCharacter.GlobalTransform.Origin, out var seat);
		if (vehicle == null || seat == null)
			return false;

		info.PlayerRestTransform = info.PlayerCharacter.GlobalTransform;
		info.ControlledVehicleId = vehicle.Id;
		vehicle.OccupantPeerId = info.Id;
		info.Mode = PlayerMode.Vehicle;
		info.PlayerCharacter.SetWorldActive(false);
		return true;
	}

	private bool TryExitVehicle(PeerInfo info)
	{
		if (info == null || info.PlayerCharacter == null)
			return false;

		var vehicle = GetVehicleInfo(info.ControlledVehicleId);
		if (vehicle?.Car == null)
			return false;

		var exitTransform = GetSeatExitTransform(vehicle);
		info.Mode = PlayerMode.Foot;
		info.ControlledVehicleId = 0;
		vehicle.OccupantPeerId = 0;
		info.PlayerCharacter.TeleportTo(new Transform3D( info.PlayerCharacter.GlobalBasis, exitTransform.Origin));
		var yaw = vehicle.Car.GlobalTransform.Basis.GetEuler().Y;
		info.PlayerCharacter.SetYawPitch(yaw, 0f);
		info.PlayerCharacter.SetWorldActive(true);
		return true;
	}

	private Transform3D GetSeatExitTransform(VehicleInfo vehicle)
	{
		var carTransform = vehicle.Car?.GlobalTransform ?? Transform3D.Identity;
		var seat = vehicle.DriverSeat;
		if (seat != null)
		{
			var exit = seat.GetExitPosition();
			return new Transform3D(carTransform.Basis, exit);
		}

		Debug.Assert(false, "Failed to exit seat");

		var fallback = carTransform.Origin - carTransform.Basis.Z * 3f + Vector3.Up;
		return new Transform3D(carTransform.Basis, fallback);
	}

	private VehicleSeat FindDriverSeat(Node node)
	{
		if (node == null)
			return null;

		foreach (var child in node.GetChildren())
		{
			if (child is VehicleSeat seat && seat.IsDriverSeat)
				return seat;
			if (child is Node childNode)
			{
				var nested = FindDriverSeat(childNode);
				if (nested != null)
					return nested;
			}
		}

		return null;
	}

	private VehicleInfo GetVehicleInfo(int vehicleId)
	{
		if (vehicleId == 0)
			return null;

		return _serverVehicles.TryGetValue(vehicleId, out var info) ? info : null;
	}

	private VehicleInfo FindAvailableVehicle(Vector3 position, out VehicleSeat seat)
	{
		seat = null;
		VehicleInfo bestVehicle = null;
		var bestDistance = float.MaxValue;

		foreach (var vehicle in _serverVehicles.Values)
		{
			var candidateSeat = vehicle.DriverSeat;
			if (candidateSeat == null)
				continue;
			if (vehicle.OccupantPeerId != 0)
				continue;

			var distance = candidateSeat.GetSeatPosition().DistanceTo(position);
			if (distance > candidateSeat.InteractionRadius)
				continue;

			if (distance < bestDistance)
			{
				bestDistance = distance;
				bestVehicle = vehicle;
				seat = candidateSeat;
			}
		}

		return bestVehicle;
	}

	private void ProcessClient()
	{
		_tick++;
		switch (CurrentClientMode)
		{
			case PlayerMode.Vehicle:
			{
				if (_clientVehicleId == 0)
					break;

				var localInput = CollectLocalCarInput();
				localInput.Tick = _tick;
				_clientInput.CopyFrom(localInput);
				_car?.SetInputState(_clientInput);
				if (_clientPeer != null)
					_clientPeer.PutPacket(NetworkSerializer.SerializeCarInput(localInput));
				break;
			}
			case PlayerMode.Foot:
			{
				var localInput = CollectLocalPlayerInput();
				localInput.Tick = _tick;
				_clientPlayerInput.CopyFrom(localInput);
				_playerCharacter?.SetInputState(_clientPlayerInput);
				if (_clientPeer != null)
					_clientPeer.PutPacket(NetworkSerializer.SerializePlayerInput(localInput));
				break;
			}
		}

		if (_clientPeer != null)
			PollClientPackets();
	}

	private CarInputState CollectLocalCarInput()
	{
		if (_testInputMode != TestInputMode.None)
			return CollectTestInput();

		var state = new CarInputState();
		var forward = Input.GetActionStrength("accelerate");
		var backward = Input.GetActionStrength("decelerate");
		state.Throttle = Mathf.Clamp(forward - backward, -1.0f, 1.0f);
		var right = Input.GetActionStrength("turn_right");
		var left = Input.GetActionStrength("turn_left");
		state.Steer = Mathf.Clamp(left - right, -1.0f, 1.0f);
		state.Handbrake = Input.IsActionPressed("handbreak");
		state.Brake = Input.IsActionPressed("brake");
		state.Respawn = Input.IsKeyPressed(Key.R);
		state.Interact = Input.IsActionJustPressed("interact");
		state.VehicleId = _clientVehicleId;
		
		/* VERBOSE PRINT
		if (Mathf.Abs(state.Throttle) > 0.1f || Mathf.Abs(state.Steer) > 0.1f || state.Handbrake || state.Brake || state.Respawn)
		{
			GD.Print($"CLIENT input tick={_tick + 1} throttle={state.Throttle:F2} steer={state.Steer:F2} hb={state.Handbrake} br={state.Brake} respawn={state.Respawn}");
		}*/
		return state;
	}

	private PlayerInputState CollectLocalPlayerInput()
	{
		if (_playerCharacter == null)
			return new PlayerInputState();

		var state = _playerCharacter.CollectClientInputState();
		return state;
	}

	public void RecordLocalPlayerPrediction(int tick, Transform3D transform, Vector3 velocity)
	{
		if (!IsClient || CurrentClientMode != PlayerMode.Foot || tick <= 0)
			return;

		var sample = new PlayerPredictionSample(tick, transform, velocity);
		if (_playerPredictionHistory.Count > 0 && _playerPredictionHistory[_playerPredictionHistory.Count - 1].Tick == tick)
		{
			_playerPredictionHistory[_playerPredictionHistory.Count - 1] = sample;
		}
		else
		{
			_playerPredictionHistory.Add(sample);
			if (_playerPredictionHistory.Count > MaxPredictionHistory)
				_playerPredictionHistory.RemoveAt(0);
		}
	}

	private bool TryConsumePrediction(int tick, out PlayerPredictionSample sample)
	{
		sample = default;
		if (tick <= 0 || _playerPredictionHistory.Count == 0)
			return false;

		for (var i = 0; i < _playerPredictionHistory.Count; i++)
		{
			if (_playerPredictionHistory[i].Tick == tick)
			{
				sample = _playerPredictionHistory[i];
				_playerPredictionHistory.RemoveRange(0, i + 1);
				return true;
			}

			if (_playerPredictionHistory[i].Tick > tick)
				break;
		}

		// trim old samples if they lag behind acknowledgements
		while (_playerPredictionHistory.Count > 0 && _playerPredictionHistory[0].Tick < tick - 32)
		{
			_playerPredictionHistory.RemoveAt(0);
		}

		return false;
	}

	private void ReconcileLocalPlayer(PlayerSnapshot serverSnapshot, int processedInputTick)
	{
		if (_playerCharacter == null || serverSnapshot == null)
			return;

		var hasPrediction = TryConsumePrediction(processedInputTick, out var predicted);

		if (!hasPrediction)
		{
			ApplyServerSnap(serverSnapshot);
			return;
		}

		var positionError = serverSnapshot.Transform.Origin - predicted.Transform.Origin;
		var errorMagnitude = positionError.Length();

		if (errorMagnitude > PlayerSnapDistance)
		{
			ApplyServerSnap(serverSnapshot);
			return;
		}
		else
		{
			var blend = PlayerSmallCorrectionBlend;
			var transform = _playerCharacter.GlobalTransform;
			transform.Origin += positionError * blend;
			var currentRot = transform.Basis.GetRotationQuaternion();
			var targetRot = serverSnapshot.Transform.Basis.GetRotationQuaternion();
			transform.Basis = new Basis(currentRot.Slerp(targetRot, blend));
			_playerCharacter.GlobalTransform = transform;
			_playerCharacter.Velocity = _playerCharacter.Velocity.Lerp(serverSnapshot.Velocity, blend);
		}

		_lastAcknowledgedPlayerInputTick = Math.Max(_lastAcknowledgedPlayerInputTick, processedInputTick);
	}

	private void ApplyServerSnap(PlayerSnapshot serverSnapshot)
	{
		_playerCharacter.GlobalTransform = serverSnapshot.Transform;
		_playerCharacter.Velocity = serverSnapshot.Velocity;
		_playerPredictionHistory.Clear();
	}

	private void PollClientPackets()
	{
		if (_clientPeer == null) return;

		while (_clientPeer.GetAvailablePacketCount() > 0)
		{
			var packet = _clientPeer.GetPacket();
			if (_clientPeer.GetPacketError() != Error.Ok || packet.Length == 0)
				continue;
			var packetType = packet[0];
			switch (packetType)
			{
				case NetworkSerializer.PacketWelcome:
					var newId = NetworkSerializer.DeserializeWelcome(packet);
					if (newId != 0)
					{
						_clientId = newId;
						GD.Print($"CLIENT received welcome, assigned ID: {_clientId}");
						GD.Print($"TEST_EVENT: CLIENT_RECEIVED_WELCOME id={_clientId}");
					}
					break;
				case NetworkSerializer.PacketPlayerState:
					var remoteState = NetworkSerializer.DeserializePlayerState(packet);
					if (remoteState?.Snapshot != null)
					{
						var remoteId = remoteState.PlayerId;
						var remoteSnapshot = remoteState.Snapshot;
						if (remoteId == _clientId)
						{
							ApplyLocalSnapshot(remoteSnapshot);
						}
						else
						{
							_remotePlayerSnapshots[remoteId] = remoteSnapshot;
							EmitSignal(SignalName.PlayerStateUpdated, remoteId, remoteSnapshot);
						}
					}
					break;
				case NetworkSerializer.PacketRemovePlayer:
					var removedId = NetworkSerializer.DeserializeRemovePlayer(packet);
					if (removedId != 0)
					{
						_remotePlayerSnapshots.Remove(removedId);
						EmitSignal(SignalName.PlayerDisconnected, removedId);
					}
					break;
				case NetworkSerializer.PacketVehicleState:
					var vehicleStates = NetworkSerializer.DeserializeVehicleStates(packet);
					if (vehicleStates != null)
					{
						foreach (var snapshot in vehicleStates)
							EmitSignal(SignalName.VehicleStateUpdated, snapshot.VehicleId, snapshot);
					}
					break;
				case NetworkSerializer.PacketVehicleDespawn:
					var removedVehicleId = NetworkSerializer.DeserializeVehicleDespawn(packet);
					if (removedVehicleId != 0)
						EmitSignal(SignalName.VehicleDespawned, removedVehicleId);
					break;
			}
		}
	}

	private void RegisterPeer(PacketPeerUdp newPeer)
	{
		var peerId = _nextPeerId++;
		var info = new PeerInfo(peerId, newPeer);
		_peers[peerId] = info;
		if (_role == NetworkRole.Server)
		{
			info.PlayerCharacter = SpawnServerPlayerCharacter(peerId, null);
		}
		GD.Print($"Client connected from {newPeer.GetPacketIP()}:{newPeer.GetPacketPort()} assigned id={peerId}");
		GD.Print($"TEST_EVENT: CLIENT_CONNECTED id={peerId}");
		newPeer.PutPacket(NetworkSerializer.SerializeWelcome(peerId));
		SendExistingPlayerStates(peerId);
		SendExistingVehicleStates(peerId);
	}

	private void SendExistingPlayerStates(int targetPeerId)
	{
		if (!_peers.ContainsKey(targetPeerId)) return;
		var targetInfo = _peers[targetPeerId];
		foreach (var peerId in _peers.Keys)
		{
			if (peerId == targetPeerId) continue;
			var other = _peers[peerId];
			if (other == null)
				continue;
			if (other.LastCarSnapshot == null && other.LastPlayerSnapshot == null)
				continue;
			var snapshot = new PlayerStateSnapshot
			{
				Tick = _tick,
				Mode = other.Mode,
				VehicleId = other.ControlledVehicleId,
				LastProcessedInputTick = other.PlayerInputState?.Tick ?? 0,
				CarSnapshot = other.LastCarSnapshot,
				PlayerSnapshot = other.LastPlayerSnapshot
			};
			targetInfo.Peer.PutPacket(NetworkSerializer.SerializePlayerState(peerId, snapshot));
		}
	}

	private void SendExistingVehicleStates(int targetPeerId)
	{
		if (!_peers.ContainsKey(targetPeerId))
			return;

		if (_serverVehicles.Count == 0)
			return;

		var list = new System.Collections.Generic.List<VehicleStateSnapshot>();
		foreach (var vehicle in _serverVehicles.Values)
		{
			if (vehicle.LastSnapshot != null)
				list.Add(vehicle.LastSnapshot);
		}

		if (list.Count == 0)
			return;

		var packet = NetworkSerializer.SerializeVehicleStates(list);
		var target = _peers[targetPeerId];
		target?.Peer.PutPacket(packet);
	}

	private PlayerCharacter SpawnServerPlayerCharacter(int peerId, RaycastCar ownerCar)
	{
		if (_serverPlayerParent == null || _playerCharacterScene == null)
			return null;

		var player = _playerCharacterScene.Instantiate<PlayerCharacter>();
		if (player == null)
			return null;

		player.AutoRegisterWithNetwork = false;
		player.Name = $"ServerPlayer_{peerId}";
		player.SetCameraActive(false);
		player.ConfigureAuthority(true);
		player.SetWorldActive(false);
		_serverPlayerParent.AddChild(player);

		var transform = ownerCar?.GlobalTransform ?? GetSpawnTransform(peerId);
		transform.Origin += Vector3.Up * 1.2f;
		player.TeleportTo(transform);
		return player;
	}

	private Transform3D GetSpawnTransform(int peerId)
	{
		EnsureRespawnPointsCached();
		var manager = RespawnManager.Instance;
		var contextNode = _serverCarParent as Node3D ?? GetTree().CurrentScene as Node3D;
		var otherPositions = _serverVehicles.Values
			.Where(vehicle => vehicle?.Car != null)
			.Select(vehicle => vehicle.Car.GlobalTransform.Origin)
			.ToList();

		var query = RespawnManager.SpawnQuery.Create(contextNode);
		query.OpponentPositions = otherPositions;
		query.AllyPositions = System.Array.Empty<Vector3>();
		if (_hasFallbackSpawn)
			query.FallbackTransform = _fallbackSpawnTransform;

		if (manager.TryGetBestSpawnTransform(query, out var transform))
			return transform;

		return _hasFallbackSpawn ? _fallbackSpawnTransform : Transform3D.Identity;
	}

	private void EnsureRespawnPointsCached()
	{
		if (_respawnPointsCached)
			return;

		_respawnPointsCached = true;
		var manager = RespawnManager.Instance;
		var tree = GetTree();
		if (tree == null)
			return;

		foreach (var groupName in new[] { "RespawnPoints", "respawn_points", "SpawnPoints" })
		{
			var nodes = tree.GetNodesInGroup(groupName);
			foreach (Node node in nodes)
			{
				if (node is Node3D node3D)
				{
					manager.RegisterSpawnPoint(node3D, 1.0f, 0.5f, 0.35f);
					if (!_hasFallbackSpawn)
					{
						_fallbackSpawnTransform = node3D.GlobalTransform;
						_hasFallbackSpawn = true;
					}
				}
			}
		}

		var root = tree.Root;
		var carSpawn = root.FindChild("CarSpawnPoint", true, false) as Marker3D;
		carSpawn ??= root.GetNodeOrNull<Marker3D>("/root/GameRoot/CarSpawnPoint");
		if (carSpawn != null)
		{
			manager.RegisterSpawnPoint(carSpawn, 1.15f, 0.4f, 0.2f);
			_fallbackSpawnTransform = carSpawn.GlobalTransform;
			_hasFallbackSpawn = true;
		}
	}

	private enum TestInputMode
	{
		None,
		DriveRespawn
	}

	private void InitializeTestInputScript()
	{
		var scriptName = System.Environment.GetEnvironmentVariable("ARENA_TEST_INPUT_SCRIPT");
		if (string.IsNullOrWhiteSpace(scriptName))
			return;

		if (scriptName.Equals("drive_respawn", System.StringComparison.OrdinalIgnoreCase))
		{
			_testInputMode = TestInputMode.DriveRespawn;
			GD.Print("TEST_EVENT: INPUT_SCRIPT drive_respawn enabled");
		}
	}

	private CarInputState CollectTestInput()
	{
		var state = new CarInputState();
		if (_testInputStartTick < 0)
			_testInputStartTick = _tick;

		var elapsed = _tick - _testInputStartTick;
		switch (_testInputMode)
		{
			case TestInputMode.DriveRespawn:
				state.Throttle = elapsed < 360 ? 1.0f : 0.0f;
				state.Steer = 0.0f;
				state.Handbrake = false;
				state.Brake = false;
				state.Respawn = elapsed >= 420 && elapsed < 435;
				break;
		}

		state.VehicleId = _clientVehicleId;
		return state;
	}

	private void SendSnapshotToAll(int playerId, PlayerStateSnapshot snapshot)
	{
		var packet = NetworkSerializer.SerializePlayerState(playerId, snapshot);
		foreach (var info in _peers.Values)
		{
			info?.Peer.PutPacket(packet);
		}
	}

	private void BroadcastVehicleStates()
	{
		if (_serverVehicles.Count == 0 || _peers.Count == 0)
			return;

		_vehicleSnapshotBuffer.Clear();
		foreach (var vehicle in _serverVehicles.Values)
		{
			if (vehicle?.Car == null)
				continue;
			var snapshot = new VehicleStateSnapshot
			{
				Tick = _tick,
				VehicleId = vehicle.Id,
				OccupantPeerId = vehicle.OccupantPeerId,
				Transform = vehicle.Car.GlobalTransform,
				LinearVelocity = vehicle.Car.LinearVelocity,
				AngularVelocity = vehicle.Car.AngularVelocity
			};
			vehicle.LastSnapshot = snapshot;
			_vehicleSnapshotBuffer.Add(snapshot);
		}

		if (_vehicleSnapshotBuffer.Count == 0)
			return;

		var packet = NetworkSerializer.SerializeVehicleStates(_vehicleSnapshotBuffer);
		foreach (var peer in _peers.Values)
			peer?.Peer.PutPacket(packet);
	}

	private void OnServerVehicleExiting(int vehicleId)
	{
		if (!_serverVehicles.TryGetValue(vehicleId, out var info))
			return;

		_serverVehicles.Remove(vehicleId);
		if (info.InstanceId != 0)
			_vehicleIdByInstance.Remove(info.InstanceId);

		if (_peers.Count > 0)
		{
			var packet = NetworkSerializer.SerializeVehicleDespawn(vehicleId);
			foreach (var peer in _peers.Values)
				peer?.Peer.PutPacket(packet);
		}
	}

	private void CheckPeerTimeouts()
	{
		if (_peers.Count == 0) return;
		var nowMsec = (long)Time.GetTicksMsec();
		var toRemove = new System.Collections.Generic.List<int>();
		foreach (var peerId in _peers.Keys)
		{
			var info = _peers[peerId];
			if (info != null && nowMsec - info.LastSeenMsec > PeerTimeoutMsec)
				toRemove.Add(peerId);
		}
		foreach (var peerId in toRemove)
			RemovePeer(peerId, true);
	}

	private void RemovePeer(int peerId, bool notifyClients)
	{
		if (!_peers.ContainsKey(peerId)) return;
		var info = _peers[peerId];
		_peers.Remove(peerId);
		if (info.ControlledVehicleId != 0)
		{
			var vehicle = GetVehicleInfo(info.ControlledVehicleId);
			if (vehicle != null && vehicle.OccupantPeerId == peerId)
				vehicle.OccupantPeerId = 0;
		}
		if (info.PlayerCharacter != null && GodotObject.IsInstanceValid(info.PlayerCharacter))
			info.PlayerCharacter.QueueFree();
		if (notifyClients && _peers.Count > 0)
		{
			var packet = NetworkSerializer.SerializeRemovePlayer(peerId);
			foreach (var other in _peers.Values)
			{
				other?.Peer.PutPacket(packet);
			}
		}
		GD.Print($"Client {peerId} removed");
	}

	private void ApplyLocalSnapshot(PlayerStateSnapshot snapshot)
	{
		if (snapshot == null)
			return;

		var snapshotMode = snapshot.Mode;
		_clientVehicleId = snapshot.VehicleId;

		if (snapshot.CarSnapshot != null)
		{
			_car?.QueueSnapshot(snapshot.CarSnapshot);
		}

		if (snapshot.PlayerSnapshot != null)
		{
			if (snapshotMode == PlayerMode.Foot)
			{
				ReconcileLocalPlayer(snapshot.PlayerSnapshot, snapshot.LastProcessedInputTick);
			}
			else
			{
				_playerCharacter?.QueueSnapshot(snapshot.PlayerSnapshot);
			}
		}

		UpdateClientMode(snapshotMode);
	}

	private void UpdateClientMode(PlayerMode newMode)
	{
		if (CurrentClientMode == newMode)
			return;
		CurrentClientMode = newMode;
		_playerPredictionHistory.Clear();
		_lastAcknowledgedPlayerInputTick = 0;
		if (newMode == PlayerMode.Foot)
			_playerCharacter?.ClearPendingSnapshot();
		ClientModeChanged?.Invoke(newMode);
	}

	private void ApplyClientInputToCar()
	{
		_car?.SetInputState(_clientInput);
	}

	private void ApplyClientInputToPlayer()
	{
		_playerCharacter?.SetInputState(_clientPlayerInput);
	}
}
