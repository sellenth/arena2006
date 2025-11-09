using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[Flags]
public enum GameModePlayerForm
{
	None = 0,
	Car = 1 << 0,
	Shooter = 1 << 1,
	Hybrid = Car | Shooter
}

public enum GameModePhaseType
{
	None = 0,
	Lobby = 1,
	Countdown = 2,
	Racing = 3,
	FragWindow = 4,
	SuddenDeath = 5,
	Results = 6,
	Warmup = 7
}

public readonly struct GameModeScoreRules
{
	public static readonly GameModeScoreRules Default = new GameModeScoreRules(
		trackLaps: true,
		trackEliminations: false,
		pointsPerLap: 1,
		pointsPerElimination: 1,
		allowRespawns: true,
		suddenDeathOnTie: false
	);

	public bool TrackLaps { get; }
	public bool TrackEliminations { get; }
	public int PointsPerLap { get; }
	public int PointsPerElimination { get; }
	public bool AllowRespawns { get; }
	public bool SuddenDeathOnTie { get; }

	public GameModeScoreRules(
		bool trackLaps,
		bool trackEliminations,
		int pointsPerLap,
		int pointsPerElimination,
		bool allowRespawns,
		bool suddenDeathOnTie)
	{
		TrackLaps = trackLaps;
		TrackEliminations = trackEliminations;
		PointsPerLap = Math.Max(pointsPerLap, 0);
		PointsPerElimination = Math.Max(pointsPerElimination, 0);
		AllowRespawns = allowRespawns;
		SuddenDeathOnTie = suddenDeathOnTie;
	}

	public Godot.Collections.Dictionary ToDictionary()
	{
		return new Godot.Collections.Dictionary
		{
			{ "track_laps", TrackLaps },
			{ "track_eliminations", TrackEliminations },
			{ "points_per_lap", PointsPerLap },
			{ "points_per_elimination", PointsPerElimination },
			{ "allow_respawns", AllowRespawns },
			{ "sudden_death_on_tie", SuddenDeathOnTie }
		};
	}
}

public partial class GameModePhaseDefinition : RefCounted
{
	public GameModePhaseType PhaseType { get; }
	public float DurationSeconds { get; }
	public GameModePlayerForm AllowedForms { get; }
	public bool WeaponsEnabled { get; }
	public bool AutoAdvance { get; }
	public string Description { get; }

	public bool HasFixedDuration => DurationSeconds > 0.0001f;

	public GameModePhaseDefinition(
		GameModePhaseType phaseType,
		float durationSeconds,
		GameModePlayerForm allowedForms,
		bool weaponsEnabled,
		bool autoAdvance,
		string description)
	{
		PhaseType = phaseType;
		DurationSeconds = Math.Max(durationSeconds, 0.0f);
		AllowedForms = allowedForms;
		WeaponsEnabled = weaponsEnabled;
		AutoAdvance = autoAdvance && DurationSeconds > 0.0001f;
		Description = description ?? string.Empty;
	}

	public static GameModePhaseDefinition Timed(
		GameModePhaseType phaseType,
		float durationSeconds,
		GameModePlayerForm forms,
		bool weaponsEnabled,
		string description = "")
	{
		return new GameModePhaseDefinition(
			phaseType,
			durationSeconds,
			forms,
			weaponsEnabled,
			autoAdvance: true,
			description: description
		);
	}

	public static GameModePhaseDefinition Indefinite(
		GameModePhaseType phaseType,
		GameModePlayerForm forms,
		bool weaponsEnabled,
		string description = "")
	{
		return new GameModePhaseDefinition(
			phaseType,
			0.0f,
			forms,
			weaponsEnabled,
			autoAdvance: false,
			description: description
		);
	}
}

public partial class GameModePhaseState : RefCounted
{
	public GameModePhaseDefinition Definition { get; }
	public int Index { get; }
	public float ElapsedSeconds { get; private set; }
	public GameModePhaseType PhaseType => Definition.PhaseType;
	public bool IsExpired => Definition.HasFixedDuration && ElapsedSeconds >= Definition.DurationSeconds;
	public float RemainingSeconds => Definition.HasFixedDuration
		? Math.Max(Definition.DurationSeconds - ElapsedSeconds, 0.0f)
		: float.PositiveInfinity;

	public GameModePhaseState(GameModePhaseDefinition definition, int index)
	{
		Definition = definition ?? throw new ArgumentNullException(nameof(definition));
		Index = index;
	}

	public void Advance(double delta)
	{
		if (delta <= 0.0)
		{
			return;
		}

		ElapsedSeconds += (float)delta;
	}
}

public abstract partial class GameMode : GodotObject
{
	public abstract string Id { get; }
	public abstract string DisplayName { get; }
	public virtual string Description => string.Empty;
	public virtual GameModeScoreRules ScoreRules => GameModeScoreRules.Default;
	public virtual bool LoopPhases => false;
	public virtual int LoopStartIndex => 0;

	private IReadOnlyList<GameModePhaseDefinition> _phases;

	public IReadOnlyList<GameModePhaseDefinition> Phases
	{
		get
		{
			if (_phases == null)
			{
				var built = BuildPhases() ?? Array.Empty<GameModePhaseDefinition>();
				_phases = built.Where(d => d != null).ToArray();
			}
			return _phases;
		}
	}

	protected abstract IReadOnlyList<GameModePhaseDefinition> BuildPhases();

	internal void EnsurePhasesCached() => _ = Phases;

	public virtual void OnActivated(GameModeManager manager) { }
	public virtual void OnDeactivated(GameModeManager manager) { }
	public virtual void OnPhaseEntered(GameModeManager manager, GameModePhaseState phase) { }
	public virtual void OnPhaseExited(GameModeManager manager, GameModePhaseState phase) { }
	public virtual void OnPhaseTick(GameModeManager manager, GameModePhaseState phase, double delta) { }

	public virtual int ResolveNextPhaseIndex(int currentPhaseIndex)
	{
		if (Phases.Count == 0)
		{
			return -1;
		}

		if (currentPhaseIndex < 0)
		{
			return 0;
		}

		var nextIndex = currentPhaseIndex + 1;
		if (nextIndex >= Phases.Count)
		{
			return LoopPhases
				? Math.Clamp(LoopStartIndex, 0, Phases.Count - 1)
				: -1;
		}

		return nextIndex;
	}
}

[GlobalClass]
public partial class GameModeManager : Node
{
	public static GameModeManager Instance { get; private set; }

	[Export(PropertyHint.Enum, "classic,race_only,deathmatch")]
	public string DefaultModeId { get; set; } = ClassicMode.ModeId;

	private readonly Dictionary<string, GameMode> _modes = new(StringComparer.OrdinalIgnoreCase);

	private GameMode _activeMode;
	private GameModePhaseState _activePhase;
	private int _phaseIndex = -1;
	private GameModeScoreRules _activeScoreRules = GameModeScoreRules.Default;
	private GameModePlayerForm _activeForms = GameModePlayerForm.None;
	private bool _carControlEnabled = false;
	private bool _shooterControlEnabled = false;
	private bool _weaponsEnabled = false;

	[Signal]
	public delegate void GameModeChangedEventHandler(string modeId, string displayName);

	[Signal]
	public delegate void PhaseStartedEventHandler(int phaseType, string description, string reason);

	[Signal]
	public delegate void PhaseEndedEventHandler(int phaseType, string description, string reason);

	[Signal]
	public delegate void PlayerFormChangedEventHandler(int activeForms, int phaseType, string reason);

	[Signal]
	public delegate void CarControlToggledEventHandler(bool enabled, int phaseType, string reason);

	[Signal]
	public delegate void ShooterControlToggledEventHandler(bool enabled, int phaseType, string reason);

	[Signal]
	public delegate void WeaponsToggledEventHandler(bool enabled, int phaseType, string reason);

	[Signal]
	public delegate void ScoreRulesChangedEventHandler(Godot.Collections.Dictionary scoreRules);

	public GameMode ActiveMode => _activeMode;
	public GameModePhaseState ActivePhase => _activePhase;
	public GameModeScoreRules ActiveScoreRules => _activeScoreRules;
	public GameModePlayerForm ActiveForms => _activeForms;
	public bool CarControlEnabled => _carControlEnabled;
	public bool ShooterControlEnabled => _shooterControlEnabled;
	public bool WeaponsEnabled => _weaponsEnabled;

	public override void _EnterTree()
	{
		if (Instance != null && Instance != this)
		{
			GD.PushWarning("GameModeManager: Duplicate instance detected, freeing the new node.");
			QueueFree();
			return;
		}

		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public override void _Ready()
	{
		RegisterBuiltInModes();

		if (!string.IsNullOrEmpty(DefaultModeId) && _modes.ContainsKey(DefaultModeId))
		{
			SetActiveMode(DefaultModeId);
		}
		else if (_modes.Count > 0)
		{
			var first = _modes.Keys.FirstOrDefault();
			if (!string.IsNullOrEmpty(first))
			{
				SetActiveMode(first);
			}
		}
	}

	public override void _Process(double delta)
	{
		if (_activeMode == null || _activePhase == null)
		{
			return;
		}

		_activePhase.Advance(delta);
		_activeMode.OnPhaseTick(this, _activePhase, delta);

		if (_activePhase.Definition.AutoAdvance && _activePhase.IsExpired)
		{
			AdvanceToNextPhase("auto_advance");
		}
	}

	public IReadOnlyCollection<GameMode> GetRegisteredModes() => _modes.Values;

	public bool RegisterMode(GameMode mode)
	{
		if (mode == null)
		{
			return false;
		}

		var id = mode.Id?.Trim();
		if (string.IsNullOrEmpty(id))
		{
			GD.PushWarning("GameModeManager: Attempted to register a mode without an identifier.");
			return false;
		}

		if (_modes.ContainsKey(id))
		{
			GD.PushWarning($"GameModeManager: Mode '{id}' is already registered.");
			return false;
		}

		mode.EnsurePhasesCached();
		_modes[id] = mode;
		return true;
	}

	public bool SetActiveMode(string modeId)
	{
		if (string.IsNullOrEmpty(modeId))
		{
			return false;
		}

		if (!_modes.TryGetValue(modeId, out var mode))
		{
			GD.PushWarning($"GameModeManager: Unable to activate unknown mode '{modeId}'.");
			return false;
		}

		if (_activeMode == mode)
		{
			return true;
		}

		DeactivateCurrentMode("mode_switch");
		_activeMode = mode;
		_activeScoreRules = mode.ScoreRules;
		EmitSignal(SignalName.GameModeChanged, mode.Id, mode.DisplayName);
		EmitSignal(SignalName.ScoreRulesChanged, _activeScoreRules.ToDictionary());
		mode.OnActivated(this);
		_phaseIndex = -1;
		_activePhase = null;
		AdvanceToNextPhase("mode_start");
		return true;
	}

	public bool TryAdvancePhase(string reason = "manual_request")
	{
		if (_activeMode == null)
		{
			return false;
		}

		AdvanceToNextPhase(reason);
		return true;
	}

	private void AdvanceToNextPhase(string reason)
	{
		if (_activeMode == null)
		{
			return;
		}

		if (_activePhase != null)
		{
			_activeMode.OnPhaseExited(this, _activePhase);
			EmitSignal(
				SignalName.PhaseEnded,
				(int)_activePhase.PhaseType,
				_activePhase.Definition.Description,
				reason
			);
		}

		var nextIndex = _activeMode.ResolveNextPhaseIndex(_phaseIndex);
		if (nextIndex < 0)
		{
			_phaseIndex = -1;
			_activePhase = null;
			UpdateForms(GameModePlayerForm.None, GameModePhaseType.None, "phase_sequence_complete");
			SetWeaponsEnabled(false, GameModePhaseType.None, "phase_sequence_complete");
			return;
		}

		_phaseIndex = nextIndex;
		var definition = _activeMode.Phases[nextIndex];
		_activePhase = new GameModePhaseState(definition, nextIndex);
		ApplyPhaseAccess(definition);
		_activeMode.OnPhaseEntered(this, _activePhase);
		EmitSignal(
			SignalName.PhaseStarted,
			(int)definition.PhaseType,
			definition.Description,
			reason
		);
	}

	private void DeactivateCurrentMode(string reason)
	{
		if (_activeMode == null)
		{
			return;
		}

		if (_activePhase != null)
		{
			_activeMode.OnPhaseExited(this, _activePhase);
			EmitSignal(
				SignalName.PhaseEnded,
				(int)_activePhase.PhaseType,
				_activePhase.Definition.Description,
				reason
			);
			_activePhase = null;
		}

		_activeMode.OnDeactivated(this);
		_activeMode = null;
		_phaseIndex = -1;
		UpdateForms(GameModePlayerForm.None, GameModePhaseType.None, "mode_deactivated");
		SetWeaponsEnabled(false, GameModePhaseType.None, "mode_deactivated");
	}

	private void ApplyPhaseAccess(GameModePhaseDefinition definition)
	{
		UpdateForms(definition.AllowedForms, definition.PhaseType, "phase_definition");
		SetWeaponsEnabled(definition.WeaponsEnabled, definition.PhaseType, "phase_definition");
	}

	private void UpdateForms(GameModePlayerForm forms, GameModePhaseType phaseType, string reason)
	{
		if (_activeForms == forms)
		{
			return;
		}

		_activeForms = forms;
		EmitSignal(SignalName.PlayerFormChanged, (int)forms, (int)phaseType, reason);

		var carEnabled = forms.HasFlag(GameModePlayerForm.Car);
		var shooterEnabled = forms.HasFlag(GameModePlayerForm.Shooter);
		SetCarControlEnabled(carEnabled, phaseType, reason);
		SetShooterControlEnabled(shooterEnabled, phaseType, reason);
	}

	public void SetCarControlEnabled(bool enabled, GameModePhaseType contextPhase, string reason)
	{
		if (_carControlEnabled == enabled)
		{
			return;
		}

		_carControlEnabled = enabled;
		EmitSignal(SignalName.CarControlToggled, enabled, (int)contextPhase, reason);
	}

	public void SetShooterControlEnabled(bool enabled, GameModePhaseType contextPhase, string reason)
	{
		if (_shooterControlEnabled == enabled)
		{
			return;
		}

		_shooterControlEnabled = enabled;
		EmitSignal(SignalName.ShooterControlToggled, enabled, (int)contextPhase, reason);
	}

	public void SetWeaponsEnabled(bool enabled, GameModePhaseType contextPhase, string reason)
	{
		if (_weaponsEnabled == enabled)
		{
			return;
		}

		_weaponsEnabled = enabled;
		EmitSignal(SignalName.WeaponsToggled, enabled, (int)contextPhase, reason);
	}

	private void RegisterBuiltInModes()
	{
		RegisterMode(new ClassicMode());
		RegisterMode(new RaceOnlyMode());
		RegisterMode(new DeathmatchMode());
	}
}
