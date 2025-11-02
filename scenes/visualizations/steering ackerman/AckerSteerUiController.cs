using Godot;

public partial class AckerSteerUiController : Node3D
{
	private Node3D _wRight;
	private Node3D _wLeft;
	private Node3D _wLeftReal;

	[Export] public float Speed { get; set; } = 1.5f;
	private Vector3 _prev1Pos;
	private Vector3 _prev2Pos;
	private float _tickTime = 0.06f;
	private float _dt = 0.05f;
	private bool _stopped = false;

	public override void _Ready()
	{
		_wRight = GetNode<Node3D>("%WRight");
		_wLeft = GetNode<Node3D>("%WLeft");
		_wLeftReal = GetNode<Node3D>("%WLeftReal");
		_prev1Pos = _wRight.GlobalPosition;
		_prev2Pos = _wLeft.GlobalPosition;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_stopped) return;

		GetNode<Node3D>("%WRightPivot").RotateY(-(float)delta * Speed);
		GetNode<Node3D>("%WLeftPivot").RotateY(-(float)delta * Speed);

		_dt -= (float)delta;
		if (_dt <= 0)
		{
			_dt += _tickTime;
			// DebugDraw.DrawLineThick(_prev1Pos, _wRight.GlobalPosition, 5.0f, Colors.Red, 5);
			// DebugDraw.DrawLineThick(_prev2Pos, _wLeftReal.GlobalPosition, 5.0f, Colors.Red, 5);
			_prev1Pos = _wRight.GlobalPosition;
			_prev2Pos = _wLeftReal.GlobalPosition;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsAction("click"))
			_stopped = !_stopped;
		if (@event.IsAction("quit"))
			GetTree().Quit();
	}
}

