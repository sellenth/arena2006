using Godot;
using Godot.Collections;

public partial class RemotePlayerLabels : Node3D
{
	[Export] public PackedScene RemoteCarScene { get; set; }
	[Export] public Color LabelColor { get; set; } = new Color(0.95f, 0.86f, 0.3f);
	[Export] public float LabelHeight { get; set; } = 2.5f;
	[Export] public float LabelPixelSize { get; set; } = 0.01f;
	[Export] public Color PlaceholderColor { get; set; } = new Color(0.2f, 0.6f, 0.9f);

	private partial class RemotePlayerView : GodotObject
	{
		public Node3D Root { get; set; }
		public Label3D Label { get; set; }
	}

	private Node _network;
	private Dictionary<int, RemotePlayerView> _views = new Dictionary<int, RemotePlayerView>();

	public override void _Ready()
	{
		if (RemoteCarScene == null)
			RemoteCarScene = GD.Load<PackedScene>("res://scenes/remote_car_proxy.tscn");

		_network = GetTree().Root.GetNodeOrNull("/root/NetworkController");
		if (_network == null) return;

		if (_network.HasSignal("player_state_updated"))
			_network.Connect("player_state_updated", Callable.From<int, CarSnapshot>(OnPlayerStateUpdated));
		if (_network.HasSignal("player_disconnected"))
			_network.Connect("player_disconnected", Callable.From<int>(OnPlayerDisconnected));
	}

	private void OnPlayerStateUpdated(int playerId, CarSnapshot snapshot)
	{
		if (snapshot == null) return;
		if (!_views.ContainsKey(playerId))
		{
			var view = CreateView(playerId);
			_views[playerId] = view;
		}
		_views[playerId].Root.GlobalTransform = snapshot.Transform;
	}

	private void OnPlayerDisconnected(int playerId)
	{
		if (!_views.ContainsKey(playerId)) return;
		var view = _views[playerId];
		_views.Remove(playerId);
		if (GodotObject.IsInstanceValid(view.Root))
			view.Root.QueueFree();
	}

	private RemotePlayerView CreateView(int playerId)
	{
		var view = new RemotePlayerView
		{
			Root = InstantiateRemoteCar()
		};
		view.Root.Name = $"RemotePlayer_{playerId}";
		AddChild(view.Root);
		view.Label = CreateLabel(playerId);
		view.Label.Position = new Vector3(0, LabelHeight, 0);
		view.Root.AddChild(view.Label);
		return view;
	}

	private Node3D InstantiateRemoteCar()
	{
		if (RemoteCarScene != null)
		{
			var inst = RemoteCarScene.Instantiate();
			if (inst is Node3D node3D)
				return node3D;
		}
		var placeholder = new Node3D();
		var mesh = new MeshInstance3D();
		var box = new BoxMesh { Size = new Vector3(2, 0.5f, 4) };
		mesh.Mesh = box;
		var mat = new StandardMaterial3D { AlbedoColor = PlaceholderColor };
		mesh.MaterialOverride = mat;
		mesh.Position = new Vector3(0, 0.25f, 0);
		placeholder.AddChild(mesh);
		return placeholder;
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

