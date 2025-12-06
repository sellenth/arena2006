using Godot;

public partial class ReplicatedDoor : Node3D, IReplicatedEntity
{
	[Export] public int NetworkId { get; set; }
	[Export] public bool IsAuthority { get; set; } = false;
	[Export] public Vector3 ClosedPosition { get; set; } = Vector3.Zero;
	[Export] public Vector3 OpenPosition { get; set; } = new Vector3(0, 3, 0);
	[Export] public float OpenSpeed { get; set; } = 2.0f;
	
	private ReplicatedTransform3D _transformProperty;
	private ReplicatedInt _stateProperty;
	private int _doorState = 0;
	private float _openProgress = 0.0f;
	
	public override void _Ready()
	{
		_transformProperty = new ReplicatedTransform3D(
			"Transform",
			() => GlobalTransform,
			(value) => GlobalTransform = value,
			ReplicationMode.Always
		);
		
		_stateProperty = new ReplicatedInt(
			"State",
			() => _doorState,
			(value) => {
				_doorState = value;
				GD.Print($"Door state changed to: {_doorState}");
			},
			ReplicationMode.OnChange
		);
		
		if (IsAuthority)
		{
			NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
			GD.Print($"ReplicatedDoor: Registered as authority with ID {NetworkId}");
		}
		else
		{
			var remoteManager = GetTree().Root.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
			if (remoteManager != null)
			{
				remoteManager.RegisterRemoteEntity(NetworkId, this);
				GD.Print($"ReplicatedDoor: Registered as remote with ID {NetworkId}");
			}
		}
	}
	
	public override void _PhysicsProcess(double delta)
	{
		if (IsAuthority)
		{
			switch (_doorState)
			{
				case 0:
					_openProgress = Mathf.Max(0, _openProgress - (float)delta * OpenSpeed);
					if (_openProgress <= 0)
						_doorState = 0;
					break;
				case 1:
					_openProgress = Mathf.Min(1, _openProgress + (float)delta * OpenSpeed);
					if (_openProgress >= 1)
						_doorState = 2;
					break;
				case 2:
					break;
			}
			
			GlobalPosition = ClosedPosition.Lerp(OpenPosition, _openProgress);
		}
	}
	
	public void Open()
	{
		if (IsAuthority && _doorState != 2)
			_doorState = 1;
	}
	
	public void Close()
	{
		if (IsAuthority && _doorState != 0)
			_doorState = 0;
	}
	
	public void WriteSnapshot(StreamPeerBuffer buffer)
	{
		_transformProperty.Write(buffer);
		_stateProperty.Write(buffer);
	}
	
	public void ReadSnapshot(StreamPeerBuffer buffer)
	{
		_transformProperty.Read(buffer);
		_stateProperty.Read(buffer);
	}
	
	public int GetSnapshotSizeBytes()
	{
		return _transformProperty.GetSizeBytes() + _stateProperty.GetSizeBytes();
	}
}

