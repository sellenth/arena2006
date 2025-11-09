using Godot;
using Godot.Collections;

public partial class RemoteFootPlayerManager : Node3D
{
	private Dictionary<int, FootPlayerController> _remoteFootPlayers = new Dictionary<int, FootPlayerController>();
	private PackedScene _footPlayerScene;
	private NetworkController _networkController;

	public override void _Ready()
	{
		_footPlayerScene = GD.Load<PackedScene>("res://src/entities/player/foot/foot_player.tscn");
		_networkController = GetNode<NetworkController>("/root/NetworkController");

		if (_networkController != null)
		{
			_networkController.PlayerStateUpdated += OnPlayerStateUpdated;
			_networkController.PlayerDisconnected += OnPlayerDisconnected;
		}
		else
		{
			GD.PushError("RemoteFootPlayerManager: NetworkController not found!");
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
		var footSnapshot = snapshot?.FootSnapshot;
		var hasFootData = footSnapshot != null;

		if (!_remoteFootPlayers.TryGetValue(playerId, out var foot) || !GodotObject.IsInstanceValid(foot))
		{
			if (!hasFootData)
				return;
			foot = SpawnRemoteFoot(playerId, footSnapshot.Transform);
		}

		if (!hasFootData)
		{
			foot.SetWorldActive(false);
			return;
		}

		foot.SetWorldActive(snapshot.Mode == PlayerMode.Foot);
		foot.ConfigureAuthority(false);
		foot.SetCameraActive(false);
		foot.QueueSnapshot(footSnapshot);
	}

	private void OnPlayerDisconnected(int playerId)
	{
		if (_remoteFootPlayers.TryGetValue(playerId, out var foot) && GodotObject.IsInstanceValid(foot))
		{
			foot.QueueFree();
		}
		_remoteFootPlayers.Remove(playerId);
	}

	private FootPlayerController SpawnRemoteFoot(int playerId, Transform3D transform)
	{
		var foot = _footPlayerScene.Instantiate<FootPlayerController>();
		foot.AutoRegisterWithNetwork = false;
		foot.Name = $"RemoteFoot_{playerId}";
		AddChild(foot);
		foot.ConfigureAuthority(false);
		foot.SetCameraActive(false);
		foot.SetWorldActive(false);
		foot.TeleportTo(transform);
		_remoteFootPlayers[playerId] = foot;
		return foot;
	}
}
