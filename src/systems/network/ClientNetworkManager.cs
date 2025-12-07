using Godot;
using System;
using System.Collections.Generic;

public partial class ClientNetworkManager : GodotObject
{
	private PacketPeerUdp _clientPeer;
	private CarInputState _clientInput = new CarInputState();
	private PlayerInputState _clientPlayerInput = new PlayerInputState();
	private int _clientId = 0;
	private int _clientVehicleId = 0;
	private int _lastAcknowledgedPlayerInputTick = 0;
	private int _tick = 0;
	private RaycastCar _car;
	private PlayerCharacter _playerCharacter;
	private PlayerMode _currentMode = PlayerMode.Foot;

	private readonly List<PlayerPredictionSample> _playerPredictionHistory = new List<PlayerPredictionSample>();
	private TestInputMode _testInputMode = TestInputMode.None;
	private int _testInputStartTick = -1;

	public event Action<PlayerMode> ClientModeChanged;
	public event Action<RaycastCar> LocalCarChanged;
	public event Action<int> PlayerDisconnectedEvent;
	public event Action<int, byte[]> EntitySnapshotReceivedEvent;
	public event Action<System.Collections.Generic.List<NetworkSerializer.ScoreboardEntry>> ScoreboardUpdatedEvent;
	public event Action<float, WeaponType, bool> HitMarkerReceived;
	public event Action<MatchStateSnapshot> MatchStateReceivedEvent;
	public event Action<int, int> TeamAssignmentReceivedEvent;
	public event Action<int, int, WeaponType> KillFeedReceived;

	public int ClientId => _clientId;
	public PlayerMode CurrentMode => _currentMode;
	public RaycastCar LocalCar => _car;
	public PlayerCharacter LocalPlayer => _playerCharacter;

	private enum TestInputMode
	{
		None,
		DriveRespawn
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

	public void Initialize(string serverIp)
	{
		_clientPeer = new PacketPeerUdp();
		var err = _clientPeer.ConnectToHost(serverIp, NetworkConfig.DefaultPort);
		if (err != Error.Ok)
		{
			GD.PushError($"Failed to start UDP client (err {err})");
		}
		else
		{
			_clientId = 0;
			_playerPredictionHistory.Clear();
			_lastAcknowledgedPlayerInputTick = 0;
			_currentMode = PlayerMode.Foot;
			GD.Print($"Client connecting to {serverIp}:{NetworkConfig.DefaultPort}");
			ApplyClientInputToCar();
		}

		InitializeTestInputScript();
	}

	public void Process()
	{
		_tick++;
		switch (_currentMode)
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
		if (_currentMode != PlayerMode.Foot || tick <= 0)
			return;

		var sample = new PlayerPredictionSample(tick, transform, velocity);
		if (_playerPredictionHistory.Count > 0 && _playerPredictionHistory[_playerPredictionHistory.Count - 1].Tick == tick)
		{
			_playerPredictionHistory[_playerPredictionHistory.Count - 1] = sample;
		}
		else
		{
			_playerPredictionHistory.Add(sample);
			if (_playerPredictionHistory.Count > NetworkConfig.MaxPredictionHistory)
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

		if (errorMagnitude > NetworkConfig.PlayerSnapDistance)
		{
			ApplyServerSnap(serverSnapshot);
			return;
		}
		else
		{
			var blend = NetworkConfig.PlayerSmallCorrectionBlend;
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
							_playerCharacter.SetReplicatedMode(_currentMode, _clientVehicleId);
						}
					}
					break;
				case NetworkSerializer.PacketRemovePlayer:
					var removedId = NetworkSerializer.DeserializeRemovePlayer(packet);
					if (removedId != 0)
					{
						PlayerDisconnectedEvent?.Invoke(removedId);
					}
					break;
				case NetworkSerializer.PacketEntitySnapshot:
					var entitySnapshots = NetworkSerializer.DeserializeEntitySnapshots(packet);
					if (entitySnapshots != null)
					{
						foreach (var snapshot in entitySnapshots)
							EntitySnapshotReceivedEvent?.Invoke(snapshot.EntityId, snapshot.Data);
					}
					break;
				case NetworkSerializer.PacketEntityDespawn:
					var despawnedEntityId = NetworkSerializer.DeserializeEntityDespawn(packet);
					if (despawnedEntityId != 0)
						GD.Print($"Entity {despawnedEntityId} despawned");
					break;
				case NetworkSerializer.PacketScoreboard:
					var scoreboardEntries = NetworkSerializer.DeserializeScoreboard(packet);
					if (scoreboardEntries != null)
					{
						ScoreboardUpdatedEvent?.Invoke(scoreboardEntries);
					}
					break;
				case NetworkSerializer.PacketHitMarker:
					if (NetworkSerializer.DeserializeHitMarker(packet, out var damage, out var weaponType, out var wasKill))
					{
						HitMarkerReceived?.Invoke(damage, weaponType, wasKill);
					}
					break;
				case NetworkSerializer.PacketMatchState:
					var matchState = NetworkSerializer.DeserializeMatchState(packet);
					if (matchState.HasValue)
					{
						MatchStateReceivedEvent?.Invoke(matchState.Value);
					}
					break;
				case NetworkSerializer.PacketTeamAssignment:
					var teamAssignment = NetworkSerializer.DeserializeTeamAssignment(packet);
					if (teamAssignment.HasValue)
					{
						TeamAssignmentReceivedEvent?.Invoke(teamAssignment.Value.PeerId, teamAssignment.Value.TeamId);
					}
					break;
				case NetworkSerializer.PacketKillFeed:
					if (NetworkSerializer.DeserializeKillFeed(packet, out var killerId, out var victimId, out var killWeapon))
					{
						KillFeedReceived?.Invoke(killerId, victimId, killWeapon);
					}
					break;
			}
		}
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

	private void ApplyClientInputToCar()
	{
		_car?.SetInputState(_clientInput);
	}

	private void ApplyClientInputToPlayer()
	{
		_playerCharacter?.SetInputState(_clientPlayerInput);
	}

	public void RegisterLocalPlayerCar(RaycastCar car)
	{
		if (car == null)
			return;

		GD.Print($"ClientNetworkManager: RegisterLocalPlayerCar car={car.Name}");
		UpdateLocalCarReference(car);
		_car?.ConfigureForLocalDriver(false);
		ApplyClientInputToCar();
	}

	public void RegisterPlayerCharacter(PlayerCharacter player)
	{
		GD.Print($"ClientNetworkManager: RegisterPlayerCharacter called, player={player?.Name ?? "null"}");
		_playerCharacter = player;
		if (_clientId != 0)
		{
			_playerCharacter.SetNetworkId(GetPlayerEntityId(_clientId));
			_playerCharacter.RegisterAsRemoteReplica();
		}
		_playerCharacter.SetReplicatedMode(_currentMode, _clientVehicleId);
		player.ConfigureAuthority(true);
		player.SetCameraActive(_currentMode == PlayerMode.Foot);
		ApplyClientInputToPlayer();
	}

	public void SetLocalPlayerColor(Color color)
	{
		_playerCharacter?.SetPlayerColor(color);
	}

	public void AttachLocalVehicle(int vehicleId, RaycastCar car)
	{
		if (car == null)
			return;

		_clientVehicleId = vehicleId;
		UpdateLocalCarReference(car);
		_car?.ConfigureForLocalDriver(true);
		_currentMode = PlayerMode.Vehicle;
		ClientModeChanged?.Invoke(_currentMode);
		GD.Print($"ClientNetworkManager: Local client now controls vehicle {vehicleId}");
	}

	public void DetachLocalVehicle(int vehicleId, RaycastCar car)
	{
		if (_clientVehicleId == vehicleId && _car != null && car == _car)
		{
			_car.ConfigureForLocalDriver(false);
			UpdateLocalCarReference(null);
		}

		if (_clientVehicleId == vehicleId)
		{
			_clientVehicleId = 0;
			_currentMode = PlayerMode.Foot;
			ClientModeChanged?.Invoke(_currentMode);
		}
	}

	private void UpdateLocalCarReference(RaycastCar car)
	{
		if (_car == car)
			return;

		_car = car;
		LocalCarChanged?.Invoke(_car);
	}

	private int GetPlayerEntityId(int peerId) => NetworkConfig.PlayerEntityIdOffset + peerId;
}
