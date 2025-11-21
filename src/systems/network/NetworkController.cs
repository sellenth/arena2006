using Godot;
using System;

public partial class NetworkController : Node
{
	[Signal] public delegate void PlayerDisconnectedEventHandler(int playerId);
	[Signal] public delegate void EntitySnapshotReceivedEventHandler(int entityId, byte[] data);
	[Signal] public delegate void ScoreboardUpdatedEventHandler(Godot.Collections.Array scoreboard);

	private NetworkRole _role = NetworkRole.None;
	private ServerNetworkManager _serverManager;
	private ClientNetworkManager _clientManager;
	private VehicleSessionManager _vehicleManager;
	private PlayerSpawnManager _spawnManager;
	private ScoreboardManager _scoreboardManager;

	private PackedScene _playerCharacterScene;
	private PackedScene _scoreboardUiScene;

	public event Action<PlayerMode> ClientModeChanged;
	public event Action<RaycastCar> LocalCarChanged;
	public bool IsServer => _role == NetworkRole.Server;
	public bool IsClient => _role == NetworkRole.Client;
	public int ClientPeerId => _clientManager?.ClientId ?? 0;
	public RaycastCar LocalCar => _clientManager?.LocalCar;
	public PlayerMode CurrentClientMode => _clientManager?.CurrentMode ?? PlayerMode.Foot;

	public override void _Ready()
	{
		_playerCharacterScene = GD.Load<PackedScene>("res://src/entities/player/player_character.tscn");
		_scoreboardUiScene = GD.Load<PackedScene>("res://src/systems/ui/scoreboard_ui.tscn");
		Engine.PhysicsTicksPerSecond = 60;
		_role = CmdLineArgsManager.GetNetworkRole();

		InitializeManagers();

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

	private void InitializeManagers()
	{
		_vehicleManager = new VehicleSessionManager();
		_spawnManager = new PlayerSpawnManager();
		_spawnManager.SetPlayerCharacterScene(_playerCharacterScene);
		_scoreboardManager = new ScoreboardManager();

		if (_role == NetworkRole.Server)
		{
			_serverManager = new ServerNetworkManager();
			_serverManager.SetManagers(_vehicleManager, _spawnManager, _scoreboardManager);
		}
		else if (_role == NetworkRole.Client)
		{
			_clientManager = new ClientNetworkManager();
			_clientManager.ClientModeChanged += mode => ClientModeChanged?.Invoke(mode);
			_clientManager.LocalCarChanged += car => LocalCarChanged?.Invoke(car);
			_clientManager.PlayerDisconnectedEvent += id => EmitSignal(SignalName.PlayerDisconnected, id);
			_clientManager.EntitySnapshotReceivedEvent += (id, data) => EmitSignal(SignalName.EntitySnapshotReceived, id, data);
			_clientManager.ScoreboardUpdatedEvent += entries =>
			{
				var godotScoreboard = ToGodotScoreboard(entries);
				_scoreboardManager.UpdateScoreboardFromPacket(godotScoreboard);
				EmitSignal(SignalName.ScoreboardUpdated, godotScoreboard);
			};
		}
	}

	private void StartServer()
	{
		var carParent = GetTree().CurrentScene.GetNodeOrNull<Node3D>("AuthoritativeCars");
		if (carParent == null)
			carParent = GetTree().CurrentScene as Node3D;
		var playerParent = GetTree().CurrentScene.GetNodeOrNull<Node3D>("AuthoritativePlayers");
		if (playerParent == null)
			playerParent = carParent;

		_serverManager.Initialize(carParent, playerParent);
		_spawnManager.CacheSpawnPoints(GetTree(), carParent);
	}

	private void StartClient()
	{
		var clientIp = CmdLineArgsManager.GetClientIp();
		_clientManager.Initialize(clientIp);
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
				_serverManager?.Process();
				break;
			case NetworkRole.Client:
				_clientManager?.Process();
				break;
		}
	}

	public void RegisterLocalPlayerCar(RaycastCar car)
	{
		if (car == null)
			return;

		GD.Print($"NetworkController: RegisterLocalPlayerCar role={_role}, car={car.Name}");
		if (_role == NetworkRole.Client)
		{
			_clientManager?.RegisterLocalPlayerCar(car);
		}
	}

	public void RegisterAuthoritativeVehicle(RaycastCar car)
	{
		if (_role != NetworkRole.Server || car == null)
			return;

		_vehicleManager?.RegisterVehicle(car);
	}

	public void RegisterPlayerCharacter(PlayerCharacter player)
	{
		GD.Print($"NetworkController: RegisterPlayerCharacter called, role={_role}, player={player?.Name ?? "null"}");
		if (_role == NetworkRole.Client)
		{
			_clientManager?.RegisterPlayerCharacter(player);
		}
	}

	public void AttachLocalVehicle(int vehicleId, RaycastCar car)
	{
		if (!IsClient || car == null)
			return;

		_clientManager?.AttachLocalVehicle(vehicleId, car);
	}

	public void DetachLocalVehicle(int vehicleId, RaycastCar car)
	{
		if (!IsClient)
			return;

		_clientManager?.DetachLocalVehicle(vehicleId, car);
	}

	public void RecordLocalPlayerPrediction(int tick, Transform3D transform, Vector3 velocity)
	{
		if (!IsClient)
			return;

		_clientManager?.RecordLocalPlayerPrediction(tick, transform, velocity);
	}

	public void NotifyPlayerKilled(PlayerCharacter victim, long killerPeerId)
	{
		if (!IsServer || victim == null)
			return;

		_serverManager?.NotifyPlayerKilled(victim, killerPeerId);
	}

	private void InitializeScoreboardUi()
	{
		_scoreboardManager?.Initialize(_scoreboardUiScene, this);
	}

	public Godot.Collections.Array GetScoreboardSnapshot()
	{
		return _scoreboardManager?.GetScoreboardSnapshot() ?? new Godot.Collections.Array();
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
}
