using System;
using System.Collections.Generic;

public sealed partial class DeathmatchMode : GameMode
{
	public const string ModeId = "deathmatch";

	private readonly float _warmupSeconds;
	private readonly GameModeScoreRules _scoreRules;

	public DeathmatchMode(float warmupSeconds = 15.0f)
	{
		_warmupSeconds = Math.Max(warmupSeconds, 0.0f);
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
	public override string Description => "On-foot frag-only mode with optional warmup.";
	public override GameModeScoreRules ScoreRules => _scoreRules;
	public override bool LoopPhases => false;

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

		return phases;
	}

	public override void OnPhaseEntered(GameModeManager manager, GameModePhaseState phase)
	{
		switch (phase.PhaseType)
		{
			case GameModePhaseType.Warmup:
				manager.SetWeaponsEnabled(false, phase.PhaseType, "deathmatch_warmup");
				break;
			case GameModePhaseType.FragWindow:
				manager.SetWeaponsEnabled(true, phase.PhaseType, "deathmatch_round");
				break;
		}
	}
}
