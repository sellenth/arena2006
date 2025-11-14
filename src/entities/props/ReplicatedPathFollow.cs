using Godot;

public partial class ReplicatedPathFollow : PathFollow3D, IReplicatedEntity
{
	[Export] public int NetworkId { get; set; } = 1001;
	[Export] public float Speed { get; set; } = 0.01f;
	
	private ReplicatedFloat _progressProperty;
	private bool _registered;
	private bool _isAuthority;
	
	public override void _Ready()
	{
		// Auto-detect if we're on server or client
		var networkController = GetNode<NetworkController>("/root/NetworkController");
		_isAuthority = networkController?.IsServer ?? false;
		
		_progressProperty = new ReplicatedFloat(
			"ProgressRatio",
			() => ProgressRatio,
			(value) => ProgressRatio = value,
			ReplicationMode.Always
		);
		
		if (_isAuthority)
		{
			// Server: Register with EntityReplicationRegistry
			NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? NetworkId;
			_registered = true;
			GD.Print($"ReplicatedPathFollow: Registered as SERVER authority with ID {NetworkId}");
		}
		else
		{
			// Client: Register with RemoteEntityManager
			var remoteManager = GetTree().CurrentScene?.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
			if (remoteManager != null)
			{
				remoteManager.RegisterRemoteEntity(NetworkId, this);
				GD.Print($"ReplicatedPathFollow: Registered as CLIENT remote with ID {NetworkId}");
			}
			else
			{
				GD.PushWarning("ReplicatedPathFollow: RemoteEntityManager not found in scene!");
			}
		}
	}
	
	public override void _PhysicsProcess(double delta)
	{
		if (_isAuthority)
		{
			ProgressRatio += Speed;
			if (ProgressRatio >= 1.0f)
				ProgressRatio = 0.0f;
		}
	}
	
	public void WriteSnapshot(StreamPeerBuffer buffer)
	{
		_progressProperty.Write(buffer);
	}
	
	public void ReadSnapshot(StreamPeerBuffer buffer)
	{
		_progressProperty.Read(buffer);
	}
	
	public int GetSnapshotSizeBytes()
	{
		return _progressProperty.GetSizeBytes();
	}
}

