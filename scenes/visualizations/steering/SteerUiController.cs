using Godot;

public partial class SteerUiController : Node3D
{
	private Marker3D _velocityMarker;
	private Node3D _wRight;
	private Node3D _wLeft;

	public override void _Ready()
	{
		_velocityMarker = GetNode<Marker3D>("%VelocityMarker");
		_wRight = GetNode<Node3D>("%WRight");
		_wLeft = GetNode<Node3D>("%WLeft");
	}

	public override void _PhysicsProcess(double delta)
	{
		var turnInput = Input.GetAxis("turn_right", "turn_left") * 0.5f;
		_wLeft.RotateY(turnInput * (float)delta);
		_wRight.RotateY(turnInput * (float)delta);

		var turnSpeed = Input.GetAxis("accelerate", "decelerate");
		_velocityMarker.GlobalPosition += new Vector3(turnSpeed * (float)delta, 0, 0);

		var vel = _velocityMarker.GlobalPosition - GlobalPosition;
		var steerDir = _wRight.GlobalBasis.X;
		var tireVel = vel;
		var steeringVel = steerDir.Dot(tireVel);

		var xForce = -_wRight.GlobalBasis.X * steeringVel;
		// DebugDraw.DrawArrowRay(_wRight.GlobalPosition + Vector3.Up, xForce, 1.5f, 0.1f, Colors.Red);

		var zForce = -_wLeft.GlobalBasis.Z * _wLeft.GlobalBasis.Z.Dot(tireVel) * 0.2f;
		// DebugDraw.DrawArrowRay(_wRight.GlobalPosition + Vector3.Up, zForce, 1.0f, 0.1f, Colors.Blue);

		// DebugDraw.DrawArrowRay(GlobalPosition + Vector3.Up, vel, 1.5f, 0.1f, Colors.Yellow);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsAction("quit"))
			GetTree().Quit();
	}
}

