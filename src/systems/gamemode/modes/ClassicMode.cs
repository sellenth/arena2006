using System;
using System.Collections.Generic;

public sealed partial class ClassicMode : GameMode
{
	public const string ModeId = "classic";

	private readonly float _raceWindowSeconds;
	private readonly float _fragWindowSeconds;
	private readonly GameModeScoreRules _scoreRules;

	public ClassicMode(float raceWindowSeconds = 90.0f, float fragWindowSeconds = 45.0f)
	{
		_raceWindowSeconds = Math.Max(raceWindowSeconds, 15.0f);
		_fragWindowSeconds = Math.Max(fragWindowSeconds, 20.0f);
		_scoreRules = new GameModeScoreRules(
			trackLaps: true,
			trackEliminations: true,
			pointsPerLap: 5,
			pointsPerElimination: 1,
			allowRespawns: true,
			suddenDeathOnTie: true
		);
	}

	public override string Id => ModeId;
	public override string DisplayName => "Classic Circuit";
	public override string Description => "Alternates between lap racing and short frag windows between laps.";
	public override GameModeScoreRules ScoreRules => _scoreRules;
	public override bool LoopPhases => true;
	public override int LoopStartIndex => 0;

	public override TeamStructure TeamStructure => TeamStructure.FreeForAll;

	protected override IReadOnlyList<GameModePhaseDefinition> BuildPhases()
	{
		return new[]
		{
			GameModePhaseDefinition.Timed(
				GameModePhaseType.Racing,
				_raceWindowSeconds,
				GameModePlayerForm.Car,
				weaponsEnabled: false,
				description: "Racing window"),
			GameModePhaseDefinition.Timed(
				GameModePhaseType.FragWindow,
				_fragWindowSeconds,
				GameModePlayerForm.Shooter,
				weaponsEnabled: true,
				description: "Frag window"),
		};
	}

	public override void OnPhaseEntered(GameModeManager manager, GameModePhaseState phase)
	{
		switch (phase.PhaseType)
		{
			case GameModePhaseType.Racing:
				manager.SetWeaponsEnabled(false, phase.PhaseType, "classic_racing_phase");
				break;
			case GameModePhaseType.FragWindow:
				manager.SetWeaponsEnabled(true, phase.PhaseType, "classic_frag_phase");
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
