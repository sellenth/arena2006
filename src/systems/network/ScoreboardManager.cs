using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class ScoreboardManager : GodotObject
{
	private ScoreboardUI _scoreboardUi;
	private Godot.Collections.Array _latestScoreboard = new Godot.Collections.Array();

	public void Initialize(PackedScene uiScene, Node uiParent)
	{
		if (uiScene == null || _scoreboardUi != null)
			return;

		var instance = uiScene.Instantiate<ScoreboardUI>();
		if (instance == null)
			return;

		uiParent.AddChild(instance);
		_scoreboardUi = instance;
		_scoreboardUi.ApplyScoreboard(GetScoreboardSnapshot());
	}

	public void NotifyKill(PeerInfo victim, PeerInfo killer, PlayerCharacter victimPlayer, Transform3D respawnTransform)
	{
		if (victim != null)
		{
			victim.Deaths++;
		}

		if (killer != null && killer.Id != victim?.Id)
		{
			killer.Kills++;
		}

		var allowRespawns = GameModeManager.Instance?.ActiveScoreRules.AllowRespawns ?? true;
		if (victimPlayer != null && allowRespawns)
		{
			victimPlayer.ForceRespawn(respawnTransform);
		}
	}

	public void UpdateScoreboard(IEnumerable<PeerInfo> peers, System.Action<byte[]> broadcastAction)
	{
		var entries = BuildScoreboardEntries(peers);
		BroadcastScoreboard(entries, broadcastAction);
	}

	private List<NetworkSerializer.ScoreboardEntry> BuildScoreboardEntries(IEnumerable<PeerInfo> peers)
	{
		return peers
			.Where(p => p != null)
			.Select(p => new NetworkSerializer.ScoreboardEntry
			{
				Id = p.Id,
				Kills = p.Kills,
				Deaths = p.Deaths
			})
			.OrderByDescending(p => p.Kills)
			.ThenBy(p => p.Deaths)
			.ThenBy(p => p.Id)
			.ToList();
	}

	private Godot.Collections.Array ToGodotScoreboard(IEnumerable<NetworkSerializer.ScoreboardEntry> entries)
	{
		var array = new Godot.Collections.Array();
		if (entries == null)
			return array;

		foreach (var entry in entries)
		{
			var row = new Godot.Collections.Dictionary
			{
				{ "id", entry.Id },
				{ "kills", entry.Kills },
				{ "deaths", entry.Deaths }
			};
			array.Add(row);
		}

		return array;
	}

	private Godot.Collections.Array CloneScoreboard(Godot.Collections.Array source)
	{
		var clone = new Godot.Collections.Array();
		if (source == null)
			return clone;

		foreach (var item in source)
			clone.Add(item);

		return clone;
	}

	private void BroadcastScoreboard(List<NetworkSerializer.ScoreboardEntry> entries, System.Action<byte[]> broadcastAction)
	{
		var scoreboardArray = ToGodotScoreboard(entries);
		SetScoreboardSnapshot(scoreboardArray);

		if (broadcastAction != null)
		{
			var packet = NetworkSerializer.SerializeScoreboard(entries);
			broadcastAction(packet);
		}
	}

	private void SetScoreboardSnapshot(Godot.Collections.Array scoreboard)
	{
		_latestScoreboard = CloneScoreboard(scoreboard);
		if (_scoreboardUi != null)
		{
			_scoreboardUi.ApplyScoreboard(_latestScoreboard);
		}
	}

	public Godot.Collections.Array GetScoreboardSnapshot()
	{
		return CloneScoreboard(_latestScoreboard);
	}

	public void UpdateScoreboardFromPacket(Godot.Collections.Array scoreboard)
	{
		SetScoreboardSnapshot(scoreboard);
	}
}

