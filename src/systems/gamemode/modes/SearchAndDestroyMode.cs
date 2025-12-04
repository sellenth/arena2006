using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public sealed partial class SearchAndDestroyMode : GameMode
{
	public const string ModeId = "search_and_destroy";

	public const int AttackersTeamId = 0;
	public const int DefendersTeamId = 1;

	private readonly int _totalRounds;
	private readonly int _halftimeRound;
	private readonly int _roundsToWin;
	private readonly float _roundDuration;
	private readonly float _buyPhaseDuration;
	private readonly float _roundEndDuration;
	private readonly float _halftimeDuration;
	private readonly float _bombTimerDuration;
	private readonly float _warmupDuration;
	private readonly float _resultsSeconds;
	private readonly GameModeScoreRules _scoreRules;

	private int _currentRound;
	private int _attackersScore;
	private int _defendersScore;
	private bool _teamsSwapped;
	private RoundState _roundState;
	private bool _bombPlanted;
	private float _bombTimer;
	private int _bombPlanterId;
	private readonly HashSet<int> _eliminatedThisRound = new();

	public enum RoundState
	{
		None,
		Warmup,
		BuyPhase,
		Active,
		BombPlanted,
		RoundEnd,
		Halftime,
		MatchEnd
	}

	public SearchAndDestroyMode(
		int totalRounds = 12,
		float roundDuration = 180f,
		float buyPhaseDuration = 1f,
		float roundEndDuration = 5f,
		float halftimeDuration = 15f,
		float bombTimerDuration = 45f,
		float warmupDuration = 1f,
		float resultsSeconds = 10f)
	{
		_totalRounds = Math.Max(totalRounds, 2);
		_halftimeRound = _totalRounds / 2;
		_roundsToWin = (_totalRounds / 2) + 1;
		_roundDuration = Math.Max(roundDuration, 30f);
		_buyPhaseDuration = Math.Max(buyPhaseDuration, 0f);
		_roundEndDuration = Math.Max(roundEndDuration, 3f);
		_halftimeDuration = Math.Max(halftimeDuration, 5f);
		_bombTimerDuration = Math.Max(bombTimerDuration, 10f);
		_warmupDuration = Math.Max(warmupDuration, 0f);
		_resultsSeconds = Math.Max(resultsSeconds, 3f);

		_scoreRules = new GameModeScoreRules(
			trackLaps: false,
			trackEliminations: true,
			pointsPerLap: 0,
			pointsPerElimination: 100,
			allowRespawns: false,
			suddenDeathOnTie: false
		);
	}

	public override string Id => ModeId;
	public override string DisplayName => "Search & Destroy";
	public override string Description => $"Plant or defuse the bomb. First to {_roundsToWin} rounds wins. No respawns.";
	public override GameModeScoreRules ScoreRules => _scoreRules;
	public override bool LoopPhases => true;
	public override int LoopStartIndex => 1;

	public override TeamStructure TeamStructure => TeamStructure.TwoTeams;
	public override int TeamCount => 2;
	public override bool HasRounds => true;
	public override float RoundDuration => _roundDuration;
	public override float MatchTimeLimit => 0f;

	public override IWinCondition WinCondition => new RoundWinsCondition(_roundsToWin);

	public int CurrentRound => _currentRound;
	public int AttackersScore => _attackersScore;
	public int DefendersScore => _defendersScore;
	public int RoundsToWin => _roundsToWin;
	public bool TeamsSwapped => _teamsSwapped;
	public RoundState CurrentRoundState => _roundState;
	public bool IsBombPlanted => _bombPlanted;
	public float BombTimeRemaining => _bombTimer;
	public int BombPlanterId => _bombPlanterId;

	public int GetAttackersTeamId() => _teamsSwapped ? DefendersTeamId : AttackersTeamId;
	public int GetDefendersTeamId() => _teamsSwapped ? AttackersTeamId : DefendersTeamId;

	protected override IReadOnlyList<GameModePhaseDefinition> BuildPhases()
	{
		var phases = new List<GameModePhaseDefinition>();

		if (_warmupDuration > 0.01f)
		{
			phases.Add(GameModePhaseDefinition.Timed(
				GameModePhaseType.Warmup,
				_warmupDuration,
				GameModePlayerForm.Shooter,
				weaponsEnabled: false,
				description: "Warmup"));
		}

		phases.Add(GameModePhaseDefinition.Timed(
			GameModePhaseType.Countdown,
			_buyPhaseDuration,
			GameModePlayerForm.Shooter,
			weaponsEnabled: false,
			description: "Buy Phase"));

		phases.Add(GameModePhaseDefinition.Timed(
			GameModePhaseType.FragWindow,
			_roundDuration,
			GameModePlayerForm.Shooter,
			weaponsEnabled: true,
			description: "Round Active"));

		phases.Add(GameModePhaseDefinition.Timed(
			GameModePhaseType.Results,
			_roundEndDuration,
			GameModePlayerForm.Shooter,
			weaponsEnabled: false,
			description: "Round Over"));

		return phases;
	}

	public override void OnActivated(GameModeManager manager)
	{
		_currentRound = 0;
		_attackersScore = 0;
		_defendersScore = 0;
		_teamsSwapped = false;
		_roundState = RoundState.None;
		_bombPlanted = false;
		_bombTimer = 0f;
		_bombPlanterId = 0;

		SetupTeamDefinitions(manager);
		GD.Print($"[{DisplayName}] Mode activated. First to {_roundsToWin} round wins.");
	}

	public override void OnDeactivated(GameModeManager manager)
	{
		_roundState = RoundState.None;
		GD.Print($"[{DisplayName}] Mode deactivated.");
	}

	private void SetupTeamDefinitions(GameModeManager manager)
	{
		var attackerDef = new TeamDefinition(
			GetAttackersTeamId(),
			"Attackers",
			new Color(1f, 0f, 0f)
		);
		var defenderDef = new TeamDefinition(
			GetDefendersTeamId(),
			"Defenders",
			new Color(0f, 1f, 0f)
		);

		manager.TeamManager.SetTeamDefinition(GetAttackersTeamId(), attackerDef);
		manager.TeamManager.SetTeamDefinition(GetDefendersTeamId(), defenderDef);
	}

	public override void OnPhaseEntered(GameModeManager manager, GameModePhaseState phase)
	{
		switch (phase.PhaseType)
		{
			case GameModePhaseType.Warmup:
				_roundState = RoundState.Warmup;
				GD.Print($"[{DisplayName}] Warmup phase - {_warmupDuration}s");
				manager.SetWeaponsEnabled(false, phase.PhaseType, "snd_warmup");
				break;

			case GameModePhaseType.Countdown:
				_roundState = RoundState.BuyPhase;
				_currentRound++;
				ResetRoundState();
				manager.StartRound(_currentRound);
				GD.Print($"[{DisplayName}] Round {_currentRound} - Buy Phase ({_buyPhaseDuration}s)");
				manager.SetWeaponsEnabled(false, phase.PhaseType, "snd_buy");
				RespawnAllPlayers(manager);
				break;

			case GameModePhaseType.FragWindow:
				_roundState = RoundState.Active;
				GD.Print($"[{DisplayName}] Round {_currentRound} is LIVE! ({_roundDuration}s)");
				manager.SetWeaponsEnabled(true, phase.PhaseType, "snd_live");
				break;

		case GameModePhaseType.Results:
			if (_roundState == RoundState.Active || _roundState == RoundState.BombPlanted)
			{
				HandleRoundTimeout(manager);
			}
			_roundState = RoundState.RoundEnd;
			GD.Print($"[{DisplayName}] Round {_currentRound} over. Next round in {_roundEndDuration}s...");
			manager.SetWeaponsEnabled(false, phase.PhaseType, "snd_round_end");
			break;
		}
	}

	public override void OnPhaseExited(GameModeManager manager, GameModePhaseState phase)
	{
		if (phase.PhaseType == GameModePhaseType.Results)
		{
			if (CheckMatchOver(manager))
			{
				ScheduleMatchRestart(manager);
				return;
			}

			if (_currentRound == _halftimeRound && !_teamsSwapped)
			{
				PerformHalftime(manager);
			}
		}
	}

	private async void ScheduleMatchRestart(GameModeManager manager)
	{
		GD.Print($"[{DisplayName}] Match will restart in {_resultsSeconds} seconds...");
		await manager.ToSignal(manager.GetTree().CreateTimer(_resultsSeconds), SceneTreeTimer.SignalName.Timeout);

		if (!GodotObject.IsInstanceValid(manager))
			return;

		manager.RestartActiveMode();
	}

	public override void OnPhaseTick(GameModeManager manager, GameModePhaseState phase, double delta)
	{
		if (_roundState == RoundState.BombPlanted && _bombPlanted)
		{
			_bombTimer -= (float)delta;

			if (_bombTimer <= 0f)
			{
				OnBombDetonated(manager);
			}
		}
	}

	private void ResetRoundState()
	{
		_bombPlanted = false;
		_bombTimer = 0f;
		_bombPlanterId = 0;
		_eliminatedThisRound.Clear();

		BombSite.ResetAllSites();
		PlantedBomb.DestroyActiveBomb();
		WeaponPickup.RespawnAll();
	}

	private void RespawnAllPlayers(GameModeManager manager)
	{
		GD.Print($"[{DisplayName}] Respawning all players for round {_currentRound}");

		var teamSpawns = new Dictionary<int, string>
		{
			{ GetAttackersTeamId(), "AttackerSpawn" },
			{ GetDefendersTeamId(), "DefenderSpawn" }
		};
		manager.RequestTeamRespawns(teamSpawns);
	}

	public override void OnPlayerKilled(MatchContext ctx, int victimId, int killerId)
	{
		if (_roundState != RoundState.Active && _roundState != RoundState.BombPlanted)
			return;

		_eliminatedThisRound.Add(victimId);

		if (killerId > 0 && killerId != victimId && ctx.AreEnemies(killerId, victimId))
		{
			var stats = ctx.State.GetPlayerStats(killerId);
			stats.Score += ScoreRules.PointsPerElimination;
		}

		CheckTeamEliminated(ctx);
	}

	private void CheckTeamEliminated(MatchContext ctx)
	{
		var attackersTeam = GetAttackersTeamId();
		var defendersTeam = GetDefendersTeamId();

		bool attackersAlive = IsTeamAlive(ctx, attackersTeam);
		bool defendersAlive = IsTeamAlive(ctx, defendersTeam);

		if (!attackersAlive && !defendersAlive)
		{
			EndRound(ctx.ModeManager, defendersTeam, "Both teams eliminated - Defenders win");
		}
		else if (!attackersAlive)
		{
			if (_bombPlanted)
			{
				GD.Print($"[{DisplayName}] Attackers eliminated but bomb is planted!");
			}
			else
			{
				EndRound(ctx.ModeManager, defendersTeam, "Attackers eliminated");
			}
		}
		else if (!defendersAlive)
		{
			EndRound(ctx.ModeManager, attackersTeam, "Defenders eliminated");
		}
	}

	private bool IsTeamAlive(MatchContext ctx, int teamId)
	{
		var players = ctx.TeamManager.GetPlayersOnTeam(teamId);
		foreach (var playerId in players)
		{
			if (!_eliminatedThisRound.Contains(playerId))
			{
				return true;
			}
		}
		return false;
	}

	public override void OnObjectiveEvent(MatchContext ctx, ObjectiveEventData evt)
	{
		switch (evt.Type)
		{
			case ObjectiveEventType.BombPlanted:
				OnBombPlanted(ctx, evt);
				break;
			case ObjectiveEventType.BombDefused:
				OnBombDefused(ctx, evt);
				break;
			case ObjectiveEventType.BombExploded:
				OnBombDetonated(ctx.ModeManager);
				break;
		}
	}

	private void OnBombPlanted(MatchContext ctx, ObjectiveEventData evt)
	{
		if (_roundState != RoundState.Active)
			return;

		_bombPlanted = true;
		_bombTimer = _bombTimerDuration;
		_bombPlanterId = evt.PlayerId;
		_roundState = RoundState.BombPlanted;

		var stats = ctx.State.GetPlayerStats(evt.PlayerId);
		stats.ObjectivePoints += 300;
		stats.Score += 300;

		GD.Print($"[{DisplayName}] BOMB PLANTED by Player {evt.PlayerId}! {_bombTimerDuration}s to defuse!");
	}

	private void OnBombDefused(MatchContext ctx, ObjectiveEventData evt)
	{
		if (!_bombPlanted || _roundState != RoundState.BombPlanted)
			return;

		var defuserStats = ctx.State.GetPlayerStats(evt.PlayerId);
		defuserStats.ObjectivePoints += 500;
		defuserStats.Score += 500;

		GD.Print($"[{DisplayName}] BOMB DEFUSED by Player {evt.PlayerId}!");
		EndRound(ctx.ModeManager, GetDefendersTeamId(), "Bomb defused");
	}

	private void OnBombDetonated(GameModeManager manager)
	{
		if (!_bombPlanted)
			return;

		var planterStats = manager.MatchState.GetPlayerStats(_bombPlanterId);
		if (planterStats != null)
		{
			planterStats.ObjectivePoints += 200;
			planterStats.Score += 200;
		}

		GD.Print($"[{DisplayName}] BOMB DETONATED!");
		EndRound(manager, GetAttackersTeamId(), "Bomb detonated");
	}

	private void HandleRoundTimeout(GameModeManager manager)
	{
		if (_roundState == RoundState.BombPlanted)
		{
			return;
		}

		if (_roundState == RoundState.Active)
		{
			GD.Print($"[{DisplayName}] Round time expired - Defenders win!");
			EndRound(manager, GetDefendersTeamId(), "Time expired");
		}
	}

	private void EndRound(GameModeManager manager, int winningTeam, string reason)
	{
		if (_roundState == RoundState.RoundEnd || _roundState == RoundState.MatchEnd)
			return;

		_roundState = RoundState.RoundEnd;
		_bombPlanted = false;

		if (winningTeam == GetAttackersTeamId())
		{
			_attackersScore++;
			manager.ScoreTracker.AddTeamScore(GetAttackersTeamId(), 1);
			GD.Print($"[{DisplayName}] Round {_currentRound}: ATTACKERS WIN! ({reason}) | Score: {_attackersScore}-{_defendersScore}");
		}
		else
		{
			_defendersScore++;
			manager.ScoreTracker.AddTeamScore(GetDefendersTeamId(), 1);
			GD.Print($"[{DisplayName}] Round {_currentRound}: DEFENDERS WIN! ({reason}) | Score: {_attackersScore}-{_defendersScore}");
		}

		manager.EndRound(winningTeam);
		manager.TryAdvancePhase("round_ended_early");
	}

	private void PerformHalftime(GameModeManager manager)
	{
		_teamsSwapped = true;

		var tempScore = _attackersScore;
		_attackersScore = _defendersScore;
		_defendersScore = tempScore;

		SetupTeamDefinitions(manager);

		GD.Print($"[{DisplayName}] === HALFTIME === Teams switched! Score: {_attackersScore}-{_defendersScore}");
	}

	private bool CheckMatchOver(GameModeManager manager)
	{
		if (_attackersScore >= _roundsToWin)
		{
			_roundState = RoundState.MatchEnd;
			GD.Print($"[{DisplayName}] === MATCH OVER === Attackers WIN {_attackersScore}-{_defendersScore}!");
			return true;
		}

		if (_defendersScore >= _roundsToWin)
		{
			_roundState = RoundState.MatchEnd;
			GD.Print($"[{DisplayName}] === MATCH OVER === Defenders WIN {_defendersScore}-{_attackersScore}!");
			return true;
		}

		if (_currentRound >= _totalRounds)
		{
			_roundState = RoundState.MatchEnd;
			var winner = _attackersScore > _defendersScore ? "Attackers" : "Defenders";
			GD.Print($"[{DisplayName}] === MATCH OVER === All rounds played! {winner} WIN!");
			return true;
		}

		return false;
	}

	public override void OnPlayerJoined(MatchContext ctx, int peerId)
	{
		ctx.TeamManager.TryRebalanceTeams();
	}

	public override int ResolveNextPhaseIndex(int currentPhaseIndex)
	{
		if (_roundState == RoundState.MatchEnd)
		{
			return -1;
		}

		return base.ResolveNextPhaseIndex(currentPhaseIndex);
	}

	public void RestartMatch(GameModeManager manager)
	{
		_currentRound = 0;
		_attackersScore = 0;
		_defendersScore = 0;
		_teamsSwapped = false;
		_roundState = RoundState.None;
		_bombPlanted = false;
		_bombTimer = 0f;
		_bombPlanterId = 0;
		_eliminatedThisRound.Clear();

		manager.ScoreTracker.Reset();
		SetupTeamDefinitions(manager);

		GD.Print($"[{DisplayName}] Match restarted. First to {_roundsToWin} round wins.");
	}
}

