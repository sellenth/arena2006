using Godot;

public partial class CarFollowCamera : Camera3D
{
	[Export] public float MinDistance { get; set; } = 4.0f;
	[Export] public float MaxDistance { get; set; } = 8.0f;
	[Export] public float Height { get; set; } = 3.0f;
	[Export] public float CameraSensibility { get; set; } = 0.001f;

	private Node3D _target;

	public override void _Ready()
	{
		_target = GetParent().GetParent() as Node3D;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion mouseMotion)
		{
			TopLevel = false;
			GetParent<Node3D>().RotateY(-mouseMotion.Relative.X * CameraSensibility);
			TopLevel = true;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		var fromTarget = GlobalPosition - _target.GlobalPosition;

		if (fromTarget.Length() < MinDistance)
			fromTarget = fromTarget.Normalized() * MinDistance;
		else if (fromTarget.Length() > MaxDistance)
			fromTarget = fromTarget.Normalized() * MaxDistance;

		fromTarget.Y = Height;
		GlobalPosition = _target.GlobalPosition + fromTarget;

		var lookDir = GlobalPosition.DirectionTo(_target.GlobalPosition).Abs() - Vector3.Up;
		if (!lookDir.IsZeroApprox())
			LookAtFromPosition(GlobalPosition, _target.GlobalPosition, Vector3.Up);
	}
}

