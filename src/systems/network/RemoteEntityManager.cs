using Godot;
using System.Collections.Generic;

public partial class RemoteEntityManager : Node
{
	private NetworkController _networkController;
	private readonly Dictionary<int, IReplicatedEntity> _remoteEntities = new Dictionary<int, IReplicatedEntity>();
	private PackedScene _playerScene;
	private PackedScene _vehicleScene;
	private const int PlayerEntityIdOffset = 3000;
	private const int VehicleEntityIdOffset = 2000;
	
	public override void _Ready()
	{
		_networkController = GetNode<NetworkController>("/root/NetworkController");
		if (_networkController == null)
		{
			GD.PushError("RemoteEntityManager: NetworkController not found");
			return;
		}
		
		_networkController.EntitySnapshotReceived += OnEntitySnapshotReceived;
		GD.Print("RemoteEntityManager: Ready");

		_playerScene = GD.Load<PackedScene>("res://src/entities/player/player_character.tscn");
		_vehicleScene = GD.Load<PackedScene>("res://src/entities/vehicle/car/player_car.tscn");
	}
	
	public void RegisterRemoteEntity(int entityId, IReplicatedEntity entity)
	{
		_remoteEntities[entityId] = entity;
		GD.Print($"RemoteEntityManager: Registered remote entity {entityId}");
	}
	
	public void UnregisterRemoteEntity(int entityId)
	{
		_remoteEntities.Remove(entityId);
		GD.Print($"RemoteEntityManager: Unregistered remote entity {entityId}");
	}
	
	private void OnEntitySnapshotReceived(int entityId, byte[] data)
	{
		if (!_remoteEntities.TryGetValue(entityId, out var entity))
		{
			entity = TrySpawnEntity(entityId);
			if (entity == null)
				return;
		}
		
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = data;
		
		entity.ReadSnapshot(buffer);
	}

	private IReplicatedEntity TrySpawnEntity(int entityId)
	{
		// Avoid spawning local-controlled entities
		var localPlayerId = _networkController != null && _networkController.ClientPeerId != 0
			? PlayerEntityIdOffset + _networkController.ClientPeerId
			: 0;
		if (localPlayerId != 0 && entityId == localPlayerId)
			return null;

		if (entityId >= PlayerEntityIdOffset && entityId < PlayerEntityIdOffset + 1000)
		{
			return SpawnRemotePlayer(entityId);
		}

		if (entityId >= VehicleEntityIdOffset && entityId < VehicleEntityIdOffset + 1000)
		{
			return SpawnRemoteVehicle(entityId);
		}

		return null;
	}

	private IReplicatedEntity SpawnRemotePlayer(int entityId)
	{
		if (_playerScene == null)
		{
			GD.PushWarning("RemoteEntityManager: Player scene missing, cannot spawn remote player.");
			return null;
		}

		var player = _playerScene.Instantiate<PlayerCharacter>();
		if (player == null)
			return null;

		player.AutoRegisterWithNetwork = false;
		player.Name = $"RemotePlayer_{entityId - PlayerEntityIdOffset}";
		player.ConfigureAuthority(false);
		player.SetCameraActive(false);
		player.SetWorldActive(true);
		player.SetNetworkId(entityId);
		AddChild(player);
		RegisterRemoteEntity(entityId, player);
		return player;
	}

	private IReplicatedEntity SpawnRemoteVehicle(int entityId)
	{
		if (_vehicleScene == null)
		{
			GD.PushWarning("RemoteEntityManager: Vehicle scene missing, cannot spawn remote vehicle.");
			return null;
		}

		var car = _vehicleScene.Instantiate<RaycastCar>();
		if (car == null)
			return null;

		car.RegistrationMode = RaycastCar.NetworkRegistrationMode.None;
		car.AutoRespawnOnReady = false;
		car.Name = $"Vehicle_{entityId - VehicleEntityIdOffset}";
		car.SetNetworkId(entityId);
		car.SetSimulationEnabled(false);
		AddChild(car);
		RegisterRemoteEntity(entityId, car);
		return car;
	}
}
