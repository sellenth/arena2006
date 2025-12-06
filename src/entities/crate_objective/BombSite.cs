using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public partial class BombSite : Area3D
{
	[Export] public string SiteName { get; set; } = "A";
	[Export(PropertyHint.Range, "0,32,1")] public int SiteIndex { get; set; } = -1;
	[Export] public float PlantTime { get; set; } = 4f;
	[Export] public PackedScene PlantedBombScene { get; set; }
	[Export] public Marker3D BombSpawnPoint { get; set; }

	private static readonly List<BombSite> _allSites = new();
	public static IReadOnlyList<BombSite> AllSites => _allSites;

	private GameModeManager _gameModeManager;
	private PlayerCharacter _playerInZone;
	private float _plantProgress;
	private bool _isPlanting;
	private bool _bombPlantedHere;

	[Signal]
	public delegate void PlantStartedEventHandler(int playerId);

	[Signal]
	public delegate void PlantProgressEventHandler(float progress);

	[Signal]
	public delegate void PlantCancelledEventHandler();

	[Signal]
	public delegate void BombPlantedEventHandler(int playerId, string siteName);

	public string DisplayName => $"Site {SiteName}";
	public bool IsPlanting => _isPlanting;
	public float CurrentPlantProgress => _plantProgress;
	public bool HasBomb => _bombPlantedHere;

	public override void _Ready()
	{
		_gameModeManager = GetNodeOrNull<GameModeManager>("/root/GameModeManager");
		_allSites.Add(this);
		AssignSiteIndexIfNeeded();

		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
	}

	public override void _ExitTree()
	{
		_allSites.Remove(this);
	}

	public override void _Process(double delta)
	{
		if (_bombPlantedHere)
			return;

		// Client side prediction for UI progress is fine, but completion is server only.
		
		if (!CanPlant())
		{
			if (_isPlanting)
			{
				CancelPlant();
			}
			return;
		}

		if (_playerInZone != null && IsPlayerPlanting(_playerInZone))
		{
			if (!_isPlanting)
			{
				StartPlant();
			}

			_plantProgress += (float)delta;
			EmitSignal(SignalName.PlantProgress, _plantProgress / PlantTime);

			if (_plantProgress >= PlantTime)
			{
				CompletePlant();
			}
		}
		else if (_isPlanting)
		{
			CancelPlant();
		}
	}

	private bool CanPlant()
	{
		if (_gameModeManager?.ActiveMode is IGameModeObjectiveDelegate objectiveMode)
		{
			if (_playerInZone != null)
			{
				return objectiveMode.CanPlant(_playerInZone, this);
			}
		}
		return false;
	}

	private bool IsPlayerPlanting(PlayerCharacter player)
	{
		if (player == null)
			return false;
		
		// Rules are checked in CanPlant. Input is checked here.
		return player.IsInteractHeld();
	}

	private bool IsAttacker(PlayerCharacter player)
	{
		if (_gameModeManager?.ActiveMode is IGameModeObjectiveDelegate objectiveMode)
		{
			return objectiveMode.IsAttacker((int)player.OwnerPeerId);
		}
		return false;
	}

	private void OnBodyEntered(Node body)
	{
		if (body is PlayerCharacter player)
		{
			var isAttacker = IsAttacker(player);
			GD.Print($"[BombSite {SiteName}] Body entered: {body.Name}, IsPlayerCharacter=true, PeerId={player.OwnerPeerId}, IsAttacker={isAttacker}");
			if (isAttacker)
			{
				_playerInZone = player;
				GD.Print($"[BombSite {SiteName}] Attacker entered plant zone");
			}
		}
		else
		{
			// GD.Print($"[BombSite {SiteName}] Body entered: {body.Name}");
		}
	}

	private void OnBodyExited(Node body)
	{
		if (body == _playerInZone)
		{
			_playerInZone = null;
			if (_isPlanting)
			{
				CancelPlant();
			}
			GD.Print($"[BombSite {SiteName}] Player left plant zone");
		}
	}

	private void StartPlant()
	{
		_isPlanting = true;
		_plantProgress = 0f;
		var playerId = (int)(_playerInZone?.OwnerPeerId ?? 0);
		EmitSignal(SignalName.PlantStarted, playerId);
		GD.Print($"[BombSite {SiteName}] Plant started by Player {playerId}");
	}

	private void CancelPlant()
	{
		_isPlanting = false;
		_plantProgress = 0f;
		EmitSignal(SignalName.PlantCancelled);
		GD.Print($"[BombSite {SiteName}] Plant cancelled");
	}

	private void CompletePlant()
	{
		// Critical: Only server handles completion logic and spawning
		if (!IsMultiplayerAuthority())
		{
			_isPlanting = false;
			_plantProgress = 0f;
			// Client waits for server replication
			return;
		}

		_isPlanting = false;
		_bombPlantedHere = true;

		var playerId = (int)(_playerInZone?.OwnerPeerId ?? 0);

		var spawnPos = BombSpawnPoint?.GlobalPosition ?? GlobalPosition;
		if (PlantedBombScene != null)
		{
			var bomb = PlantedBombScene.Instantiate<PlantedBomb>();
			bomb.PlanterId = playerId;
			bomb.SiteName = SiteName;
			bomb.SiteIndex = SiteIndex;
			GetTree().CurrentScene.AddChild(bomb);
			bomb.GlobalPosition = spawnPos;
		}

		var evt = new ObjectiveEventData
		{
			Type = ObjectiveEventType.BombPlanted,
			PlayerId = playerId,
			TeamId = _gameModeManager?.GetTeamForPlayer(playerId) ?? -1,
			ObjectiveId = SiteIndex,
			Position = spawnPos
		};
		_gameModeManager?.NotifyObjectiveEvent(evt);

		if (_gameModeManager?.ActiveMode is IGameModeObjectiveDelegate objectiveMode)
		{
			objectiveMode.OnPlantCompleted(_playerInZone, this);
		}

		EmitSignal(SignalName.BombPlanted, playerId, SiteName);
		GD.Print($"[BombSite {SiteName}] BOMB PLANTED by Player {playerId}!");
	}

	public void ResetForNewRound()
	{
		_isPlanting = false;
		_plantProgress = 0f;
		_bombPlantedHere = false;
		_playerInZone = null;
	}

	public static void ResetAllSites()
	{
		foreach (var site in _allSites)
		{
			site.ResetForNewRound();
		}
	}

	private void AssignSiteIndexIfNeeded()
	{
		// Use a deterministic ordering by site name to avoid index drift across clients.
		var ordered = _allSites
			.OrderBy(s => s.SiteName, StringComparer.Ordinal)
			.ToList();

		for (int i = 0; i < ordered.Count; i++)
		{
			if (ordered[i].SiteIndex < 0)
			{
				ordered[i].SiteIndex = i;
			}
		}
	}
}
