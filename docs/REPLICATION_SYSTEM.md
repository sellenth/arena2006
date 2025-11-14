# Entity Replication System

## Overview

This is a general-purpose, data-driven entity replication system inspired by id Software's Quake 3 and Valve's Source Engine networking architectures.

### Key Features

1. **Declarative Property Registration**: Define what to replicate, not how
2. **Delta Compression**: Only send changed properties (OnChange mode)
3. **Type-Safe Properties**: Strongly-typed property wrappers (Float, Vector3, Transform3D, Int)
4. **Automatic Serialization**: Properties handle their own serialization
5. **Centralized Registry**: Single source of truth for all replicated entities
6. **Flexible**: Works with any Node3D-based entity or custom classes

## Architecture

### Core Components

```
IReplicatedEntity (interface)
    ↓ implemented by
ReplicatedEntity (base class) or custom implementations
    ↓ contains
ReplicatedProperty (abstract)
    ↓ subclasses
ReplicatedFloat, ReplicatedVector3, ReplicatedTransform3D, ReplicatedInt
    ↓ registered with
EntityReplicationRegistry (singleton)
    ↓ used by
NetworkController (broadcasts snapshots)
    ↓ received by
RemoteEntityManager (applies snapshots to remote entities)
```

### Replication Modes

- **Always**: Property is sent every tick (for frequently changing values)
- **OnChange**: Property is only sent when it changes (for infrequent updates)

## Usage Examples

### Example 1: Simple Moving Platform

```csharp
using Godot;

public partial class ReplicatedPlatform : Node3D, IReplicatedEntity
{
    [Export] public int NetworkId { get; set; }
    [Export] public bool IsAuthority { get; set; } = false;
    
    private ReplicatedTransform3D _transformProperty;
    
    public override void _Ready()
    {
        _transformProperty = new ReplicatedTransform3D(
            "Transform",
            () => GlobalTransform,
            (value) => GlobalTransform = value,
            ReplicationMode.Always  // Always send because it's constantly moving
        );
        
        if (IsAuthority)
        {
            NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
        }
        else
        {
            var remoteManager = GetTree().Root.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
            remoteManager?.RegisterRemoteEntity(NetworkId, this);
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
    
    public int GetSnapshotSizeBytes() => _transformProperty.GetSizeBytes();
}
```

### Example 2: Door with State

```csharp
using Godot;

public partial class ReplicatedDoor : Node3D, IReplicatedEntity
{
    [Export] public int NetworkId { get; set; }
    [Export] public bool IsAuthority { get; set; } = false;
    
    private ReplicatedTransform3D _transformProperty;
    private ReplicatedInt _stateProperty;
    private int _doorState = 0; // 0 = closed, 1 = opening, 2 = open, 3 = closing
    
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
            (value) => _doorState = value,
            ReplicationMode.OnChange  // Only send when door state changes
        );
        
        if (IsAuthority)
        {
            NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
        }
        else
        {
            var remoteManager = GetTree().Root.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
            remoteManager?.RegisterRemoteEntity(NetworkId, this);
        }
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
        return _transformProperty.GetSnapshotSizeBytes() + _stateProperty.GetSnapshotSizeBytes();
    }
}
```

### Example 3: Using ReplicatedEntity Base Class

For simpler cases, inherit from `ReplicatedEntity`:

```csharp
using Godot;

public partial class SimpleReplicatedProp : ReplicatedEntity
{
    [Export] public bool IsAuthority { get; set; } = false;
    
    private Vector3 _customData;
    
    public override void _Ready()
    {
        // Add properties before registering
        AddProperty(new ReplicatedTransform3D(
            "Transform",
            () => GlobalTransform,
            (value) => GlobalTransform = value
        ));
        
        AddProperty(new ReplicatedVector3(
            "CustomData",
            () => _customData,
            (value) => _customData = value,
            ReplicationMode.OnChange
        ));
        
        base._Ready();  // This registers with the registry
    }
}
```

## Advanced: Delta Compression with Dirty Flags

The system automatically tracks which properties have changed:

```csharp
public void WriteSnapshot(StreamPeerBuffer buffer)
{
    byte dirtyMask = 0;
    var dirtyProps = new List<ReplicatedProperty>();
    
    // Check which properties have changed
    for (int i = 0; i < _properties.Count && i < 8; i++)
    {
        var prop = _properties[i];
        if (prop.Mode == ReplicationMode.Always || prop.HasChanged())
        {
            dirtyMask |= (byte)(1 << i);
            dirtyProps.Add(prop);
        }
    }
    
    buffer.PutU8(dirtyMask);  // 1 byte for up to 8 properties
    
    // Only write dirty properties
    foreach (var prop in dirtyProps)
    {
        prop.Write(buffer);
    }
}
```

This means if you have 8 properties but only 1 changed, you only send:
- 1 byte (dirty mask)
- N bytes (the changed property)

Instead of sending all 8 properties every frame.

## Network Flow

### Server Side

1. Entity implements `IReplicatedEntity`
2. Entity registers with `EntityReplicationRegistry` (authority)
3. `NetworkController.BroadcastEntitySnapshots()` is called every physics tick
4. For each registered entity:
   - Call `entity.WriteSnapshot(buffer)`
   - Serialize entity ID + snapshot data
5. Broadcast packet to all clients

### Client Side

1. Client receives `PacketEntitySnapshot`
2. `NetworkController` emits `EntitySnapshotReceived` signal
3. `RemoteEntityManager` receives signal
4. Looks up entity by ID in local registry
5. Calls `entity.ReadSnapshot(buffer)` to apply state

## Setup Instructions

### 1. Add EntityReplicationRegistry to Autoload

In `project.godot`:

```
[autoload]
EntityReplicationRegistry="*res://src/systems/network/EntityReplicationRegistry.cs"
```

### 2. Add RemoteEntityManager to Scene (Client Only)

In your main scene, add a `RemoteEntityManager` node. This is typically done in the scene root.

### 3. Mark Entities as Authority or Remote

- **Server**: Set `IsAuthority = true` on entities
- **Client**: Set `IsAuthority = false` and assign matching `NetworkId`

### 4. Ensure Matching NetworkIds

The server and client must agree on NetworkIds. Options:
- **Static IDs**: Hardcode in scene (e.g., skybox = 1001)
- **Dynamic IDs**: Server assigns and sends in welcome packet
- **Hybrid**: Static for world props, dynamic for spawned entities

## Performance Considerations

### Bandwidth

- **Always mode**: ~30-60 bytes per entity per tick (depending on properties)
- **OnChange mode**: 1 byte (dirty mask) when nothing changes
- **60 Hz tick rate**: 1.8-3.6 KB/s per entity (Always mode)

### Optimization Tips

1. **Use OnChange for infrequent updates**: Door states, health, ammo
2. **Use Always for continuous motion**: Moving platforms, projectiles
3. **Batch entities**: System already batches all entities into one packet
4. **Relevance filtering** (future): Only send entities near the player
5. **Update rate scaling** (future): Send less important entities at lower rates

## Comparison to Old System

### Old Approach (PacketSceneState)

```csharp
// Server
pathFollow3d.ProgressRatio += 0.01f;
BroadcastSceneState(pathFollow3d.ProgressRatio);

// Custom packet type per entity type
public const byte PacketSceneState = 8;

// Custom serialization
public static byte[] SerializeSceneState(float progressRatio) { ... }

// Client
var progressRatio = NetworkSerializer.DeserializeSceneState(packet);
pathFollow3d.ProgressRatio = progressRatio;
```

**Problems:**
- New packet type for each entity type
- Manual serialization code
- No delta compression
- Doesn't scale

### New Approach (Entity Replication)

```csharp
// Server
public partial class ReplicatedPathFollow : PathFollow3D, IReplicatedEntity
{
    // Properties define themselves
    private ReplicatedFloat _progressProperty;
    
    // Automatic registration
    NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
}

// No custom packet types needed
// No manual serialization
// Automatic delta compression
// Scales to hundreds of entities
```

## Future Enhancements

1. **Priority/Relevance System**: Only replicate entities near players
2. **Interpolation Buffers**: Smooth out network jitter
3. **Extrapolation**: Predict entity movement between updates
4. **Baseline Snapshots**: Per-client delta compression
5. **Quantization**: Compress floats to 16-bit for position data
6. **Custom Property Types**: Quaternions, Colors, etc.

## Debugging

Enable verbose logging:

```csharp
GD.Print($"Entity {entityId} snapshot size: {entity.GetSnapshotSizeBytes()} bytes");
```

Check registry contents:

```csharp
GD.Print($"Registered entities: {EntityReplicationRegistry.Instance.GetEntityCount()}");
```

## Credits

Inspired by:
- **Quake 3**: Delta compression, entity snapshots
- **Source Engine**: Networked variables, data tables
- **Unreal Engine**: Property replication system

