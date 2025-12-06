using Godot;
using System;

public partial class MatchStateClient : Node
{
	public static MatchStateClient Instance { get; private set; }

	private MatchPhase _phase = MatchPhase.None;
	private GameModePhaseType _gameModePhase = GameModePhaseType.None;
	private float _phaseTimeRemaining;
	private float _lastServerTime;
	private float _localTimeOffset;
	private int _roundNumber;
	private readonly int[] _teamScores = new int[MatchState.MaxTeams];
	private string _currentModeId = string.Empty;
	private int _serverTick;
	private int _localTeamId = TeamManager.NoTeam;
	private bool _weaponsEnabled;
	private ObjectiveState _objectiveState;

	public event Action<MatchPhase> PhaseChanged;
	public event Action<int, int> TeamScoreChanged;
	public event Action<int> RoundChanged;
	public event Action<int> MatchEnded;
	public event Action StateUpdated;
	public event Action<bool> WeaponsEnabledChanged;

	public MatchPhase Phase => _phase;
	public GameModePhaseType GameModePhase => _gameModePhase;
	public float PhaseTimeRemaining => PredictTimeRemaining();
	public int RoundNumber => _roundNumber;
	public string CurrentModeId => _currentModeId;
	public int ServerTick => _serverTick;
	public int LocalTeamId => _localTeamId;
	public bool IsLive => _phase == MatchPhase.Live;
	public bool IsWarmup => _phase == MatchPhase.Warmup;
	public bool WeaponsEnabled => _weaponsEnabled;
	public ObjectiveState Objective => _objectiveState;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	public int GetTeamScore(int teamId)
	{
		if (teamId < 0 || teamId >= MatchState.MaxTeams)
			return 0;
		return _teamScores[teamId];
	}

	public void SetLocalTeam(int teamId)
	{
		_localTeamId = teamId;
	}

	public void ApplySnapshot(MatchStateSnapshot snapshot)
	{
		var oldPhase = _phase;
		_phase = snapshot.Phase;
		_gameModePhase = snapshot.GameModePhase;
		_phaseTimeRemaining = snapshot.PhaseTimeRemaining;
		_lastServerTime = (float)Time.GetTicksMsec() / 1000f;
		_serverTick = snapshot.ServerTick;
		_currentModeId = snapshot.ModeId;
		_objectiveState = snapshot.Objective;

		if (oldPhase != _phase)
		{
			PhaseChanged?.Invoke(_phase);
		}

		if (_roundNumber != snapshot.RoundNumber)
		{
			_roundNumber = snapshot.RoundNumber;
			RoundChanged?.Invoke(_roundNumber);
		}

		for (int i = 0; i < MatchState.MaxTeams && i < snapshot.TeamScores.Length; i++)
		{
			var oldScore = _teamScores[i];
			_teamScores[i] = snapshot.TeamScores[i];
			if (oldScore != _teamScores[i])
			{
				TeamScoreChanged?.Invoke(i, _teamScores[i]);
			}
		}

		var oldWeaponsEnabled = _weaponsEnabled;
		_weaponsEnabled = snapshot.WeaponsEnabled;
		if (oldWeaponsEnabled != _weaponsEnabled)
		{
			WeaponsEnabledChanged?.Invoke(_weaponsEnabled);
		}

		if (snapshot.WinningTeam >= 0)
		{
			MatchEnded?.Invoke(snapshot.WinningTeam);
		}

		StateUpdated?.Invoke();
	}

	private float PredictTimeRemaining()
	{
		if (_phaseTimeRemaining <= 0f)
			return 0f;

		var now = (float)Time.GetTicksMsec() / 1000f;
		var elapsed = now - _lastServerTime;
		return Mathf.Max(0f, _phaseTimeRemaining - elapsed);
	}

	public string FormatTimeRemaining()
	{
		var time = PhaseTimeRemaining;
		if (time <= 0f)
			return "0:00";

		var minutes = (int)(time / 60f);
		var seconds = (int)(time % 60f);
		return $"{minutes}:{seconds:D2}";
	}
}

public struct MatchStateSnapshot
{
	public MatchPhase Phase;
	public GameModePhaseType GameModePhase;
	public float PhaseTimeRemaining;
	public int RoundNumber;
	public int WinningTeam;
	public int[] TeamScores;
	public string ModeId;
	public int ServerTick;
	public bool WeaponsEnabled;
	public ObjectiveState Objective;

	public static MatchStateSnapshot FromState(MatchState state, GameModeManager manager)
	{
		var objState = new ObjectiveState();
		if (manager?.ActiveMode is IGameModeObjectiveDelegate objDelegate)
		{
			objState = objDelegate.GetObjectiveState();
		}

		return new MatchStateSnapshot
		{
			Phase = state.Phase,
			GameModePhase = manager?.ActivePhase?.PhaseType ?? GameModePhaseType.None,
			PhaseTimeRemaining = manager?.ActivePhase?.RemainingSeconds ?? state.PhaseTimeRemaining,
			RoundNumber = state.RoundNumber,
			WinningTeam = state.WinningTeam,
			TeamScores = state.GetAllTeamScores(),
			ModeId = state.CurrentModeId,
			ServerTick = state.ServerTick,
			WeaponsEnabled = manager?.WeaponsEnabled ?? false,
			Objective = objState
		};
	}
}

