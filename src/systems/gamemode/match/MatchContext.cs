using Godot;

public class MatchContext
{
	public MatchState State { get; }
	public TeamManager TeamManager { get; }
	public ScoreTracker ScoreTracker { get; }
	public GameModeManager ModeManager { get; }

	public MatchContext(
		MatchState state,
		TeamManager teamManager,
		ScoreTracker scoreTracker,
		GameModeManager modeManager)
	{
		State = state;
		TeamManager = teamManager;
		ScoreTracker = scoreTracker;
		ModeManager = modeManager;
	}

	public int GetTeamForPlayer(int peerId)
	{
		return TeamManager?.GetTeamForPlayer(peerId) ?? TeamManager.NoTeam;
	}

	public bool AreEnemies(int peerId1, int peerId2)
	{
		return TeamManager?.ArePlayersEnemies(peerId1, peerId2) ?? (peerId1 != peerId2);
	}

	public void AddKill(int killerId, int victimId)
	{
		ScoreTracker?.AddPlayerKill(killerId);
		ScoreTracker?.AddPlayerDeath(victimId);

		if (TeamManager != null && !TeamManager.IsFreeForAll)
		{
			var killerTeam = TeamManager.GetTeamForPlayer(killerId);
			if (killerTeam != TeamManager.NoTeam)
			{
				ScoreTracker?.AddTeamScore(killerTeam, 1);
			}
		}
	}

	public void AdvancePhase(string reason = "context_advance")
	{
		ModeManager?.TryAdvancePhase(reason);
	}
}

