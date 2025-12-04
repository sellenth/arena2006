using System;
using System.Collections.Generic;
using Godot;

public sealed partial class TeamDeathmatchMode : GameMode
{
	public const string ModeId = "team_deathmatch";

	private readonly int _scoreLimit;
	private readonly float _timeLimit;
	private readonly float _warmupSeconds;
	private readonly float _resultsSeconds;
	private readonly GameModeScoreRules _scoreRules;

	public TeamDeathmatchMode(int scoreLimit = 1, float timeLimit = 600f, float warmupSeconds = 1f, float resultsSeconds = 10f)
	{
		_scoreLimit = Math.Max(scoreLimit, 1);
		_timeLimit = Math.Max(timeLimit, 60f);
		_warmupSeconds = Math.Max(warmupSeconds, 0f);
		_resultsSeconds = Math.Max(resultsSeconds, 3f);
		_scoreRules = new GameModeScoreRules(
			trackLaps: false,
			trackEliminations: true,
			pointsPerLap: 0,
			pointsPerElimination: 1,
			allowRespawns: true,
			suddenDeathOnTie: false
		);
	}

	public override string Id => ModeId;
	public override string DisplayName => "Team Deathmatch";
	public override string Description => $"First team to {_scoreLimit} kills wins. Respawns enabled.";
	public override GameModeScoreRules ScoreRules => _scoreRules;
	public override bool LoopPhases => false;

	public override TeamStructure TeamStructure => TeamStructure.TwoTeams;
	public override int TeamCount => 2;
	public override IWinCondition WinCondition => new CompositeCondition(
		new ScoreLimitCondition(_scoreLimit),
		new TimeLimitCondition(_timeLimit)
	);
	public override float MatchTimeLimit => _timeLimit;

	protected override IReadOnlyList<GameModePhaseDefinition> BuildPhases()
	{
		var phases = new List<GameModePhaseDefinition>();

		if (_warmupSeconds > 0.01f)
		{
			phases.Add(GameModePhaseDefinition.Timed(
				GameModePhaseType.Warmup,
				_warmupSeconds,
				GameModePlayerForm.Shooter,
				weaponsEnabled: false,
				description: "Warmup"));
		}

		phases.Add(GameModePhaseDefinition.Indefinite(
			GameModePhaseType.FragWindow,
			GameModePlayerForm.Shooter,
			weaponsEnabled: true,
			description: "Team Deathmatch"));

		phases.Add(GameModePhaseDefinition.Timed(
			GameModePhaseType.Results,
			_resultsSeconds,
			GameModePlayerForm.Shooter,
			weaponsEnabled: false,
			description: "Match Over"));

		return phases;
	}

	public override void OnPhaseEntered(GameModeManager manager, GameModePhaseState phase)
	{
		switch (phase.PhaseType)
		{
			case GameModePhaseType.Warmup:
				GD.Print($"[{DisplayName}] Warmup phase started - {_warmupSeconds}s");
				manager.SetWeaponsEnabled(false, phase.PhaseType, "tdm_warmup");
				break;
			case GameModePhaseType.FragWindow:
				GD.Print($"[{DisplayName}] Match is LIVE! First team to {_scoreLimit} kills wins.");
				manager.SetWeaponsEnabled(true, phase.PhaseType, "tdm_live");
				break;
			case GameModePhaseType.Results:
				GD.Print($"[{DisplayName}] Results phase - Match Over!");
				manager.SetWeaponsEnabled(false, phase.PhaseType, "tdm_results");
				break;
		}
	}

	public override void OnPlayerKilled(MatchContext ctx, int victimId, int killerId)
	{
		if (killerId <= 0 || killerId == victimId)
			return;

		if (ctx.AreEnemies(killerId, victimId))
		{
			var killerTeam = ctx.GetTeamForPlayer(killerId);
			if (killerTeam != TeamManager.NoTeam)
			{
				ctx.ScoreTracker.AddTeamScore(killerTeam, 1);
			}
		}
	}

	public override void OnPlayerJoined(MatchContext ctx, int peerId)
	{
		ctx.TeamManager.TryRebalanceTeams();
	}
}

