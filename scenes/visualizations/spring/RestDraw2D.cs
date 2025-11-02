using Godot;

public partial class RestDraw2D : Node2D
{
	private MeshInstance3D _wheel;
	private Node3D _springRoot;
	private Camera3D _camera;
	private Label3D _label;

	public override void _Ready()
	{
		_wheel = GetNode<MeshInstance3D>("%WheelMesh");
		_springRoot = GetNode<Node3D>("%SpringRoot");
		_camera = GetNode<Camera3D>("%Camera3D");
		_label = GetNode<Label3D>("%Label3D");
	}

	public override void _Draw()
	{
		var wheelPos = _camera.UnprojectPosition(_wheel.GlobalPosition);
		var rootPos = _camera.UnprojectPosition(_springRoot.GlobalPosition);
		var restPosition = _camera.UnprojectPosition(Vector3.Zero);
		const float LineWidth = 3;
		DrawLine(rootPos, rootPos + new Vector2(-250, 0), Colors.Red, LineWidth, true);
		DrawLine(rootPos + new Vector2(-250, 0), rootPos + new Vector2(-250, 0) + new Vector2(0, wheelPos.Y - rootPos.Y), Colors.Red, LineWidth);
		DrawLine(wheelPos, wheelPos + new Vector2(-250, 0), Colors.Red, LineWidth);

		DrawLine(wheelPos + new Vector2(-150, 0), restPosition + new Vector2(-150, 0), Colors.Blue, LineWidth);
		var yMult = wheelPos.Y > restPosition.Y ? 1 : -1;
		DrawArrow(restPosition + new Vector2(-150, 0), yMult);

		DrawLine(new Vector2(-500, restPosition.Y), new Vector2(3000, restPosition.Y), Colors.RosyBrown, 1.5f);

		var diffToRest = Vector3.Zero.Y - _wheel.GlobalPosition.Y;
		_label.Text = $"Offset: {diffToRest:F2}";
	}

	private void DrawArrow(Vector2 aPos, float yMult = 1)
	{
		var arrow1 = new Vector2(-10, 15 * yMult);
		var arrow2 = new Vector2(10, 15 * yMult);

		DrawLine(aPos, aPos + arrow1, Colors.Blue, 3);
		DrawLine(aPos, aPos + arrow2, Colors.Blue, 3);
	}

	public override void _Process(double delta)
	{
		QueueRedraw();
	}
}

