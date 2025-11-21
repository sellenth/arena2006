using Godot;
using System.Collections.Generic;

public partial class VehicleSessionManager : GodotObject
{
	private readonly Dictionary<int, VehicleInfo> _serverVehicles = new Dictionary<int, VehicleInfo>();
	private readonly Dictionary<ulong, int> _vehicleIdByInstance = new Dictionary<ulong, int>();
	private int _nextVehicleId = 1;

	public int GetVehicleEntityId(int vehicleId) => NetworkConfig.VehicleEntityIdOffset + vehicleId;

	public void RegisterVehicle(RaycastCar car)
	{
		if (car == null)
			return;

		var id = _nextVehicleId++;
		var entityId = GetVehicleEntityId(id);
		car.RegisterAsAuthority(entityId);

		var info = new VehicleInfo
		{
			Id = id,
			Car = car,
			DriverSeat = FindDriverSeat(car),
			OccupantPeerId = 0
		};

		_serverVehicles[id] = info;
		_vehicleIdByInstance[car.GetInstanceId()] = id;
		car.TreeExiting += () => OnServerVehicleExiting(id);
		GD.Print($"VehicleSessionManager: Server vehicle registered id={id} name={car.Name}");
	}

	public bool TryEnterVehicle(PeerInfo info)
	{
		if (info == null || info.PlayerCharacter == null)
			return false;

		var vehicle = FindAvailableVehicle(info.PlayerCharacter.GlobalTransform.Origin, out var seat);
		if (vehicle == null || seat == null)
			return false;

		info.PlayerRestTransform = info.PlayerCharacter.GlobalTransform;
		info.ControlledVehicleId = vehicle.Id;
		vehicle.OccupantPeerId = info.Id;
		vehicle.Car?.SetOccupantPeerId(info.Id);
		info.PlayerCharacter?.SetReplicatedMode(PlayerMode.Vehicle, vehicle.Id);
		info.Mode = PlayerMode.Vehicle;
		info.PlayerCharacter.SetWorldActive(false);
		return true;
	}

	public bool TryExitVehicle(PeerInfo info)
	{
		if (info == null || info.PlayerCharacter == null)
			return false;

		var vehicle = GetVehicleInfo(info.ControlledVehicleId);
		if (vehicle?.Car == null)
			return false;

		var exitTransform = GetSeatExitTransform(vehicle);
		info.Mode = PlayerMode.Foot;
		info.ControlledVehicleId = 0;
		vehicle.OccupantPeerId = 0;
		vehicle.Car.SetOccupantPeerId(0);
		info.PlayerCharacter?.SetReplicatedMode(PlayerMode.Foot, 0);
		var transform = new Transform3D(info.PlayerCharacter.GlobalBasis, exitTransform.Origin);
		RespawnManager.Instance.TeleportEntity(info.PlayerCharacter, transform);
		var yaw = vehicle.Car.GlobalTransform.Basis.GetEuler().Y;
		info.PlayerCharacter.SetYawPitch(yaw, 0f);
		info.PlayerCharacter.SetWorldActive(true);
		return true;
	}

	private Transform3D GetSeatExitTransform(VehicleInfo vehicle)
	{
		var carTransform = vehicle.Car?.GlobalTransform ?? Transform3D.Identity;
		var seat = vehicle.DriverSeat;
		if (seat != null)
		{
			var exit = seat.GetExitPosition();
			return new Transform3D(carTransform.Basis, exit);
		}

		System.Diagnostics.Debug.Assert(false, "Failed to exit seat");

		var fallback = carTransform.Origin - carTransform.Basis.Z * 3f + Vector3.Up;
		return new Transform3D(carTransform.Basis, fallback);
	}

	private VehicleSeat FindDriverSeat(Node node)
	{
		if (node == null)
			return null;

		foreach (var child in node.GetChildren())
		{
			if (child is VehicleSeat seat && seat.IsDriverSeat)
				return seat;
			if (child is Node childNode)
			{
				var nested = FindDriverSeat(childNode);
				if (nested != null)
					return nested;
			}
		}

		return null;
	}

	public VehicleInfo GetVehicleInfo(int vehicleId)
	{
		if (vehicleId == 0)
			return null;

		return _serverVehicles.TryGetValue(vehicleId, out var info) ? info : null;
	}

	private VehicleInfo FindAvailableVehicle(Vector3 position, out VehicleSeat seat)
	{
		seat = null;
		VehicleInfo bestVehicle = null;
		var bestDistance = float.MaxValue;

		foreach (var vehicle in _serverVehicles.Values)
		{
			var candidateSeat = vehicle.DriverSeat;
			if (candidateSeat == null)
				continue;
			if (vehicle.OccupantPeerId != 0)
				continue;

			var distance = candidateSeat.GetSeatPosition().DistanceTo(position);
			if (distance > candidateSeat.InteractionRadius)
				continue;

			if (distance < bestDistance)
			{
				bestDistance = distance;
				bestVehicle = vehicle;
				seat = candidateSeat;
			}
		}

		return bestVehicle;
	}

	public IEnumerable<VehicleInfo> GetAllVehicles()
	{
		return _serverVehicles.Values;
	}

	private void OnServerVehicleExiting(int vehicleId)
	{
		if (!_serverVehicles.TryGetValue(vehicleId, out var info))
			return;

		_serverVehicles.Remove(vehicleId);
		if (info.InstanceId != 0)
			_vehicleIdByInstance.Remove(info.InstanceId);
	}
}

