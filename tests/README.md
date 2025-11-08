# Arena2006 Integration Test Suite

This directory contains integration tests for Arena2006 that test the game at a high level, as close to the player experience as possible.

## Overview

The test suite uses shell scripts to orchestrate multiple Godot instances (server and client) running in headless mode, monitors their logs, and validates game behavior through log-based assertions.

## Architecture

- **Shell orchestration scripts** (zsh) launch and manage multiple Godot instances
- **Log-based assertions** parse game output to verify conditions
- **Headless by default** with easy visualization for debugging
- **Test events** are logged with `TEST_EVENT:` prefix for easy parsing

## Prerequisites

- Godot 4.4+ installed and available in PATH (or at `/Applications/Godot.app/Contents/MacOS/Godot` on macOS)
- .NET SDK for building C# projects
- zsh shell (default on macOS)

## Directory Structure

```
tests/
├── README.md                    # This file
├── test_client_car_spawn.sh     # Example test: verifies client car spawns on server
├── test_client_car_respawn.sh   # Respawn test: drives, respawns, and validates positions
├── lib/
│   └── test_utils.sh            # Shared test utilities and functions
├── configs/                     # Test configuration files (future use)
└── logs/                        # Test logs (generated at runtime)
```

## Running Tests

### Run a Single Test

```bash
# Run in headless mode (default)
./tests/test_client_car_spawn.sh
./tests/test_client_car_respawn.sh

# Run with visualization (see the game running)
./tests/test_client_car_spawn.sh --visual
./tests/test_client_car_respawn.sh --visual
```

### Using the Test Runner

```bash
# Run a test through the test runner
./tests/run_test.sh ./tests/test_client_car_spawn.sh
./tests/run_test.sh ./tests/test_client_car_respawn.sh

# Run with visualization
./tests/run_test.sh ./tests/test_client_car_spawn.sh --visual
./tests/run_test.sh ./tests/test_client_car_respawn.sh --visual
```

## Writing New Tests

### Basic Test Structure

```bash
#!/usr/bin/env zsh

# Source test utilities
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/lib/test_utils.sh"

# Initialize if not already done
if [ -z "$GODOT_BIN" ]; then
    init_test_utils
fi

# Test configuration
TEST_NAME="My Test Name"
SERVER_LOG="$PROJECT_ROOT/tests/logs/my_test_server.log"
CLIENT_LOG="$PROJECT_ROOT/tests/logs/my_test_client.log"

# Clean up old logs
rm -f "$SERVER_LOG" "$CLIENT_LOG"

# Variables for process IDs
SERVER_PID=""
CLIENT_PID=""

# Cleanup function
cleanup() {
    cleanup_test_processes $SERVER_PID $CLIENT_PID
}

# Set up trap to cleanup on exit
trap cleanup EXIT INT TERM

# Start the test
print_info "Starting test: $TEST_NAME"

# Step 1: Start server
SERVER_PID=$(start_godot_instance "server" "$SERVER_LOG" "$VISUAL_MODE")

# Step 2: Wait for server to be ready
if ! wait_for_log_pattern "$SERVER_LOG" "TEST_EVENT: SERVER_STARTED" 10; then
    print_error "Server failed to start"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

# Step 3: Start client
CLIENT_PID=$(start_godot_instance "client" "$CLIENT_LOG" "$VISUAL_MODE")

# Step 4: Verify expected behavior
if ! wait_for_log_pattern "$SERVER_LOG" "TEST_EVENT: CLIENT_CONNECTED" 10; then
    print_error "Client failed to connect"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

# Test passed!
print_test_result "$TEST_NAME" "PASS"
exit 0
```

### Available Test Utilities

#### `init_test_utils()`
Initializes the test environment, finds Godot binary, and ensures project is imported.

#### `start_godot_instance <mode> <log_file> [--visual]`
Starts a Godot instance in server or client mode.
- `mode`: "server" or "client"
- `log_file`: Path to log file for output
- `--visual`: Optional flag to run with rendering (for debugging)

#### `wait_for_log_pattern <log_file> <pattern> <timeout>`
Waits for a pattern to appear in a log file.
- Returns 0 if pattern found, 1 if timeout

#### `check_log_pattern <log_file> <pattern>`
Checks if a pattern exists in a log file (no waiting).
- Returns 0 if found, 1 if not found

#### `extract_log_value <log_file> <pattern>`
Extracts a value from a log file using a regex pattern.

#### `cleanup_test_processes <pid1> [pid2] ...`
Cleans up test processes gracefully.

#### `print_test_result <test_name> <result>`
Prints a formatted test result ("PASS" or "FAIL").

## Test Events

The following test events are logged by the game for test assertions:

- `TEST_EVENT: SERVER_STARTED` - Server has started and is listening
- `TEST_EVENT: CLIENT_CONNECTED id=X` - Client X connected to server
- `TEST_EVENT: SERVER_CAR_SPAWNED player_id=X` - Server spawned car for player X
- `TEST_EVENT: CLIENT_RECEIVED_WELCOME id=X` - Client received welcome with ID X
- `TEST_EVENT: CAR_INITIAL_SPAWN name=... pos=x,y,z` - Local car completed its managed spawn selection
- `TEST_EVENT: CAR_RESPAWNED name=... prev=x,y,z new=x,y,z distance=D` - Local car respawned with travel distance logged
- `TEST_EVENT: INPUT_SCRIPT drive_respawn enabled` - Deterministic input script enabled (used by respawn test)

## Adding New Test Events

To add new test events to the game code:

```csharp
GD.Print("TEST_EVENT: MY_EVENT_NAME key=value");
```

Use the `TEST_EVENT:` prefix for easy parsing, and include key=value pairs for extracting data.

## Debugging Tests

### View Logs

Logs are stored in `tests/logs/` directory:

```bash
# View server log
cat tests/logs/server_car_spawn.log

# View client log
cat tests/logs/client_car_spawn.log
```

### Run with Visualization

Add `--visual` flag to see the game running:

```bash
./tests/test_client_car_spawn.sh --visual
```

This removes the `--headless` flag and lets you watch the test execution in real-time.

### Manual Testing

You can manually start server and client for debugging:

```bash
# Terminal 1: Start server
godot --path . --headless res://src/entities/root/game_root.tscn -- --server

# Terminal 2: Start client
godot --path . res://src/entities/root/game_root.tscn -- --client
```

Note: User arguments (--server, --client) must come after the `--` separator.

## Continuous Integration

These tests can be integrated into CI/CD pipelines:

```yaml
# Example GitHub Actions workflow
- name: Run Integration Tests
  run: |
    ./tests/test_client_car_spawn.sh
```

## Future Enhancements

- JSON-based test configuration files
- Simulated input injection for gameplay tests
- Multi-client tests (3+ clients)
- Physics and collision tests
- Performance benchmarks
- Test result reporting and aggregation

## Troubleshooting

### "Godot binary not found"
Ensure Godot is installed and either:
- Available in PATH as `godot`
- Installed at `/Applications/Godot.app/Contents/MacOS/Godot` (macOS)
- Set `GODOT_BIN` environment variable to the Godot binary path

### "Failed to import project"
The project needs to be imported before tests can run. This happens automatically on first run, but if it fails:
```bash
# Manually import the project
godot --path . --editor --quit-after 10
```

### Tests hang or timeout
- Check if Godot processes are still running: `ps aux | grep godot`
- Kill hung processes: `pkill -f godot`
- Check logs in `tests/logs/` for error messages

### "C# script not compiling"
Build the C# project manually:
```bash
dotnet build Arena2006.csproj
```
