using Godot;

public partial class DebugUiController : Node
{
	private RaycastCar _car;
	
	private Label _offsetLabel;
	private CheckBox _allForcesCB;
	private CheckBox _pullForcesCB;
	private CheckBox _handBreakCB;
	private CheckBox _slippingCB;
	private Label _timeScaleLabel;
	private Label _speedLabel;
	private ProgressBar _motorRatio;
	private Label _accelLabel;
	private ProgressBar _turnRatio;

	public override void _Ready()
	{
		_car = GetNode<RaycastCar>("../Car");
		
		var canvasLayer = GetNode<CanvasLayer>("CanvasLayer");
		
		_offsetLabel = canvasLayer.GetNode<Label>("BOTTOM/OffsetLabel");
		_allForcesCB = canvasLayer.GetNode<CheckBox>("TOP/AllForcesCB");
		_pullForcesCB = canvasLayer.GetNode<CheckBox>("TOP/PullForcesCB");
		_handBreakCB = canvasLayer.GetNode<CheckBox>("TOP/HandBreakCB");
		_slippingCB = canvasLayer.GetNode<CheckBox>("TOP/SlippingCB");
		_timeScaleLabel = canvasLayer.GetNode<Label>("TOP/TimeScaleLabel");
		_speedLabel = canvasLayer.GetNode<Label>("RIGHT_TOP/SpeedLabel");
		_motorRatio = canvasLayer.GetNode<ProgressBar>("RIGHT_TOP/MotorRatio");
		_accelLabel = canvasLayer.GetNode<Label>("RIGHT_TOP/AccelLabel");
		_turnRatio = canvasLayer.GetNode<ProgressBar>("RIGHT_TOP/TurnRatio");
	}

	public override void _Process(double delta)
	{
		if (_car == null) return;
		
		_handBreakCB.ButtonPressed = _car.HandBreak;
		_slippingCB.ButtonPressed = _car.IsSlipping;
		
		var speed = _car.LinearVelocity.Length();
		_speedLabel.Text = $"Speed: {speed:F2} m/s";
		
		_motorRatio.Value = Mathf.Abs(_car.MotorInput);
		
		_turnRatio.Value = -_car.SteerInput;
		
		var accelForce = _car.Acceleration * _car.MotorInput;
		_accelLabel.Text = $"AccelForce: {accelForce:F1}";
		
		_timeScaleLabel.Text = $"Time Scale: {Engine.TimeScale:F2}";
		
		if (_car.Wheels != null && _car.Wheels.Count > 0)
		{
			var firstWheel = _car.Wheels[0];
			if (firstWheel != null)
			{
				var offset = firstWheel.RestDist;
				_offsetLabel.Text = $"Offset: {offset:F3}";
			}
		}
	}
}
