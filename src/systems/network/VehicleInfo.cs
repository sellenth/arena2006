using Godot;

public partial class VehicleInfo : GodotObject
{
	public int Id { get; set; }
	public RaycastCar Car { get; set; }
	public VehicleSeat DriverSeat { get; set; }
	public int OccupantPeerId { get; set; }
	public VehicleStateSnapshot LastSnapshot { get; set; }

	public ulong InstanceId => Car?.GetInstanceId() ?? 0;
}

