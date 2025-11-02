#!/bin/zsh

# Generic test runner for Arena2006 integration tests
# Usage: ./run_test.sh <test_script> [--visual]

# Source test utilities
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/lib/test_utils.sh"

# Initialize test utilities
init_test_utils

# Parse arguments
TEST_SCRIPT=$1
VISUAL_MODE=""

if [ -z "$TEST_SCRIPT" ]; then
    print_error "Usage: $0 <test_script> [--visual]"
    exit 1
fi

if [ ! -f "$TEST_SCRIPT" ]; then
    print_error "Test script not found: $TEST_SCRIPT"
    exit 1
fi

# Check for visual mode flag
shift
while [ $# -gt 0 ]; do
    case $1 in
        --visual)
            VISUAL_MODE="--visual"
            print_info "Visual mode enabled"
            ;;
        *)
            print_warning "Unknown argument: $1"
            ;;
    esac
    shift
done

# Export variables for test scripts
export GODOT_BIN
export PROJECT_ROOT
export VISUAL_MODE
export SCRIPT_DIR

# Run the test script
print_info "Running test: $TEST_SCRIPT"
echo ""

# Execute the test script with zsh
zsh "$TEST_SCRIPT"
TEST_RESULT=$?

# Exit with test result
exit $TEST_RESULT

