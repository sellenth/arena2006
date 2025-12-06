using Godot;

public partial class MuzzleFxSet : Resource
{
	[Export] public PackedScene? MuzzleFlashScene { get; set; }
	[Export] public PackedScene? SmokeScene { get; set; }
	[Export] public float LifeTime { get; set; } = 0.4f;

	public void Spawn(Node parent, Transform3D socket)
	{
		if (parent == null)
			return;

		if (MuzzleFlashScene != null)
		{
			var flash = MuzzleFlashScene.Instantiate<Node3D>();
			parent.AddChild(flash);
			flash.GlobalTransform = socket;
			if (flash is GpuParticles3D particles)
			{
				particles.Emitting = true;
			}
			flash.QueueFree();
		}

		if (SmokeScene != null)
		{
			var smoke = SmokeScene.Instantiate<Node3D>();
			parent.AddChild(smoke);
			smoke.GlobalTransform = socket;
			smoke.Owner = parent.GetTree().CurrentScene;
			if (smoke is GpuParticles3D smokeParticles)
			{
				smokeParticles.Emitting = true;
			}
			smoke.QueueFree();
		}
	}
}
