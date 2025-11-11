using Godot;
using Godot.Collections;

public partial class RemotePlayerManager : Node3D
{
	private Dictionary<int, PlayerCharacter> _remotePlayers = new Dictionary<int, PlayerCharacter>();
	private PackedScene _playerScene;
	private NetworkController _networkController;

	public override void _Ready()
	{
		_playerScene = GD.Load<PackedScene>("res://src/entities/player/player_character.tscn");
		_networkController = GetNode<NetworkController>("/root/NetworkController");

		if (_networkController != null)
		{
			_networkController.PlayerStateUpdated += OnPlayerStateUpdated;
			_networkController.PlayerDisconnected += OnPlayerDisconnected;
		}
		else
		{
			GD.PushError("RemotePlayerManager: NetworkController not found!");
		}
	}

	public override void _ExitTree()
	{
		if (_networkController != null)
		{
			_networkController.PlayerStateUpdated -= OnPlayerStateUpdated;
			_networkController.PlayerDisconnected -= OnPlayerDisconnected;
		}
	}

	private void OnPlayerStateUpdated(int playerId, PlayerStateSnapshot snapshot)
	{
		var playerSnapshot = snapshot?.PlayerSnapshot;
		var hasPlayerData = playerSnapshot != null;

		if (!_remotePlayers.TryGetValue(playerId, out var player) || !GodotObject.IsInstanceValid(player))
		{
			if (!hasPlayerData)
				return;
			player = SpawnRemotePlayer(playerId, playerSnapshot.Transform);
		}

		if (!hasPlayerData)
		{
			player.SetWorldActive(false);
			return;
		}

		player.SetWorldActive(snapshot.Mode == PlayerMode.Foot);
		player.ConfigureAuthority(false);
		player.SetCameraActive(false);
		player.QueueSnapshot(playerSnapshot);
	}

	private void OnPlayerDisconnected(int playerId)
	{
		if (_remotePlayers.TryGetValue(playerId, out var player) && GodotObject.IsInstanceValid(player))
		{
			player.QueueFree();
		}
		_remotePlayers.Remove(playerId);
	}

	private PlayerCharacter SpawnRemotePlayer(int playerId, Transform3D transform)
	{
		var player = _playerScene.Instantiate<PlayerCharacter>();
		player.AutoRegisterWithNetwork = false;
		player.Name = $"RemotePlayer_{playerId}";
		AddChild(player);
		player.ConfigureAuthority(false);
		player.SetCameraActive(false);
		player.SetWorldActive(false);
		player.TeleportTo(transform);
		_remotePlayers[playerId] = player;
		return player;
	}
}
