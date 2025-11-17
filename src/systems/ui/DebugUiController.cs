using Godot;

public partial class DebugUiController : Node
{
	private RaycastCar _car;
	private RaycastCar _fallbackCar;
	private PlayerCharacter _player;
	private NetworkController _network;
	
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
		
		_allForcesCB = canvasLayer.GetNode<CheckBox>("TOP/AllForcesCB");
		_pullForcesCB = canvasLayer.GetNode<CheckBox>("TOP/PullForcesCB");
		_handBreakCB = canvasLayer.GetNode<CheckBox>("TOP/HandBreakCB");
		_slippingCB = canvasLayer.GetNode<CheckBox>("TOP/SlippingCB");
		_frameRateLabel = canvasLayer.GetNode<Label>("TOP/FramerateLabel");
		_timeScaleLabel = canvasLayer.GetNode<Label>("TOP/TimeScaleLabel");
		_gameModeLabel = canvasLayer.GetNode<Label>("TOP/GameModeLabel");
		_weaponLabel = canvasLayer.GetNode<Label>("TOP/WeaponLabel");
		_ammoLabel = canvasLayer.GetNode<Label>("TOP/AmmoLabel");
		_speedLabel = canvasLayer.GetNode<Label>("RIGHT_TOP/SpeedLabel");
		_motorRatio = canvasLayer.GetNode<ProgressBar>("RIGHT_TOP/MotorRatio");
		_accelLabel = canvasLayer.GetNode<Label>("RIGHT_TOP/AccelLabel");
		_turnRatio = canvasLayer.GetNode<ProgressBar>("RIGHT_TOP/TurnRatio");
		_healthLabel = canvasLayer.GetNode<Label>("BOTTOM/HealthLabel");
		_armorLabel = canvasLayer.GetNode<Label>("BOTTOM/ArmorLabel");
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

	private void OnLocalCarChanged(RaycastCar car)
	{
		_car = car ?? _fallbackCar;
	}
}
