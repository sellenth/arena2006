using Godot;

public partial class VehicleStateSnapshot : RefCounted
{
	public int Tick { get; set; }
	public int VehicleId { get; set; }
	public int OccupantPeerId { get; set; }
	public Transform3D Transform { get; set; } = Transform3D.Identity;
	public Vector3 LinearVelocity { get; set; } = Vector3.Zero;
	public Vector3 AngularVelocity { get; set; } = Vector3.Zero;

	public CarSnapshot ToCarSnapshot()
	{
		return new CarSnapshot
		{
			Tick = Tick,
			Transform = Transform,
			LinearVelocity = LinearVelocity,
			AngularVelocity = AngularVelocity
		};
	}
}
