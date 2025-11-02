using Godot;

public partial class Camera3D : Godot.Camera3D
{
	private const float MouseSensitivity = 0.002f;

	[Export] public float MoveSpeed { get; set; } = 1.5f;
	[Export] public float ShiftMult { get; set; } = 2.5f;

	private Vector3 _motion = Vector3.Zero;
	private Vector3 _velocity = Vector3.Zero;

	[Export] public bool IsActive { get; set; } = true;
	[Export] public bool StartEnabled { get; set; } = false;
	private bool _mousePressed = false;

	public override void _Ready()
	{
		if (StartEnabled)
			Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_wireframe"))
		{
			if (GetViewport().DebugDraw == Viewport.DebugDrawEnum.Wireframe)
				GetViewport().DebugDraw = Viewport.DebugDrawEnum.Disabled;
			else
			{
				RenderingServer.SetDebugGenerateWireframes(true);
				GetViewport().DebugDraw = Viewport.DebugDrawEnum.Wireframe;
			}
		}

		if (@event.IsActionPressed("change_camera"))
		{
			var orthoCamera = GetNode<Godot.Camera3D>("%OrthoCamera");
			if (IsActive)
			{
				IsActive = false;
				orthoCamera.Set("is_active", true);
				Current = false;
				orthoCamera.Current = true;
			}
			else
			{
				IsActive = true;
				orthoCamera.Set("is_active", false);
				Current = true;
				orthoCamera.Current = false;
			}
		}

		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Right)
		{
			_mousePressed = mouseButton.Pressed;
		}

		if (_mousePressed || !IsActive) return;

		if (@event is InputEventKey key)
		{
			if (key.Keycode == Key.Tab && key.Pressed)
				Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured 
					? Input.MouseModeEnum.Visible 
					: Input.MouseModeEnum.Captured;
		}

		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			Rotation = new Vector3(Rotation.X, Rotation.Y - mouseMotion.Relative.X * MouseSensitivity, Rotation.Z);
			Rotation = new Vector3(
				Mathf.Clamp(Rotation.X - mouseMotion.Relative.Y * MouseSensitivity, Mathf.DegToRad(-90), Mathf.DegToRad(90)),
				Rotation.Y,
				Rotation.Z
			);
		}

		if (Input.IsKeyPressed(Key.Escape))
			GetTree().Quit();
	}

	public override void _Process(double delta)
	{
		if (!IsActive) return;

		if (Input.IsKeyPressed(Key.W))
			_motion.Z = -1;
		else if (Input.IsKeyPressed(Key.S))
			_motion.Z = 1;
		else
			_motion.Z = 0;

		if (Input.IsKeyPressed(Key.A))
			_motion.X = -1;
		else if (Input.IsKeyPressed(Key.D))
			_motion.X = 1;
		else
			_motion.X = 0;

		if (Input.IsKeyPressed(Key.E))
			_motion.Y = 1;
		else if (Input.IsKeyPressed(Key.Q))
			_motion.Y = -1;
		else
			_motion.Y = 0;

		_motion = _motion.Normalized();

		if (Input.IsKeyPressed(Key.Shift))
			_motion *= ShiftMult;

		_motion = _motion
			.Rotated(Vector3.Up, Rotation.Y)
			.Rotated(Vector3.Right, Mathf.Cos(Rotation.Y) * Rotation.X)
			.Rotated(Vector3.Back, -Mathf.Sin(Rotation.Y) * Rotation.X);

		_velocity += _motion * MoveSpeed;
		_velocity *= 0.9f;
		var unscaledDelta = Engine.TimeScale == 0 ? delta : delta / Engine.TimeScale;
		Position += _velocity * (float)unscaledDelta;
	}
}

