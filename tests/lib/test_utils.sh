#!/usr/bin/env zsh

# Test utilities for Arena2006 integration tests

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Global variables
GODOT_BIN=""
PROJECT_ROOT=""
TEST_TIMEOUT=30

# Initialize test utilities
init_test_utils() {
    # Get the absolute path to the project root
    if [ -n "$PROJECT_ROOT" ] && [ "$PROJECT_ROOT" != "" ]; then
        # Already set, use it
        :
    else
        # Calculate from script location - use $0 for zsh compatibility
        local script_path="${(%):-%x}"
        if [ -z "$script_path" ]; then
            script_path="${BASH_SOURCE[0]}"
        fi
        SCRIPT_DIR_LOCAL="$(cd "$(dirname "$script_path")" && pwd)"
        PROJECT_ROOT="$(cd "$SCRIPT_DIR_LOCAL/../.." && pwd)"
    fi
    
    # Find Godot binary
    if command -v godot &> /dev/null; then
        GODOT_BIN="godot"
    elif [ -f "/Applications/Godot.app/Contents/MacOS/Godot" ]; then
        GODOT_BIN="/Applications/Godot.app/Contents/MacOS/Godot"
    else
        echo -e "${RED}ERROR: Godot binary not found${NC}"
        echo "Please ensure Godot is installed or set GODOT_BIN environment variable"
        exit 1
    fi
    
    # Create logs directory if it doesn't exist
    mkdir -p "$PROJECT_ROOT/tests/logs"
    
    # Ensure project is imported (check for .godot directory and UID cache)
    if [ ! -d "$PROJECT_ROOT/.godot" ] || [ ! -f "$PROJECT_ROOT/.godot/uid_cache.bin" ]; then
        print_info "Project not fully imported, importing..."
        
        # Build C# project first
        if [ -f "$PROJECT_ROOT/Arena2006.csproj" ]; then
            print_info "Building C# project..."
            dotnet build "$PROJECT_ROOT/Arena2006.csproj" > "$PROJECT_ROOT/tests/logs/build.log" 2>&1
            if [ $? -ne 0 ]; then
                print_error "Failed to build C# project"
                cat "$PROJECT_ROOT/tests/logs/build.log"
                exit 1
            fi
        fi
        
        # Import project with editor
        print_info "Importing project with Godot editor..."
        $GODOT_BIN --path "$PROJECT_ROOT" --editor --quit-after 10 > "$PROJECT_ROOT/tests/logs/import.log" 2>&1 &
        local import_pid=$!
        
        # Wait for import to complete
        sleep 12
        
        if [ ! -d "$PROJECT_ROOT/.godot" ] || [ ! -f "$PROJECT_ROOT/.godot/uid_cache.bin" ]; then
            print_error "Failed to import project"
            cat "$PROJECT_ROOT/tests/logs/import.log"
            exit 1
        fi
        print_success "Project imported successfully"
    fi
}

# Print colored message
print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

# Start a Godot instance
# Usage: start_godot_instance <mode> <log_file> [--visual]
start_godot_instance() {
    local mode=$1
    local log_file=$2
    local visual=$3
    
    local args="--path $PROJECT_ROOT --headless"
    
    # Add headless flag unless visual mode is requested
    if [ "$visual" = "--visual" ]; then
        args="--path $PROJECT_ROOT"
    fi
    
    # Specify the main scene explicitly
    args="$args res://src/entities/root/game_root.tscn -- --$mode"
    
    print_info "Starting $mode instance (log: $log_file)"
    
    # Start Godot in background and redirect output to log file
    $GODOT_BIN $args > "$log_file" 2>&1 &
    local pid=$!
    
    echo $pid
}

# Wait for a log pattern to appear in a file
# Usage: wait_for_log_pattern <log_file> <pattern> <timeout>
# Returns: 0 if pattern found, 1 if timeout
wait_for_log_pattern() {
    local log_file=$1
    local pattern=$2
    local timeout=$3
    local elapsed=0
    
    print_info "Waiting for pattern: '$pattern' (timeout: ${timeout}s)"
    
    while [ $elapsed -lt $timeout ]; do
        if [ -f "$log_file" ] && grep -q "$pattern" "$log_file"; then
            print_success "Pattern found after ${elapsed}s"
            return 0
        fi
        sleep 0.5
        elapsed=$((elapsed + 1))
    done
    
    print_error "Timeout waiting for pattern: '$pattern'"
    return 1
}

# Check if a log pattern exists in a file
# Usage: check_log_pattern <log_file> <pattern>
# Returns: 0 if pattern found, 1 if not found
check_log_pattern() {
    local log_file=$1
    local pattern=$2
    
    if [ -f "$log_file" ] && grep -q "$pattern" "$log_file"; then
        return 0
    else
        return 1
    fi
}

# Extract value from log pattern
# Usage: extract_log_value <log_file> <pattern> <capture_group>
extract_log_value() {
    local log_file=$1
    local pattern=$2
    
    if [ -f "$log_file" ]; then
        grep -o "$pattern" "$log_file" | head -n 1
    fi
}

# Kill a process and its children
# Usage: kill_process <pid>
kill_process() {
    local pid=$1
    
    if [ -z "$pid" ]; then
        return
    fi
    
    if ps -p $pid > /dev/null 2>&1; then
        print_info "Killing process $pid"
        kill $pid 2>/dev/null || true
        
        # Wait a bit for graceful shutdown
        sleep 1
        
        # Force kill if still running
        if ps -p $pid > /dev/null 2>&1; then
            kill -9 $pid 2>/dev/null || true
        fi
    fi
}

# Cleanup all test processes
# Usage: cleanup_test_processes <pid1> [pid2] [pid3] ...
cleanup_test_processes() {
    print_info "Cleaning up test processes..."
    
    for pid in "$@"; do
        kill_process $pid
    done
    
    # Also kill any stray Godot processes that might be running
    pkill -f "godot.*--server" 2>/dev/null || true
    pkill -f "godot.*--client" 2>/dev/null || true
}

# Print test result
# Usage: print_test_result <test_name> <result>
print_test_result() {
    local test_name=$1
    local result=$2
    
    echo ""
    echo "=========================================="
    if [ "$result" = "PASS" ]; then
        echo -e "${GREEN}TEST RESULT: PASS${NC}"
        echo "Test: $test_name"
    else
        echo -e "${RED}TEST RESULT: FAIL${NC}"
        echo "Test: $test_name"
    fi
    echo "=========================================="
    echo ""
}

# Display log file contents
# Usage: display_log <log_file> <label>
display_log() {
    local log_file=$1
    local label=$2
    
    if [ -f "$log_file" ]; then
        echo ""
        echo "--- $label Log ---"
        cat "$log_file"
        echo "--- End $label Log ---"
        echo ""
    fi
}

