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
	private RespawnService _respawnService;

	private PackedScene _playerCharacterScene;
	private PackedScene _scoreboardUiScene;
	private HitMarkerUI _hitMarkerUi;
	public event Action<int, int, WeaponType> KillFeedReceived;

	public event Action<PlayerMode> ClientModeChanged;
	public event Action<RaycastCar> LocalCarChanged;
	public bool IsServer => _role == NetworkRole.Server;
	public bool IsClient => _role == NetworkRole.Client;
	public int ClientPeerId => _clientManager?.ClientId ?? 0;
	public RaycastCar LocalCar => _clientManager?.LocalCar;
	public PlayerCharacter LocalPlayer => _clientManager?.LocalPlayer;
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
			_respawnService = new RespawnService();
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
			_clientManager.HitMarkerReceived += OnClientHitMarker;
			_clientManager.MatchStateReceivedEvent += OnClientMatchStateReceived;
			_clientManager.TeamAssignmentReceivedEvent += OnClientTeamAssignmentReceived;
			_clientManager.KillFeedReceived += OnClientKillFeed;
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
		_serverManager.SetWorldBoundsManager(WorldBoundsManager.Instance);
		_vehicleManager.SetWorldBoundsManager(WorldBoundsManager.Instance);

		var gameModeManager = GameModeManager.Instance;
		if (gameModeManager != null)
		{
			gameModeManager.PlayerTeamColorChanged += _serverManager.OnPlayerTeamColorChanged;
			gameModeManager.RespawnPlayersAtTeamSpawns += teamSpawnNodes =>
			{
				var root = GetTree()?.Root;
				if (root == null)
					return;

				_respawnService?.RespawnPlayersAtTeamSpawns(
					gameModeManager,
					root,
					_serverManager.GetAllPeers(),
					teamSpawnNodes);
			};
		}
	}

	private MatchStateClient _matchStateClient;

	private void StartClient()
	{
		var clientIp = CmdLineArgsManager.GetClientIp();
		_clientManager.Initialize(clientIp);

		_matchStateClient = new MatchStateClient();
		_matchStateClient.Name = "MatchStateClient";
		AddChild(_matchStateClient);
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

	public void NotifyPlayerKilled(PlayerCharacter victim, long killerPeerId, WeaponType weaponType)
	{
		if (!IsServer || victim == null)
			return;

		_serverManager?.NotifyPlayerKilled(victim, killerPeerId, weaponType);

		KillFeedReceived?.Invoke((int)killerPeerId, (int)victim.OwnerPeerId, weaponType);
	}

	public void SendHitMarkerToPeer(int peerId, float damage, WeaponType weaponType, bool wasKill)
	{
		if (!IsServer)
			return;

		_serverManager?.SendHitMarker(peerId, damage, weaponType, wasKill);
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

	private void OnClientHitMarker(float damage, WeaponType weaponType, bool wasKill)
	{
		var ui = FindHitMarkerUi();
		if (ui != null)
		{
			ui.ShowHitFeedback(damage, weaponType, wasKill);
		}
	}

	private HitMarkerUI FindHitMarkerUi()
	{
		if (_hitMarkerUi != null && IsInstanceValid(_hitMarkerUi) && _hitMarkerUi.IsInsideTree())
			return _hitMarkerUi;

		_hitMarkerUi = null;
		var scene = GetTree()?.CurrentScene;
		if (scene == null)
			return null;

		_hitMarkerUi = scene.FindChild("HitMarkerUI", true, false) as HitMarkerUI;
		return _hitMarkerUi;
	}

	private void OnClientKillFeed(int killerId, int victimId, WeaponType weaponType)
	{
		KillFeedReceived?.Invoke(killerId, victimId, weaponType);
	}

	private void OnClientMatchStateReceived(MatchStateSnapshot snapshot)
	{
		_matchStateClient?.ApplySnapshot(snapshot);
	}

	private void OnClientTeamAssignmentReceived(int peerId, int teamId)
	{
		if (peerId == ClientPeerId)
		{
			var matchStateClient = GetTree()?.CurrentScene?.GetNodeOrNull<MatchStateClient>("MatchStateClient");
			matchStateClient?.SetLocalTeam(teamId);

			var teamDef = TeamDefinition.GetDefault(teamId);
			_clientManager?.SetLocalPlayerColor(teamDef.Color);
		}
	}
}
