#!/usr/bin/env zsh

# Integration test: Verify that a client car spawns on the server
# This test validates the basic client-server connection and car spawning flow

# Source test utilities
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/lib/test_utils.sh"

# Initialize if not already done
if [ -z "$GODOT_BIN" ]; then
    init_test_utils
fi

# Test configuration
TEST_NAME="Client Car Spawn"
SERVER_LOG="$PROJECT_ROOT/tests/logs/server_car_spawn.log"
CLIENT_LOG="$PROJECT_ROOT/tests/logs/client_car_spawn.log"
TEST_TIMEOUT=15

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
echo ""

# Step 1: Start the server
print_info "Step 1: Starting server..."
SERVER_PID=$(start_godot_instance "server" "$SERVER_LOG" "$VISUAL_MODE")

if [ -z "$SERVER_PID" ]; then
    print_error "Failed to start server"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

print_info "Server PID: $SERVER_PID"

# Step 2: Wait for server to be ready
print_info "Step 2: Waiting for server to start..."
if ! wait_for_log_pattern "$SERVER_LOG" "TEST_EVENT: SERVER_STARTED" 10; then
    print_error "Server failed to start"
    display_log "$SERVER_LOG" "Server"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

# Give server a moment to stabilize
sleep 1

# Step 3: Start the client
print_info "Step 3: Starting client..."
CLIENT_PID=$(start_godot_instance "client" "$CLIENT_LOG" "$VISUAL_MODE")

if [ -z "$CLIENT_PID" ]; then
    print_error "Failed to start client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

print_info "Client PID: $CLIENT_PID"

# Step 4: Wait for client to connect
print_info "Step 4: Waiting for client to connect..."
if ! wait_for_log_pattern "$SERVER_LOG" "TEST_EVENT: CLIENT_CONNECTED" 10; then
    print_error "Client failed to connect to server"
    display_log "$SERVER_LOG" "Server"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

# Step 5: Wait for client to receive welcome
print_info "Step 5: Waiting for client to receive welcome..."
if ! wait_for_log_pattern "$CLIENT_LOG" "TEST_EVENT: CLIENT_RECEIVED_WELCOME" 5; then
    print_error "Client did not receive welcome message"
    display_log "$SERVER_LOG" "Server"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

# Step 6: Verify server spawned car for client
print_info "Step 6: Verifying server spawned car for client..."
if ! wait_for_log_pattern "$SERVER_LOG" "TEST_EVENT: SERVER_CAR_SPAWNED" 5; then
    print_error "Server did not spawn car for client"
    display_log "$SERVER_LOG" "Server"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

# Extract player ID from logs to verify it matches
CLIENT_ID=$(extract_log_value "$CLIENT_LOG" "CLIENT_RECEIVED_WELCOME id=[0-9]*" | grep -o "[0-9]*")
SERVER_CAR_ID=$(extract_log_value "$SERVER_LOG" "SERVER_CAR_SPAWNED player_id=[0-9]*" | grep -o "[0-9]*")

print_info "Client ID: $CLIENT_ID"
print_info "Server spawned car for player ID: $SERVER_CAR_ID"

if [ "$CLIENT_ID" = "$SERVER_CAR_ID" ]; then
    print_success "Player IDs match!"
else
    print_error "Player ID mismatch: Client=$CLIENT_ID, Server Car=$SERVER_CAR_ID"
    display_log "$SERVER_LOG" "Server"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

# Test passed!
print_success "All test steps completed successfully"

# Display logs if in visual mode
if [ -n "$VISUAL_MODE" ]; then
    display_log "$SERVER_LOG" "Server"
    display_log "$CLIENT_LOG" "Client"
fi

print_test_result "$TEST_NAME" "PASS"
exit 0

