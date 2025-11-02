using Godot;

public partial class FollowCamera : Camera3D
{
	[Export] public float MinDistance { get; set; } = 4.0f;
	[Export] public float MaxDistance { get; set; } = 8.0f;
	[Export] public float AngleVAdjust { get; set; } = 15.0f;
	[Export] public float Height { get; set; } = 3.0f;

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion mouseMotion)
		{
			TopLevel = false;
			GetParent<Node3D>().RotateY(-mouseMotion.Relative.X * 0.001f);
			TopLevel = true;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		var target = GetParent().GetParent<Node3D>().GlobalPosition;

		var fromTarget = GlobalPosition - target;

		if (fromTarget.Length() < MinDistance)
			fromTarget = fromTarget.Normalized() * MinDistance;
		else if (fromTarget.Length() > MaxDistance)
			fromTarget = fromTarget.Normalized() * MaxDistance;

		fromTarget.Y = Height;

		GlobalPosition = target + fromTarget;

		var lookDirection = GlobalPosition.DirectionTo(target);
		if (!lookDirection.IsEqualApprox(Vector3.Up) && !lookDirection.IsEqualApprox(-Vector3.Up))
			LookAtFromPosition(GlobalPosition, target, Vector3.Up);
	}
}

