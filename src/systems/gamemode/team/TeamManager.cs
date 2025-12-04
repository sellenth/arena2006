using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class TeamManager : RefCounted
{
	public const int NoTeam = -1;
	public const int Spectator = -2;

	private readonly Dictionary<int, int> _playerTeams = new();
	private readonly Dictionary<int, TeamDefinition> _teamDefinitions = new();
	private TeamConfig _config = TeamConfig.FreeForAll;

	public event Action<int, int, int> PlayerTeamChanged;
	public event Action TeamsRebalanced;

	public TeamConfig Config => _config;
	public bool IsFreeForAll => _config.IsFreeForAll;
	public bool IsTeamBased => _config.IsTeamBased;

	public void Configure(TeamConfig config)
	{
		_config = config;
		_teamDefinitions.Clear();

		if (config.IsTeamBased)
		{
			for (int i = 0; i < config.TeamCount; i++)
			{
				_teamDefinitions[i] = TeamDefinition.GetDefault(i);
			}
		}
	}

	public void SetTeamDefinition(int teamId, TeamDefinition definition)
	{
		if (definition == null || teamId < 0)
			return;

		_teamDefinitions[teamId] = definition;
	}

	public TeamDefinition GetTeamDefinition(int teamId)
	{
		if (_teamDefinitions.TryGetValue(teamId, out var def))
			return def;
		return TeamDefinition.None;
	}

	public int GetTeamForPlayer(int peerId)
	{
		if (_config.IsFreeForAll)
			return peerId;

		return _playerTeams.TryGetValue(peerId, out var teamId) ? teamId : NoTeam;
	}

	public bool AssignPlayerToTeam(int peerId, int teamId)
	{
		if (_config.IsFreeForAll)
			return false;

		if (teamId != NoTeam && teamId != Spectator && !_teamDefinitions.ContainsKey(teamId))
			return false;

		var oldTeam = GetTeamForPlayer(peerId);
		if (oldTeam == teamId)
			return true;

		_playerTeams[peerId] = teamId;
		PlayerTeamChanged?.Invoke(peerId, oldTeam, teamId);
		return true;
	}

	public int AutoAssignTeam(int peerId)
	{
		if (_config.IsFreeForAll)
			return peerId;

		var teamCounts = GetTeamPlayerCounts();
		int bestTeam = 0;
		int minCount = int.MaxValue;

		foreach (var kvp in teamCounts)
		{
			if (kvp.Value < minCount)
			{
				minCount = kvp.Value;
				bestTeam = kvp.Key;
			}
		}

		AssignPlayerToTeam(peerId, bestTeam);
		return bestTeam;
	}

	public void RemovePlayer(int peerId)
	{
		if (_playerTeams.TryGetValue(peerId, out var oldTeam))
		{
			_playerTeams.Remove(peerId);
			PlayerTeamChanged?.Invoke(peerId, oldTeam, NoTeam);
		}
	}

	public IEnumerable<int> GetPlayersOnTeam(int teamId)
	{
		if (_config.IsFreeForAll)
			return Enumerable.Empty<int>();

		return _playerTeams
			.Where(kvp => kvp.Value == teamId)
			.Select(kvp => kvp.Key);
	}

	public Dictionary<int, int> GetTeamPlayerCounts()
	{
		var counts = new Dictionary<int, int>();

		foreach (var teamId in _teamDefinitions.Keys)
		{
			counts[teamId] = 0;
		}

		foreach (var kvp in _playerTeams)
		{
			if (counts.ContainsKey(kvp.Value))
				counts[kvp.Value]++;
		}

		return counts;
	}

	public int GetTeamCount()
	{
		return _teamDefinitions.Count;
	}

	public IEnumerable<int> GetAllTeamIds()
	{
		return _teamDefinitions.Keys;
	}

	public bool ArePlayersOnSameTeam(int peerId1, int peerId2)
	{
		if (_config.IsFreeForAll)
			return peerId1 == peerId2;

		var team1 = GetTeamForPlayer(peerId1);
		var team2 = GetTeamForPlayer(peerId2);

		return team1 != NoTeam && team1 == team2;
	}

	public bool ArePlayersEnemies(int peerId1, int peerId2)
	{
		if (peerId1 == peerId2)
			return false;

		if (_config.IsFreeForAll)
			return true;

		var team1 = GetTeamForPlayer(peerId1);
		var team2 = GetTeamForPlayer(peerId2);

		if (team1 == NoTeam || team2 == NoTeam)
			return false;

		return team1 != team2;
	}

	public void TryRebalanceTeams()
	{
		if (!_config.AutoBalance || _config.IsFreeForAll)
			return;

		var counts = GetTeamPlayerCounts();
		if (counts.Count < 2)
			return;

		var sorted = counts.OrderByDescending(kvp => kvp.Value).ToList();
		var maxDiff = sorted.First().Value - sorted.Last().Value;

		if (maxDiff <= 1)
			return;

		var largestTeam = sorted.First().Key;
		var smallestTeam = sorted.Last().Key;

		var playersOnLargest = GetPlayersOnTeam(largestTeam).ToList();
		if (playersOnLargest.Count == 0)
			return;

		var playerToMove = playersOnLargest.Last();
		AssignPlayerToTeam(playerToMove, smallestTeam);
		TeamsRebalanced?.Invoke();
	}

	public void Reset()
	{
		_playerTeams.Clear();
	}

	public IReadOnlyDictionary<int, int> GetAllPlayerTeams()
	{
		return _playerTeams;
	}
}

