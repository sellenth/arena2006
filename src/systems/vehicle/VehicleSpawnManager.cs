using Godot;
using System.Collections.Generic;

public partial class VehicleSpawnManager : Node3D
{
	[Export] public PackedScene VehicleScene { get; set; }
	[Export] public NodePath SpawnPointsRootPath { get; set; } = new NodePath("");
	[Export] public bool ServerOnly { get; set; } = true;
	[Export] public int MaxVehicles { get; set; } = 8;

	private NetworkController _network;
	private readonly List<Node3D> _spawnPoints = new();

	public override void _Ready()
	{
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");
		if (ServerOnly && (_network == null || !_network.IsServer))
			return;

		if (VehicleScene == null)
		{
			GD.PushWarning($"{nameof(VehicleSpawnManager)} has no VehicleScene assigned.");
			return;
		}

		CollectSpawnPoints();
		SpawnVehicles();
	}

	private void CollectSpawnPoints()
	{
		_spawnPoints.Clear();
		Node spawnRoot = this;
		var spawnPath = SpawnPointsRootPath.ToString();
		if (!string.IsNullOrEmpty(spawnPath))
			spawnRoot = GetNodeOrNull<Node>(SpawnPointsRootPath) ?? this;

		if (spawnRoot == null)
			return;

		foreach (var child in spawnRoot.GetChildren())
		{
			if (child is Node3D node3D)
				_spawnPoints.Add(node3D);
		}
	}

	private void SpawnVehicles()
	{
		if (_spawnPoints.Count == 0)
		{
			GD.PushWarning($"{nameof(VehicleSpawnManager)} found no spawn points.");
			return;
		}

		var count = Mathf.Min(MaxVehicles, _spawnPoints.Count);
		for (var i = 0; i < count; i++)
		{
			var spawnPoint = _spawnPoints[i % _spawnPoints.Count];
			SpawnVehicleAt(spawnPoint.GlobalTransform, i);
		}
	}

	private void SpawnVehicleAt(Transform3D transform, int index)
	{
		var car = VehicleScene.Instantiate<RaycastCar>();
		if (car == null)
			return;

		car.Name = $"ServerVehicle_{index}";
		car.RegistrationMode = RaycastCar.NetworkRegistrationMode.AuthoritativeVehicle;
		car.AutoRespawnOnReady = false;
		AddChild(car);
		car.GlobalTransform = transform;
	}
}
