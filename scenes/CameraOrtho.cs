using Godot;

public partial class CameraOrtho : Godot.Camera3D
{
	private const float MouseSensitivity = 0.002f;

	[Export] public float MoveSpeed { get; set; } = 1.5f;

	private Vector3 _motion = Vector3.Zero;
	private Vector3 _velocity = Vector3.Zero;

	[Export] public bool IsActive { get; set; } = true;
	[Export] public bool StartEnabled { get; set; } = false;

	public override void _Ready()
	{
		if (StartEnabled)
			Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		if (!IsActive) return;

		if (@event is InputEventKey key)
		{
			if (key.Keycode == Key.Tab && key.Pressed)
				Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured 
					? Input.MouseModeEnum.Visible 
					: Input.MouseModeEnum.Captured;
		}
	}

	public override void _Process(double delta)
	{
		if (!IsActive) return;

		if (Input.IsKeyPressed(Key.Q))
		{
			Size -= 0.1f;
			if (Input.IsKeyPressed(Key.Shift))
				Size -= 0.3f;
		}
		else if (Input.IsKeyPressed(Key.E))
		{
			Size += 0.1f;
			if (Input.IsKeyPressed(Key.Shift))
				Size += 0.3f;
		}

		if (Input.IsKeyPressed(Key.A))
			_motion.Z = -1;
		else if (Input.IsKeyPressed(Key.D))
			_motion.Z = 1;
		else
			_motion.Z = 0;

		if (Input.IsKeyPressed(Key.W))
			_motion.Y = 1;
		else if (Input.IsKeyPressed(Key.S))
			_motion.Y = -1;
		else
			_motion.Y = 0;

		_motion = _motion.Normalized();

		if (Input.IsKeyPressed(Key.Shift))
			_motion *= 2;

		_velocity += _motion * MoveSpeed;
		_velocity *= 0.9f;
		var unscaledDelta = Engine.TimeScale == 0 ? delta : delta / Engine.TimeScale;
		Position += _velocity * (float)unscaledDelta;
	}
}

