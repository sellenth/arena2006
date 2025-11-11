using Godot;

public partial class PlayerWrapper : Node3D
{
	[Export] public NodePath FootPath { get; set; }

	private FootPlayerController _foot;
	private NetworkController _network;
	private PlayerMode _currentMode = PlayerMode.Foot;

	public override void _Ready()
	{
		_foot = GetNodeOrNull<FootPlayerController>(FootPath);
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");

		if (_network != null && !_network.IsClient)
		{
			_foot?.SetWorldActive(false);
			SetProcess(false);
			SetPhysicsProcess(false);
			return;
		}

		if (_network != null)
		{
			_currentMode = _network.CurrentClientMode;
			_network.ClientModeChanged += OnClientModeChanged;
		}

		ApplyMode(_currentMode);
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
		ApplyMode(mode);
	}

	private void ApplyMode(PlayerMode mode)
	{
		if (_foot == null)
			return;

		var isFoot = mode == PlayerMode.Foot;
		_foot.SetWorldActive(isFoot);
		_foot.SetCameraActive(isFoot);
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
