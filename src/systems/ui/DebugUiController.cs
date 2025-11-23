using Godot;

	public partial class DebugUiController : Node
{
	private RaycastCar _car;
	private RaycastCar _fallbackCar;
	private PlayerCharacter _player;
	private NetworkController _network;
	private Control _debugBoxContainer;
	
	private CheckBox _allForcesCB;
	private CheckBox _pullForcesCB;
	private CheckBox _handBreakCB;
	private CheckBox _slippingCB;
	private Label _frameRateLabel;
	private Label _timeScaleLabel;
	private Label _gameModeLabel;
	private Label _weaponLabel;
	private Label _ammoLabel;
	private Label _speedLabel;
	private ProgressBar _motorRatio;
	private Label _accelLabel;
	private ProgressBar _turnRatio;
	private Label _healthLabel;
	private Label _armorLabel;
	private Label _moveStateLabel;

	public override void _Ready()
	{
		_car = GetNodeOrNull<RaycastCar>("../Car");
		_fallbackCar = _car;
		_player = GetNodeOrNull<PlayerCharacter>("../PlayerCharacter");
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");

		if (_network != null)
		{
			_network.LocalCarChanged += OnLocalCarChanged;
			OnLocalCarChanged(_network.LocalCar);
		}

		var canvasLayer = GetNode<CanvasLayer>("CanvasLayer");
		_debugBoxContainer = GetNode<Control>("CanvasLayer/DebugUi");
		_debugBoxContainer.Visible = OS.IsDebugBuild();

		var topContainer = canvasLayer.GetNode<VBoxContainer>("DebugUi/TOP");
		_allForcesCB = topContainer.GetNode<CheckBox>("AllForcesCB");
		_pullForcesCB = topContainer.GetNode<CheckBox>("PullForcesCB");
		_handBreakCB = topContainer.GetNode<CheckBox>("HandBreakCB");
		_slippingCB = topContainer.GetNode<CheckBox>("SlippingCB");
		_frameRateLabel = topContainer.GetNode<Label>("FramerateLabel");
		_timeScaleLabel = topContainer.GetNode<Label>("TimeScaleLabel");
		_gameModeLabel = topContainer.GetNode<Label>("GameModeLabel");
		_weaponLabel = topContainer.GetNode<Label>("WeaponLabel");
		_ammoLabel = canvasLayer.GetNode<Label>("BOTTOM/AmmoLabel");
		_speedLabel = canvasLayer.GetNode<Label>("RIGHT_TOP/SpeedLabel");
		_motorRatio = canvasLayer.GetNode<ProgressBar>("RIGHT_TOP/MotorRatio");
		_accelLabel = canvasLayer.GetNode<Label>("RIGHT_TOP/AccelLabel");
		_turnRatio = canvasLayer.GetNode<ProgressBar>("RIGHT_TOP/TurnRatio");
		_healthLabel = canvasLayer.GetNode<Label>("BOTTOM/HealthLabel");
		_armorLabel = canvasLayer.GetNode<Label>("BOTTOM/ArmorLabel");
		_moveStateLabel = topContainer.GetNodeOrNull<Label>("MoveStateLabel");
	}

	public override void _ExitTree()
	{
		if (_network != null)
			_network.LocalCarChanged -= OnLocalCarChanged;
	}

	public override void _Process(double delta)
	{
		var isFootMode = _network != null && _network.CurrentClientMode == PlayerMode.Foot;
		
		if (isFootMode && _player != null)
		{
			var speed = _player.Velocity.Length();
			_speedLabel.Text = $"Speed: {speed:F2} m/s";
			_healthLabel.Text = $"Health: {_player.Health}/{_player.MaxHealth}";
			_armorLabel.Text = $"Armor: {_player.Armor}/{_player.MaxArmor}";
			if (_moveStateLabel != null)
			{
				_moveStateLabel.Text = $"Move: {_player.GetMovementStateName()}";
			}
		}
		else if (_car != null)
		{
			_handBreakCB.ButtonPressed = _car.HandBreak;
			_slippingCB.ButtonPressed = _car.IsSlipping;
			
			var speed = _car.LinearVelocity.Length();
			_speedLabel.Text = $"Speed: {speed:F2} m/s";
			
			_motorRatio.Value = Mathf.Abs(_car.MotorInput);
			
			_turnRatio.Value = -_car.SteerInput;
			
			var accelForce = _car.Acceleration * _car.MotorInput;
			_accelLabel.Text = $"AccelForce: {accelForce:F1}";
			
			_healthLabel.Text = $"Health: {_car.Health}/{_car.MaxHealth}";
			_armorLabel.Text = $"Armor: {_car.Armor}/{_car.MaxArmor}";
		}

		_frameRateLabel.Text = $"FPS: {Engine.GetFramesPerSecond()}";
		_timeScaleLabel.Text = $"Time Scale: {Engine.TimeScale:F2}";

		var weaponInventory = _player?.GetNodeOrNull<WeaponInventory>("WeaponInventory");
		var weaponType = weaponInventory?.EquippedType ?? WeaponType.None;
		var mag = weaponInventory?.Equipped?.Magazine ?? 0;
		var reserve = weaponInventory?.Equipped?.Reserve ?? 0;
		var magSize = weaponInventory?.Equipped?.Definition?.MagazineSize ?? 0;
		_weaponLabel.Text = $"Weapon: {weaponType.ToString()}";
		if (_ammoLabel != null)
		{
			_ammoLabel.Text = $"Ammo: {mag}/{magSize} | Total: {mag + reserve}";
		}

		if (GameModeManager.Instance != null && GameModeManager.Instance.ActiveMode != null)
		{
			_gameModeLabel.Text = $"Game Mode: {GameModeManager.Instance.ActiveMode.DisplayName}";
		}
		else
		{
			_gameModeLabel.Text = "Game Mode: None";
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_debug_ui"))
		{
			_debugBoxContainer.Visible = !_debugBoxContainer.Visible;
		}
	}

	private void OnLocalCarChanged(RaycastCar car)
	{
		_car = car ?? _fallbackCar;
	}
}
