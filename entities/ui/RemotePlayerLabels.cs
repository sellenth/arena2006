using Godot;
using Godot.Collections;

public partial class RemotePlayerLabels : Node3D
{
	[Export] public Color LabelColor { get; set; } = new Color(0.95f, 0.86f, 0.3f);
	[Export] public float LabelHeight { get; set; } = 2.5f;
	[Export] public float LabelPixelSize { get; set; } = 0.01f;

	private NetworkController _networkController;
	private Dictionary<int, Label3D> _labels = new Dictionary<int, Label3D>();
	private Node3D _remotePlayersContainer;

	public override void _Ready()
	{
		_networkController = GetNode<NetworkController>("/root/NetworkController");
		if (_networkController == null)
		{
			GD.PushError("RemotePlayerLabels: NetworkController not found!");
			return;
		}

		// Find the RemotePlayers container (managed by RemotePlayerManager)
		_remotePlayersContainer = GetNodeOrNull<Node3D>("../RemotePlayers");
		if (_remotePlayersContainer == null)
		{
			GD.PushWarning("RemotePlayerLabels: RemotePlayers node not found, labels won't work");
			return;
		}

		_networkController.PlayerStateUpdated += OnPlayerStateUpdated;
		_networkController.PlayerDisconnected += OnPlayerDisconnected;
	}

	public override void _ExitTree()
	{
		if (_networkController != null)
		{
			_networkController.PlayerStateUpdated -= OnPlayerStateUpdated;
			_networkController.PlayerDisconnected -= OnPlayerDisconnected;
		}
	}

	private void OnPlayerStateUpdated(int playerId, CarSnapshot snapshot)
	{
		if (snapshot == null) return;

		// Create label if it doesn't exist
		if (!_labels.ContainsKey(playerId))
		{
			// Find the remote player car
			var remotePlayerCar = _remotePlayersContainer?.GetNodeOrNull<Node3D>($"RemotePlayer_{playerId}");
			if (remotePlayerCar != null)
			{
				var label = CreateLabel(playerId);
				label.Position = new Vector3(0, LabelHeight, 0);
				remotePlayerCar.AddChild(label);
				_labels[playerId] = label;
			}
		}
	}

	private void OnPlayerDisconnected(int playerId)
	{
		if (_labels.ContainsKey(playerId))
		{
			var label = _labels[playerId];
			_labels.Remove(playerId);
			if (GodotObject.IsInstanceValid(label))
				label.QueueFree();
		}
	}

	private Label3D CreateLabel(int playerId)
	{
		var label = new Label3D
		{
			Name = $"RemoteLabel_{playerId}",
			Text = $"Player {playerId}",
			PixelSize = LabelPixelSize,
			Modulate = LabelColor,
			OutlineModulate = Colors.Black,
			OutlineSize = 4,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true
		};
		return label;
	}
}

