using Godot;
using System.Collections.Generic;

public partial class RemoteEntityManager : Node
{
	private NetworkController _networkController;
	private readonly Dictionary<int, IReplicatedEntity> _remoteEntities = new Dictionary<int, IReplicatedEntity>();
	
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
			return;
		
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = data;
		
		entity.ReadSnapshot(buffer);
	}
}

