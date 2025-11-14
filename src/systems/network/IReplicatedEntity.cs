using Godot;

public interface IReplicatedEntity
{
	int NetworkId { get; }
	
	void WriteSnapshot(StreamPeerBuffer buffer);
	
	void ReadSnapshot(StreamPeerBuffer buffer);
	
	int GetSnapshotSizeBytes();
}

