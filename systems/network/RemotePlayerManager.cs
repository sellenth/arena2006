using Godot;
using Godot.Collections;

public partial class RemotePlayerManager : Node3D
{
	private Dictionary<int, RaycastCar> _remotePlayers = new Dictionary<int, RaycastCar>();
	private PackedScene _playerCarScene;
	private NetworkController _networkController;

	public override void _Ready()
	{
		_playerCarScene = GD.Load<PackedScene>("res://entities/vehicle/car/player_car.tscn");
		_networkController = GetNode<NetworkController>("/root/NetworkController");
		
		if (_networkController != null)
		{
			_networkController.PlayerStateUpdated += OnPlayerStateUpdated;
			_networkController.PlayerDisconnected += OnPlayerDisconnected;
			GD.Print("RemotePlayerManager: Connected to NetworkController");
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

	private void OnPlayerStateUpdated(int playerId, CarSnapshot snapshot)
	{
		if (!_remotePlayers.ContainsKey(playerId))
		{
			SpawnRemotePlayer(playerId, snapshot);
		}
		else
		{
			UpdateRemotePlayer(playerId, snapshot);
		}
	}

	private void OnPlayerDisconnected(int playerId)
	{
		if (_remotePlayers.ContainsKey(playerId))
		{
			var car = _remotePlayers[playerId];
			if (GodotObject.IsInstanceValid(car))
			{
				car.QueueFree();
			}
			_remotePlayers.Remove(playerId);
			GD.Print($"RemotePlayerManager: Removed remote player {playerId}");
		}
	}

	private void SpawnRemotePlayer(int playerId, CarSnapshot snapshot)
	{
		var car = _playerCarScene.Instantiate<RaycastCar>();
		car.Name = $"RemotePlayer_{playerId}";
		
		CleanupCameraNodes(car);
		
		AddChild(car);
		
		car.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
		car.Freeze = false;
		car.GlobalTransform = snapshot.Transform;
		
		_remotePlayers[playerId] = car;
		GD.Print($"RemotePlayerManager: Spawned remote player {playerId} at {snapshot.Transform.Origin}");
	}

	private void UpdateRemotePlayer(int playerId, CarSnapshot snapshot)
	{
		if (_remotePlayers.TryGetValue(playerId, out var car) && GodotObject.IsInstanceValid(car))
		{
			car.GlobalTransform = car.GlobalTransform.InterpolateWith(snapshot.Transform, 0.3f);
			car.LinearVelocity = car.LinearVelocity.Lerp(snapshot.LinearVelocity, 0.3f);
			car.AngularVelocity = car.AngularVelocity.Lerp(snapshot.AngularVelocity, 0.3f);
		}
	}

	private void CleanupCameraNodes(Node car)
	{
		var childrenToRemove = new System.Collections.Generic.List<Node>();
		
		foreach (var child in car.GetChildren())
		{
			var nodeName = child.Name.ToString();
			
			if (child is Camera3D || child is RemoteTransform3D)
			{
				childrenToRemove.Add(child);
			}
			else if (nodeName.Contains("CameraPivot") || nodeName.Contains("Camera"))
			{
				CleanupCameraNodes(child);
				childrenToRemove.Add(child);
			}
		}
		
		foreach (var child in childrenToRemove)
		{
			child.QueueFree();
		}
	}
}

