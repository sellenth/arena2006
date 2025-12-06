using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class PlantedBomb : Area3D
{
	[Export] public float FuseTime { get; set; } = 45f;
	[Export] public float DefuseTime { get; set; } = 7f;
	[Export] public float DefuseRadius { get; set; } = 2f;
	[Export] public PackedScene ExplosionEffect { get; set; }

	public int PlanterId { get; set; }
	public string SiteName { get; set; } = "";

	private static PlantedBomb _activeBomb;
	public static PlantedBomb ActiveBomb => _activeBomb;

	private GameModeManager _gameModeManager;
	private float _fuseTimer;
	private float _defuseProgress;
	private bool _isBeingDefused;
	private bool _exploded;
	private bool _defused;
	private PlayerCharacter _defusingPlayer;
	private readonly List<PlayerCharacter> _playersInRange = new();

	[Signal]
	public delegate void DefuseStartedEventHandler(int playerId);

	[Signal]
	public delegate void DefuseProgressEventHandler(float progress);

	[Signal]
	public delegate void DefuseCancelledEventHandler();

	[Signal]
	public delegate void BombDefusedEventHandler(int playerId);

	[Signal]
	public delegate void BombExplodedEventHandler();

	[Signal]
	public delegate void FuseTickEventHandler(float remaining);

	public float FuseRemaining => _fuseTimer;
	public float CurrentDefuseProgress => _defuseProgress;
	public bool IsBeingDefused => _isBeingDefused;
	public bool IsExploded => _exploded;
	public bool IsDefused => _defused;

	public override void _Ready()
	{
		_gameModeManager = GetNodeOrNull<GameModeManager>("/root/GameModeManager");
		_fuseTimer = FuseTime;
		_activeBomb = this;

		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;

		GD.Print($"[PlantedBomb] Bomb armed at Site {SiteName}! {FuseTime}s until detonation.");
	}

	public override void _ExitTree()
	{
		if (_activeBomb == this)
		{
			_activeBomb = null;
		}
	}

	public override void _Process(double delta)
	{
		if (_exploded || _defused)
			return;

		_fuseTimer -= (float)delta;
		EmitSignal(SignalName.FuseTick, _fuseTimer);

		if (_fuseTimer <= 0f)
		{
			Explode();
			return;
		}

		UpdateDefuse((float)delta);
	}

	private void UpdateDefuse(float delta)
	{
		var defuser = FindDefusingPlayer();

		if (defuser != null)
		{
			if (_defusingPlayer != defuser)
			{
				StartDefuse(defuser);
			}

			_defuseProgress += delta;
			EmitSignal(SignalName.DefuseProgress, _defuseProgress / DefuseTime);

			if (_defuseProgress >= DefuseTime)
			{
				CompleteDefuse();
			}
		}
		else if (_isBeingDefused)
		{
			CancelDefuse();
		}
	}

	private PlayerCharacter FindDefusingPlayer()
	{
		foreach (var player in _playersInRange)
		{
			if (player == null || !player.IsInsideTree())
				continue;

			if (!IsDefender(player))
				continue;

			if (player.IsInteractHeld())
			{
				return player;
			}
		}
		return null;
	}

	private bool IsDefender(PlayerCharacter player)
	{
		if (_gameModeManager?.ActiveMode is IGameModeObjectiveDelegate objectiveMode)
		{
			return objectiveMode.IsDefender((int)player.OwnerPeerId);
		}
		return false;
	}

	private void OnBodyEntered(Node body)
	{
		if (body is PlayerCharacter player)
		{
			_playersInRange.Add(player);
			if (IsDefender(player))
			{
				GD.Print($"[PlantedBomb] Defender in defuse range");
			}
		}
	}

	private void OnBodyExited(Node body)
	{
		if (body is PlayerCharacter player)
		{
			_playersInRange.Remove(player);
			if (player == _defusingPlayer)
			{
				CancelDefuse();
			}
		}
	}

	private void StartDefuse(PlayerCharacter player)
	{
		_isBeingDefused = true;
		_defuseProgress = 0f;
		_defusingPlayer = player;
		var playerId = (int)player.OwnerPeerId;
		EmitSignal(SignalName.DefuseStarted, playerId);
		GD.Print($"[PlantedBomb] Defuse started by Player {playerId}");
	}

	private void CancelDefuse()
	{
		_isBeingDefused = false;
		_defuseProgress = 0f;
		_defusingPlayer = null;
		EmitSignal(SignalName.DefuseCancelled);
		GD.Print($"[PlantedBomb] Defuse cancelled");
	}

	private void CompleteDefuse()
	{
		// Only server processes completion
		if (!IsMultiplayerAuthority())
		{
			_isBeingDefused = false;
			_defuseProgress = 0f;
			return;
		}

		_defused = true;
		_isBeingDefused = false;

		var playerId = (int)(_defusingPlayer?.OwnerPeerId ?? 0);

		var evt = new ObjectiveEventData
		{
			Type = ObjectiveEventType.BombDefused,
			PlayerId = playerId,
			TeamId = _gameModeManager?.GetTeamForPlayer(playerId) ?? -1,
			Position = GlobalPosition
		};
		_gameModeManager?.NotifyObjectiveEvent(evt);

		EmitSignal(SignalName.BombDefused, playerId);
		GD.Print($"[PlantedBomb] BOMB DEFUSED by Player {playerId}!");

		QueueFree();
	}

	private void Explode()
	{
		// Only server processes explosion
		if (!IsMultiplayerAuthority())
			return;

		_exploded = true;

		if (ExplosionEffect != null)
		{
			var explosion = ExplosionEffect.Instantiate<Node3D>();
			GetTree().CurrentScene.AddChild(explosion);
			explosion.GlobalPosition = GlobalPosition;
		}

		var evt = new ObjectiveEventData
		{
			Type = ObjectiveEventType.BombExploded,
			PlayerId = PlanterId,
			TeamId = _gameModeManager?.GetTeamForPlayer(PlanterId) ?? -1,
			Position = GlobalPosition
		};
		_gameModeManager?.NotifyObjectiveEvent(evt);

		EmitSignal(SignalName.BombExploded);
		GD.Print($"[PlantedBomb] BOMB EXPLODED!");

		QueueFree();
	}

	public static void DestroyActiveBomb()
	{
		if (_activeBomb != null && _activeBomb.IsInsideTree())
		{
			_activeBomb.QueueFree();
			_activeBomb = null;
		}
	}
}