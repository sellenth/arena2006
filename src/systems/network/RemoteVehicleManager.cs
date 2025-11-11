using Godot;
using System.Collections.Generic;

public partial class RemoteVehicleManager : Node3D
{
	[Export] public PackedScene VehicleScene { get; set; }

	private NetworkController _networkController;
	private readonly Dictionary<int, RaycastCar> _vehicles = new Dictionary<int, RaycastCar>();
	private readonly Dictionary<int, int> _vehicleOccupants = new Dictionary<int, int>();
	private readonly Dictionary<int, int> _occupantToVehicle = new Dictionary<int, int>();
	private PackedScene _fallbackVehicleScene;
	private int _localVehicleId = 0;

	public override void _Ready()
	{
		_networkController = GetNodeOrNull<NetworkController>("/root/NetworkController");
		if (_networkController == null)
		{
			GD.PushError("RemoteVehicleManager: NetworkController not found!");
			return;
		}

		if (!_networkController.IsClient)
			return;

		_fallbackVehicleScene = VehicleScene ?? GD.Load<PackedScene>("res://src/entities/vehicle/car/player_car.tscn");
		_networkController.VehicleStateUpdated += OnVehicleStateUpdated;
		_networkController.VehicleDespawned += OnVehicleDespawned;
	}

	public override void _ExitTree()
	{
		if (_networkController != null && _networkController.IsClient)
		{
			_networkController.VehicleStateUpdated -= OnVehicleStateUpdated;
			_networkController.VehicleDespawned -= OnVehicleDespawned;
		}
	}

	private void OnVehicleStateUpdated(int vehicleId, VehicleStateSnapshot snapshot)
	{
		if (snapshot == null || vehicleId == 0)
			return;

		var car = EnsureVehicle(vehicleId, snapshot);
		if (car == null)
			return;

		car.ApplyRemoteSnapshot(snapshot);
		UpdateOccupant(vehicleId, snapshot.OccupantPeerId, car);
	}

	private void OnVehicleDespawned(int vehicleId)
	{
		if (_vehicles.TryGetValue(vehicleId, out var car))
		{
			if (vehicleId == _localVehicleId)
			{
				_networkController.DetachLocalVehicle(vehicleId, car);
				_localVehicleId = 0;
			}

			if (GodotObject.IsInstanceValid(car))
				car.QueueFree();
		}

		_vehicles.Remove(vehicleId);
		if (_vehicleOccupants.TryGetValue(vehicleId, out var occupant))
		{
			_vehicleOccupants.Remove(vehicleId);
			if (_occupantToVehicle.TryGetValue(occupant, out var current) && current == vehicleId)
				_occupantToVehicle.Remove(occupant);
		}
	}

	private RaycastCar EnsureVehicle(int vehicleId, VehicleStateSnapshot snapshot)
	{
		if (_vehicles.TryGetValue(vehicleId, out var car) && GodotObject.IsInstanceValid(car))
			return car;

		var scene = _fallbackVehicleScene;
		if (scene == null)
		{
			GD.PushError("RemoteVehicleManager: Vehicle scene not configured.");
			return null;
		}

		car = scene.Instantiate<RaycastCar>();
		if (car == null)
			return null;

		car.RegistrationMode = RaycastCar.NetworkRegistrationMode.None;
		car.AutoRespawnOnReady = false;
		AddChild(car);
		car.Name = $"Vehicle_{vehicleId}";
		car.SetSimulationEnabled(false);
		car.GlobalTransform = snapshot.Transform;
		_vehicles[vehicleId] = car;
		return car;
	}

	private void UpdateOccupant(int vehicleId, int occupantPeerId, RaycastCar car)
	{
		if (_vehicleOccupants.TryGetValue(vehicleId, out var previousOccupant) && previousOccupant == occupantPeerId)
		{
			if (occupantPeerId == _networkController.ClientPeerId)
				car.SetCameraActive(true);
			else if (occupantPeerId == 0)
				car.SetCameraActive(false);
			return;
		}

		if (previousOccupant != 0)
		{
			_occupantToVehicle.Remove(previousOccupant);
			if (previousOccupant == _networkController.ClientPeerId && vehicleId == _localVehicleId)
			{
				_networkController.DetachLocalVehicle(vehicleId, car);
				_localVehicleId = 0;
				car.SetCameraActive(false);
			}
		}

		_vehicleOccupants[vehicleId] = occupantPeerId;
		if (occupantPeerId != 0)
			_occupantToVehicle[occupantPeerId] = vehicleId;
		else
			car.SetCameraActive(false);

		if (occupantPeerId == _networkController.ClientPeerId)
		{
			_localVehicleId = vehicleId;
			car.SetCameraActive(true);
			_networkController.AttachLocalVehicle(vehicleId, car);
		}
		else if (vehicleId == _localVehicleId)
		{
			_networkController.DetachLocalVehicle(vehicleId, car);
			_localVehicleId = 0;
			car.SetCameraActive(false);
		}
	}

	public Node3D GetVehicleNodeForPlayer(int playerId)
	{
		if (playerId == 0)
			return null;

		if (_occupantToVehicle.TryGetValue(playerId, out var vehicleId) &&
		    _vehicles.TryGetValue(vehicleId, out var car) &&
		    GodotObject.IsInstanceValid(car))
		{
			return car;
		}

		return null;
	}
}
