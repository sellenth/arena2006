using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class RespawnService : GodotObject
{
	public void RespawnPlayersAtTeamSpawns(
		GameModeManager gameModeManager,
		Node sceneRoot,
		IEnumerable<PeerInfo> peers,
		Godot.Collections.Dictionary teamSpawnNodes)
	{
		if (sceneRoot == null || gameModeManager == null || peers == null || teamSpawnNodes == null)
			return;

		var spawnTransformsByTeam = new Dictionary<int, Transform3D>();
		foreach (var key in teamSpawnNodes.Keys)
		{
			var teamId = (int)key;
			var nodeName = (string)teamSpawnNodes[key];
			var spawnNode = sceneRoot.FindChild(nodeName, true, false) as Node3D;
			if (spawnNode != null)
			{
				spawnTransformsByTeam[teamId] = spawnNode.GlobalTransform;
				GD.Print($"[RespawnService] Found spawn '{nodeName}' for team {teamId}");
			}
			else
			{
				GD.PrintErr($"[RespawnService] Could not find spawn node '{nodeName}' for team {teamId}");
			}
		}

		var rng = new RandomNumberGenerator();
		rng.Randomize();

		var playerList = new List<(PlayerCharacter player, int teamId)>();
		foreach (var info in peers.ToList())
		{
			if (info?.PlayerCharacter == null)
				continue;

			var teamId = gameModeManager.GetTeamForPlayer(info.Id);
			playerList.Add((info.PlayerCharacter, teamId));
		}

		var transformsAreJittered = false;
		if (gameModeManager.ActiveMode is IGameModeSpawnDelegate spawnDelegate)
		{
			var jittered = new Dictionary<int, Transform3D>();
			foreach (var kvp in spawnTransformsByTeam)
			{
				var jitterOffset = new Vector3(
					rng.RandfRange(-2f, 2f),
					0f,
					rng.RandfRange(-2f, 2f));
				var t = kvp.Value;
				t.Origin += jitterOffset;
				jittered[kvp.Key] = t;
			}

			if (spawnDelegate.TryHandleTeamRespawns(gameModeManager, jittered, playerList))
				return;

			spawnTransformsByTeam = jittered;
			transformsAreJittered = true;
		}

		foreach (var entry in playerList)
		{
			var teamId = entry.teamId;
			var player = entry.player;

			if (!spawnTransformsByTeam.TryGetValue(teamId, out var baseTransform))
			{
				GD.PrintErr($"[RespawnService] No spawn transform for player {player.Name} on team {teamId}");
				continue;
			}

			var spawnTransform = baseTransform;
			if (!transformsAreJittered)
			{
				var jitterOffset = new Vector3(
					rng.RandfRange(-2f, 2f),
					0f,
					rng.RandfRange(-2f, 2f));
				spawnTransform.Origin += jitterOffset;
			}

			var handled = false;
			if (gameModeManager.ActiveMode is IGameModeSpawnDelegate spawnDelegatePerPlayer)
			{
				handled = spawnDelegatePerPlayer.TrySpawnPlayer(gameModeManager, player, teamId, isLateJoin: false);
			}

			if (!handled)
			{
				var includeStartingWeapons = gameModeManager.ActiveMode is not SearchAndDestroyMode;
				player.ResetInventory(includeStartingWeapons);
				player.ForceRespawn(spawnTransform);
				GD.Print($"[RespawnService] Respawned player {player.Name} at team {teamId} spawn (fallback)");
			}
		}
	}
}
