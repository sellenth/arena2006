using System;
using System.Collections.Generic;
using Godot;

public sealed partial class RaceOnlyMode : GameMode
{
	public const string ModeId = "race_only";

	private readonly float _countdownSeconds;
	private readonly int _targetLaps;
	private readonly GameModeScoreRules _scoreRules;

	public RaceOnlyMode(float countdownSeconds = 5.0f, int targetLaps = 3)
	{
		_countdownSeconds = Math.Max(countdownSeconds, 0.0f);
		_targetLaps = Math.Max(targetLaps, 1);
		_scoreRules = new GameModeScoreRules(
			trackLaps: true,
			trackEliminations: false,
			pointsPerLap: 5,
			pointsPerElimination: 0,
			allowRespawns: true,
			suddenDeathOnTie: false
		);
	}

	public override string Id => ModeId;
	public override string DisplayName => "Race Only";
	public override string Description => $"Pure racing - {_targetLaps} laps to win.";
	public override GameModeScoreRules ScoreRules => _scoreRules;
	public override bool LoopPhases => false;

	public override TeamStructure TeamStructure => TeamStructure.FreeForAll;

	protected override IReadOnlyList<GameModePhaseDefinition> BuildPhases()
	{
		var phases = new List<GameModePhaseDefinition>();
		if (_countdownSeconds > 0.01f)
		{
			phases.Add(GameModePhaseDefinition.Timed(
				GameModePhaseType.Countdown,
				_countdownSeconds,
				GameModePlayerForm.Car,
				weaponsEnabled: false,
				description: "Grid countdown"));
		}

		phases.Add(GameModePhaseDefinition.Indefinite(
			GameModePhaseType.Racing,
			GameModePlayerForm.Car,
			weaponsEnabled: false,
			description: "Race"));

		return phases;
	}

	public override void OnPhaseEntered(GameModeManager manager, GameModePhaseState phase)
	{
		switch (phase.PhaseType)
		{
			case GameModePhaseType.Countdown:
				manager.SetCarControlEnabled(false, phase.PhaseType, "race_only_countdown");
				break;
			case GameModePhaseType.Racing:
				manager.SetCarControlEnabled(true, phase.PhaseType, "race_only_racing");
				break;
		}
	}

	public override void OnObjectiveEvent(MatchContext ctx, ObjectiveEventData evt)
	{
		if (evt.Type == ObjectiveEventType.LapCompleted)
		{
			ctx.ScoreTracker?.AddPlayerScore(evt.PlayerId, ScoreRules.PointsPerLap);
		}
	}
}
