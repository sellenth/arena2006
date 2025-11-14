using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class EntityReplicationRegistry : Node
{
	private static EntityReplicationRegistry _instance;
	public static EntityReplicationRegistry Instance => _instance;
	
	private readonly Dictionary<int, IReplicatedEntity> _entities = new Dictionary<int, IReplicatedEntity>();
	private readonly Dictionary<Node, int> _nodeToId = new Dictionary<Node, int>();
	private int _nextEntityId = 1000;
	
	[Signal] public delegate void EntityRegisteredEventHandler(int entityId);
	[Signal] public delegate void EntityUnregisteredEventHandler(int entityId);
	
	public override void _Ready()
	{
		_instance = this;
	}
	
	public int RegisterEntity(IReplicatedEntity entity, Node node = null)
	{
		var id = entity.NetworkId;
		if (id == 0)
		{
			id = _nextEntityId++;
		}
		
		_entities[id] = entity;
		
		if (node != null)
		{
			_nodeToId[node] = id;
			node.TreeExiting += () => UnregisterEntity(id);
		}
		
		EmitSignal(SignalName.EntityRegistered, id);
		GD.Print($"EntityReplicationRegistry: Registered entity {id}");
		return id;
	}
	
	public void UnregisterEntity(int entityId)
	{
		if (!_entities.ContainsKey(entityId))
			return;
		
		_entities.Remove(entityId);
		
		var nodeEntry = _nodeToId.FirstOrDefault(kvp => kvp.Value == entityId);
		if (nodeEntry.Key != null)
			_nodeToId.Remove(nodeEntry.Key);
		
		EmitSignal(SignalName.EntityUnregistered, entityId);
		GD.Print($"EntityReplicationRegistry: Unregistered entity {entityId}");
	}
	
	public IReplicatedEntity GetEntity(int entityId)
	{
		return _entities.TryGetValue(entityId, out var entity) ? entity : null;
	}
	
	public IEnumerable<KeyValuePair<int, IReplicatedEntity>> GetAllEntities()
	{
		return _entities;
	}
	
	public int GetEntityCount() => _entities.Count;
}

