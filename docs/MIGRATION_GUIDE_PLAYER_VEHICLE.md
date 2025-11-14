# Migration Guide: Player and Vehicle to Entity Replication System

## Overview

This guide shows how to migrate `PlayerCharacter.cs` and `RaycastCar.cs` from the current ad-hoc replication approach to the new unified Entity Replication System. The migration maintains backward compatibility while providing a cleaner, more scalable architecture.

---

## Current Architecture (Before Migration)

### RaycastCar.cs
```csharp
// Registration with NetworkController
network.RegisterLocalPlayerCar(this);
network.RegisterAuthoritativeVehicle(this);

// Custom snapshot system
public CarSnapshot CaptureSnapshot(int tick);
public void QueueSnapshot(CarSnapshot snapshot);
public void ApplyRemoteSnapshot(VehicleStateSnapshot snapshot, float blend);

// NetworkController directly manages cars
```

### PlayerCharacter.cs
```csharp
// Registration with NetworkController
_networkController.RegisterPlayerCharacter(this);

// Custom snapshot system
public PlayerSnapshot CaptureSnapshot(int tick);
public void QueueSnapshot(PlayerSnapshot snapshot);

// NetworkController directly manages players
```

### Problems
- ❌ Tight coupling to `NetworkController`
- ❌ Separate snapshot systems for each entity type
- ❌ Manual serialization in `NetworkSerializer`
- ❌ Different replication logic than props/world objects
- ❌ Hard to add new replicated properties

---

## New Architecture (After Migration)

### Key Changes
1. **Implement `IReplicatedEntity`** interface
2. **Use `ReplicatedProperty`** classes for state
3. **Register with `EntityReplicationRegistry`** (authority) or `RemoteEntityManager`** (clients)
4. **Keep existing functionality** (reconciliation, input handling, etc.)

---

## Migration: RaycastCar.cs

### Step 1: Add IReplicatedEntity Interface

```csharp
public partial class RaycastCar : RigidBody3D, IReplicatedEntity
{
    // Add these fields
    [Export] public int NetworkId { get; set; } = 0;
    
    private ReplicatedTransform3D _transformProperty;
    private ReplicatedVector3 _linearVelocityProperty;
    private ReplicatedVector3 _angularVelocityProperty;
    
    // Existing fields...
    public int TotalWheels { get; private set; }
    public float MotorInput { get; set; } = 0.0f;
    // ... rest of fields
}
```

### Step 2: Initialize Replicated Properties in _Ready()

```csharp
public override void _Ready()
{
    TotalWheels = Wheels.Count;
    _camera = GetNodeOrNull<Camera3D>(CameraPath);

    // Initialize replicated properties
    InitializeReplication();

    var network = GetTree().Root.GetNodeOrNull<NetworkController>("/root/NetworkController");

    switch (RegistrationMode)
    {
        case NetworkRegistrationMode.LocalPlayer:
            if (network != null && network.IsClient)
            {
                // NEW: Register with Entity Replication System
                RegisterAsLocalVehicle();
                
                // KEEP: Still register with NetworkController for input handling
                network.RegisterLocalPlayerCar(this);
                _isNetworked = true;
            }
            break;
        case NetworkRegistrationMode.AuthoritativeVehicle:
            if (network != null && network.IsServer)
            {
                // NEW: Register with Entity Replication System
                RegisterAsAuthority();
                
                // KEEP: Still register with NetworkController for legacy support
                network.RegisterAuthoritativeVehicle(this);
                _isNetworked = true;
            }
            break;
        default:
            _simulateLocally = false;
            break;
    }

    if (AutoRespawnOnReady && _simulateLocally)
        RespawnAtManagedPoint();

    ApplySimulationMode();
}
```

### Step 3: Add Helper Methods

```csharp
private void InitializeReplication()
{
    // Transform with thresholds to avoid jitter
    _transformProperty = new ReplicatedTransform3D(
        "Transform",
        () => GlobalTransform,
        (value) => ApplyTransformFromReplication(value),
        ReplicationMode.Always,
        positionThreshold: 0.05f,
        rotationThreshold: Mathf.DegToRad(2.0f)
    );
    
    // Linear velocity
    _linearVelocityProperty = new ReplicatedVector3(
        "LinearVelocity",
        () => LinearVelocity,
        (value) => LinearVelocity = value,
        ReplicationMode.Always
    );
    
    // Angular velocity
    _angularVelocityProperty = new ReplicatedVector3(
        "AngularVelocity",
        () => AngularVelocity,
        (value) => AngularVelocity = value,
        ReplicationMode.Always
    );
}

private void RegisterAsAuthority()
{
    // Server: Register with EntityReplicationRegistry
    NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? 0;
    GD.Print($"RaycastCar: Registered as authority with NetworkId {NetworkId}");
}

private void RegisterAsLocalVehicle()
{
    // Client: Register with RemoteEntityManager for receiving snapshots
    var remoteManager = GetTree().CurrentScene?.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
    if (remoteManager != null)
    {
        remoteManager.RegisterRemoteEntity(NetworkId, this);
        GD.Print($"RaycastCar: Registered as remote with NetworkId {NetworkId}");
    }
    else
    {
        GD.PushWarning("RaycastCar: RemoteEntityManager not found in scene!");
    }
}

private void ApplyTransformFromReplication(Transform3D value)
{
    // Blend with existing snapshot system if needed
    if (_pendingSnapshot != null)
    {
        // Use existing reconciliation system
        var snapshot = new CarSnapshot
        {
            Transform = value,
            LinearVelocity = _linearVelocityProperty._getter(),
            AngularVelocity = _angularVelocityProperty._getter()
        };
        QueueSnapshot(snapshot);
    }
    else
    {
        // Direct application for remote vehicles
        GlobalTransform = value;
    }
}
```

### Step 4: Implement IReplicatedEntity Methods

```csharp
// IReplicatedEntity implementation
public void WriteSnapshot(StreamPeerBuffer buffer)
{
    _transformProperty.Write(buffer);
    _linearVelocityProperty.Write(buffer);
    _angularVelocityProperty.Write(buffer);
}

public void ReadSnapshot(StreamPeerBuffer buffer)
{
    _transformProperty.Read(buffer);
    _linearVelocityProperty.Read(buffer);
    _angularVelocityProperty.Read(buffer);
}

public int GetSnapshotSizeBytes()
{
    return _transformProperty.GetSizeBytes() 
         + _linearVelocityProperty.GetSizeBytes() 
         + _angularVelocityProperty.GetSizeBytes();
}
```

### Step 5: Update ApplyRemoteSnapshot (Optional - For Backward Compatibility)

```csharp
public void ApplyRemoteSnapshot(VehicleStateSnapshot snapshot, float blend = 0.35f)
{
    if (snapshot == null)
        return;

    // Option A: Keep legacy snapshot system for now
    if (_simulateLocally)
    {
        QueueSnapshot(snapshot.ToCarSnapshot());
        return;
    }

    // Option B: Use new replication properties
    var appliedBlend = Mathf.Clamp(blend, 0.01f, 1.0f);
    var current = GlobalTransform;
    var blendedOrigin = current.Origin.Lerp(snapshot.Transform.Origin, appliedBlend);
    var currentQuat = current.Basis.GetRotationQuaternion();
    var targetQuat = snapshot.Transform.Basis.GetRotationQuaternion();
    var blendedQuat = currentQuat.Slerp(targetQuat, appliedBlend);
    GlobalTransform = new Transform3D(new Basis(blendedQuat), blendedOrigin);
    LinearVelocity = LinearVelocity.Lerp(snapshot.LinearVelocity, appliedBlend);
    AngularVelocity = AngularVelocity.Lerp(snapshot.AngularVelocity, appliedBlend);
}
```

### Summary: RaycastCar Changes
- ✅ Add `IReplicatedEntity` interface
- ✅ Add `NetworkId` export property
- ✅ Initialize replicated properties in `_Ready()`
- ✅ Register with `EntityReplicationRegistry` (server) or `RemoteEntityManager` (client)
- ✅ Implement `WriteSnapshot()`, `ReadSnapshot()`, `GetSnapshotSizeBytes()`
- ✅ Keep existing snapshot system for reconciliation
- ✅ Keep existing `NetworkController` registration for input handling

---

## Migration: PlayerCharacter.cs

### Step 1: Add IReplicatedEntity Interface

```csharp
public partial class PlayerCharacter : CharacterBody3D, IReplicatedEntity
{
    // Add these fields
    [Export] public int NetworkId { get; set; } = 0;
    
    private ReplicatedTransform3D _transformProperty;
    private ReplicatedVector3 _velocityProperty;
    private ReplicatedFloat _viewYawProperty;
    private ReplicatedFloat _viewPitchProperty;
    
    // Existing fields...
    private Node3D _head;
    private Camera3D _camera;
    // ... rest of fields
}
```

### Step 2: Initialize Replicated Properties in _Ready()

```csharp
public override void _Ready()
{
    _head = GetNodeOrNull<Node3D>(HeadPath);
    _camera = GetNodeOrNull<Camera3D>(CameraPath);
    _mesh = GetNodeOrNull<MeshInstance3D>(MeshPath);
    _collisionShape = GetNodeOrNull<CollisionShape3D>(CollisionShapePath);

    if (_head == null || _camera == null)
    {
        Debug.Assert(false, "head or camera not found :o");
    }

    InitializeControllerModules();
    
    // NEW: Initialize replication
    InitializeReplication();

    if (AutoRegisterWithNetwork)
    {
        _networkController = GetNodeOrNull<NetworkController>("/root/NetworkController");
        if (_networkController != null && _networkController.IsClient)
        {
            // NEW: Register with Entity Replication System
            RegisterAsLocalPlayer();
            
            // KEEP: Still register with NetworkController for input handling
            _networkController.RegisterPlayerCharacter(this);
        }
    }

    _managesMouseMode = AutoRegisterWithNetwork && (_networkController == null || _networkController.IsClient);

    ApplyColor(_playerColor);
}
```

### Step 3: Add Helper Methods

```csharp
private void InitializeReplication()
{
    _transformProperty = new ReplicatedTransform3D(
        "Transform",
        () => GlobalTransform,
        (value) => ApplyTransformFromReplication(value),
        ReplicationMode.Always,
        positionThreshold: 0.01f,
        rotationThreshold: Mathf.DegToRad(1.0f)
    );
    
    _velocityProperty = new ReplicatedVector3(
        "Velocity",
        () => Velocity,
        (value) => Velocity = value,
        ReplicationMode.Always
    );
    
    _viewYawProperty = new ReplicatedFloat(
        "ViewYaw",
        () => _lookController?.Yaw ?? 0f,
        (value) => SetViewYaw(value),
        ReplicationMode.Always
    );
    
    _viewPitchProperty = new ReplicatedFloat(
        "ViewPitch",
        () => _lookController?.Pitch ?? 0f,
        (value) => SetViewPitch(value),
        ReplicationMode.Always
    );
}

private void RegisterAsLocalPlayer()
{
    // Client: Register with RemoteEntityManager for receiving snapshots
    var remoteManager = GetTree().CurrentScene?.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
    if (remoteManager != null)
    {
        remoteManager.RegisterRemoteEntity(NetworkId, this);
        GD.Print($"PlayerCharacter: Registered as remote with NetworkId {NetworkId}");
    }
    else
    {
        GD.PushWarning("PlayerCharacter: RemoteEntityManager not found in scene!");
    }
}

private void ApplyTransformFromReplication(Transform3D value)
{
    // Use existing reconciliation system
    var snapshot = new PlayerSnapshot
    {
        Transform = value,
        Velocity = _velocityProperty._getter(),
        ViewYaw = _viewYawProperty._getter(),
        ViewPitch = _viewPitchProperty._getter()
    };
    QueueSnapshot(snapshot);
}

private void SetViewYaw(float yaw)
{
    if (_lookController != null && !_isAuthority)
    {
        _lookController.SetYawPitch(yaw, _lookController.Pitch);
    }
}

private void SetViewPitch(float pitch)
{
    if (_lookController != null && !_isAuthority)
    {
        _lookController.SetYawPitch(_lookController.Yaw, pitch);
    }
}
```

### Step 4: Implement IReplicatedEntity Methods

```csharp
// IReplicatedEntity implementation
public void WriteSnapshot(StreamPeerBuffer buffer)
{
    _transformProperty.Write(buffer);
    _velocityProperty.Write(buffer);
    _viewYawProperty.Write(buffer);
    _viewPitchProperty.Write(buffer);
}

public void ReadSnapshot(StreamPeerBuffer buffer)
{
    _transformProperty.Read(buffer);
    _velocityProperty.Read(buffer);
    _viewYawProperty.Read(buffer);
    _viewPitchProperty.Read(buffer);
}

public int GetSnapshotSizeBytes()
{
    return _transformProperty.GetSizeBytes() 
         + _velocityProperty.GetSizeBytes() 
         + _viewYawProperty.GetSizeBytes() 
         + _viewPitchProperty.GetSizeBytes();
}
```

### Summary: PlayerCharacter Changes
- ✅ Add `IReplicatedEntity` interface
- ✅ Add `NetworkId` export property
- ✅ Initialize replicated properties in `_Ready()`
- ✅ Register with `RemoteEntityManager` (client)
- ✅ Implement `WriteSnapshot()`, `ReadSnapshot()`, `GetSnapshotSizeBytes()`
- ✅ Keep existing reconciliation system
- ✅ Keep existing `NetworkController` registration for input handling

---

## Network ID Assignment

### Static IDs (Recommended)

Assign unique IDs in the scene or via export:

```gdscript
# game_root.tscn
[node name="PlayerCar" type="RigidBody3D" parent="."]
script = ExtResource("path/to/RaycastCar.cs")
NetworkId = 2001
RegistrationMode = 1  # LocalPlayer

[node name="ServerCar1" type="RigidBody3D" parent="."]
script = ExtResource("path/to/RaycastCar.cs")
NetworkId = 2002
RegistrationMode = 2  # AuthoritativeVehicle
```

### ID Ranges (Convention)
- **1000-1999**: World props (platforms, doors)
- **2000-2999**: Vehicles
- **3000-3999**: Players
- **4000+**: Dynamic entities

---

## Integration Checklist

### Prerequisites
1. ✅ `EntityReplicationRegistry` is autoloaded in `project.godot`
2. ✅ `RemoteEntityManager` node exists in main scene
3. ✅ `NetworkController` updated to broadcast entity snapshots

### RaycastCar Migration
- [ ] Add `IReplicatedEntity` interface
- [ ] Add `NetworkId` field
- [ ] Add `InitializeReplication()` method
- [ ] Add `RegisterAsAuthority()` / `RegisterAsLocalVehicle()` methods
- [ ] Implement `WriteSnapshot()`, `ReadSnapshot()`, `GetSnapshotSizeBytes()`
- [ ] Update `_Ready()` to call new methods
- [ ] Test in server mode
- [ ] Test in client mode

### PlayerCharacter Migration
- [ ] Add `IReplicatedEntity` interface
- [ ] Add `NetworkId` field
- [ ] Add `InitializeReplication()` method
- [ ] Add `RegisterAsLocalPlayer()` method
- [ ] Implement `WriteSnapshot()`, `ReadSnapshot()`, `GetSnapshotSizeBytes()`
- [ ] Update `_Ready()` to call new methods
- [ ] Test in client mode

### Scene Updates
- [ ] Add `NetworkId` to all vehicle instances
- [ ] Add `NetworkId` to all player instances
- [ ] Verify unique IDs across all entities
- [ ] Test multiplayer scenario

---

## Backward Compatibility

### Hybrid Approach (Recommended)

Keep both systems running during migration:

1. **New system** handles replication (snapshot broadcast)
2. **Old system** handles input and reconciliation
3. Both `EntityReplicationRegistry` and `NetworkController` registrations active

### Benefits
- ✅ Gradual migration
- ✅ Can test new system without breaking old
- ✅ Easy rollback if issues arise

### Cleanup (Future)
Once stable, you can remove:
- `RegisterLocalPlayerCar()` / `RegisterAuthoritativeVehicle()` from `NetworkController`
- `RemoteVehicleManager` (replaced by `RemoteEntityManager`)
- Custom `VehicleStateSnapshot` / `PlayerStateSnapshot` serialization

---

## Testing

### Test Server Mode
```powershell
godot --server
```

Expected output:
```
RaycastCar: Registered as authority with NetworkId 2001
EntityReplicationRegistry: Registered entity 2001
```

### Test Client Mode
```powershell
godot --client --server-ip=127.0.0.1
```

Expected output:
```
RaycastCar: Registered as remote with NetworkId 2001
RemoteEntityManager: Registered remote entity 2001
```

### Verify Replication
1. Move vehicle on server
2. Check vehicle moves on client
3. Check network stats (bandwidth should be similar to before)

---

## Troubleshooting

### Vehicle not replicating
- Check `NetworkId` is set and unique
- Check `EntityReplicationRegistry` is autoloaded
- Check `RemoteEntityManager` is in scene (client)
- Check `IsAuthority` or `RegistrationMode` is correct

### High bandwidth usage
- Reduce update rate (e.g., 30 Hz instead of 60 Hz)
- Use `ReplicationMode.OnChange` for infrequent properties
- Add thresholds to `ReplicatedTransform3D`

### Jittery movement
- Increase blend factor in `ApplyTransformFromReplication()`
- Add interpolation buffer (future feature)
- Check network latency

### NetworkId conflicts
- Use unique ID ranges per entity type
- Log all registrations to check for duplicates
- Use dynamic IDs for spawned entities

---

## Performance

### Bandwidth (60 Hz)

**Per Vehicle:**
- Transform: 28 bytes
- LinearVelocity: 12 bytes
- AngularVelocity: 12 bytes
- Total: **52 bytes × 60 = 3.12 KB/s**

**Per Player:**
- Transform: 28 bytes
- Velocity: 12 bytes
- ViewYaw: 4 bytes
- ViewPitch: 4 bytes
- Total: **48 bytes × 60 = 2.88 KB/s**

**10 players + 5 vehicles:**
- (10 × 2.88) + (5 × 3.12) = **44.4 KB/s** (very reasonable!)

---

## Next Steps

1. **Migrate RaycastCar** using this guide
2. **Migrate PlayerCharacter** using this guide
3. **Test extensively** in multiplayer
4. **Monitor bandwidth** and adjust if needed
5. **Remove legacy code** once stable
6. **Add interpolation** for smoother remote entities
7. **Add relevance filtering** for large worlds

---

## See Also

- [REPLICATION_SYSTEM.md](REPLICATION_SYSTEM.md) - Full replication system documentation
- [REPLICATION_MIGRATION.md](REPLICATION_MIGRATION.md) - General migration guide for props
- [IMPLEMENTATION_NOTES.md](IMPLEMENTATION_NOTES.md) - Design decisions
- [README.md](../src/systems/network/README.md) - Network system overview

