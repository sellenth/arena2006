# Raycast Car - Project Architecture

## Directory Structure

```
entities/                    # Scene entities (visual components + behavior)
  ├── camera/               # Camera scripts (follow camera, free camera)
  ├── props/                # World props (traffic cones, barriers)
  ├── ui/                   # UI components (debug UI, player labels)
  ├── vehicle/
  │   ├── car/             # Car scripts and scenes
  │   │   ├── RaycastCar.cs
  │   │   ├── RaycastWheel.cs
  │   │   ├── player_car.tscn
  │   │   └── *.tres       # Tire curve resources
  │   └── models/          # Vehicle 3D models
  │       ├── nissan35/
  │       └── mustang67/
  └── world/               # World scenes and assets
      ├── main_world.tscn
      ├── environment_and_light.tscn
      └── environment/     # Environment models (trees, hills)

systems/                     # System logic (not tied to specific scenes)
  └── network/              # Networking system
      ├── NetworkController.cs           # Main network controller (autoloaded)
      ├── RemotePlayerManager.cs         # Manages remote on-foot player spawning
      ├── RemoteVehicleManager.cs        # Manages replicated vehicle proxies
      ├── RemoteEntityManager.cs         # Manages generic replicated entities
      ├── EntityReplicationRegistry.cs   # Central registry for replicated entities
      ├── IReplicatedEntity.cs           # Interface for replicated entities
      ├── ReplicatedProperty.cs          # Property replication system
      ├── ReplicatedEntity.cs            # Base class for replicated entities
      ├── CarInputState.cs               # Vehicle input state serialization
      ├── PlayerInputState.cs            # On-foot input state serialization
      ├── CarSnapshot.cs                 # Car state snapshot
      └── PlayerSnapshot.cs              # On-foot player snapshot

resources/                   # Shared resources
  └── materials/            # Materials (prototype, etc.)
```

## Network Architecture

### Overview
The project uses a client-server UDP networking model where:
- **Server**: Authoritative physics simulation, spawns cars for each client
- **Client**: Local physics simulation for own car, visual-only for remote players

### Components

#### 1. NetworkController (Autoloaded)
- **Location**: `systems/network/NetworkController.cs`
- **Purpose**: Core networking logic, handles UDP connections, packet routing
- **Modes**:
  - `Server`: Listens for clients, spawns server-side cars, broadcasts state
  - `Client`: Connects to server, sends input, receives world state

#### 2. RemotePlayerManager
- **Location**: `systems/network/RemotePlayerManager.cs`
- **Purpose**: CLIENT-ONLY - Spawns and updates the remote on-foot player characters
- **Functionality**:
  - Listens to `NetworkController.PlayerStateUpdated` signal
  - Spawns `player_character.tscn` instances in spectator (non-authority) mode
  - Applies replicated `PlayerSnapshot` data for smooth interpolation
  - Cleans up disconnected players

#### 3. RemoteVehicleManager
- **Location**: `systems/network/RemoteVehicleManager.cs`
- **Purpose**: CLIENT-ONLY - Spawns and updates replicated vehicles owned by the server
- **Functionality**:
  - Listens to `VehicleStateUpdated`/`VehicleDespawned` signals
  - Instantiates kinematic `RaycastCar` proxies for authoritative vehicles
  - Keeps track of which player is occupying which vehicle
  - Hands camera control to the local client when they occupy a remote vehicle

#### 4. RemotePlayerLabels
- **Location**: `entities/ui/RemotePlayerLabels.cs`
- **Purpose**: CLIENT-ONLY - Adds floating name labels above remote players
- **Functionality**:
  - Listens to `NetworkController.PlayerStateUpdated` signal
  - Finds remote player characters spawned by `RemotePlayerManager`
  - For vehicle occupants, looks up the matching car via `RemoteVehicleManager`
  - Attaches 3D billboard labels above the appropriate node so they follow automatically

#### 5. RaycastCar (Universal car scene)
- **Location**: `entities/vehicle/car/RaycastCar.cs`
- **Purpose**: Universal car controller, works for both local and network-driven cars
- **Modes**:
  - **Local Player**: Full physics simulation, input from keyboard/controller
  - **Server Simulation**: Full physics simulation, input from network packets
  - **Remote Player (Client)**: Kinematic (frozen), position updated by snapshots

### Network Flow

#### Server Flow
1. Client connects → Server assigns player ID
2. Server spawns `player_car.tscn` for that client
3. Server removes camera nodes (not needed on server)
4. Server receives input packets from client
5. Server applies input to client's car via `SetInputState()`
6. Server captures `CarSnapshot` each physics tick
7. Server broadcasts snapshot to all clients

#### Client Flow (Own Car)
1. Collect local input (keyboard/controller)
2. Send input packet to server
3. Apply input to local car for immediate response
4. Receive snapshot from server
5. Apply snapshot for server reconciliation (smoothly blend)

#### Client Flow (Remote Players & Vehicles)
1. `RemotePlayerManager` listens for `PlayerStateUpdated` and spawns `player_character.tscn` proxies
2. `RemoteVehicleManager` listens for authoritative vehicle snapshots and spawns kinematic `player_car.tscn` proxies
3. `RemotePlayerLabels` attaches floating labels to either the player character or the vehicle the player currently occupies
4. Subsequent updates → both player characters and vehicles smoothly interpolate toward the replicated transforms
5. On disconnect → remove associated player characters, vehicles, and labels from the scene

## Key Design Decisions

### Why Reuse player_car.tscn?
- **Single source of truth**: One car scene for all contexts
- **Easy maintenance**: Update car once, works everywhere
- **Consistent visuals**: All cars look the same (just different physics modes)
- **No proxy needed**: Remote cars ARE real cars, just frozen kinematically

### Why Kinematic Remote Cars?
- **Performance**: No physics simulation needed for remote cars on client
- **Smooth movement**: Interpolation looks better than physics replication
- **Network efficiency**: Only need position/rotation, not full physics state

## Entity Replication System

The project includes a general-purpose entity replication system for synchronizing arbitrary game objects (props, platforms, doors, etc.) across the network.

### Key Features
- **Declarative Property Registration**: Define what to replicate, not how
- **Delta Compression**: Only send changed properties (OnChange mode)
- **Type-Safe Properties**: Strongly-typed wrappers (Float, Vector3, Transform3D, Int)
- **Automatic Serialization**: Properties handle their own serialization
- **Scalable**: Handles hundreds of entities efficiently

### Components

#### EntityReplicationRegistry (Autoloaded)
Central registry that tracks all replicated entities. Entities register themselves on spawn.

#### RemoteEntityManager
CLIENT-ONLY manager that receives entity snapshots and applies them to local entities.

#### IReplicatedEntity Interface
Entities implement this interface to participate in replication:
```csharp
public interface IReplicatedEntity
{
    int NetworkId { get; }
    void WriteSnapshot(StreamPeerBuffer buffer);
    void ReadSnapshot(StreamPeerBuffer buffer);
    int GetSnapshotSizeBytes();
}
```

#### ReplicatedProperty System
Strongly-typed property wrappers that handle serialization:
- `ReplicatedFloat` - Single float value
- `ReplicatedInt` - Integer value
- `ReplicatedVector3` - 3D vector
- `ReplicatedTransform3D` - Full transform (position + rotation)

### Example Usage

```csharp
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

### Documentation
- **[REPLICATION_SUMMARY.md](docs/REPLICATION_SUMMARY.md)** - Quick overview
- **[REPLICATION_SYSTEM.md](docs/REPLICATION_SYSTEM.md)** - Full documentation
- **[REPLICATION_MIGRATION.md](docs/REPLICATION_MIGRATION.md)** - Migration guide

### Why Autoload NetworkController?
- **Persistent**: Survives scene changes
- **Global access**: Any node can register with network
- **Early initialization**: Ready before scenes load

## Main Scene Flow

**File**: `entities/world/main_world.tscn`

Key nodes:
- `Car`: Local player's car (full physics)
- `RemotePlayers`: Container for remote player characters (managed by RemotePlayerManager)
- `RemoteVehicles`: Container for remote vehicle proxies (managed by RemoteVehicleManager)
- `RemotePlayerLabels`: Manages floating labels above whichever node the player occupies
- `Camera3D`: Debug/spectator camera
- `Car/CameraPivot/Camera3D`: Follow camera (tracks local player)

## System Interaction Flow

```
NetworkController (autoload)
    ↓ signals: PlayerStateUpdated, VehicleStateUpdated, PlayerDisconnected
    ├→ RemotePlayerManager (spawns player characters)
    │       ↓ creates Node3D "RemotePlayer_X"
    │       └→ adds to RemotePlayers container
    │
    ├→ RemoteVehicleManager (spawns vehicles)
    │       ↓ creates Node3D "Vehicle_X"
    │       └→ adds to RemoteVehicles container
    │
    └→ RemotePlayerLabels (adds labels)
            ↓ finds "RemotePlayer_X" or vehicle occupant node
            └→ attaches Label3D as child
```

## Getting Started

### Running as Server
```bash
godot --server
```

### Running as Client (default)
```bash
godot
# or
godot --client
```

### Testing Locally
1. Start server: `godot --server`
2. Start client 1: `godot`
3. Start client 2: `godot`
4. All clients should see each other's cars


### creating vm

// godot linux x64

wget https://builds.dotnet.microsoft.com/dotnet/Sdk/9.0.306/dotnet-sdk-9.0.306-linux-x64.tar.gz
mkdir -p $HOME/dotnet && tar zxf dotnet-sdk-9.0.306-linux-x64.tar.gz -C $HOME/dotnet
export DOTNET_ROOT=$HOME/dotnet
export PATH=$PATH:$HOME/dotnet


// some subset of these instructions
Downloading a compatible version of Blender
Adding blender to ~/.config/godot/editor_settings-4.*.tres
Installing various xorg-related packages (on Ubuntu this was apt install xorg xz-utils xvfb)
