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

	public virtual TeamStructure TeamStructure => TeamStructure.FreeForAll;
	public virtual int TeamCount => 0;
	public virtual IWinCondition WinCondition => null;
	public virtual bool HasRounds => false;
	public virtual float RoundDuration => 0f;
	public virtual float MatchTimeLimit => 0f;

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

	public virtual TeamConfig GetTeamConfig()
	{
		return TeamStructure switch
		{
			TeamStructure.TwoTeams => TeamConfig.TwoTeam,
			TeamStructure.MultiTeam => TeamConfig.CreateMultiTeam(TeamCount),
			_ => TeamConfig.FreeForAll
		};
	}

	public virtual void OnActivated(GameModeManager manager) { }
	public virtual void OnDeactivated(GameModeManager manager) { }
	public virtual void OnPhaseEntered(GameModeManager manager, GameModePhaseState phase) { }
	public virtual void OnPhaseExited(GameModeManager manager, GameModePhaseState phase) { }
	public virtual void OnPhaseTick(GameModeManager manager, GameModePhaseState phase, double delta) { }

	public virtual void OnPlayerKilled(MatchContext ctx, int victimId, int killerId) { }
	public virtual void OnObjectiveEvent(MatchContext ctx, ObjectiveEventData evt) { }
	public virtual void OnRoundStart(MatchContext ctx, int roundNumber) { }
	public virtual void OnRoundEnd(MatchContext ctx, int winningTeam) { }
	public virtual void OnPlayerJoined(MatchContext ctx, int peerId) { }
	public virtual void OnPlayerLeft(MatchContext ctx, int peerId) { }

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

public enum ObjectiveEventType
{
	None = 0,
	FlagPickedUp,
	FlagDropped,
	FlagCaptured,
	FlagReturned,
	BombPlanted,
	BombDefused,
	BombExploded,
	CheckpointReached,
	LapCompleted,
	ZoneCaptured,
	ZoneLost
}

public struct ObjectiveEventData
{
	public ObjectiveEventType Type;
	public int PlayerId;
	public int TeamId;
	public int ObjectiveId;
	public Vector3 Position;
}

[GlobalClass]
public partial class GameModeManager : Node
{
	public static GameModeManager Instance { get; private set; }

	[Export(PropertyHint.Enum, "classic,race_only,deathmatch,team_deathmatch,search_and_destroy")]
	public string DefaultModeId { get; set; } = SearchAndDestroyMode.ModeId;

	private readonly Dictionary<string, GameMode> _modes = new(StringComparer.OrdinalIgnoreCase);

	private GameMode _activeMode;
	private GameModePhaseState _activePhase;
	private int _phaseIndex = -1;
	private GameModeScoreRules _activeScoreRules = GameModeScoreRules.Default;
	private GameModePlayerForm _activeForms = GameModePlayerForm.None;
	private bool _carControlEnabled = false;
	private bool _shooterControlEnabled = false;
	private bool _weaponsEnabled = false;

	private TeamManager _teamManager;
	private MatchState _matchState;
	private ScoreTracker _scoreTracker;
	private MatchContext _matchContext;
	private int _serverTick;

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

	[Signal]
	public delegate void PlayerKilledEventHandler(int killerId, int victimId, int teamId);

	[Signal]
	public delegate void TeamScoreChangedEventHandler(int teamId, int score);

	[Signal]
	public delegate void MatchEndedEventHandler(int winningTeam);

	[Signal]
	public delegate void RoundStartedEventHandler(int roundNumber);

	[Signal]
	public delegate void RoundEndedEventHandler(int roundNumber, int winningTeam);

	[Signal]
	public delegate void PlayerTeamColorChangedEventHandler(int peerId, Color color);

	[Signal]
	public delegate void RespawnPlayersAtTeamSpawnsEventHandler(Godot.Collections.Dictionary teamSpawnNodes);

	public GameMode ActiveMode => _activeMode;
	public GameModePhaseState ActivePhase => _activePhase;
	public GameModeScoreRules ActiveScoreRules => _activeScoreRules;
	public GameModePlayerForm ActiveForms => _activeForms;
	public bool CarControlEnabled => _carControlEnabled;
	public bool ShooterControlEnabled => _shooterControlEnabled;
	public bool WeaponsEnabled => _weaponsEnabled;
	public TeamManager TeamManager => _teamManager;
	public MatchState MatchState => _matchState;
	public ScoreTracker ScoreTracker => _scoreTracker;
	public MatchContext Context => _matchContext;

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
		InitializeMatchSystems();
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

	private void InitializeMatchSystems()
	{
		_teamManager = new TeamManager();
		_matchState = new MatchState();
		_scoreTracker = new ScoreTracker(_matchState, _teamManager);
		_matchContext = new MatchContext(_matchState, _teamManager, _scoreTracker, this);

		_scoreTracker.TeamScoreChanged += OnTeamScoreChanged;
		_scoreTracker.PlayerKilled += OnPlayerKilledInternal;
		_scoreTracker.WinConditionMet += OnWinConditionMet;
		_teamManager.PlayerTeamChanged += OnPlayerTeamChanged;
	}

	private void OnPlayerTeamChanged(int peerId, int oldTeam, int newTeam)
	{
		var teamDef = _teamManager.GetTeamDefinition(newTeam);
		var color = teamDef?.Color ?? Colors.White;
		EmitSignal(SignalName.PlayerTeamColorChanged, peerId, color);
	}

	public Color GetTeamColorForPlayer(int peerId)
	{
		var teamId = _teamManager?.GetTeamForPlayer(peerId) ?? TeamManager.NoTeam;
		var teamDef = _teamManager?.GetTeamDefinition(teamId);
		return teamDef?.Color ?? Colors.White;
	}

	private void OnTeamScoreChanged(int teamId, int score)
	{
		EmitSignal(SignalName.TeamScoreChanged, teamId, score);
	}

	private void OnPlayerKilledInternal(int killerId, int victimId, int assisterId)
	{
		var killerTeam = _teamManager.GetTeamForPlayer(killerId);
		EmitSignal(SignalName.PlayerKilled, killerId, victimId, killerTeam);
	}

	private void OnWinConditionMet(int winningTeam)
	{
		if (_matchState.IsOver)
			return;

		_matchState.EndMatch(winningTeam);

		if (_teamManager.IsFreeForAll)
		{
			GD.Print($"=== MATCH OVER === Player {winningTeam} WINS! ===");
		}
		else
		{
			var teamDef = _teamManager.GetTeamDefinition(winningTeam);
			GD.Print($"=== MATCH OVER === {teamDef.Name} WINS! ===");
		}

		EmitSignal(SignalName.MatchEnded, winningTeam);
		TransitionToResultsPhase();
	}

	private void TransitionToResultsPhase()
	{
		if (_activeMode == null)
			return;

		for (int i = 0; i < _activeMode.Phases.Count; i++)
		{
			if (_activeMode.Phases[i].PhaseType == GameModePhaseType.Results)
			{
				if (_activePhase != null)
				{
					_activeMode.OnPhaseExited(this, _activePhase);
					EmitSignal(
						SignalName.PhaseEnded,
						(int)_activePhase.PhaseType,
						_activePhase.Definition.Description,
						"win_condition_met"
					);
				}

				_phaseIndex = i;
				var definition = _activeMode.Phases[i];
				_activePhase = new GameModePhaseState(definition, i);
				ApplyPhaseAccess(definition);
				_activeMode.OnPhaseEntered(this, _activePhase);
				EmitSignal(
					SignalName.PhaseStarted,
					(int)definition.PhaseType,
					definition.Description,
					"win_condition_met"
				);
				return;
			}
		}

		GD.Print("No Results phase defined - advancing to next phase");
		AdvanceToNextPhase("win_condition_met");
	}

	public override void _Process(double delta)
	{
		_serverTick++;
		_matchState?.SetServerTick(_serverTick);

		if (_matchState != null && _matchState.IsLive)
		{
			_matchState.TickPhaseTime((float)delta);

			if (_activeMode?.WinCondition != null)
			{
				_scoreTracker?.CheckWinCondition(_activeMode.WinCondition);
			}
		}

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

		_teamManager.Configure(mode.GetTeamConfig());
		_matchState.Reset();
		_matchState.SetModeId(mode.Id);

		if (mode.MatchTimeLimit > 0)
		{
			_matchState.SetPhase(MatchPhase.Live, mode.MatchTimeLimit);
		}
		else
		{
			_matchState.SetPhase(MatchPhase.Live);
		}

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
		RegisterMode(new TeamDeathmatchMode());
		RegisterMode(new SearchAndDestroyMode());
	}

	public void NotifyPlayerKilled(int victimId, int killerId)
	{
		if (_activeMode == null || _matchContext == null)
			return;

		if (_matchState.IsOver)
		{
			GD.Print($"[GameMode] Kill ignored - match already over");
			return;
		}

		var victimTeam = _teamManager.GetTeamForPlayer(victimId);
		var killerTeam = _teamManager.GetTeamForPlayer(killerId);

		if (killerId == victimId || killerId <= 0)
		{
			_scoreTracker.RecordSuicide(victimId);
			GD.Print($"[GameMode] Player {victimId} died (suicide/environment)");
		}
		else if (_teamManager.ArePlayersOnSameTeam(killerId, victimId))
		{
			_scoreTracker.RecordTeamKill(killerId, victimId);
			GD.Print($"[GameMode] Player {killerId} team-killed Player {victimId}");
		}
		else
		{
			_scoreTracker.RecordKill(killerId, victimId);
			var killerScore = _scoreTracker.GetPlayerScore(killerId);
			var killerKills = _scoreTracker.GetPlayerKills(killerId);
			GD.Print($"[GameMode] Player {killerId} killed Player {victimId} | Score: {killerScore} Kills: {killerKills}");
		}

		_activeMode.OnPlayerKilled(_matchContext, victimId, killerId);
	}

	public void NotifyPlayerJoined(int peerId)
	{
		if (_activeMode == null || _matchContext == null)
			return;

		if (_teamManager.IsTeamBased)
		{
			_teamManager.AutoAssignTeam(peerId);
		}

		_activeMode.OnPlayerJoined(_matchContext, peerId);
	}

	public void NotifyPlayerLeft(int peerId)
	{
		if (_activeMode == null || _matchContext == null)
			return;

		_activeMode.OnPlayerLeft(_matchContext, peerId);
		_teamManager.RemovePlayer(peerId);
		_matchState.RemovePlayer(peerId);
	}

	public void NotifyObjectiveEvent(ObjectiveEventData evt)
	{
		if (_activeMode == null || _matchContext == null)
			return;

		_activeMode.OnObjectiveEvent(_matchContext, evt);
	}

	public int GetTeamForPlayer(int peerId)
	{
		return _teamManager?.GetTeamForPlayer(peerId) ?? TeamManager.NoTeam;
	}

	public bool AssignPlayerToTeam(int peerId, int teamId)
	{
		return _teamManager?.AssignPlayerToTeam(peerId, teamId) ?? false;
	}

	public MatchStateSnapshot GetMatchStateSnapshot()
	{
		return MatchStateSnapshot.FromState(_matchState, this);
	}

	public void StartRound(int roundNumber)
	{
		if (_activeMode == null || !_activeMode.HasRounds)
			return;

		_matchState.SetRound(roundNumber);
		_activeMode.OnRoundStart(_matchContext, roundNumber);
		EmitSignal(SignalName.RoundStarted, roundNumber);
	}

	public void EndRound(int winningTeam)
	{
		if (_activeMode == null || !_activeMode.HasRounds)
			return;

		var roundNumber = _matchState.RoundNumber;
		_activeMode.OnRoundEnd(_matchContext, winningTeam);
		EmitSignal(SignalName.RoundEnded, roundNumber, winningTeam);
	}

	public void RequestTeamRespawns(System.Collections.Generic.Dictionary<int, string> teamSpawnNodes)
	{
		var dict = new Godot.Collections.Dictionary();
		foreach (var kvp in teamSpawnNodes)
		{
			dict[kvp.Key] = kvp.Value;
		}

		// Emit signal for external handlers (e.g., ServerNetworkManager).
		EmitSignal(SignalName.RespawnPlayersAtTeamSpawns, dict);
	}

	public void RestartActiveMode()
	{
		if (_activeMode == null)
			return;

		var modeId = _activeMode.Id;

		if (_activeMode is SearchAndDestroyMode sndMode)
		{
			sndMode.RestartMatch(this);
		}

		_matchState.Reset();
		_phaseIndex = -1;
		_activePhase = null;
		AdvanceToNextPhase("mode_restart");

		GD.Print($"[GameModeManager] Mode '{modeId}' restarted");
	}
}
