using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class BombSite : Area3D
{
	[Export] public string SiteName { get; set; } = "A";
	[Export] public float PlantTime { get; set; } = 4f;
	[Export] public PackedScene PlantedBombScene { get; set; }
	[Export] public Marker3D BombSpawnPoint { get; set; }

	private static readonly List<BombSite> _allSites = new();
	public static IReadOnlyList<BombSite> AllSites => _allSites;

	private GameModeManager _gameMode;
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
		_gameMode = GetNodeOrNull<GameModeManager>("/root/GameModeManager");
		_allSites.Add(this);

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
		if (_gameMode == null)
		{
			return false;
		}

		if (_gameMode.ActiveMode is not SearchAndDestroyMode sndMode)
		{
			return false;
		}

		if (sndMode.CurrentRoundState != SearchAndDestroyMode.RoundState.Active)
		{
			return false;
		}

		if (sndMode.IsBombPlanted)
			return false;

		return true;
	}

	private bool IsPlayerPlanting(PlayerCharacter player)
	{
		if (player == null)
			return false;

		if (!IsAttacker(player))
			return false;

		return player.IsInteractHeld();
	}

	private bool IsAttacker(PlayerCharacter player)
	{
		if (_gameMode?.ActiveMode is not SearchAndDestroyMode sndMode)
			return false;

		var teamId = _gameMode.GetTeamForPlayer((int)player.OwnerPeerId);
		return teamId == sndMode.GetAttackersTeamId();
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
			GD.Print($"[BombSite {SiteName}] Body entered: {body.Name}, Type={body.GetType().Name}");
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
		_isPlanting = false;
		_bombPlantedHere = true;

		var playerId = (int)(_playerInZone?.OwnerPeerId ?? 0);

		var spawnPos = BombSpawnPoint?.GlobalPosition ?? GlobalPosition;
		if (PlantedBombScene != null)
		{
			var bomb = PlantedBombScene.Instantiate<PlantedBomb>();
			bomb.PlanterId = playerId;
			bomb.SiteName = SiteName;
			GetTree().CurrentScene.AddChild(bomb);
			bomb.GlobalPosition = spawnPos;
		}

		var evt = new ObjectiveEventData
		{
			Type = ObjectiveEventType.BombPlanted,
			PlayerId = playerId,
			TeamId = _gameMode?.GetTeamForPlayer(playerId) ?? -1,
			Position = spawnPos
		};
		_gameMode?.NotifyObjectiveEvent(evt);

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
}

