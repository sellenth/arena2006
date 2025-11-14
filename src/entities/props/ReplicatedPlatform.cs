using Godot;

public partial class ReplicatedPlatform : Node3D, IReplicatedEntity
{
	[Export] public int NetworkId { get; set; }
	[Export] public bool IsAuthority { get; set; } = false;
	[Export] public Vector3 StartPosition { get; set; } = Vector3.Zero;
	[Export] public Vector3 EndPosition { get; set; } = new Vector3(0, 0, 10);
	[Export] public float Speed { get; set; } = 2.0f;
	
	private ReplicatedTransform3D _transformProperty;
	private float _time = 0.0f;
	
	public override void _Ready()
	{
		_transformProperty = new ReplicatedTransform3D(
			"Transform",
			() => GlobalTransform,
			(value) => GlobalTransform = value,
			ReplicationMode.Always,
			positionThreshold: 0.01f,
			rotationThreshold: 0.01f
		);
		
		if (IsAuthority)
		{
			NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
			GD.Print($"ReplicatedPlatform: Registered as authority with ID {NetworkId}");
		}
		else
		{
			var remoteManager = GetTree().Root.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
			if (remoteManager != null)
			{
				remoteManager.RegisterRemoteEntity(NetworkId, this);
				GD.Print($"ReplicatedPlatform: Registered as remote with ID {NetworkId}");
			}
		}
	}
	
	public override void _PhysicsProcess(double delta)
	{
		if (IsAuthority)
		{
			_time += (float)delta * Speed;
			var t = (Mathf.Sin(_time) + 1.0f) / 2.0f;
			var newPos = StartPosition.Lerp(EndPosition, t);
			GlobalPosition = newPos;
		}
	}
	
	public void WriteSnapshot(StreamPeerBuffer buffer)
	{
		_transformProperty.Write(buffer);
	}
	
	public void ReadSnapshot(StreamPeerBuffer buffer)
	{
		_transformProperty.Read(buffer);
	}
	
	public int GetSnapshotSizeBytes()
	{
		return _transformProperty.GetSizeBytes();
	}
}

