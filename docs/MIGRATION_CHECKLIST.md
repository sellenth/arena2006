# Migration Checklist: Player & Vehicle to Entity Replication

This is a detailed, step-by-step checklist for migrating `RaycastCar.cs` and `PlayerCharacter.cs` to the new Entity Replication System.

---

## Prerequisites

### ✓ System Requirements

- [ ] `EntityReplicationRegistry.cs` exists in `src/systems/network/`
- [ ] `RemoteEntityManager.cs` exists in `src/systems/network/`
- [ ] `IReplicatedEntity.cs` exists in `src/systems/network/`
- [ ] `ReplicatedProperty.cs` exists in `src/systems/network/`
- [ ] `EntityReplicationRegistry` is autoloaded in `project.godot`

### ✓ Scene Setup

- [ ] `RemoteEntityManager` node added to main scene (`game_root.tscn`)
- [ ] `NetworkController` updated to support `PacketEntitySnapshot`
- [ ] `NetworkController` has `BroadcastEntitySnapshots()` method

### ✓ Backup

- [ ] **IMPORTANT:** Commit current working state to git
- [ ] Create backup branch: `git checkout -b backup-before-migration`
- [ ] Return to main: `git checkout main`

---

## Part 1: Migrate RaycastCar.cs

### Step 1: Add Interface (2 minutes)

- [ ] Open `src/entities/vehicle/car/RaycastCar.cs`
- [ ] Change class declaration from:
  ```csharp
  public partial class RaycastCar : RigidBody3D
  ```
  To:
  ```csharp
  public partial class RaycastCar : RigidBody3D, IReplicatedEntity
  ```

### Step 2: Add NetworkId Property (1 minute)

- [ ] Add after the export properties, before other fields:
  ```csharp
  [Export] public int NetworkId { get; set; } = 0;
  ```

### Step 3: Add Replicated Property Fields (2 minutes)

- [ ] Add at the end of the private fields section:
  ```csharp
  private ReplicatedTransform3D _transformProperty;
  private ReplicatedVector3 _linearVelocityProperty;
  private ReplicatedVector3 _angularVelocityProperty;
  ```

### Step 4: Add InitializeReplication Method (5 minutes)

- [ ] Add this method after `_Ready()`:
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
  ```

### Step 5: Add Registration Methods (5 minutes)

- [ ] Add these two methods after `InitializeReplication()`:
  ```csharp
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
  ```

### Step 6: Add Transform Application Helper (3 minutes)

- [ ] Add this method after the registration methods:
  ```csharp
  private void ApplyTransformFromReplication(Transform3D value)
  {
      if (_simulateLocally && _pendingSnapshot != null)
      {
          return;
      }
      
      GlobalTransform = value;
  }
  ```

### Step 7: Update _Ready() Method (3 minutes)

- [ ] Find the `_Ready()` method
- [ ] Add `InitializeReplication();` right after `_camera = GetNodeOrNull<Camera3D>(CameraPath);`
- [ ] In the `LocalPlayer` case, add `RegisterAsLocalVehicle();` before `network.RegisterLocalPlayerCar(this);`
- [ ] In the `AuthoritativeVehicle` case, add `RegisterAsAuthority();` before `network.RegisterAuthoritativeVehicle(this);`

The switch block should look like:
```csharp
switch (RegistrationMode)
{
    case NetworkRegistrationMode.LocalPlayer:
        if (network != null && network.IsClient)
        {
            RegisterAsLocalVehicle();  // NEW
            network.RegisterLocalPlayerCar(this);
            _isNetworked = true;
        }
        break;
    case NetworkRegistrationMode.AuthoritativeVehicle:
        if (network != null && network.IsServer)
        {
            RegisterAsAuthority();  // NEW
            network.RegisterAuthoritativeVehicle(this);
            _isNetworked = true;
        }
        break;
    default:
        _simulateLocally = false;
        break;
}
```

### Step 8: Implement IReplicatedEntity Interface (5 minutes)

- [ ] Add these three methods at the end of the class (before the closing brace):
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

### Step 9: Verify RaycastCar.cs (3 minutes)

- [ ] Check for compilation errors
- [ ] Verify all methods are in the right place
- [ ] Compare with `docs/examples/RaycastCar_MIGRATED.cs`
- [ ] Save the file

**Total Time for RaycastCar:** ~30 minutes

---

## Part 2: Migrate PlayerCharacter.cs

### Step 1: Add Interface (2 minutes)

- [ ] Open `src/entities/player/PlayerCharacter.cs`
- [ ] Change class declaration from:
  ```csharp
  public partial class PlayerCharacter : CharacterBody3D
  ```
  To:
  ```csharp
  public partial class PlayerCharacter : CharacterBody3D, IReplicatedEntity
  ```

### Step 2: Add NetworkId Property (1 minute)

- [ ] Add after the export properties:
  ```csharp
  [Export] public int NetworkId { get; set; } = 0;
  ```

### Step 3: Add Replicated Property Fields (2 minutes)

- [ ] Add at the end of the private fields section:
  ```csharp
  private ReplicatedTransform3D _transformProperty;
  private ReplicatedVector3 _velocityProperty;
  private ReplicatedFloat _viewYawProperty;
  private ReplicatedFloat _viewPitchProperty;
  ```

### Step 4: Add InitializeReplication Method (5 minutes)

- [ ] Add this method after `_Ready()`:
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
  ```

### Step 5: Add Registration Method (3 minutes)

- [ ] Add this method after `InitializeReplication()`:
  ```csharp
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
  ```

### Step 6: Add Helper Methods (5 minutes)

- [ ] Add these methods after `RegisterAsLocalPlayer()`:
  ```csharp
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

### Step 7: Update _Ready() Method (3 minutes)

- [ ] Find the `_Ready()` method
- [ ] Add `InitializeReplication();` right after `InitializeControllerModules();`
- [ ] Add `RegisterAsLocalPlayer();` in the `AutoRegisterWithNetwork` block before `_networkController.RegisterPlayerCharacter(this);`

Should look like:
```csharp
InitializeControllerModules();

InitializeReplication();  // NEW

if (AutoRegisterWithNetwork)
{
    _networkController = GetNodeOrNull<NetworkController>("/root/NetworkController");
    if (_networkController != null && _networkController.IsClient)
    {
        RegisterAsLocalPlayer();  // NEW
        _networkController.RegisterPlayerCharacter(this);
    }
}
```

### Step 8: Implement IReplicatedEntity Interface (5 minutes)

- [ ] Add these three methods at the end of the class:
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

### Step 9: Verify PlayerCharacter.cs (3 minutes)

- [ ] Check for compilation errors
- [ ] Verify all methods are in the right place
- [ ] Compare with `docs/examples/PlayerCharacter_MIGRATED.cs`
- [ ] Save the file

**Total Time for PlayerCharacter:** ~30 minutes

---

## Part 3: Update Scene Files

### Step 1: Assign NetworkIds to Vehicles (5 minutes)

- [ ] Open all scenes with `RaycastCar` instances
- [ ] For each vehicle, set unique `NetworkId`:
  - Local player car: `2001`
  - Server car 1: `2002`
  - Server car 2: `2003`
  - etc.

Example in `.tscn`:
```gdscript
[node name="PlayerCar" type="RigidBody3D" parent="."]
script = ExtResource("path/to/RaycastCar.cs")
NetworkId = 2001
RegistrationMode = 1
```

### Step 2: Assign NetworkIds to Players (5 minutes)

- [ ] Open all scenes with `PlayerCharacter` instances
- [ ] For each player, set unique `NetworkId`:
  - Local player: `3001`
  - Remote player 1: `3002`
  - Remote player 2: `3003`
  - etc.

Example in `.tscn`:
```gdscript
[node name="Player" type="CharacterBody3D" parent="."]
script = ExtResource("path/to/PlayerCharacter.cs")
NetworkId = 3001
AutoRegisterWithNetwork = true
```

### Step 3: Verify RemoteEntityManager (2 minutes)

- [ ] Open `game_root.tscn`
- [ ] Verify `RemoteEntityManager` node exists
- [ ] If not, add it:
  ```gdscript
  [node name="RemoteEntityManager" type="Node" parent="."]
  script = ExtResource("path/to/RemoteEntityManager.cs")
  ```

**Total Time for Scene Files:** ~12 minutes

---

## Part 4: Testing

### Test 1: Compilation (2 minutes)

- [ ] Build the project
- [ ] Fix any compilation errors
- [ ] Verify no warnings related to replication

### Test 2: Server Mode (10 minutes)

- [ ] Start server: `godot --server`
- [ ] Expected console output:
  ```
  EntityReplicationRegistry: Ready
  RaycastCar: Registered as authority with NetworkId 2002
  ```
- [ ] Verify vehicle moves
- [ ] Verify no errors in console

### Test 3: Client Mode (10 minutes)

- [ ] Start client: `godot --client --server-ip=127.0.0.1`
- [ ] Expected console output:
  ```
  RemoteEntityManager: Ready
  RaycastCar: Registered as remote with NetworkId 2001
  PlayerCharacter: Registered as remote with NetworkId 3001
  ```
- [ ] Drive vehicle
- [ ] Verify vehicle replicates to server
- [ ] Verify no jitter or stuttering

### Test 4: Multiplayer (15 minutes)

- [ ] Start server: `godot --server`
- [ ] Start client 1: `godot --client --server-ip=127.0.0.1`
- [ ] Start client 2: `godot --client --server-ip=127.0.0.1`
- [ ] Verify both clients see each other
- [ ] Verify smooth movement
- [ ] Check bandwidth in console

### Test 5: Network Stats (5 minutes)

- [ ] Monitor bandwidth usage
- [ ] Expected: ~3-4 KB/s per vehicle
- [ ] Expected: ~3 KB/s per player
- [ ] Total should be similar to before migration

**Total Time for Testing:** ~42 minutes

---

## Part 5: Verification & Cleanup

### Final Checks (10 minutes)

- [ ] All vehicles replicate correctly
- [ ] All players replicate correctly
- [ ] No console errors
- [ ] Bandwidth usage is acceptable
- [ ] Movement is smooth
- [ ] Reconciliation still works

### Documentation (5 minutes)

- [ ] Update any project-specific docs
- [ ] Add migration notes to CHANGELOG
- [ ] Document any issues encountered

### Git Commit (3 minutes)

- [ ] Review all changes
- [ ] Stage modified files
- [ ] Commit with message:
  ```
  feat: Migrate RaycastCar and PlayerCharacter to Entity Replication System
  
  - Implemented IReplicatedEntity interface
  - Added ReplicatedProperty fields for state
  - Registered entities with EntityReplicationRegistry/RemoteEntityManager
  - Maintained backward compatibility with existing systems
  - Tested in server/client/multiplayer scenarios
  ```

**Total Time for Verification:** ~18 minutes

---

## Troubleshooting

### Common Issues

**Issue: "IReplicatedEntity not found"**
- [ ] Verify `IReplicatedEntity.cs` exists
- [ ] Check file is included in project
- [ ] Rebuild project

**Issue: "RemoteEntityManager not found in scene"**
- [ ] Check `game_root.tscn` has `RemoteEntityManager` node
- [ ] Verify node path is correct
- [ ] Check scene is loading properly

**Issue: Vehicle not replicating**
- [ ] Verify `NetworkId` is set and unique
- [ ] Check `RegisterAsAuthority()` or `RegisterAsLocalVehicle()` is called
- [ ] Verify `EntityReplicationRegistry` is autoloaded
- [ ] Check console for registration messages

**Issue: Jittery movement**
- [ ] Increase blend factor in `ApplyTransformFromReplication()`
- [ ] Check network latency
- [ ] Verify tick rate is consistent

**Issue: High bandwidth usage**
- [ ] Check if all properties are set to `ReplicationMode.Always`
- [ ] Consider using `OnChange` for infrequent updates
- [ ] Verify delta compression is working (check dirty masks)

---

## Rollback Plan

If migration fails:

1. [ ] `git checkout backup-before-migration`
2. [ ] Verify old system works
3. [ ] Review migration docs
4. [ ] Try again with more care

---

## Timeline Summary

| Phase                | Estimated Time |
|----------------------|----------------|
| RaycastCar migration | 30 minutes     |
| PlayerCharacter migration | 30 minutes |
| Scene updates        | 12 minutes     |
| Testing              | 42 minutes     |
| Verification         | 18 minutes     |
| **Total**            | **2.2 hours**  |

---

## Success Criteria

Migration is successful when:

- ✓ Both files compile without errors
- ✓ Server mode starts without errors
- ✓ Client mode starts without errors
- ✓ Vehicles replicate smoothly
- ✓ Players replicate smoothly
- ✓ Bandwidth usage is similar to before
- ✓ No regression in existing functionality
- ✓ All tests pass

---

## Next Steps After Successful Migration

1. **Monitor production** (1 week)
2. **Gather metrics** (bandwidth, CPU, latency)
3. **Optimize properties** (use OnChange where appropriate)
4. **Remove legacy code** (after 1-2 weeks of stability)
5. **Migrate other entities** (pickups, projectiles, etc.)
6. **Add interpolation** for smoother remote movement
7. **Add relevance filtering** for large worlds

---

## Support

If you encounter issues:

1. Check [MIGRATION_GUIDE_PLAYER_VEHICLE.md](MIGRATION_GUIDE_PLAYER_VEHICLE.md)
2. Check [MIGRATION_QUICK_REFERENCE.md](MIGRATION_QUICK_REFERENCE.md)
3. Review examples in [docs/examples/](examples/)
4. Check [REPLICATION_SYSTEM.md](REPLICATION_SYSTEM.md)
5. Check existing working examples in `src/entities/props/`

