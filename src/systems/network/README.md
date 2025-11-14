# Network System

This directory contains the networking subsystem for Arena2006, including both player/vehicle replication and a general-purpose entity replication system.

## Overview

The networking system uses a **client-server UDP model** with authoritative server physics.

## Core Components

### NetworkController.cs
Main network controller (autoloaded). Handles UDP connections, packet routing, and broadcasts.

### Player/Vehicle Replication (Legacy)
- **RemotePlayerManager.cs** - Spawns and updates remote player characters
- **RemoteVehicleManager.cs** - Spawns and updates remote vehicles
- **CarInputState.cs** - Vehicle input serialization
- **PlayerInputState.cs** - Player input serialization
- **CarSnapshot.cs** - Vehicle state snapshot
- **PlayerSnapshot.cs** - Player state snapshot

### Entity Replication System (New)
General-purpose replication for arbitrary game objects.

#### Core Files
- **IReplicatedEntity.cs** - Interface for replicated entities
- **ReplicatedProperty.cs** - Property replication system (Float, Int, Vector3, Transform3D)
- **ReplicatedEntity.cs** - Base class for replicated entities
- **EntityReplicationRegistry.cs** - Central registry (autoloaded)
- **RemoteEntityManager.cs** - Client-side entity manager

#### Serialization
- **NetworkSerializer.cs** - Packet serialization/deserialization

## Quick Start

### Creating a Replicated Entity

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
            (value) => GlobalTransform = value,
            ReplicationMode.Always
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

## Documentation

### Main Docs
- **[REPLICATION_SUMMARY.md](../../../docs/REPLICATION_SUMMARY.md)** - Quick overview and key concepts
- **[REPLICATION_SYSTEM.md](../../../docs/REPLICATION_SYSTEM.md)** - Full system documentation with examples
- **[REPLICATION_MIGRATION.md](../../../docs/REPLICATION_MIGRATION.md)** - Migration guide from old system
- **[ARCHITECTURE.md](../../../ARCHITECTURE.md)** - Overall project architecture

## Examples

See `src/entities/props/` for example implementations:
- **ReplicatedPathFollow.cs** - Moving prop on a path
- **ReplicatedPlatform.cs** - Moving platform
- **ReplicatedDoor.cs** - Door with state machine

## Network Flow

### Server
1. Entities register with `EntityReplicationRegistry`
2. `NetworkController.BroadcastEntitySnapshots()` called every physics tick
3. For each entity: serialize snapshot and broadcast to clients

### Client
1. Receive `PacketEntitySnapshot`
2. `RemoteEntityManager` looks up entity by NetworkId
3. Apply snapshot to local entity

## Property Types

| Type | Size | Use Case |
|------|------|----------|
| ReplicatedFloat | 4 bytes | Progress, health, speed |
| ReplicatedInt | 4 bytes | State, ammo, score |
| ReplicatedVector3 | 12 bytes | Position, velocity, scale |
| ReplicatedTransform3D | 28 bytes | Full transform (pos + rot) |

## Replication Modes

- **Always** - Send every tick (for continuous motion)
- **OnChange** - Only send when changed (for infrequent updates)

## Performance

At 60 Hz:
- **Transform (Always)**: 1.68 KB/s per entity
- **Int (OnChange, unchanged)**: 60 bytes/s per entity

Delta compression saves 80-90% bandwidth for OnChange properties.

## Credits

Inspired by:
- **Quake 3** (id Software) - Delta compression
- **Source Engine** (Valve) - Networked variables
- **Unreal Engine** (Epic) - Property replication

