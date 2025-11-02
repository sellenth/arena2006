using Godot;
using Godot.Collections;
using System.Linq;

public partial class NetworkController : Node
{
	private const int DefaultPort = 45000;
	private const byte PacketInput = 1;
	private const byte PacketPlayerState = 2;
	private const byte PacketWelcome = 3;
	private const byte PacketRemovePlayer = 4;

	private const int PeerTimeoutMsec = 5000;
	private const int SnapshotPayloadBytes = 4 + 12 + 16 + 12 + 12;

	[Signal] public delegate void PlayerStateUpdatedEventHandler(int playerId, CarSnapshot snapshot);
	[Signal] public delegate void PlayerDisconnectedEventHandler(int playerId);

	private partial class PeerInfo : GodotObject
	{
		public int Id { get; set; }
		public PacketPeerUdp Peer { get; set; }
		public CarInputState InputState { get; set; } = new CarInputState();
		public CarSnapshot LastSnapshot { get; set; }
		public long LastSeenMsec { get; set; }
		public RaycastCar Car { get; set; }

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

	private partial class PlayerStateData : GodotObject
	{
		public int PlayerId { get; set; }
		public CarSnapshot Snapshot { get; set; }
	}

	private enum Role { None, Server, Client }

	private Role _role = Role.None;
	private RaycastCar _car;
	private int _tick = 0;

	private UdpServer _udpServer;
	private Dictionary<int, PeerInfo> _peers = new Dictionary<int, PeerInfo>();
	private int _nextPeerId = 1;
	private Node3D _serverCarParent;

	private PacketPeerUdp _clientPeer;
	private CarInputState _clientInput = new CarInputState();
	private int _clientId = 0;
	private Dictionary<int, CarSnapshot> _remotePlayerSnapshots = new Dictionary<int, CarSnapshot>();

	private PackedScene _playerCarScene;

	public override void _Ready()
	{
		_playerCarScene = GD.Load<PackedScene>("res://entities/vehicle/car/player_car.tscn");
		Engine.PhysicsTicksPerSecond = 60;
		_role = DetermineRole();

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

	private Role DetermineRole()
	{
		var args = OS.GetCmdlineArgs();
		var parsedRole = Role.Client;
		foreach (var arg in args)
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
			GD.Print($"Server listening on UDP {DefaultPort}");
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
			GD.Print($"Client connecting to 127.0.0.1:{DefaultPort}");
			ApplyClientInputToCar();
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
		UpdateServerCars();
		CheckPeerTimeouts();
	}

	private void HandleServerPacket(int peerId, byte[] packet)
	{
		if (packet.Length == 0) return;
		if (!_peers.ContainsKey(peerId)) return;
		var peerInfo = _peers[peerId];
		peerInfo.Touch();
		var packetType = packet[0];
		if (packetType == PacketInput)
		{
			var state = DeserializeInput(packet);
			if (state != null && state.Tick >= peerInfo.InputState.Tick)
				peerInfo.InputState.CopyFrom(state);
		}
	}

	private void UpdateServerCars()
	{
		foreach (var peerId in _peers.Keys.ToList())
		{
			var info = _peers[peerId];
			if (info == null) continue;
			if (info.Car == null)
				info.Car = SpawnServerCar(peerId);
			if (info.Car != null)
				info.Car.SetInputState(info.InputState);
		}

		foreach (var peerId in _peers.Keys.ToList())
		{
			var info = _peers[peerId];
			if (info == null || info.Car == null) continue;
			var snapshot = info.Car.CaptureSnapshot(_tick);
			info.LastSnapshot = snapshot;
			SendSnapshotToAll(peerId, snapshot);
		}
	}

	private void ProcessClient()
	{
		_tick++;
		var localInput = CollectLocalInput();
		localInput.Tick = _tick;
		_clientInput.CopyFrom(localInput);

		if (_car != null)
			_car.SetInputState(_clientInput);

		if (_clientPeer != null)
		{
			_clientPeer.PutPacket(SerializeInput(localInput));
			PollClientPackets();
		}
	}

	private CarInputState CollectLocalInput()
	{
		var state = new CarInputState();
		var forward = Input.GetActionStrength("accelerate");
		var backward = Input.GetActionStrength("decelerate");
		state.Throttle = Mathf.Clamp(forward - backward, -1.0f, 1.0f);
		var right = Input.GetActionStrength("turn_right");
		var left = Input.GetActionStrength("turn_left");
		state.Steer = Mathf.Clamp(left - right, -1.0f, 1.0f);
		state.Handbrake = Input.IsActionPressed("handbreak");
		state.Brake = Input.IsActionPressed("brake");
		if (Mathf.Abs(state.Throttle) > 0.1f || Mathf.Abs(state.Steer) > 0.1f || state.Handbrake || state.Brake)
		{
			GD.Print($"CLIENT input tick={_tick + 1} throttle={state.Throttle:F2} steer={state.Steer:F2} hb={state.Handbrake} br={state.Brake}");
		}
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
				case PacketWelcome:
					var newId = DeserializeWelcome(packet);
					if (newId != 0)
					{
						_clientId = newId;
						GD.Print($"CLIENT received welcome, assigned ID: {_clientId}");
					}
					break;
				case PacketPlayerState:
					var remoteState = DeserializePlayerState(packet);
					if (remoteState?.Snapshot != null)
					{
						var remoteId = remoteState.PlayerId;
						var remoteSnapshot = remoteState.Snapshot;
						if (remoteId == _clientId)
						{
							GD.Print($"CLIENT received snapshot for self: tick={remoteSnapshot.Tick} pos={remoteSnapshot.Transform.Origin}");
							ApplyLocalSnapshot(remoteSnapshot);
						}
						else
						{
							_remotePlayerSnapshots[remoteId] = remoteSnapshot;
							EmitSignal(SignalName.PlayerStateUpdated, remoteId, remoteSnapshot);
						}
					}
					break;
				case PacketRemovePlayer:
					var removedId = DeserializeRemovePlayer(packet);
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
			info.Car = SpawnServerCar(peerId);
		GD.Print($"Client connected from {newPeer.GetPacketIP()}:{newPeer.GetPacketPort()} assigned id={peerId}");
		newPeer.PutPacket(SerializeWelcome(peerId));
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
			if (other?.LastSnapshot != null)
				targetInfo.Peer.PutPacket(SerializePlayerState(peerId, other.LastSnapshot));
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
		car.GlobalTransform = new Transform3D(Basis.Identity, GetSpawnPosition(peerId));
		CleanupServerOnlyNodes(car);
		return car;
	}

	private Vector3 GetSpawnPosition(int peerId)
	{
		var offset = peerId * 6.0f;
		return new Vector3(offset, 2.0f, -20.0f + (peerId % 2) * 5.0f);
	}

	private void CleanupServerOnlyNodes(Node car)
	{
		// Remove camera and remote transform nodes from server cars
		// These are only needed for client-side player cars
		foreach (var child in car.GetChildren())
		{
			if (child is Camera3D || child is RemoteTransform3D)
			{
				child.QueueFree();
			}
			else if (child.Name.ToString().Contains("CameraPivot") || child.Name.ToString().Contains("Camera"))
			{
				CleanupServerOnlyNodes(child);
			}
		}
	}

	private void SendSnapshotToAll(int playerId, CarSnapshot snapshot)
	{
		var packet = SerializePlayerState(playerId, snapshot);
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
		if (notifyClients && _peers.Count > 0)
		{
			var packet = SerializeRemovePlayer(peerId);
			foreach (var other in _peers.Values)
			{
				other?.Peer.PutPacket(packet);
			}
		}
		GD.Print($"Client {peerId} removed");
	}

	private void ApplyLocalSnapshot(CarSnapshot snapshot)
	{
		if (_car != null)
		{
			GD.Print($"CLIENT applying snapshot to car: pos={snapshot.Transform.Origin}");
			_car.QueueSnapshot(snapshot);
		}
		else
		{
			GD.Print("CLIENT: _car is null, cannot apply snapshot");
		}
	}

	private void ApplyClientInputToCar()
	{
		_car?.SetInputState(_clientInput);
	}

	private byte[] SerializeInput(CarInputState state)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketInput);
		buffer.PutU32((uint)state.Tick);
		buffer.PutFloat(state.Throttle);
		buffer.PutFloat(state.Steer);
		buffer.PutU8((byte)(state.Handbrake ? 1 : 0));
		buffer.PutU8((byte)(state.Brake ? 1 : 0));
		return buffer.DataArray;
	}

	private CarInputState DeserializeInput(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		if (buffer.GetAvailableBytes() < 1) return null;
		var packetType = buffer.GetU8();
		if (packetType != PacketInput) return null;
		if (buffer.GetAvailableBytes() < 4 + 4 + 4 + 1 + 1) return null;
		var state = new CarInputState
		{
			Tick = (int)buffer.GetU32(),
			Throttle = buffer.GetFloat(),
			Steer = buffer.GetFloat(),
			Handbrake = buffer.GetU8() == 1,
			Brake = buffer.GetU8() == 1
		};
		return state;
	}

	private byte[] SerializePlayerState(int playerId, CarSnapshot snapshot)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketPlayerState);
		buffer.PutU32((uint)playerId);
		WriteSnapshotPayload(buffer, snapshot);
		return buffer.DataArray;
	}

	private PlayerStateData DeserializePlayerState(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		if (buffer.GetAvailableBytes() < 1) return null;
		var packetType = buffer.GetU8();
		if (packetType != PacketPlayerState) return null;
		if (buffer.GetAvailableBytes() < 4 + SnapshotPayloadBytes) return null;
		var data = new PlayerStateData
		{
			PlayerId = (int)buffer.GetU32(),
			Snapshot = ReadSnapshotPayload(buffer)
		};
		return data;
	}

	private byte[] SerializeWelcome(int peerId)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketWelcome);
		buffer.PutU32((uint)peerId);
		return buffer.DataArray;
	}

	private int DeserializeWelcome(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		if (buffer.GetAvailableBytes() < 5) return 0;
		var packetType = buffer.GetU8();
		if (packetType != PacketWelcome) return 0;
		return (int)buffer.GetU32();
	}

	private byte[] SerializeRemovePlayer(int peerId)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketRemovePlayer);
		buffer.PutU32((uint)peerId);
		return buffer.DataArray;
	}

	private int DeserializeRemovePlayer(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		if (buffer.GetAvailableBytes() < 5) return 0;
		var packetType = buffer.GetU8();
		if (packetType != PacketRemovePlayer) return 0;
		return (int)buffer.GetU32();
	}

	private void WriteSnapshotPayload(StreamPeerBuffer buffer, CarSnapshot snapshot)
	{
		buffer.PutU32((uint)snapshot.Tick);
		var origin = snapshot.Transform.Origin;
		buffer.PutFloat(origin.X);
		buffer.PutFloat(origin.Y);
		buffer.PutFloat(origin.Z);
		var rotation = snapshot.Transform.Basis.GetRotationQuaternion();
		buffer.PutFloat(rotation.X);
		buffer.PutFloat(rotation.Y);
		buffer.PutFloat(rotation.Z);
		buffer.PutFloat(rotation.W);
		var lin = snapshot.LinearVelocity;
		buffer.PutFloat(lin.X);
		buffer.PutFloat(lin.Y);
		buffer.PutFloat(lin.Z);
		var ang = snapshot.AngularVelocity;
		buffer.PutFloat(ang.X);
		buffer.PutFloat(ang.Y);
		buffer.PutFloat(ang.Z);
	}

	private CarSnapshot ReadSnapshotPayload(StreamPeerBuffer buffer)
	{
		if (buffer.GetAvailableBytes() < SnapshotPayloadBytes) return null;
		var snapshot = new CarSnapshot
		{
			Tick = (int)buffer.GetU32()
		};
		var origin = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		var rotation = new Quaternion(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		snapshot.Transform = new Transform3D(new Basis(rotation), origin);
		snapshot.LinearVelocity = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		snapshot.AngularVelocity = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		return snapshot;
	}
}

