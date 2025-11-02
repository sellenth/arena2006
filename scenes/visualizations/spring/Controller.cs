using Godot;

public partial class Controller : Node3D
{
	private RigidBody3D _wheelRb;
	[Export] public float SpringStrength { get; set; } = 50.0f;
	[Export] public float DampingStrength { get; set; } = 10.0f;

	public override void _Ready()
	{
		_wheelRb = GetNode<RigidBody3D>("%WheelRB");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Input.IsMouseButtonPressed(MouseButton.Left))
			return;

		var restPos = Vector3.Zero;
		var offset = restPos.Y - _wheelRb.GlobalPosition.Y;
		var damping = _wheelRb.LinearVelocity.Y * DampingStrength;
		var force = new Vector3(0, offset * SpringStrength, 0);
		force.Y -= damping;
		_wheelRb.ApplyForce(force);

		GetNode<Label>("%StrengthLabel").Text = $"Strength: {SpringStrength:F2}";
		if (DampingStrength <= 0.1f)
		{
			GetNode<Label>("%DampLabel").Hide();
			GetNode<Label>("%DampLabelForce").Hide();
		}
		else
		{
			GetNode<Label>("%DampLabel").Show();
			GetNode<Label>("%DampLabelForce").Show();
			GetNode<Label>("%DampLabel").Text = $"Damping : {DampingStrength:F2}";
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion mouseMotion)
		{
			if (mouseMotion.ButtonMask == MouseButtonMask.Left)
				_wheelRb.LinearVelocity = new Vector3(_wheelRb.LinearVelocity.X, -mouseMotion.Relative.Y / 20.0f, _wheelRb.LinearVelocity.Z);
		}
	}
}

