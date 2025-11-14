# Migration Guide: From Ad-hoc to Entity Replication System

## What Changed?

### Before: Ad-hoc Replication (PacketSceneState)

```csharp
// NetworkController.cs - Server
var pathFollow3d = root.FindChild("PathFollow3D", true, false) as PathFollow3D;
if (pathFollow3d != null) {
    pathFollow3d.ProgressRatio += 0.01f;
    BroadcastSceneState(pathFollow3d.ProgressRatio);
}

// NetworkSerializer.cs
public const byte PacketSceneState = 8;
public static byte[] SerializeSceneState(float progressRatio) { ... }
public static float DeserializeSceneState(byte[] packet) { ... }

// NetworkController.cs - Client
case NetworkSerializer.PacketSceneState:
    var progressRatio = NetworkSerializer.DeserializeSceneState(packet);
    if (progressRatio >= 0f)
        ApplySceneState(progressRatio);
    break;

private void ApplySceneState(float progressRatio)
{
    var root = GetTree().CurrentScene;
    var pathFollow3d = root?.FindChild("PathFollow3D", true, false) as PathFollow3D;
    if (pathFollow3d != null)
        pathFollow3d.ProgressRatio = progressRatio;
}
```

**Problems:**
- ❌ Hardcoded entity lookup by name
- ❌ Custom packet type per entity type
- ❌ Manual serialization for each property
- ❌ Doesn't scale to multiple entities
- ❌ No delta compression
- ❌ Tight coupling between NetworkController and entity types

### After: Entity Replication System

```csharp
// ReplicatedPathFollow.cs
public partial class ReplicatedPathFollow : PathFollow3D, IReplicatedEntity
{
    [Export] public int NetworkId { get; set; }
    [Export] public bool IsAuthority { get; set; } = false;
    
    private ReplicatedFloat _progressProperty;
    
    public override void _Ready()
    {
        _progressProperty = new ReplicatedFloat(
            "ProgressRatio",
            () => ProgressRatio,
            (value) => ProgressRatio = value
        );
        
        if (IsAuthority)
            NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
        else
            GetTree().Root.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager")
                ?.RegisterRemoteEntity(NetworkId, this);
    }
    
    public void WriteSnapshot(StreamPeerBuffer buffer) => _progressProperty.Write(buffer);
    public void ReadSnapshot(StreamPeerBuffer buffer) => _progressProperty.Read(buffer);
    public int GetSnapshotSizeBytes() => _progressProperty.GetSizeBytes();
}
```

**Benefits:**
- ✅ Self-contained entity logic
- ✅ One packet type for all entities (`PacketEntitySnapshot`)
- ✅ Automatic serialization
- ✅ Scales to hundreds of entities
- ✅ Built-in delta compression
- ✅ Zero coupling to NetworkController

## Migration Steps

### 1. Remove Old Code

**In NetworkController.cs:**

Remove this block from `ProcessServer()`:
```csharp
var root = GetTree().CurrentScene;
var pathFollow3d = root.FindChild("PathFollow3D", true, false) as PathFollow3D;
if (pathFollow3d != null) {
    pathFollow3d.ProgressRatio += 0.01f;
    BroadcastSceneState(pathFollow3d.ProgressRatio);
}
```

Remove this method:
```csharp
private void ApplySceneState(float progressRatio)
{
    var root = GetTree().CurrentScene;
    var pathFollow3d = root?.FindChild("PathFollow3D", true, false) as PathFollow3D;
    if (pathFollow3d != null)
        pathFollow3d.ProgressRatio = progressRatio;
}
```

Remove this case from `PollClientPackets()`:
```csharp
case NetworkSerializer.PacketSceneState:
    var progressRatio = NetworkSerializer.DeserializeSceneState(packet);
    if (progressRatio >= 0f)
        ApplySceneState(progressRatio);
    break;
```

**Optional:** Keep `PacketSceneState` for backward compatibility, or remove it entirely.

### 2. Add EntityReplicationRegistry to Autoload

In `project.godot`:
```ini
[autoload]
NetworkController="*res://src/systems/network/NetworkController.cs"
EntityReplicationRegistry="*res://src/systems/network/EntityReplicationRegistry.cs"
```

### 3. Add RemoteEntityManager to Scene

In your main scene (e.g., `game_root.tscn`), add a `RemoteEntityManager` node:

```gdscript
[node name="RemoteEntityManager" type="Node3D" parent="."]
script = ExtResource("path/to/RemoteEntityManager.cs")
```

### 4. Convert Entities

Replace your `PathFollow3D` node with `ReplicatedPathFollow`:

**Before (in .tscn):**
```gdscript
[node name="PathFollow3D" type="PathFollow3D" parent="Path3D2"]
```

**After:**
```gdscript
[node name="PathFollow3D" type="PathFollow3D" parent="Path3D2"]
script = ExtResource("path/to/ReplicatedPathFollow.cs")
NetworkId = 1001
IsAuthority = false
Speed = 0.01
```

**Important:** 
- On the **server**, set `IsAuthority = true`
- On **clients**, set `IsAuthority = false`
- Use the same `NetworkId` on both server and clients

### 5. Test

1. Start server: `godot --server`
2. Start client: `godot --client`
3. Verify the entity moves on both server and client

## Adding New Replicated Entities

### Example: Replicated Door

1. **Create the script:**

```csharp
public partial class ReplicatedDoor : Node3D, IReplicatedEntity
{
    [Export] public int NetworkId { get; set; } = 1002;
    [Export] public bool IsAuthority { get; set; } = false;
    
    private ReplicatedTransform3D _transformProperty;
    private ReplicatedInt _stateProperty;
    private int _doorState = 0;
    
    public override void _Ready()
    {
        _transformProperty = new ReplicatedTransform3D(
            "Transform",
            () => GlobalTransform,
            (value) => GlobalTransform = value
        );
        
        _stateProperty = new ReplicatedInt(
            "State",
            () => _doorState,
            (value) => _doorState = value,
            ReplicationMode.OnChange  // Only send when state changes!
        );
        
        if (IsAuthority)
            NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
        else
            GetTree().Root.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager")
                ?.RegisterRemoteEntity(NetworkId, this);
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
```

2. **Add to scene:**

```gdscript
[node name="Door" type="Node3D" parent="."]
script = ExtResource("path/to/ReplicatedDoor.cs")
NetworkId = 1002
IsAuthority = false
```

3. **Done!** The entity will automatically replicate.

## Network ID Management

### Static IDs (Recommended for World Props)

Assign IDs manually in the scene:
- Skybox: 1001
- Door 1: 1002
- Door 2: 1003
- Platform 1: 1004

**Pros:**
- Simple
- No synchronization needed
- Works for static world entities

**Cons:**
- Must manually track IDs
- Risk of collisions

### Dynamic IDs (For Spawned Entities)

Let the registry assign IDs:
```csharp
NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
```

**Pros:**
- No collision risk
- Automatic management

**Cons:**
- Server must send ID to clients
- Requires spawn synchronization

## Performance Tips

### Use OnChange for Infrequent Updates

```csharp
// Door state rarely changes
_stateProperty = new ReplicatedInt(
    "State",
    () => _doorState,
    (value) => _doorState = value,
    ReplicationMode.OnChange  // Only 1 byte when unchanged!
);
```

### Use Always for Continuous Motion

```csharp
// Platform is always moving
_transformProperty = new ReplicatedTransform3D(
    "Transform",
    () => GlobalTransform,
    (value) => GlobalTransform = value,
    ReplicationMode.Always  // Send every tick
);
```

### Bandwidth Calculation

At 60 Hz tick rate:
- **Transform (Always)**: 28 bytes × 60 = 1.68 KB/s
- **Int (OnChange, unchanged)**: 1 byte × 60 = 60 bytes/s
- **Int (OnChange, changed)**: 5 bytes × 1 = 5 bytes (one-time)

## Troubleshooting

### Entity not replicating

1. Check `EntityReplicationRegistry` is in autoload
2. Check `RemoteEntityManager` is in scene (clients only)
3. Verify `NetworkId` matches on server and client
4. Verify `IsAuthority = true` on server, `false` on client

### NetworkId conflicts

Use unique IDs:
```csharp
// Reserve ranges
// 1000-1999: World props
// 2000-2999: Doors
// 3000-3999: Platforms
// 4000+: Dynamic entities
```

### Performance issues

1. Use `OnChange` mode where possible
2. Reduce update frequency for distant entities (future feature)
3. Use relevance filtering (future feature)

## Next Steps

1. Convert remaining entities (doors, platforms, pickups)
2. Implement relevance filtering
3. Add interpolation buffers
4. Add quantization for position data
5. Implement priority system

## See Also

- [REPLICATION_SYSTEM.md](REPLICATION_SYSTEM.md) - Full system documentation
- [ARCHITECTURE.md](../ARCHITECTURE.md) - Overall architecture

