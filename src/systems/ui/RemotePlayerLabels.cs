using Godot;
using Godot.Collections;

public partial class RemotePlayerLabels : Node3D
{
	[Export] public Color LabelColor { get; set; } = new Color(0.95f, 0.86f, 0.3f);
	[Export] public float LabelHeight { get; set; } = 2.5f;
	[Export] public float LabelPixelSize { get; set; } = 0.01f;

	private NetworkController _networkController;
	private Dictionary<int, Label3D> _labels = new Dictionary<int, Label3D>();
	private Node3D _remotePlayerCharacters;
	private RemoteVehicleManager _vehicleManager;

	public override void _Ready()
	{
		_networkController = GetNode<NetworkController>("/root/NetworkController");
		if (_networkController == null)
		{
			GD.PushError("RemotePlayerLabels: NetworkController not found!");
			return;
		}

		// Find player/vehicle containers so we can attach labels
		_remotePlayerCharacters = GetNodeOrNull<Node3D>("../RemotePlayers");
		if (_remotePlayerCharacters == null)
		{
			GD.PushWarning("RemotePlayerLabels: RemotePlayers node not found, labels won't work");
			return;
		}

		var remoteVehiclesNode = GetNodeOrNull<Node3D>("../RemoteVehicles");
		_vehicleManager = remoteVehiclesNode as RemoteVehicleManager;
		_vehicleManager ??= remoteVehiclesNode?.GetNodeOrNull<RemoteVehicleManager>(".");

		_networkController.EntitySnapshotReceived += OnEntitySnapshot;
		_networkController.PlayerDisconnected += OnPlayerDisconnected;
	}

	public override void _ExitTree()
	{
		if (_networkController != null)
		{
			_networkController.EntitySnapshotReceived -= OnEntitySnapshot;
			_networkController.PlayerDisconnected -= OnPlayerDisconnected;
		}
	}

	private void OnEntitySnapshot(int entityId, byte[] data)
	{
		if (entityId < 3000 || entityId >= 4000) // players use 3000+ IDs
			return;

		var playerId = entityId - 3000;
		if (!_labels.ContainsKey(playerId))
		{
			var label = CreateLabel(playerId);
			AddChild(label);
			_labels[playerId] = label;
		}

		var playerNode = _remotePlayerCharacters?.GetNodeOrNull<Node3D>($"RemotePlayer_{playerId}");
		var target = playerNode ?? _vehicleManager?.GetVehicleNodeForPlayer(playerId);
		if (target != null)
			AttachLabelToTarget(playerId, playerNode != null ? PlayerMode.Foot : PlayerMode.Vehicle);
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

	private void AttachLabelToTarget(int playerId, PlayerMode mode)
	{
		if (!_labels.TryGetValue(playerId, out var label))
			return;

		Node3D target = null;
		target = mode == PlayerMode.Foot
			? _remotePlayerCharacters?.GetNodeOrNull<Node3D>($"RemotePlayer_{playerId}")
			: _vehicleManager?.GetVehicleNodeForPlayer(playerId);

		if (target == null)
			return;

		if (label.GetParent() != target)
		{
			label.GetParent()?.RemoveChild(label);
			target.AddChild(label);
			label.Position = new Vector3(0, LabelHeight, 0);
		}
	}
}
