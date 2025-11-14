# Entity Replication System - Summary

## What is it?

A general-purpose, data-driven entity replication system for synchronizing game objects across the network. Inspired by Quake 3's delta compression and Source Engine's networked variables.

## Key Concepts

### 1. Replicated Properties

Properties define **what** to replicate and **how**:

```csharp
var property = new ReplicatedFloat(
    "ProgressRatio",           // Property name
    () => ProgressRatio,       // Getter
    (value) => ProgressRatio = value,  // Setter
    ReplicationMode.Always     // When to send
);
```

**Available Types:**
- `ReplicatedFloat` - 4 bytes
- `ReplicatedInt` - 4 bytes
- `ReplicatedVector3` - 12 bytes
- `ReplicatedTransform3D` - 28 bytes (position + quaternion)

**Replication Modes:**
- `Always` - Send every tick (for continuous motion)
- `OnChange` - Only send when changed (for infrequent updates)

### 2. IReplicatedEntity Interface

Entities implement this interface:

```csharp
public interface IReplicatedEntity
{
    int NetworkId { get; }
    void WriteSnapshot(StreamPeerBuffer buffer);
    void ReadSnapshot(StreamPeerBuffer buffer);
    int GetSnapshotSizeBytes();
}
```

### 3. EntityReplicationRegistry

Central registry that tracks all replicated entities:

```csharp
// Server: Register entity
NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;

// Client: Register remote entity
remoteManager.RegisterRemoteEntity(NetworkId, this);
```

### 4. Automatic Serialization

The system handles all serialization automatically:

```csharp
// Server: NetworkController calls this every tick
entity.WriteSnapshot(buffer);

// Client: NetworkController calls this when packet arrives
entity.ReadSnapshot(buffer);
```

## Quick Start

### 1. Create a Replicated Entity

```csharp
using Godot;

public partial class MyReplicatedProp : Node3D, IReplicatedEntity
{
    [Export] public int NetworkId { get; set; } = 1001;
    [Export] public bool IsAuthority { get; set; } = false;
    
    private ReplicatedTransform3D _transformProperty;
    
    public override void _Ready()
    {
        _transformProperty = new ReplicatedTransform3D(
            "Transform",
            () => GlobalTransform,
            (value) => GlobalTransform = value
        );
        
        if (IsAuthority)
            NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
        else
            GetTree().Root.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager")
                ?.RegisterRemoteEntity(NetworkId, this);
    }
    
    public void WriteSnapshot(StreamPeerBuffer buffer) => _transformProperty.Write(buffer);
    public void ReadSnapshot(StreamPeerBuffer buffer) => _transformProperty.Read(buffer);
    public int GetSnapshotSizeBytes() => _transformProperty.GetSizeBytes();
}
```

### 2. Add to Scene

```gdscript
[node name="MyProp" type="Node3D" parent="."]
script = ExtResource("path/to/MyReplicatedProp.cs")
NetworkId = 1001
IsAuthority = false  # Set to true on server
```

### 3. Done!

The entity will automatically replicate across the network.

## Architecture Flow

```
Server:
  1. Entity updates its state
  2. NetworkController.BroadcastEntitySnapshots() called every tick
  3. For each entity in EntityReplicationRegistry:
     - Call entity.WriteSnapshot(buffer)
     - Serialize entity ID + snapshot data
  4. Broadcast packet to all clients

Client:
  1. Receive PacketEntitySnapshot
  2. NetworkController emits EntitySnapshotReceived signal
  3. RemoteEntityManager receives signal
  4. Look up entity by NetworkId
  5. Call entity.ReadSnapshot(buffer)
  6. Entity applies state
```

## Delta Compression

The system automatically tracks which properties changed:

```csharp
// Only 1 byte sent if no properties changed
byte dirtyMask = 0b00000000;

// If property 0 changed: 0b00000001
// If property 1 changed: 0b00000010
// If both changed:       0b00000011
```

**Example:**
- 8 properties, only 1 changed
- Without delta: 8 × 4 bytes = 32 bytes
- With delta: 1 byte (mask) + 4 bytes (property) = 5 bytes
- **84% bandwidth savings!**

## Comparison to Alternatives

### vs. Godot's MultiplayerSynchronizer

| Feature | Entity Replication | MultiplayerSynchronizer |
|---------|-------------------|------------------------|
| Protocol | UDP (custom) | ENet/WebRTC |
| Delta Compression | ✅ Built-in | ❌ No |
| Property Types | Strongly typed | Variant-based |
| Scalability | Hundreds of entities | Dozens |
| Control | Full control | Limited |
| Complexity | Medium | Low |

### vs. Manual Serialization

| Feature | Entity Replication | Manual |
|---------|-------------------|--------|
| Boilerplate | Low | High |
| Type Safety | ✅ Yes | ⚠️ Manual |
| Delta Compression | ✅ Automatic | ❌ Manual |
| Maintainability | High | Low |
| Flexibility | Medium | High |

## Performance

### Bandwidth (60 Hz tick rate)

**Always Mode:**
- Transform: 28 bytes × 60 = 1.68 KB/s per entity
- Float: 4 bytes × 60 = 240 bytes/s per entity

**OnChange Mode (unchanged):**
- Any property: 1 byte × 60 = 60 bytes/s per entity

**Example: 10 moving platforms**
- 10 × 1.68 KB/s = 16.8 KB/s
- With 4 clients: 67.2 KB/s total
- **Easily handles hundreds of entities**

### CPU

- Minimal overhead
- O(n) where n = number of entities
- Serialization is simple memcpy operations
- No reflection or dynamic dispatch

## Limitations

1. **Max 8 properties per entity** (due to 1-byte dirty mask)
   - Solution: Use multiple entities or extend to 2-byte mask
2. **No automatic interpolation** (yet)
   - Solution: Add interpolation buffer (future feature)
3. **No relevance filtering** (yet)
   - Solution: Add spatial partitioning (future feature)
4. **Static NetworkIds** (for now)
   - Solution: Add dynamic ID assignment (future feature)

## Future Enhancements

1. **Interpolation Buffers** - Smooth out network jitter
2. **Relevance Filtering** - Only replicate nearby entities
3. **Priority System** - Send important entities more frequently
4. **Quantization** - Compress floats to 16-bit
5. **Baseline Snapshots** - Per-client delta compression
6. **Custom Property Types** - Quaternions, Colors, etc.
7. **Dynamic ID Assignment** - Server assigns IDs on spawn

## Examples Included

1. **ReplicatedPathFollow** - Moving skybox prop
2. **ReplicatedPlatform** - Moving platform
3. **ReplicatedDoor** - Door with state machine

See `docs/REPLICATION_SYSTEM.md` for full documentation.

## Credits

Inspired by:
- **Quake 3** (id Software) - Delta compression, entity snapshots
- **Source Engine** (Valve) - Networked variables, data tables
- **Unreal Engine** (Epic) - Property replication system
- **Glenn Fiedler** - Networking articles (gafferongames.com)

