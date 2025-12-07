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
			QueueFreeAfter(flash, LifeTime);
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
			QueueFreeAfter(smoke, LifeTime);
		}
	}

	private void QueueFreeAfter(Node node, float lifetime)
	{
		if (node == null)
			return;

		var tree = node.GetTree();
		if (tree == null)
		{
			node.QueueFree();
			return;
		}

		var timer = tree.CreateTimer(Mathf.Max(lifetime, 0.01f));
		timer.Timeout += () =>
		{
			if (IsInstanceValid(node))
				node.QueueFree();
		};
	}
}
