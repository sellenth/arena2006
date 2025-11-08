#!/usr/bin/env zsh

# Integration test: Verify that a client car can move, respawn, and log positions

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/lib/test_utils.sh"

if [ -z "$GODOT_BIN" ]; then
    init_test_utils
fi

TEST_NAME="Client Car Respawn"
SERVER_LOG="$PROJECT_ROOT/tests/logs/server_car_respawn.log"
CLIENT_LOG="$PROJECT_ROOT/tests/logs/client_car_respawn.log"
TEST_TIMEOUT=30
MIN_TRAVEL_DISTANCE=5.0
MAX_RESPAWN_OFFSET=1.0

rm -f "$SERVER_LOG" "$CLIENT_LOG"

SERVER_PID=""
CLIENT_PID=""
ORIGINAL_INPUT_SCRIPT="$ARENA_TEST_INPUT_SCRIPT"

cleanup() {
    cleanup_test_processes $SERVER_PID $CLIENT_PID
    if [ -n "$ORIGINAL_INPUT_SCRIPT" ]; then
        export ARENA_TEST_INPUT_SCRIPT="$ORIGINAL_INPUT_SCRIPT"
    else
        unset ARENA_TEST_INPUT_SCRIPT
    fi
}

trap cleanup EXIT INT TERM

print_info "Starting test: $TEST_NAME"
echo ""

print_info "Step 1: Starting server..."
SERVER_PID=$(start_godot_instance "server" "$SERVER_LOG" "$VISUAL_MODE")
if [ -z "$SERVER_PID" ]; then
    print_error "Failed to start server"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

print_info "Step 2: Waiting for server to start..."
if ! wait_for_log_pattern "$SERVER_LOG" "TEST_EVENT: SERVER_STARTED" 10; then
    print_error "Server failed to start"
    display_log "$SERVER_LOG" "Server"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

sleep 1

print_info "Step 3: Starting client with scripted input..."
export ARENA_TEST_INPUT_SCRIPT="drive_respawn"
CLIENT_PID=$(start_godot_instance "client" "$CLIENT_LOG" "$VISUAL_MODE")
unset ARENA_TEST_INPUT_SCRIPT

if [ -z "$CLIENT_PID" ]; then
    print_error "Failed to start client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

print_info "Step 4: Waiting for scripted input confirmation..."
if ! wait_for_log_pattern "$CLIENT_LOG" "TEST_EVENT: INPUT_SCRIPT drive_respawn enabled" 5; then
    print_error "Client did not enable input script"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

print_info "Step 5: Waiting for client connection..."
if ! wait_for_log_pattern "$SERVER_LOG" "TEST_EVENT: CLIENT_CONNECTED" 10; then
    print_error "Client failed to connect"
    display_log "$SERVER_LOG" "Server"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

if ! wait_for_log_pattern "$CLIENT_LOG" "TEST_EVENT: CLIENT_RECEIVED_WELCOME" 5; then
    print_error "Client did not receive welcome"
    display_log "$SERVER_LOG" "Server"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

print_info "Step 6: Waiting for client spawn logs..."
if ! wait_for_log_pattern "$CLIENT_LOG" "TEST_EVENT: CAR_INITIAL_SPAWN" 10; then
    print_error "Client did not log initial spawn"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

print_info "Step 7: Waiting for respawn event..."
if ! wait_for_log_pattern "$CLIENT_LOG" "TEST_EVENT: CAR_RESPAWNED" 25; then
    print_error "Client did not respawn in time"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

print_info "Step 8: Validating travel distance and respawn location..."
parse_output=$(
python3 - "$CLIENT_LOG" <<'PY'
import sys, re, math
log_path = sys.argv[1]
initial = None
respawn_new = None
distance = None
with open(log_path, "r", encoding="utf-8", errors="ignore") as log_file:
    for line in log_file:
        if "CAR_INITIAL_SPAWN" in line and initial is None:
            match = re.search(r'pos=([^\s]+)', line)
            if match:
                try:
                    initial = tuple(float(x) for x in match.group(1).split(','))
                except ValueError:
                    initial = None
        if "CAR_RESPAWNED" in line and respawn_new is None:
            m_prev = re.search(r'prev=([^\s]+)', line)
            m_new = re.search(r'new=([^\s]+)', line)
            m_dist = re.search(r'distance=([0-9.+-]+)', line)
            if m_prev and m_new and m_dist:
                try:
                    respawn_new = tuple(float(x) for x in m_new.group(1).split(','))
                    distance = float(m_dist.group(1))
                except ValueError:
                    respawn_new = None
                    distance = None
            if respawn_new is not None:
                break
if initial is None or respawn_new is None or distance is None:
    sys.exit(1)
return_delta = math.sqrt(sum((a - b) ** 2 for a, b in zip(initial, respawn_new)))
print(f"{distance:.3f}")
print(f"{return_delta:.3f}")
PY
)

if [ $? -ne 0 ]; then
    print_error "Failed to parse respawn metrics"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

RESPAWN_DISTANCE=$(echo "$parse_output" | sed -n '1p')
RETURN_DELTA=$(echo "$parse_output" | sed -n '2p')

print_info "Travel distance before respawn: ${RESPAWN_DISTANCE} units"
print_info "Respawn offset from initial: ${RETURN_DELTA} units"

if ! python3 - "$RESPAWN_DISTANCE" "$MIN_TRAVEL_DISTANCE" <<'PY'
import sys
distance = float(sys.argv[1])
threshold = float(sys.argv[2])
sys.exit(0 if distance >= threshold else 1)
PY
then
    print_error "Travel distance ${RESPAWN_DISTANCE} is below threshold ${MIN_TRAVEL_DISTANCE}"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

if ! python3 - "$RETURN_DELTA" "$MAX_RESPAWN_OFFSET" <<'PY'
import sys
offset = float(sys.argv[1])
threshold = float(sys.argv[2])
sys.exit(0 if offset <= threshold else 1)
PY
then
    print_error "Respawn offset ${RETURN_DELTA} exceeds threshold ${MAX_RESPAWN_OFFSET}"
    display_log "$CLIENT_LOG" "Client"
    print_test_result "$TEST_NAME" "FAIL"
    exit 1
fi

print_success "Respawn distance and location validated"
print_test_result "$TEST_NAME" "PASS"
exit 0
