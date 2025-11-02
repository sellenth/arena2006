using Godot;

public partial class CarSnapshot : RefCounted
{
	public int Tick { get; set; } = 0;
	public Transform3D Transform { get; set; } = Transform3D.Identity;
	public Vector3 LinearVelocity { get; set; } = Vector3.Zero;
	public Vector3 AngularVelocity { get; set; } = Vector3.Zero;
}

