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
		private const int PlayerEntityIdOffset = 3000;
		private const int VehicleEntityIdOffset = 2000;
		private const float PlayerSpawnJitterRadius = 5.0f;
		private const float KillPlaneY = -200.0f;

		[Signal] public delegate void PlayerDisconnectedEventHandler(int playerId);
		[Signal] public delegate void EntitySnapshotReceivedEventHandler(int entityId, byte[] data);
		[Signal] public delegate void ScoreboardUpdatedEventHandler(Godot.Collections.Array scoreboard);

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
		public int Kills { get; set; }
		public int Deaths { get; set; }

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

		private int GetVehicleEntityId(int vehicleId) => VehicleEntityIdOffset + vehicleId;
		private int GetPlayerEntityId(int peerId) => PlayerEntityIdOffset + peerId;

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
	private int _lastDamageTick = 0;

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
	public PlayerMode CurrentClientMode { get; private set; } = PlayerMode.Foot;
	public event Action<PlayerMode> ClientModeChanged;
	public event Action<RaycastCar> LocalCarChanged;
	public bool IsServer => _role == NetworkRole.Server;
	public bool IsClient => _role == NetworkRole.Client;
	public int ClientPeerId => _clientId;
	public RaycastCar LocalCar => _car;

	private PackedScene _playerCharacterScene;
	private PackedScene _scoreboardUiScene;
	private ScoreboardUI _scoreboardUi;
	private readonly System.Collections.Generic.Dictionary<int, VehicleInfo> _serverVehicles = new System.Collections.Generic.Dictionary<int, VehicleInfo>();
	private readonly System.Collections.Generic.Dictionary<ulong, int> _vehicleIdByInstance = new System.Collections.Generic.Dictionary<ulong, int>();
	private readonly System.Collections.Generic.List<PlayerPredictionSample> _playerPredictionHistory = new System.Collections.Generic.List<PlayerPredictionSample>();
	private readonly System.Collections.Generic.List<NetworkSerializer.EntitySnapshotData> _entitySnapshotBuffer = new System.Collections.Generic.List<NetworkSerializer.EntitySnapshotData>();
	private Godot.Collections.Array _latestScoreboard = new Godot.Collections.Array();
	private int _nextVehicleId = 1;
	private bool _respawnPointsCached = false;
	private bool _hasFallbackSpawn = false;
	private Transform3D _fallbackSpawnTransform = Transform3D.Identity;
	private TestInputMode _testInputMode = TestInputMode.None;
	private int _testInputStartTick = -1;
	private const int MaxPredictionHistory = 256;
	private const float PlayerSnapDistance = 2.5f;
	private const float PlayerSmallCorrectionBlend = 0.25f;
	private readonly RandomNumberGenerator _spawnRng = new RandomNumberGenerator();

	public override void _Ready()
	{
		_playerCharacterScene = GD.Load<PackedScene>("res://src/entities/player/player_character.tscn");
		_scoreboardUiScene = GD.Load<PackedScene>("res://src/systems/ui/scoreboard_ui.tscn");
		_spawnRng.Randomize();
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

		InitializeScoreboardUi();
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
			var entityId = GetVehicleEntityId(id);
			car.RegisterAsAuthority(entityId);

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
					if (_clientId != 0)
					{
						_playerCharacter.SetNetworkId(GetPlayerEntityId(_clientId));
						_playerCharacter.RegisterAsRemoteReplica();
					}
					_playerCharacter.SetReplicatedMode(CurrentClientMode, _clientVehicleId);
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
		CurrentClientMode = PlayerMode.Vehicle;
		ClientModeChanged?.Invoke(CurrentClientMode);
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
		{
			_clientVehicleId = 0;
			CurrentClientMode = PlayerMode.Foot;
			ClientModeChanged?.Invoke(CurrentClientMode);
		}
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
		ApplyPeriodicTestDamage();
		CheckPeerTimeouts();
		BroadcastEntitySnapshots();
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

		EnforceKillPlane();

		foreach (var peerId in _peers.Keys.ToList())
		{
			if (!_peers.TryGetValue(peerId, out var info) || info == null)
				continue;

			info.PlayerCharacter?.SetReplicatedMode(info.Mode, info.ControlledVehicleId);
		}
	}

	private void ApplyPeriodicTestDamage()
	{
		if (_tick - _lastDamageTick < Engine.PhysicsTicksPerSecond)
			return;

		_lastDamageTick = _tick;

		foreach (var peer in _peers.Values)
		{
			if (peer == null)
				continue;

			if (peer.Mode == PlayerMode.Vehicle)
			{
				var vehicle = GetVehicleInfo(peer.ControlledVehicleId);
				if (vehicle?.Car != null)
				{
					vehicle.Car.ApplyDamage(1);
					continue;
				}
			}

			peer.PlayerCharacter?.ApplyDamage(1);
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
			vehicle.Car?.SetOccupantPeerId(info.Id);
			info.PlayerCharacter?.SetReplicatedMode(PlayerMode.Vehicle, vehicle.Id);
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
		vehicle.Car.SetOccupantPeerId(0);
		info.PlayerCharacter?.SetReplicatedMode(PlayerMode.Foot, 0);
		var transform = new Transform3D(info.PlayerCharacter.GlobalBasis, exitTransform.Origin);
		RespawnManager.Instance.TeleportEntity(info.PlayerCharacter, transform);
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

						if (_playerCharacter != null)
						{
							_playerCharacter.SetNetworkId(GetPlayerEntityId(_clientId));
							_playerCharacter.RegisterAsRemoteReplica();
							_playerCharacter.SetReplicatedMode(CurrentClientMode, _clientVehicleId);
						}

						SetScoreboardSnapshot(GetScoreboardSnapshot());
					}
					break;
				case NetworkSerializer.PacketRemovePlayer:
					var removedId = NetworkSerializer.DeserializeRemovePlayer(packet);
					if (removedId != 0)
					{
						EmitSignal(SignalName.PlayerDisconnected, removedId);
					}
					break;
				case NetworkSerializer.PacketEntitySnapshot:
					var entitySnapshots = NetworkSerializer.DeserializeEntitySnapshots(packet);
					if (entitySnapshots != null)
					{
						foreach (var snapshot in entitySnapshots)
							EmitSignal(SignalName.EntitySnapshotReceived, snapshot.EntityId, snapshot.Data);
					}
					break;
				case NetworkSerializer.PacketEntityDespawn:
					var despawnedEntityId = NetworkSerializer.DeserializeEntityDespawn(packet);
					if (despawnedEntityId != 0)
						GD.Print($"Entity {despawnedEntityId} despawned");
					break;
				case NetworkSerializer.PacketScoreboard:
					var scoreboard = NetworkSerializer.DeserializeScoreboard(packet);
					if (scoreboard != null)
					{
						SetScoreboardSnapshot(ToGodotScoreboard(scoreboard));
					}
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
		if (IsServer)
		{
			BroadcastScoreboard(BuildScoreboardEntries());
		}
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
			player.SetNetworkId(GetPlayerEntityId(peerId));
			player.SetReplicatedMode(PlayerMode.Foot, 0);
			_serverPlayerParent.AddChild(player);
			player.RegisterAsAuthority();

			var transform = ownerCar?.GlobalTransform ?? GetSpawnTransform(peerId);
		transform.Origin += Vector3.Up * 1.2f;
		RespawnManager.Instance.TeleportEntity(player, transform);
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
			return ApplySpawnJitter(transform);

		var fallback = _hasFallbackSpawn ? _fallbackSpawnTransform : Transform3D.Identity;
		return ApplySpawnJitter(fallback);
	}

	private Transform3D ApplySpawnJitter(Transform3D transform)
	{
		if (PlayerSpawnJitterRadius <= 0.0f)
			return transform;

		var offset = new Vector3(
			_spawnRng.RandfRange(-1.0f, 1.0f),
			0.0f,
			_spawnRng.RandfRange(-1.0f, 1.0f));

		if (offset.LengthSquared() < 0.0001f)
			offset = new Vector3(1.0f, 0.0f, 0.0f);

		offset = offset.Normalized() * _spawnRng.RandfRange(0.0f, PlayerSpawnJitterRadius);
		transform.Origin += offset;
		return transform;
	}

	private void EnforceKillPlane()
	{
		foreach (var kvp in _peers.ToList())
		{
			var info = kvp.Value;
			if (info?.PlayerCharacter == null)
				continue;

			// Players in vehicles are handled by the vehicle check below.
			if (info.Mode != PlayerMode.Foot)
				continue;

			if (info.PlayerCharacter.GlobalTransform.Origin.Y < KillPlaneY)
			{
				NotifyPlayerKilled(info.PlayerCharacter, 0);
			}
		}

		var manager = RespawnManager.Instance;
		foreach (var vehicle in _serverVehicles.Values)
		{
			var car = vehicle?.Car;
			if (car == null)
				continue;

			if (car.GlobalTransform.Origin.Y >= KillPlaneY)
				continue;

			if (vehicle.OccupantPeerId != 0 && _peers.TryGetValue(vehicle.OccupantPeerId, out var occupantInfo) && occupantInfo != null)
			{
				vehicle.OccupantPeerId = 0;
				car.SetOccupantPeerId(0);
				occupantInfo.ControlledVehicleId = 0;
				occupantInfo.Mode = PlayerMode.Foot;
				if (occupantInfo.PlayerCharacter != null)
				{
					occupantInfo.PlayerCharacter.SetWorldActive(true);
					NotifyPlayerKilled(occupantInfo.PlayerCharacter, 0);
				}
			}

			var fallback = car.GlobalTransform;
			fallback.Origin += Vector3.Up * 4.0f;
			if (manager != null)
			{
				var success = manager.RespawnEntityAtBestPoint(car, car);
				if (!success)
				{
					manager.RespawnEntity(car, RespawnManager.RespawnRequest.Create(fallback));
				}
			}
			else
			{
				car.GlobalTransform = fallback;
				car.LinearVelocity = Vector3.Zero;
				car.AngularVelocity = Vector3.Zero;
			}
		}
	}

	private int FindPeerIdForPlayer(PlayerCharacter player)
	{
		if (player == null)
			return 0;

		if (player.OwnerPeerId != 0)
			return (int)player.OwnerPeerId;

		foreach (var kvp in _peers)
		{
			if (kvp.Value?.PlayerCharacter == player)
				return kvp.Key;
		}

		return 0;
	}

	public void NotifyPlayerKilled(PlayerCharacter victim, long killerPeerId)
	{
		if (!IsServer || victim == null)
			return;

		var victimPeerId = FindPeerIdForPlayer(victim);

		if (victimPeerId != 0 && _peers.TryGetValue(victimPeerId, out var victimInfo) && victimInfo != null)
		{
			victimInfo.Deaths++;
		}

		if (killerPeerId != 0 && killerPeerId != victimPeerId && _peers.TryGetValue((int)killerPeerId, out var killerInfo) && killerInfo != null)
		{
			killerInfo.Kills++;
		}

		var scoreboard = BuildScoreboardEntries();
		BroadcastScoreboard(scoreboard);

		var spawn = GetSpawnTransform(victimPeerId);
		spawn.Origin += Vector3.Up * 1.2f;
		victim.ForceRespawn(spawn);
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

	private void InitializeScoreboardUi()
	{
		if (_scoreboardUiScene == null || _scoreboardUi != null)
			return;

		var instance = _scoreboardUiScene.Instantiate<ScoreboardUI>();
		if (instance == null)
			return;

		AddChild(instance);
		_scoreboardUi = instance;
		_scoreboardUi.ApplyScoreboard(GetScoreboardSnapshot());
	}

	private System.Collections.Generic.List<NetworkSerializer.ScoreboardEntry> BuildScoreboardEntries()
	{
		return _peers.Values
			.Where(p => p != null)
			.Select(p => new NetworkSerializer.ScoreboardEntry
			{
				Id = p.Id,
				Kills = p.Kills,
				Deaths = p.Deaths
			})
			.OrderByDescending(p => p.Kills)
			.ThenBy(p => p.Deaths)
			.ThenBy(p => p.Id)
			.ToList();
	}

	private Godot.Collections.Array ToGodotScoreboard(System.Collections.Generic.IEnumerable<NetworkSerializer.ScoreboardEntry> entries)
	{
		var array = new Godot.Collections.Array();
		if (entries == null)
			return array;

		foreach (var entry in entries)
		{
			var row = new Godot.Collections.Dictionary
			{
				{ "id", entry.Id },
				{ "kills", entry.Kills },
				{ "deaths", entry.Deaths }
			};
			array.Add(row);
		}

		return array;
	}

	private Godot.Collections.Array CloneScoreboard(Godot.Collections.Array source)
	{
		var clone = new Godot.Collections.Array();
		if (source == null)
			return clone;

		foreach (var item in source)
			clone.Add(item);

		return clone;
	}

	private void BroadcastScoreboard(System.Collections.Generic.List<NetworkSerializer.ScoreboardEntry> entries)
	{
		var scoreboardArray = ToGodotScoreboard(entries);
		SetScoreboardSnapshot(scoreboardArray);

		if (!IsServer || _peers.Count == 0)
			return;

		var packet = NetworkSerializer.SerializeScoreboard(entries);
		foreach (var peer in _peers.Values)
			peer?.Peer?.PutPacket(packet);
	}

	private void SetScoreboardSnapshot(Godot.Collections.Array scoreboard)
	{
		_latestScoreboard = CloneScoreboard(scoreboard);
		EmitSignal(SignalName.ScoreboardUpdated, CloneScoreboard(_latestScoreboard));
		if (_scoreboardUi != null)
		{
			_scoreboardUi.ApplyScoreboard(_latestScoreboard);
		}
	}

	public Godot.Collections.Array GetScoreboardSnapshot()
	{
		return CloneScoreboard(_latestScoreboard);
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

	private void BroadcastEntitySnapshots()
	{
		if (_peers.Count == 0)
			return;
		
		var registry = EntityReplicationRegistry.Instance;
		if (registry == null)
			return;
		
		_entitySnapshotBuffer.Clear();
		
		foreach (var kvp in registry.GetAllEntities())
		{
			var entityId = kvp.Key;
			var entity = kvp.Value;
			
			var buffer = new StreamPeerBuffer();
			buffer.BigEndian = false;
			entity.WriteSnapshot(buffer);
			
			_entitySnapshotBuffer.Add(new NetworkSerializer.EntitySnapshotData
			{
				EntityId = entityId,
				Data = buffer.DataArray
			});
		}
		
		if (_entitySnapshotBuffer.Count == 0)
			return;
		
		var packet = NetworkSerializer.SerializeEntitySnapshots(_entitySnapshotBuffer);
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
		if (IsServer)
		{
			BroadcastScoreboard(BuildScoreboardEntries());
		}
		GD.Print($"Client {peerId} removed");
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
