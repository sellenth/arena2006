using Godot;
using System;
using System.Collections.Generic;

public partial class WorldBoundsManager : Node
{
	public static WorldBoundsManager Instance { get; private set; }

	[Export] public float KillPlaneY { get; set; } = -200.0f;
	[Export] public bool EnableBoundsChecking { get; set; } = true;

	public event Action<PlayerCharacter> PlayerOutOfBounds;
	public event Action<RaycastCar> VehicleOutOfBounds;

	private readonly List<PlayerCharacter> _trackedPlayers = new List<PlayerCharacter>();
	private readonly List<RaycastCar> _trackedVehicles = new List<RaycastCar>();

	public override void _EnterTree()
	{
		if (Instance != null)
		{
			GD.PushWarning("WorldBoundsManager: Multiple instances detected, destroying duplicate");
			QueueFree();
			return;
		}
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!EnableBoundsChecking)
			return;

		CheckPlayerBounds();
		CheckVehicleBounds();
	}

	public void RegisterPlayer(PlayerCharacter player)
	{
		if (player != null && !_trackedPlayers.Contains(player))
		{
			_trackedPlayers.Add(player);
			player.TreeExiting += () => UnregisterPlayer(player);
		}
	}

	public void UnregisterPlayer(PlayerCharacter player)
	{
		_trackedPlayers.Remove(player);
	}

	public void RegisterVehicle(RaycastCar vehicle)
	{
		if (vehicle != null && !_trackedVehicles.Contains(vehicle))
		{
			_trackedVehicles.Add(vehicle);
			vehicle.TreeExiting += () => UnregisterVehicle(vehicle);
		}
	}

	public void UnregisterVehicle(RaycastCar vehicle)
	{
		_trackedVehicles.Remove(vehicle);
	}

	private void CheckPlayerBounds()
	{
		for (int i = _trackedPlayers.Count - 1; i >= 0; i--)
		{
			var player = _trackedPlayers[i];
			if (player == null)
			{
				_trackedPlayers.RemoveAt(i);
				continue;
			}

			if (player.GlobalTransform.Origin.Y < KillPlaneY)
			{
				PlayerOutOfBounds?.Invoke(player);
			}
		}
	}

	private void CheckVehicleBounds()
	{
		for (int i = _trackedVehicles.Count - 1; i >= 0; i--)
		{
			var vehicle = _trackedVehicles[i];
			if (vehicle == null || !GodotObject.IsInstanceValid(vehicle))
			{
				_trackedVehicles.RemoveAt(i);
				continue;
			}

			if (vehicle.GlobalTransform.Origin.Y < KillPlaneY)
			{
				VehicleOutOfBounds?.Invoke(vehicle);
			}
		}
	}

	public void RespawnVehicle(RaycastCar vehicle)
	{
		if (vehicle == null)
			return;

		var manager = RespawnManager.Instance;
		var fallback = vehicle.GlobalTransform;
		fallback.Origin = new Vector3(fallback.Origin.X, 5.0f, fallback.Origin.Z);

		if (manager != null)
		{
			var success = manager.RespawnEntityAtBestPoint(vehicle, vehicle);
			if (!success)
			{
				manager.RespawnEntity(vehicle, RespawnManager.RespawnRequest.Create(fallback));
			}
		}
		else
		{
			vehicle.GlobalTransform = fallback;
			vehicle.LinearVelocity = Vector3.Zero;
			vehicle.AngularVelocity = Vector3.Zero;
		}
	}
}

