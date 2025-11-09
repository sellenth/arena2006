using Godot;

public partial class PlayerWrapper : Node3D
{
	[Export] public NodePath CarPath { get; set; }
	[Export] public NodePath FootPath { get; set; }
	[Export] public NodePath CarCameraPath { get; set; }
	[Export] public bool StartInVehicle { get; set; } = true;

	private RaycastCar _car;
	private FootPlayerController _foot;
	private Camera3D _carCamera;
	private NetworkController _network;
	private PlayerMode _currentMode = PlayerMode.Vehicle;

	public override void _Ready()
	{
		_car = GetNodeOrNull<RaycastCar>(CarPath);
		_foot = GetNodeOrNull<FootPlayerController>(FootPath);
		_carCamera = GetNodeOrNull<Camera3D>(CarCameraPath);
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");

		if (_network != null && !_network.IsClient)
		{
			_foot?.SetWorldActive(false);
			if (_carCamera != null)
				_carCamera.Current = false;
			SetProcess(false);
			SetPhysicsProcess(false);
			return;
		}

		_currentMode = StartInVehicle ? PlayerMode.Vehicle : PlayerMode.Foot;

		if (_network != null)
		{
			_currentMode = _network.CurrentClientMode;
			_network.ClientModeChanged += OnClientModeChanged;
		}

		ApplyMode(_currentMode, true);
		RandomizeLocalFootColor();
	}

	public override void _ExitTree()
	{
		if (_network != null)
		{
			_network.ClientModeChanged -= OnClientModeChanged;
		}
	}

	private void OnClientModeChanged(PlayerMode mode)
	{
		_currentMode = mode;
		ApplyMode(mode, false);
	}

	private void ApplyMode(PlayerMode mode, bool immediate)
	{
		if (_foot == null)
			return;

		var isFoot = mode == PlayerMode.Foot;
		_foot.SetWorldActive(isFoot);
		_foot.SetCameraActive(isFoot);

		if (_carCamera != null)
		{
			_carCamera.Current = !isFoot;
		}
	}

	private void RandomizeLocalFootColor()
	{
		if (_foot == null)
			return;

		var rng = new RandomNumberGenerator();
		rng.Randomize();
		var color = new Color(rng.Randf(), rng.Randf(), rng.Randf());
		_foot.SetPlayerColor(color);
	}
}
