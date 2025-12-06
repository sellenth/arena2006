using Godot;

public partial class PlayerSnapshot : RefCounted
{
	public int Tick { get; set; }
	public Transform3D Transform { get; set; } = Transform3D.Identity;
	public Vector3 Velocity { get; set; } = Vector3.Zero;
	public float ViewYaw { get; set; }
	public float ViewPitch { get; set; }
}
