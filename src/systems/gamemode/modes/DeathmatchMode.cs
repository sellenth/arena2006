using System;
using System.Collections.Generic;
using Godot;

public sealed partial class DeathmatchMode : GameMode
{
	public const string ModeId = "deathmatch";

	private readonly float _warmupSeconds;
	private readonly float _resultsSeconds;
	private readonly int _scoreLimit;
	private readonly GameModeScoreRules _scoreRules;

	public DeathmatchMode(float warmupSeconds = 15.0f, int scoreLimit = 2, float resultsSeconds = 10.0f)
	{
		_warmupSeconds = Math.Max(warmupSeconds, 0.0f);
		_resultsSeconds = Math.Max(resultsSeconds, 3.0f);
		_scoreLimit = Math.Max(scoreLimit, 1);
		_scoreRules = new GameModeScoreRules(
			trackLaps: false,
			trackEliminations: true,
			pointsPerLap: 0,
			pointsPerElimination: 1,
			allowRespawns: true,
			suddenDeathOnTie: true
		);
	}

	public override string Id => ModeId;
	public override string DisplayName => "Deathmatch";
	public override string Description => $"On-foot frag-only mode. First to {_scoreLimit} kills wins.";
	public override GameModeScoreRules ScoreRules => _scoreRules;
	public override bool LoopPhases => false;

	public override TeamStructure TeamStructure => TeamStructure.FreeForAll;
	public override IWinCondition WinCondition => new KillLimitCondition(_scoreLimit);

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
			description: "Deathmatch"));

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
				manager.SetWeaponsEnabled(false, phase.PhaseType, "deathmatch_warmup");
				break;
			case GameModePhaseType.FragWindow:
				GD.Print($"[{DisplayName}] Match is LIVE! First to {_scoreLimit} kills wins.");
				manager.SetWeaponsEnabled(true, phase.PhaseType, "deathmatch_round");
				break;
			case GameModePhaseType.Results:
				GD.Print($"[{DisplayName}] Results phase - Match Over!");
				manager.SetWeaponsEnabled(false, phase.PhaseType, "deathmatch_results");
				break;
		}
	}

	public override void OnPlayerKilled(MatchContext ctx, int victimId, int killerId)
	{
		if (killerId > 0 && killerId != victimId)
		{
			ctx.ScoreTracker?.AddPlayerScore(killerId, ScoreRules.PointsPerElimination);
		}
	}
}
