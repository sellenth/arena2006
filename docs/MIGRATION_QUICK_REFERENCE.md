# Quick Reference: Entity Replication Migration

This is a side-by-side comparison of the key changes needed to migrate to the Entity Replication System.

## RaycastCar.cs - Key Changes

### 1. Add Interface and Properties

**Before:**
```csharp
public partial class RaycastCar : RigidBody3D
{
    // Existing fields only
    public int TotalWheels { get; private set; }
    public float MotorInput { get; set; } = 0.0f;
}
```

**After:**
```csharp
public partial class RaycastCar : RigidBody3D, IReplicatedEntity
{
    [Export] public int NetworkId { get; set; } = 0;
    
    private ReplicatedTransform3D _transformProperty;
    private ReplicatedVector3 _linearVelocityProperty;
    private ReplicatedVector3 _angularVelocityProperty;
    
    // Existing fields...
    public int TotalWheels { get; private set; }
    public float MotorInput { get; set; } = 0.0f;
}
```

### 2. Update _Ready() Method

**Before:**
```csharp
public override void _Ready()
{
    TotalWheels = Wheels.Count;
    _camera = GetNodeOrNull<Camera3D>(CameraPath);
    
    var network = GetTree().Root.GetNodeOrNull<NetworkController>("/root/NetworkController");
    
    switch (RegistrationMode)
    {
        case NetworkRegistrationMode.LocalPlayer:
            if (network != null && network.IsClient)
            {
                network.RegisterLocalPlayerCar(this);
                _isNetworked = true;
            }
            break;
    }
}
```

**After:**
```csharp
public override void _Ready()
{
    TotalWheels = Wheels.Count;
    _camera = GetNodeOrNull<Camera3D>(CameraPath);
    
    InitializeReplication();  // NEW
    
    var network = GetTree().Root.GetNodeOrNull<NetworkController>("/root/NetworkController");
    
    switch (RegistrationMode)
    {
        case NetworkRegistrationMode.LocalPlayer:
            if (network != null && network.IsClient)
            {
                RegisterAsLocalVehicle();  // NEW
                network.RegisterLocalPlayerCar(this);  // KEEP
                _isNetworked = true;
            }
            break;
    }
}
```

### 3. Add New Methods

**Add these three methods:**

```csharp
private void InitializeReplication()
{
    _transformProperty = new ReplicatedTransform3D(
        "Transform",
        () => GlobalTransform,
        (value) => ApplyTransformFromReplication(value),
        ReplicationMode.Always,
        positionThreshold: SnapPosEps,
        rotationThreshold: SnapAngEps
    );
    
    _linearVelocityProperty = new ReplicatedVector3(
        "LinearVelocity",
        () => LinearVelocity,
        (value) => LinearVelocity = value,
        ReplicationMode.Always
    );
    
    _angularVelocityProperty = new ReplicatedVector3(
        "AngularVelocity",
        () => AngularVelocity,
        (value) => AngularVelocity = value,
        ReplicationMode.Always
    );
}

private void RegisterAsAuthority()
{
    NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? NetworkId;
    GD.Print($"RaycastCar: Registered as authority with NetworkId {NetworkId}");
}

private void RegisterAsLocalVehicle()
{
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
    if (_simulateLocally && _pendingSnapshot != null)
    {
        return;
    }
    
    GlobalTransform = value;
}
```

### 4. Implement IReplicatedEntity

**Add these three interface methods:**

```csharp
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

---

## PlayerCharacter.cs - Key Changes

### 1. Add Interface and Properties

**Before:**
```csharp
public partial class PlayerCharacter : CharacterBody3D
{
    [Export] public NodePath HeadPath { get; set; } = "Head";
    [Export] public bool AutoRegisterWithNetwork { get; set; } = true;
    
    private Node3D _head;
    private Camera3D _camera;
}
```

**After:**
```csharp
public partial class PlayerCharacter : CharacterBody3D, IReplicatedEntity
{
    [Export] public NodePath HeadPath { get; set; } = "Head";
    [Export] public bool AutoRegisterWithNetwork { get; set; } = true;
    [Export] public int NetworkId { get; set; } = 0;
    
    private Node3D _head;
    private Camera3D _camera;
    
    private ReplicatedTransform3D _transformProperty;
    private ReplicatedVector3 _velocityProperty;
    private ReplicatedFloat _viewYawProperty;
    private ReplicatedFloat _viewPitchProperty;
}
```

### 2. Update _Ready() Method

**Before:**
```csharp
public override void _Ready()
{
    _head = GetNodeOrNull<Node3D>(HeadPath);
    _camera = GetNodeOrNull<Camera3D>(CameraPath);
    
    InitializeControllerModules();
    
    if (AutoRegisterWithNetwork)
    {
        _networkController = GetNodeOrNull<NetworkController>("/root/NetworkController");
        if (_networkController != null && _networkController.IsClient)
            _networkController.RegisterPlayerCharacter(this);
    }
}
```

**After:**
```csharp
public override void _Ready()
{
    _head = GetNodeOrNull<Node3D>(HeadPath);
    _camera = GetNodeOrNull<Camera3D>(CameraPath);
    
    InitializeControllerModules();
    
    InitializeReplication();  // NEW
    
    if (AutoRegisterWithNetwork)
    {
        _networkController = GetNodeOrNull<NetworkController>("/root/NetworkController");
        if (_networkController != null && _networkController.IsClient)
        {
            RegisterAsLocalPlayer();  // NEW
            _networkController.RegisterPlayerCharacter(this);  // KEEP
        }
    }
}
```

### 3. Add New Methods

**Add these five methods:**

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
    var snapshot = new PlayerSnapshot
    {
        Transform = value,
        Velocity = Velocity,
        ViewYaw = _lookController?.Yaw ?? 0f,
        ViewPitch = _lookController?.Pitch ?? 0f
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

### 4. Implement IReplicatedEntity

**Add these three interface methods:**

```csharp
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

---

## Summary of Changes

### Files to Modify
1. `src/entities/vehicle/car/RaycastCar.cs`
2. `src/entities/player/PlayerCharacter.cs`

### New Code Added Per File
- **1 interface**: `IReplicatedEntity`
- **1 export property**: `NetworkId`
- **3-4 replicated properties**: Transform, velocity, etc.
- **3-5 helper methods**: Init, register, apply
- **3 interface methods**: Write/Read/GetSize

### Existing Code Changes
- **_Ready()**: Add 2 lines (InitializeReplication + Register calls)
- **Keep everything else**: All existing functionality preserved

### Total Lines Added
- **RaycastCar**: ~70 lines
- **PlayerCharacter**: ~85 lines

### Testing Required
- [ ] Server mode: Vehicle spawns and replicates
- [ ] Client mode: Vehicle receives snapshots
- [ ] Multiplayer: Both players see each other
- [ ] Bandwidth: Check similar to before (~3-4 KB/s per entity)

---

## Common Mistakes to Avoid

1. **Forgetting NetworkId**: Must be unique per entity instance
2. **Wrong registration**: Server uses `RegisterAsAuthority()`, client uses `RegisterAsLocalPlayer()`
3. **Missing RemoteEntityManager**: Client needs this node in scene
4. **Not calling InitializeReplication()**: Must be called before registration
5. **Breaking existing code**: Keep all existing methods and calls

---

## Migration Checklist

### RaycastCar
- [ ] Add `IReplicatedEntity` to class declaration
- [ ] Add `[Export] public int NetworkId { get; set; } = 0;`
- [ ] Add 3 replicated property fields
- [ ] Add `InitializeReplication()` method
- [ ] Add `RegisterAsAuthority()` method
- [ ] Add `RegisterAsLocalVehicle()` method
- [ ] Add `ApplyTransformFromReplication()` method
- [ ] Call `InitializeReplication()` in `_Ready()`
- [ ] Call `RegisterAsAuthority()` or `RegisterAsLocalVehicle()` in `_Ready()`
- [ ] Implement `WriteSnapshot()`, `ReadSnapshot()`, `GetSnapshotSizeBytes()`
- [ ] Test server mode
- [ ] Test client mode

### PlayerCharacter
- [ ] Add `IReplicatedEntity` to class declaration
- [ ] Add `[Export] public int NetworkId { get; set; } = 0;`
- [ ] Add 4 replicated property fields
- [ ] Add `InitializeReplication()` method
- [ ] Add `RegisterAsLocalPlayer()` method
- [ ] Add `ApplyTransformFromReplication()` method
- [ ] Add `SetViewYaw()` and `SetViewPitch()` methods
- [ ] Call `InitializeReplication()` in `_Ready()`
- [ ] Call `RegisterAsLocalPlayer()` in `_Ready()`
- [ ] Implement `WriteSnapshot()`, `ReadSnapshot()`, `GetSnapshotSizeBytes()`
- [ ] Test client mode

### Scene Files
- [ ] Add `NetworkId` to all vehicle instances in scenes
- [ ] Add `NetworkId` to all player instances in scenes
- [ ] Ensure unique IDs across all entities
- [ ] Verify `RemoteEntityManager` node exists

---

## Getting Help

If you run into issues:

1. Check the full examples in `docs/examples/`
2. Read the detailed guide in `docs/MIGRATION_GUIDE_PLAYER_VEHICLE.md`
3. See working examples in `src/entities/props/`
4. Review the system docs in `docs/REPLICATION_SYSTEM.md`

