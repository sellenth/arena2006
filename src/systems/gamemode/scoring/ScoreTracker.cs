using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ScoreTracker : RefCounted
{
	private readonly MatchState _matchState;
	private readonly TeamManager _teamManager;
	private readonly Dictionary<int, int> _assistTracking = new();
	private const float AssistWindow = 10f;

	public event Action<int, int, int> PlayerKilled;
	public event Action<int, int> TeamScoreChanged;
	public event Action<int, int, int> PlayerScoreChanged;
	public event Action<int> WinConditionMet;

	public ScoreTracker(MatchState matchState, TeamManager teamManager)
	{
		_matchState = matchState;
		_teamManager = teamManager;
	}

	public void AddPlayerKill(int killerId)
	{
		if (killerId <= 0)
			return;

		var stats = _matchState.GetPlayerStats(killerId);
		stats.Kills++;
		stats.Score += GetKillPointValue();

		PlayerScoreChanged?.Invoke(killerId, stats.Score, stats.Kills);
	}

	public void AddPlayerDeath(int victimId)
	{
		if (victimId <= 0)
			return;

		var stats = _matchState.GetPlayerStats(victimId);
		stats.Deaths++;
	}

	public void AddPlayerAssist(int assisterId)
	{
		if (assisterId <= 0)
			return;

		var stats = _matchState.GetPlayerStats(assisterId);
		stats.Assists++;
		stats.Score += GetAssistPointValue();
	}

	public void AddTeamScore(int teamId, int amount)
	{
		if (teamId < 0)
			return;

		var oldScore = _matchState.GetTeamScore(teamId);
		_matchState.AddTeamScore(teamId, amount);
		var newScore = _matchState.GetTeamScore(teamId);

		if (oldScore != newScore)
		{
			TeamScoreChanged?.Invoke(teamId, newScore);
		}
	}

	public void SetTeamScore(int teamId, int score)
	{
		if (teamId < 0)
			return;

		var oldScore = _matchState.GetTeamScore(teamId);
		_matchState.SetTeamScore(teamId, score);

		if (oldScore != score)
		{
			TeamScoreChanged?.Invoke(teamId, score);
		}
	}

	public void AddPlayerScore(int peerId, int amount)
	{
		if (peerId <= 0)
			return;

		var stats = _matchState.GetPlayerStats(peerId);
		stats.Score += amount;

		PlayerScoreChanged?.Invoke(peerId, stats.Score, stats.Kills);
	}

	public void AddObjectivePoints(int peerId, int amount)
	{
		if (peerId <= 0)
			return;

		var stats = _matchState.GetPlayerStats(peerId);
		stats.ObjectivePoints += amount;
		stats.Score += amount;

		PlayerScoreChanged?.Invoke(peerId, stats.Score, stats.Kills);
	}

	public void RecordKill(int killerId, int victimId, int assisterId = 0)
	{
		AddPlayerKill(killerId);
		AddPlayerDeath(victimId);

		if (assisterId > 0 && assisterId != killerId)
		{
			AddPlayerAssist(assisterId);
		}

		PlayerKilled?.Invoke(killerId, victimId, assisterId);
	}

	public void RecordTeamKill(int killerId, int victimId)
	{
		AddPlayerDeath(victimId);

		var stats = _matchState.GetPlayerStats(killerId);
		stats.Score -= GetTeamKillPenalty();

		PlayerKilled?.Invoke(killerId, victimId, 0);
	}

	public void RecordSuicide(int playerId)
	{
		AddPlayerDeath(playerId);

		var stats = _matchState.GetPlayerStats(playerId);
		stats.Score -= GetSuicidePenalty();

		PlayerKilled?.Invoke(0, playerId, 0);
	}

	public bool CheckWinCondition(IWinCondition condition)
	{
		if (condition == null)
			return false;

		if (condition.CheckWinCondition(_matchState, _teamManager))
		{
			var winner = condition.GetWinningTeam(_matchState, _teamManager);
			WinConditionMet?.Invoke(winner);
			return true;
		}

		return false;
	}

	public int GetTeamScore(int teamId)
	{
		return _matchState.GetTeamScore(teamId);
	}

	public int GetPlayerScore(int peerId)
	{
		return _matchState.GetPlayerStats(peerId).Score;
	}

	public int GetPlayerKills(int peerId)
	{
		return _matchState.GetPlayerStats(peerId).Kills;
	}

	public int GetPlayerDeaths(int peerId)
	{
		return _matchState.GetPlayerStats(peerId).Deaths;
	}

	public IEnumerable<PlayerMatchStats> GetLeaderboard()
	{
		return _matchState.GetAllPlayerStats()
			.OrderByDescending(s => s.Score)
			.ThenByDescending(s => s.Kills)
			.ThenBy(s => s.Deaths);
	}

	public IEnumerable<(int TeamId, int Score)> GetTeamLeaderboard()
	{
		return _teamManager.GetAllTeamIds()
			.Select(id => (id, _matchState.GetTeamScore(id)))
			.OrderByDescending(t => t.Item2);
	}

	public void Reset()
	{
		_matchState.ResetScores();
		_assistTracking.Clear();
	}

	protected virtual int GetKillPointValue() => 100;
	protected virtual int GetAssistPointValue() => 50;
	protected virtual int GetTeamKillPenalty() => 100;
	protected virtual int GetSuicidePenalty() => 50;
}

