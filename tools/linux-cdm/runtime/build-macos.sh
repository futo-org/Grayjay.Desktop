#!/bin/bash
set -euo pipefail
exec "$(dirname "$0")/blink/build-macos.sh" "$@"
