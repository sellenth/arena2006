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
      ├── NetworkController.cs      # Main network controller (autoloaded)
      ├── RemotePlayerManager.cs    # Manages remote player spawning
      ├── CarInputState.cs          # Input state serialization
      └── CarSnapshot.cs            # Car state snapshot

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
- **Purpose**: CLIENT-ONLY - Spawns and updates visual representations of remote players
- **Functionality**:
  - Listens to `NetworkController.PlayerStateUpdated` signal
  - Spawns `player_car.tscn` instances as kinematic (frozen) bodies
  - Interpolates remote car positions smoothly
  - Cleans up disconnected players

#### 3. RemotePlayerLabels
- **Location**: `entities/ui/RemotePlayerLabels.cs`
- **Purpose**: CLIENT-ONLY - Adds floating name labels above remote players
- **Functionality**:
  - Listens to `NetworkController.PlayerStateUpdated` signal
  - Finds remote player cars spawned by `RemotePlayerManager`
  - Attaches 3D billboard labels above each remote car
  - Labels follow cars automatically (as children)

#### 4. RaycastCar (Universal car scene)
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

#### Client Flow (Remote Players)
1. `RemotePlayerManager` listens for `PlayerStateUpdated`
2. First update → spawn kinematic car at snapshot position
3. `RemotePlayerLabels` detects new car → attaches floating label above it
4. Subsequent updates → interpolate car position smoothly (label follows)
5. On disconnect → remove car and label from scene

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

### Why Autoload NetworkController?
- **Persistent**: Survives scene changes
- **Global access**: Any node can register with network
- **Early initialization**: Ready before scenes load

## Main Scene Flow

**File**: `entities/world/main_world.tscn`

Key nodes:
- `Car`: Local player's car (full physics)
- `RemotePlayers`: Container for remote player cars (managed by RemotePlayerManager)
- `RemotePlayerLabels`: Manages floating labels above remote players
- `Camera3D`: Debug/spectator camera
- `Car/CameraPivot/Camera3D`: Follow camera (tracks local player)

## System Interaction Flow

```
NetworkController (autoload)
    ↓ signals: PlayerStateUpdated, PlayerDisconnected
    ├→ RemotePlayerManager (spawns cars)
    │       ↓ creates Node3D "RemotePlayer_X"
    │       └→ adds to RemotePlayers container
    │
    └→ RemotePlayerLabels (adds labels)
            ↓ finds "RemotePlayer_X" in RemotePlayers
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