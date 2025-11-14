using Godot;
using System.Collections.Generic;

public partial class ReplicatedEntity : Node3D, IReplicatedEntity
{
	private int _networkId;
	private readonly List<ReplicatedProperty> _properties = new List<ReplicatedProperty>();
	private bool _registered;
	
	public int NetworkId => _networkId;
	
	public override void _Ready()
	{
		if (!_registered)
			Register();
	}
	
	public void Register()
	{
		if (_registered)
			return;
		
		_networkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
		_registered = true;
	}
	
	protected void AddProperty(ReplicatedProperty property)
	{
		_properties.Add(property);
	}
	
	public void WriteSnapshot(StreamPeerBuffer buffer)
	{
		byte dirtyMask = 0;
		var dirtyProps = new List<ReplicatedProperty>();
		
		for (int i = 0; i < _properties.Count && i < 8; i++)
		{
			var prop = _properties[i];
			if (prop.Mode == ReplicationMode.Always || prop.HasChanged())
			{
				dirtyMask |= (byte)(1 << i);
				dirtyProps.Add(prop);
			}
		}
		
		buffer.PutU8(dirtyMask);
		
		foreach (var prop in dirtyProps)
		{
			prop.Write(buffer);
		}
	}
	
	public void ReadSnapshot(StreamPeerBuffer buffer)
	{
		if (buffer.GetAvailableBytes() < 1)
			return;
		
		var dirtyMask = buffer.GetU8();
		
		for (int i = 0; i < _properties.Count && i < 8; i++)
		{
			if ((dirtyMask & (1 << i)) != 0)
			{
				_properties[i].Read(buffer);
			}
		}
	}
	
	public int GetSnapshotSizeBytes()
	{
		int size = 1;
		foreach (var prop in _properties)
		{
			if (prop.Mode == ReplicationMode.Always || prop.HasChanged())
			{
				size += prop.GetSizeBytes();
			}
		}
		return size;
	}
	
	public IReadOnlyList<ReplicatedProperty> GetProperties() => _properties;
}

