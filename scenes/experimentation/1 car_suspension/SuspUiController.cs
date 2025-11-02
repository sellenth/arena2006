using Godot;

public partial class SuspUiController : Node
{
	[Export] public RigidBody3D Car { get; set; }
	[Export] public bool UpdateUiVars { get; set; } = true;
	[Export] public bool UsePauseTimeInputs { get; set; } = false;

	private bool _mousePressed = false;
	private bool _isUsingSignals = false;

	public override void _Ready()
	{
		if (UpdateUiVars && Car != null && Car.HasSignal("time_scale_changed"))
		{
			_isUsingSignals = true;
			Car.Connect("time_scale_changed", Callable.From<float>(UpdateTimeScaleLabel));
			var allForcesCB = GetNode<CheckBox>("%AllForcesCB");
			var pullForcesCB = GetNode<CheckBox>("%PullForcesCB");
			Car.Connect("all_force_changed", Callable.From((bool value) => UpdateCheckbox(value, allForcesCB)));
			Car.Connect("pull_force_changed", Callable.From((bool value) => UpdateCheckbox(value, pullForcesCB)));

			UpdateCheckbox(!(bool)Car.Get("disable_pull_force"), pullForcesCB);
			UpdateCheckbox(!(bool)Car.Get("disable_forces"), allForcesCB);
		}
		if (Car == null)
			Car = GetNode<RigidBody3D>("%Car");
		if (!UpdateUiVars)
			GetNode<CanvasLayer>("CanvasLayer").Hide();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (UpdateUiVars && !_isUsingSignals && Car != null)
		{
			if (Car.Get("disable_pull_force").VariantType != Variant.Type.Nil)
				UpdateCheckbox(!(bool)Car.Get("disable_pull_force"), GetNode<CheckBox>("%PullForcesCB"));
			if (Car.Get("disable_forces").VariantType != Variant.Type.Nil)
				UpdateCheckbox(!(bool)Car.Get("disable_forces"), GetNode<CheckBox>("%AllForcesCB"));
			if (Car.Get("hand_break").VariantType != Variant.Type.Nil)
				UpdateCheckbox((bool)Car.Get("hand_break"), GetNode<CheckBox>("%HandBreakCB"));
			if (Car.Get("is_slipping").VariantType != Variant.Type.Nil)
				UpdateCheckbox((bool)Car.Get("is_slipping"), GetNode<CheckBox>("%SlippingCB"));
			UpdateTimeScaleLabel((float)Engine.TimeScale);

			GetNode<Label>("%SpeedLabel").Text = $"Car speed: {-Car.GlobalBasis.Z.Dot(Car.LinearVelocity):F1}";

			if (Car.Get("accel_curve").VariantType != Variant.Type.Nil)
			{
				var vel = -Car.GlobalBasis.Z.Dot(Car.LinearVelocity);
				var maxSpeed = (float)Car.Get("max_speed");
				var ratio = vel / maxSpeed;
				var accelCurve = (Curve)Car.Get("accel_curve");
				var ac = accelCurve.SampleBaked(ratio);
				GetNode<ProgressBar>("%MotorRatio").Value = ac;
				var realAccel = ac * (float)Car.Get("acceleration");
				var motorInput = (float)Car.Get("motor_input");
				if (motorInput == 0)
				{
					realAccel = 0;
					GetNode<ProgressBar>("%MotorRatio").Value = 0;
				}
				GetNode<Label>("%AccelLabel").Text = $"AccelForce: {realAccel:F0}";
			}
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("reload_scene"))
			GetTree().ReloadCurrentScene();

		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Right)
		{
			if (mouseButton.Pressed)
			{
				_mousePressed = true;
				if (Car.Get("disable_forces").VariantType != Variant.Type.Nil)
				{
					Car.Set("disable_forces", true);
					Car.Freeze = false;
				}
				Car.GravityScale = 0.0f;
			}
			else
			{
				_mousePressed = false;
				if (Car.Get("disable_forces").VariantType != Variant.Type.Nil)
				{
					Car.Set("disable_forces", false);
					Car.Freeze = false;
				}
				Car.GravityScale = 1.0f;
			}
		}

		if (@event is InputEventMouseMotion mouseMotion)
		{
			if (mouseMotion.ButtonMask == MouseButtonMask.Right)
				Car.LinearVelocity = new Vector3(Car.LinearVelocity.X, -mouseMotion.Relative.Y / 20.0f, Car.LinearVelocity.Z);
		}

		if (UsePauseTimeInputs)
		{
			if (@event.IsActionPressed("speed_1"))
			{
				Engine.TimeScale = 1.0;
				Car.Freeze = false;
			}
			if (@event.IsActionPressed("speed_2"))
			{
				Engine.TimeScale = 0.25;
				Car.Freeze = false;
			}
			if (@event.IsActionPressed("speed_3"))
			{
				Engine.TimeScale = 0.1;
				Car.Freeze = false;
			}
			if (@event.IsActionPressed("speed_4"))
			{
				Engine.TimeScale = 0.01;
				Car.Freeze = false;
			}
		}

		if (@event.IsActionPressed("quit"))
			GetTree().Quit();
	}

	private void UpdateTimeScaleLabel(float value)
	{
		GetNode<Label>("%TimeScaleLabel").Text = $"Time scale: {value:F2}";
		if (value >= 0.9f)
			GetNode<Label>("%TimeScaleLabel").Hide();
		else
			GetNode<Label>("%TimeScaleLabel").Show();
	}

	private void UpdateCheckbox(bool value, CheckBox checkbox)
	{
		checkbox.ButtonPressed = value;
	}
}

