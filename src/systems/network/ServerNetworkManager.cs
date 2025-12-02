using Godot;
using Godot.Collections;
using System.Collections.Generic;
using System.Linq;

public partial class ServerNetworkManager : GodotObject
{
	private UdpServer _udpServer;
	private Godot.Collections.Dictionary<int, PeerInfo> _peers = new Godot.Collections.Dictionary<int, PeerInfo>();
	private int _nextPeerId = 1;
	private Node3D _serverCarParent;
	private Node3D _serverPlayerParent;
	private int _tick = 0;
	private int _lastDamageTick = 0;
	private readonly List<NetworkSerializer.EntitySnapshotData> _entitySnapshotBuffer = new List<NetworkSerializer.EntitySnapshotData>();

	private VehicleSessionManager _vehicleManager;
	private PlayerSpawnManager _spawnManager;
	private ScoreboardManager _scoreboardManager;
	private WorldBoundsManager _worldBoundsManager;

	public void SetManagers(VehicleSessionManager vehicleManager, PlayerSpawnManager spawnManager, ScoreboardManager scoreboardManager)
	{
		_vehicleManager = vehicleManager;
		_spawnManager = spawnManager;
		_scoreboardManager = scoreboardManager;
	}

	public void SetWorldBoundsManager(WorldBoundsManager worldBoundsManager)
	{
		_worldBoundsManager = worldBoundsManager;
		if (_worldBoundsManager != null)
		{
			_worldBoundsManager.PlayerOutOfBounds += OnPlayerOutOfBounds;
			_worldBoundsManager.VehicleOutOfBounds += OnVehicleOutOfBounds;
		}
	}

	public void Initialize(Node3D carParent, Node3D playerParent)
	{
		_udpServer = new UdpServer();
		var err = _udpServer.Listen(NetworkConfig.DefaultPort);
		if (err != Error.Ok)
		{
			GD.PushError($"Failed to bind UDP server on port {NetworkConfig.DefaultPort} (err {err})");
		}
		else
		{
			_peers.Clear();
			_nextPeerId = 1;
			_serverCarParent = carParent;
			_serverPlayerParent = playerParent;
			GD.Print($"Server listening on UDP {NetworkConfig.DefaultPort}");
			GD.Print("TEST_EVENT: SERVER_STARTED");
		}
	}

	public void Process()
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
					var vehicle = _vehicleManager.GetVehicleInfo(info.ControlledVehicleId);
					if (info.CarInputState.VehicleId != info.ControlledVehicleId)
						info.CarInputState.VehicleId = info.ControlledVehicleId;
					if (vehicle?.Car != null)
					{
						vehicle.Car.SetInputState(info.CarInputState);
						if (info.CarInputState.Interact)
						{
							_vehicleManager.TryExitVehicle(info);
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
							_vehicleManager.TryEnterVehicle(info);
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

			info.PlayerCharacter?.SetReplicatedMode(info.Mode, info.ControlledVehicleId);
		}
	}

	private void EnsureServerEntities(int peerId, PeerInfo info)
	{
		if (info.PlayerCharacter == null)
		{
			info.PlayerCharacter = SpawnServerPlayerCharacter(peerId, null);
			_worldBoundsManager?.RegisterPlayer(info.PlayerCharacter);
		}
	}

	private PlayerCharacter SpawnServerPlayerCharacter(int peerId, RaycastCar ownerCar)
	{
		if (_serverPlayerParent == null || _spawnManager == null)
			return null;

		var entityId = GetPlayerEntityId(peerId);
		var player = _spawnManager.SpawnPlayerForPeer(peerId, _serverPlayerParent, ownerCar, entityId);
		return player;
	}

	private void OnPlayerOutOfBounds(PlayerCharacter player)
	{
		if (player == null)
			return;

		var peerId = FindPeerIdForPlayer(player);
		if (peerId == 0)
			return;

		if (!_peers.TryGetValue(peerId, out var info) || info == null)
			return;

		if (info.Mode == PlayerMode.Foot)
		{
			NotifyPlayerKilled(player, 0);
		}
	}

	private void OnVehicleOutOfBounds(RaycastCar vehicle)
	{
		if (vehicle == null)
			return;

		var vehicleInfo = FindVehicleInfo(vehicle);
		if (vehicleInfo == null)
			return;

		if (vehicleInfo.OccupantPeerId != 0 && _peers.TryGetValue(vehicleInfo.OccupantPeerId, out var occupantInfo) && occupantInfo != null)
		{
			vehicleInfo.OccupantPeerId = 0;
			vehicle.SetOccupantPeerId(0);
			occupantInfo.ControlledVehicleId = 0;
			occupantInfo.Mode = PlayerMode.Foot;
			if (occupantInfo.PlayerCharacter != null)
			{
				occupantInfo.PlayerCharacter.SetWorldActive(true);
				NotifyPlayerKilled(occupantInfo.PlayerCharacter, 0);
			}
		}

		_worldBoundsManager?.RespawnVehicle(vehicle);
	}

	private VehicleInfo FindVehicleInfo(RaycastCar car)
	{
		if (car == null)
			return null;

		foreach (var vehicle in _vehicleManager.GetAllVehicles())
		{
			if (vehicle.Car == car)
				return vehicle;
		}
		return null;
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
		if (victim == null)
			return;

		var victimPeerId = FindPeerIdForPlayer(victim);

		PeerInfo victimInfo = null;
		PeerInfo killerInfo = null;

		if (victimPeerId != 0 && _peers.TryGetValue(victimPeerId, out victimInfo))
		{
		}

		if (killerPeerId != 0 && killerPeerId != victimPeerId && _peers.TryGetValue((int)killerPeerId, out killerInfo))
		{
		}

		var occupiedPositions = _vehicleManager.GetAllVehicles()
			.Where(v => v?.Car != null)
			.Select(v => v.Car.GlobalTransform.Origin);

		var spawn = _spawnManager.GetSpawnTransform(victimPeerId, occupiedPositions, _serverCarParent);
		spawn.Origin += Vector3.Up * 1.2f;

		_scoreboardManager?.NotifyKill(victimInfo, killerInfo, victim, spawn);
		_scoreboardManager?.UpdateScoreboard(_peers.Values, BroadcastPacketToAllPeers);
	}

	private void RegisterPeer(PacketPeerUdp newPeer)
	{
		var peerId = _nextPeerId++;
		var info = new PeerInfo(peerId, newPeer);
		_peers[peerId] = info;
		info.PlayerCharacter = SpawnServerPlayerCharacter(peerId, null);
		_worldBoundsManager?.RegisterPlayer(info.PlayerCharacter);
		GD.Print($"Client connected from {newPeer.GetPacketIP()}:{newPeer.GetPacketPort()} assigned id={peerId}");
		GD.Print($"TEST_EVENT: CLIENT_CONNECTED id={peerId}");
		newPeer.PutPacket(NetworkSerializer.SerializeWelcome(peerId));
		_scoreboardManager?.UpdateScoreboard(_peers.Values, BroadcastPacketToAllPeers);
	}

	private void CheckPeerTimeouts()
	{
		if (_peers.Count == 0) return;
		var nowMsec = (long)Time.GetTicksMsec();
		var toRemove = new List<int>();
		foreach (var peerId in _peers.Keys)
		{
			var info = _peers[peerId];
			if (info != null && nowMsec - info.LastSeenMsec > NetworkConfig.PeerTimeoutMsec)
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
			var vehicle = _vehicleManager.GetVehicleInfo(info.ControlledVehicleId);
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
		_scoreboardManager?.UpdateScoreboard(_peers.Values, BroadcastPacketToAllPeers);
		GD.Print($"Client {peerId} removed");
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

	private void BroadcastPacketToAllPeers(byte[] packet)
	{
		foreach (var peer in _peers.Values)
			peer?.Peer?.PutPacket(packet);
	}

	public void SendHitMarker(int peerId, float damage, WeaponType weaponType, bool wasKill)
	{
		if (peerId == 0)
			return;

		if (!_peers.TryGetValue(peerId, out var info) || info?.Peer == null)
			return;

		var packet = NetworkSerializer.SerializeHitMarker(damage, weaponType, wasKill);
		info.Peer.PutPacket(packet);
	}

	public PeerInfo GetPeer(int peerId)
	{
		return _peers.TryGetValue(peerId, out var info) ? info : null;
	}

	public IEnumerable<PeerInfo> GetAllPeers()
	{
		return _peers.Values;
	}

	private int GetPlayerEntityId(int peerId) => NetworkConfig.PlayerEntityIdOffset + peerId;
}
