using Godot;
using System;
using System.Collections.Generic;

public partial class MatchState : RefCounted
{
	public const int MaxTeams = 8;

	private MatchPhase _phase = MatchPhase.None;
	private float _phaseTimeRemaining;
	private float _phaseStartTime;
	private int _roundNumber;
	private int _winningTeam = -1;
	private readonly int[] _teamScores = new int[MaxTeams];
	private readonly Dictionary<int, PlayerMatchStats> _playerStats = new();
	private string _currentModeId = string.Empty;
	private int _serverTick;

	public event Action<MatchPhase, MatchPhase> PhaseChanged;
	public event Action<int, int, int> TeamScoreChanged;
	public event Action<int> RoundChanged;
	public event Action<int> MatchEnded;

	public MatchPhase Phase => _phase;
	public float PhaseTimeRemaining => _phaseTimeRemaining;
	public float PhaseElapsedTime => (float)Time.GetTicksMsec() / 1000f - _phaseStartTime;
	public int RoundNumber => _roundNumber;
	public int WinningTeam => _winningTeam;
	public string CurrentModeId => _currentModeId;
	public int ServerTick => _serverTick;
	public bool IsLive => _phase == MatchPhase.Live;
	public bool IsWarmup => _phase == MatchPhase.Warmup;
	public bool IsOver => _phase == MatchPhase.PostMatch;

	public void SetPhase(MatchPhase phase, float duration = 0f)
	{
		var oldPhase = _phase;
		_phase = phase;
		_phaseTimeRemaining = duration;
		_phaseStartTime = (float)Time.GetTicksMsec() / 1000f;

		if (oldPhase != phase)
		{
			PhaseChanged?.Invoke(oldPhase, phase);
		}
	}

	public void SetPhaseTimeRemaining(float time)
	{
		_phaseTimeRemaining = Mathf.Max(0f, time);
	}

	public void TickPhaseTime(float delta)
	{
		if (_phaseTimeRemaining > 0f)
		{
			_phaseTimeRemaining = Mathf.Max(0f, _phaseTimeRemaining - delta);
		}
	}

	public void SetRound(int round)
	{
		if (_roundNumber != round)
		{
			_roundNumber = round;
			RoundChanged?.Invoke(round);
		}
	}

	public void SetModeId(string modeId)
	{
		_currentModeId = modeId ?? string.Empty;
	}

	public void SetServerTick(int tick)
	{
		_serverTick = tick;
	}

	public int GetTeamScore(int teamId)
	{
		if (teamId < 0 || teamId >= MaxTeams)
			return 0;
		return _teamScores[teamId];
	}

	public void SetTeamScore(int teamId, int score)
	{
		if (teamId < 0 || teamId >= MaxTeams)
			return;

		var oldScore = _teamScores[teamId];
		_teamScores[teamId] = score;

		if (oldScore != score)
		{
			TeamScoreChanged?.Invoke(teamId, oldScore, score);
		}
	}

	public void AddTeamScore(int teamId, int amount)
	{
		SetTeamScore(teamId, GetTeamScore(teamId) + amount);
	}

	public PlayerMatchStats GetPlayerStats(int peerId)
	{
		if (!_playerStats.TryGetValue(peerId, out var stats))
		{
			stats = new PlayerMatchStats(peerId);
			_playerStats[peerId] = stats;
		}
		return stats;
	}

	public void SetPlayerStats(int peerId, PlayerMatchStats stats)
	{
		_playerStats[peerId] = stats;
	}

	public IEnumerable<PlayerMatchStats> GetAllPlayerStats()
	{
		return _playerStats.Values;
	}

	public void RemovePlayer(int peerId)
	{
		_playerStats.Remove(peerId);
	}

	public void EndMatch(int winningTeam)
	{
		_winningTeam = winningTeam;
		SetPhase(MatchPhase.PostMatch);
		MatchEnded?.Invoke(winningTeam);
	}

	public void Reset()
	{
		_phase = MatchPhase.None;
		_phaseTimeRemaining = 0f;
		_roundNumber = 0;
		_winningTeam = -1;
		Array.Clear(_teamScores, 0, _teamScores.Length);
		_playerStats.Clear();
	}

	public void ResetScores()
	{
		Array.Clear(_teamScores, 0, _teamScores.Length);
		foreach (var stats in _playerStats.Values)
		{
			stats.Reset();
		}
	}

	public int[] GetAllTeamScores()
	{
		return (int[])_teamScores.Clone();
	}
}

public class PlayerMatchStats
{
	public int PeerId { get; }
	public int Kills { get; set; }
	public int Deaths { get; set; }
	public int Assists { get; set; }
	public int Score { get; set; }
	public int ObjectivePoints { get; set; }

	public PlayerMatchStats(int peerId)
	{
		PeerId = peerId;
	}

	public void Reset()
	{
		Kills = 0;
		Deaths = 0;
		Assists = 0;
		Score = 0;
		ObjectivePoints = 0;
	}

	public float GetKillDeathRatio()
	{
		if (Deaths == 0)
			return Kills;
		return (float)Kills / Deaths;
	}
}

