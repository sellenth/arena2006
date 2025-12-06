using System.Linq;

public interface IWinCondition
{
	bool CheckWinCondition(MatchState state, TeamManager teamManager);
	int GetWinningTeam(MatchState state, TeamManager teamManager);
	string GetDescription();
}

public sealed class ScoreLimitCondition : IWinCondition
{
	public int ScoreLimit { get; }

	public ScoreLimitCondition(int scoreLimit)
	{
		ScoreLimit = scoreLimit;
	}

	public bool CheckWinCondition(MatchState state, TeamManager teamManager)
	{
		if (teamManager.IsFreeForAll)
		{
			return state.GetAllPlayerStats().Any(s => s.Score >= ScoreLimit);
		}

		foreach (var teamId in teamManager.GetAllTeamIds())
		{
			if (state.GetTeamScore(teamId) >= ScoreLimit)
				return true;
		}
		return false;
	}

	public int GetWinningTeam(MatchState state, TeamManager teamManager)
	{
		if (teamManager.IsFreeForAll)
		{
			var winner = state.GetAllPlayerStats()
				.Where(s => s.Score >= ScoreLimit)
				.OrderByDescending(s => s.Score)
				.FirstOrDefault();
			return winner?.PeerId ?? -1;
		}

		int bestTeam = -1;
		int bestScore = 0;

		foreach (var teamId in teamManager.GetAllTeamIds())
		{
			var score = state.GetTeamScore(teamId);
			if (score >= ScoreLimit && score > bestScore)
			{
				bestScore = score;
				bestTeam = teamId;
			}
		}

		return bestTeam;
	}

	public string GetDescription() => $"First to {ScoreLimit} points";
}

public sealed class KillLimitCondition : IWinCondition
{
	public int KillLimit { get; }

	public KillLimitCondition(int killLimit)
	{
		KillLimit = killLimit;
	}

	public bool CheckWinCondition(MatchState state, TeamManager teamManager)
	{
		if (teamManager.IsFreeForAll)
		{
			return state.GetAllPlayerStats().Any(s => s.Kills >= KillLimit);
		}

		foreach (var teamId in teamManager.GetAllTeamIds())
		{
			if (state.GetTeamScore(teamId) >= KillLimit)
				return true;
		}
		return false;
	}

	public int GetWinningTeam(MatchState state, TeamManager teamManager)
	{
		if (teamManager.IsFreeForAll)
		{
			var winner = state.GetAllPlayerStats()
				.Where(s => s.Kills >= KillLimit)
				.OrderByDescending(s => s.Kills)
				.ThenByDescending(s => s.Score)
				.FirstOrDefault();
			return winner?.PeerId ?? -1;
		}

		int bestTeam = -1;
		int bestScore = 0;

		foreach (var teamId in teamManager.GetAllTeamIds())
		{
			var score = state.GetTeamScore(teamId);
			if (score >= KillLimit && score > bestScore)
			{
				bestScore = score;
				bestTeam = teamId;
			}
		}

		return bestTeam;
	}

	public string GetDescription() => $"First to {KillLimit} kills";
}

public sealed class TimeLimitCondition : IWinCondition
{
	public float TimeLimit { get; }

	public TimeLimitCondition(float timeLimit)
	{
		TimeLimit = timeLimit;
	}

	public bool CheckWinCondition(MatchState state, TeamManager teamManager)
	{
		return state.PhaseTimeRemaining <= 0f && state.IsLive;
	}

	public int GetWinningTeam(MatchState state, TeamManager teamManager)
	{
		if (teamManager.IsFreeForAll)
		{
			var winner = state.GetAllPlayerStats()
				.OrderByDescending(s => s.Score)
				.FirstOrDefault();
			return winner?.PeerId ?? -1;
		}

		int bestTeam = -1;
		int bestScore = -1;

		foreach (var teamId in teamManager.GetAllTeamIds())
		{
			var score = state.GetTeamScore(teamId);
			if (score > bestScore)
			{
				bestScore = score;
				bestTeam = teamId;
			}
		}

		return bestTeam;
	}

	public string GetDescription() => $"Highest score after {TimeLimit / 60f:F0} minutes";
}

public sealed class EliminationCondition : IWinCondition
{
	public bool CheckWinCondition(MatchState state, TeamManager teamManager)
	{
		if (teamManager.IsFreeForAll)
		{
			var alive = state.GetAllPlayerStats().Count(s => !IsEliminated(s));
			return alive <= 1;
		}

		int teamsWithPlayers = 0;
		foreach (var teamId in teamManager.GetAllTeamIds())
		{
			var players = teamManager.GetPlayersOnTeam(teamId);
			if (players.Any(p => !IsEliminated(state.GetPlayerStats(p))))
			{
				teamsWithPlayers++;
			}
		}

		return teamsWithPlayers <= 1;
	}

	public int GetWinningTeam(MatchState state, TeamManager teamManager)
	{
		if (teamManager.IsFreeForAll)
		{
			var survivor = state.GetAllPlayerStats().FirstOrDefault(s => !IsEliminated(s));
			return survivor?.PeerId ?? -1;
		}

		foreach (var teamId in teamManager.GetAllTeamIds())
		{
			var players = teamManager.GetPlayersOnTeam(teamId);
			if (players.Any(p => !IsEliminated(state.GetPlayerStats(p))))
			{
				return teamId;
			}
		}

		return -1;
	}

	private static bool IsEliminated(PlayerMatchStats stats)
	{
		return stats.Deaths > 0;
	}

	public string GetDescription() => "Last team standing";
}

public sealed class RoundWinsCondition : IWinCondition
{
	public int RoundsToWin { get; }

	public RoundWinsCondition(int roundsToWin)
	{
		RoundsToWin = roundsToWin;
	}

	public bool CheckWinCondition(MatchState state, TeamManager teamManager)
	{
		foreach (var teamId in teamManager.GetAllTeamIds())
		{
			if (state.GetTeamScore(teamId) >= RoundsToWin)
				return true;
		}
		return false;
	}

	public int GetWinningTeam(MatchState state, TeamManager teamManager)
	{
		foreach (var teamId in teamManager.GetAllTeamIds())
		{
			if (state.GetTeamScore(teamId) >= RoundsToWin)
				return teamId;
		}
		return -1;
	}

	public string GetDescription() => $"First to {RoundsToWin} rounds";
}

public sealed class CompositeCondition : IWinCondition
{
	private readonly IWinCondition[] _conditions;

	public CompositeCondition(params IWinCondition[] conditions)
	{
		_conditions = conditions ?? System.Array.Empty<IWinCondition>();
	}

	public bool CheckWinCondition(MatchState state, TeamManager teamManager)
	{
		foreach (var condition in _conditions)
		{
			if (condition.CheckWinCondition(state, teamManager))
				return true;
		}
		return false;
	}

	public int GetWinningTeam(MatchState state, TeamManager teamManager)
	{
		foreach (var condition in _conditions)
		{
			if (condition.CheckWinCondition(state, teamManager))
				return condition.GetWinningTeam(state, teamManager);
		}
		return -1;
	}

	public string GetDescription()
	{
		return string.Join(" or ", _conditions.Select(c => c.GetDescription()));
	}
}

