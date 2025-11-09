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

	private partial class PeerInfo : GodotObject
	{
		public int Id { get; set; }
		public PacketPeerUdp Peer { get; set; }
		public CarInputState CarInputState { get; set; } = new CarInputState();
		public FootInputState FootInputState { get; set; } = new FootInputState();
		public CarSnapshot LastCarSnapshot { get; set; }
		public FootSnapshot LastFootSnapshot { get; set; }
		public long LastSeenMsec { get; set; }
		public RaycastCar Car { get; set; }
		public FootPlayerController Foot { get; set; }
		public PlayerMode Mode { get; set; } = PlayerMode.Vehicle;
		public VehicleSeat DriverSeat { get; set; }
		public Transform3D FootRestTransform { get; set; } = Transform3D.Identity;

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

	private enum Role { None, Server, Client }

	private Role _role = Role.None;
	private RaycastCar _car;
	private FootPlayerController _foot;
	private int _tick = 0;

	private UdpServer _udpServer;
	private Godot.Collections.Dictionary<int, PeerInfo> _peers = new Godot.Collections.Dictionary<int, PeerInfo>();
	private int _nextPeerId = 1;
	private Node3D _serverCarParent;
	private Node3D _serverFootParent;

	private PacketPeerUdp _clientPeer;
	private CarInputState _clientInput = new CarInputState();
	private FootInputState _clientFootInput = new FootInputState();
	private int _clientId = 0;
	private Godot.Collections.Dictionary<int, PlayerStateSnapshot> _remotePlayerSnapshots = new Godot.Collections.Dictionary<int, PlayerStateSnapshot>();
	public PlayerMode CurrentClientMode { get; private set; } = PlayerMode.Vehicle;
	public event Action<PlayerMode> ClientModeChanged;
	public bool IsServer => _role == Role.Server;
	public bool IsClient => _role == Role.Client;

	private PackedScene _playerCarScene;
	private PackedScene _footPlayerScene;
	private bool _respawnPointsCached = false;
	private bool _hasFallbackSpawn = false;
	private Transform3D _fallbackSpawnTransform = Transform3D.Identity;
	private TestInputMode _testInputMode = TestInputMode.None;
	private int _testInputStartTick = -1;

	public override void _Ready()
	{
		_playerCarScene = GD.Load<PackedScene>("res://src/entities/vehicle/car/player_car.tscn");
		_footPlayerScene = GD.Load<PackedScene>("res://src/entities/player/foot/foot_player.tscn");
		Engine.PhysicsTicksPerSecond = 60;
		_role = DetermineRole();
		InitializeTestInputScript();

		switch (_role)
		{
			case Role.Server:
				StartServer();
				break;
			case Role.Client:
				StartClient();
				break;
		}

		SetPhysicsProcess(_role != Role.None);
	}

	public void RegisterCar(RaycastCar car)
	{
		GD.Print($"NetworkController: RegisterCar called, role={_role}, car={car?.Name ?? "null"}");
		_car = car;
		if (_role == Role.Client)
		{
			GD.Print("NetworkController: Client car registered, applying input");
			ApplyClientInputToCar();
		}
	}

	public void RegisterFoot(FootPlayerController foot)
	{
		GD.Print($"NetworkController: RegisterFoot called, role={_role}, foot={foot?.Name ?? "null"}");
		_foot = foot;
		if (_role == Role.Client)
		{
			foot.ConfigureAuthority(true);
			foot.SetCameraActive(CurrentClientMode == PlayerMode.Foot);
			ApplyClientInputToFoot();
		}
	}

	private Role DetermineRole()
	{
		// Get command line args - check both system args and user args (passed after --)
		var args = OS.GetCmdlineArgs();
		var userArgs = OS.GetCmdlineUserArgs();
		
		var parsedRole = Role.Client;
		
		// Check system args - they may come as a single string, so split if needed
		foreach (var arg in args)
		{
			// Split the arg if it contains spaces (single string with multiple args)
			var splitArgs = arg.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
			foreach (var splitArg in splitArgs)
			{
				if (splitArg == "--server")
					return Role.Server;
				else if (splitArg == "--client")
					parsedRole = Role.Client;
			}
		}
		
		// Check user args (passed after -- separator)
		foreach (var arg in userArgs)
		{
			if (arg == "--server")
				return Role.Server;
			else if (arg == "--client")
				parsedRole = Role.Client;
		}
		
		return parsedRole;
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
			_serverFootParent = GetTree().CurrentScene.GetNodeOrNull<Node3D>("AuthoritativeFootPlayers");
			if (_serverFootParent == null)
				_serverFootParent = _serverCarParent;
			GD.Print($"Server listening on UDP {DefaultPort}");
			GD.Print("TEST_EVENT: SERVER_STARTED");
		}
	}

	private void StartClient()
	{
		_clientPeer = new PacketPeerUdp();
		var err = _clientPeer.ConnectToHost("127.0.0.1", DefaultPort);
		if (err != Error.Ok)
		{
			GD.PushError($"Failed to start UDP client (err {err})");
		}
		else
		{
			_clientId = 0;
			_remotePlayerSnapshots.Clear();
			CurrentClientMode = PlayerMode.Vehicle;
			GD.Print($"Client connecting to 127.0.0.1:{DefaultPort}");
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
			case Role.Server:
				ProcessServer();
				break;
			case Role.Client:
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
			case NetworkSerializer.PacketFootInput:
				var footState = NetworkSerializer.DeserializeFootInput(packet);
				if (footState != null && footState.Tick >= peerInfo.FootInputState.Tick)
					peerInfo.FootInputState.CopyFrom(footState);
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
					if (info.Car != null)
					{
						info.Car.SetInputState(info.CarInputState);
						if (info.CarInputState.Interact)
						{
							TryExitVehicle(info);
							info.CarInputState.Interact = false;
						}
					}
					if (info.Foot != null)
					{
						info.Foot.SetWorldActive(false);
					}
					break;
				case PlayerMode.Foot:
					if (info.Foot != null)
					{
						info.Foot.SetWorldActive(true);
						info.Foot.ConfigureAuthority(true);
						info.Foot.SetInputState(info.FootInputState);
						if (info.FootInputState.Interact)
						{
							TryEnterVehicle(info);
							info.FootInputState.Interact = false;
						}
					}
					break;
			}
		}

		foreach (var peerId in _peers.Keys.ToList())
		{
			if (!_peers.TryGetValue(peerId, out var info) || info == null)
				continue;

			var carSnapshot = info.Car?.CaptureSnapshot(_tick);
			var footSnapshot = info.Foot?.CaptureSnapshot(_tick);
			info.LastCarSnapshot = carSnapshot;
			info.LastFootSnapshot = footSnapshot;

			var snapshot = new PlayerStateSnapshot
			{
				Tick = _tick,
				Mode = info.Mode,
				CarSnapshot = carSnapshot,
				FootSnapshot = footSnapshot
			};
			SendSnapshotToAll(peerId, snapshot);
		}
	}

	private void EnsureServerEntities(int peerId, PeerInfo info)
	{
		if (info.Car == null)
		{
			info.Car = SpawnServerCar(peerId);
			info.DriverSeat = FindDriverSeat(info.Car);
		}
		if (info.Foot == null)
		{
			info.Foot = SpawnServerFoot(peerId, info.Car);
		}
	}

	private bool TryEnterVehicle(PeerInfo info)
	{
		if (info == null || info.Car == null || info.Foot == null)
			return false;

		var seat = info.DriverSeat ?? FindDriverSeat(info.Car);
		if (seat == null)
			return false;

		var distance = info.Foot.GlobalPosition.DistanceTo(seat.GlobalTransform.Origin);
		if (distance > seat.InteractionRadius)
			return false;

		info.FootRestTransform = info.Foot.GlobalTransform;
		info.Mode = PlayerMode.Vehicle;
		info.Foot.SetWorldActive(false);
		return true;
	}

	private bool TryExitVehicle(PeerInfo info)
	{
		if (info == null || info.Car == null || info.Foot == null)
			return false;

		var exitTransform = GetSeatExitTransform(info);
		info.Mode = PlayerMode.Foot;
		info.Foot.TeleportTo(exitTransform);
		var yaw = info.Car.GlobalTransform.Basis.GetEuler().Y;
		info.Foot.SetYawPitch(yaw, 0f);
		info.Foot.SetWorldActive(true);
		return true;
	}

	private Transform3D GetSeatExitTransform(PeerInfo info)
	{
		var carTransform = info.Car?.GlobalTransform ?? Transform3D.Identity;
		var seat = info.DriverSeat ?? FindDriverSeat(info.Car);
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

	private void ProcessClient()
	{
		_tick++;
		switch (CurrentClientMode)
		{
			case PlayerMode.Vehicle:
			{
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
				var localInput = CollectLocalFootInput();
				localInput.Tick = _tick;
				_clientFootInput.CopyFrom(localInput);
				_foot?.SetInputState(_clientFootInput);
				if (_clientPeer != null)
					_clientPeer.PutPacket(NetworkSerializer.SerializeFootInput(localInput));
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
		
		/* VERBOSE PRINT
		if (Mathf.Abs(state.Throttle) > 0.1f || Mathf.Abs(state.Steer) > 0.1f || state.Handbrake || state.Brake || state.Respawn)
		{
			GD.Print($"CLIENT input tick={_tick + 1} throttle={state.Throttle:F2} steer={state.Steer:F2} hb={state.Handbrake} br={state.Brake} respawn={state.Respawn}");
		}*/
		return state;
	}

	private FootInputState CollectLocalFootInput()
	{
		if (_foot == null)
			return new FootInputState();

		var state = _foot.CollectClientInputState();
		return state;
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
			}
		}
	}

	private void RegisterPeer(PacketPeerUdp newPeer)
	{
		var peerId = _nextPeerId++;
		var info = new PeerInfo(peerId, newPeer);
		_peers[peerId] = info;
		if (_role == Role.Server)
		{
			info.Car = SpawnServerCar(peerId);
			info.DriverSeat = FindDriverSeat(info.Car);
			info.Foot = SpawnServerFoot(peerId, info.Car);
		}
		GD.Print($"Client connected from {newPeer.GetPacketIP()}:{newPeer.GetPacketPort()} assigned id={peerId}");
		GD.Print($"TEST_EVENT: CLIENT_CONNECTED id={peerId}");
		newPeer.PutPacket(NetworkSerializer.SerializeWelcome(peerId));
		SendExistingPlayerStates(peerId);
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
			if (other.LastCarSnapshot == null && other.LastFootSnapshot == null)
				continue;
			var snapshot = new PlayerStateSnapshot
			{
				Tick = _tick,
				Mode = other.Mode,
				CarSnapshot = other.LastCarSnapshot,
				FootSnapshot = other.LastFootSnapshot
			};
			targetInfo.Peer.PutPacket(NetworkSerializer.SerializePlayerState(peerId, snapshot));
		}
	}

	private RaycastCar SpawnServerCar(int peerId)
	{
		if (_serverCarParent == null) return null;
		var car = _playerCarScene.Instantiate<RaycastCar>();
		if (car == null) return null;
		car.Name = $"ServerCar_{peerId}";
		car.ShowDebug = false;
		_serverCarParent.AddChild(car);
		car.GlobalTransform = GetSpawnTransform(peerId);
		CleanupServerOnlyNodes(car);
		GD.Print($"TEST_EVENT: SERVER_CAR_SPAWNED player_id={peerId}");
		return car;
	}

	private FootPlayerController SpawnServerFoot(int peerId, RaycastCar ownerCar)
	{
		if (_serverFootParent == null || _footPlayerScene == null)
			return null;

		var foot = _footPlayerScene.Instantiate<FootPlayerController>();
		if (foot == null)
			return null;

		foot.AutoRegisterWithNetwork = false;
		foot.Name = $"ServerFoot_{peerId}";
		foot.SetCameraActive(false);
		foot.ConfigureAuthority(true);
		foot.SetWorldActive(false);
		_serverFootParent.AddChild(foot);

		var transform = ownerCar?.GlobalTransform ?? Transform3D.Identity;
		transform.Origin += Vector3.Up * 1.2f;
		foot.TeleportTo(transform);
		return foot;
	}

	private Transform3D GetSpawnTransform(int peerId)
	{
		EnsureRespawnPointsCached();
		var manager = RespawnManager.Instance;
		var contextNode = _serverCarParent as Node3D ?? GetTree().CurrentScene as Node3D;
		var otherPositions = _peers.Values
			.Where(info => info != null && info.Car != null && info.Id != peerId)
			.Select(info => info.Car.GlobalTransform.Origin)
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

		return state;
	}

	private void CleanupServerOnlyNodes(Node car)
	{
		// Remove camera nodes from server cars
		// These are only needed for client-side player cars
		foreach (var child in car.GetChildren())
		{
			if (child is Camera3D)
			{
				child.QueueFree();
			}
			else if (child.Name.ToString().Contains("CameraPivot") || child.Name.ToString().Contains("Camera"))
			{
				CleanupServerOnlyNodes(child);
			}
		}
	}

	private void SendSnapshotToAll(int playerId, PlayerStateSnapshot snapshot)
	{
		var packet = NetworkSerializer.SerializePlayerState(playerId, snapshot);
		foreach (var info in _peers.Values)
		{
			info?.Peer.PutPacket(packet);
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
		if (info.Car != null && GodotObject.IsInstanceValid(info.Car))
			info.Car.QueueFree();
		if (info.Foot != null && GodotObject.IsInstanceValid(info.Foot))
			info.Foot.QueueFree();
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

		if (snapshot.CarSnapshot != null)
		{
			_car?.QueueSnapshot(snapshot.CarSnapshot);
		}

		if (snapshot.FootSnapshot != null)
		{
			_foot?.QueueSnapshot(snapshot.FootSnapshot);
		}

		UpdateClientMode(snapshot.Mode);
	}

	private void UpdateClientMode(PlayerMode newMode)
	{
		if (CurrentClientMode == newMode)
			return;
		CurrentClientMode = newMode;
		ClientModeChanged?.Invoke(newMode);
	}

	private void ApplyClientInputToCar()
	{
		_car?.SetInputState(_clientInput);
	}

	private void ApplyClientInputToFoot()
	{
		_foot?.SetInputState(_clientFootInput);
	}
}
