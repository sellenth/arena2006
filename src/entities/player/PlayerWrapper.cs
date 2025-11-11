using Godot;

public partial class PlayerWrapper : Node3D
{
	[Export] public NodePath PlayerPath { get; set; }

	private PlayerCharacter _player;
	private NetworkController _network;
	private PlayerMode _currentMode = PlayerMode.Foot;

	public override void _Ready()
	{
		_player = GetNodeOrNull<PlayerCharacter>(PlayerPath);
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");

		if (_network != null && !_network.IsClient)
		{
			_player?.SetWorldActive(false);
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
		RandomizeLocalPlayerColor();
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
		if (_player == null)
			return;

		var isFoot = mode == PlayerMode.Foot;
		_player.SetWorldActive(isFoot);
		_player.SetCameraActive(isFoot);
	}

	private void RandomizeLocalPlayerColor()
	{
		if (_player == null)
			return;

		var rng = new RandomNumberGenerator();
		rng.Randomize();
		var color = new Color(rng.Randf(), rng.Randf(), rng.Randf());
		_player.SetPlayerColor(color);
	}
}
