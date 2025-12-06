using Godot;

public interface IGameModeSpawnDelegate
{
	/// <summary>
	/// Allows a game mode to handle spawning a player at a mode-specific location.
	/// </summary>
	/// <param name="manager">The active game mode manager.</param>
	/// <param name="player">The player character to position/reset.</param>
	/// <param name="teamId">Team the player belongs to.</param>
	/// <param name="isLateJoin">True if this is during an active round (late join), false otherwise.</param>
	/// <returns>True if the delegate handled spawning; false to let default logic run.</returns>
	bool TrySpawnPlayer(GameModeManager manager, PlayerCharacter player, int teamId, bool isLateJoin);

	/// <summary>
	/// Allows the mode to handle bulk team respawns (e.g., round start).
	/// </summary>
	/// <param name="manager">The active game mode manager.</param>
	/// <param name="teamSpawns">Map of teamId -> spawn transform (already jittered if desired).</param>
	/// <param name="players">Enumerable of (player, teamId) pairs to respawn.</param>
	/// <returns>True if handled by mode; false to allow fallback.</returns>
	bool TryHandleTeamRespawns(
		GameModeManager manager,
		System.Collections.Generic.Dictionary<int, Transform3D> teamSpawns,
		System.Collections.Generic.IEnumerable<(PlayerCharacter player, int teamId)> players);
}
