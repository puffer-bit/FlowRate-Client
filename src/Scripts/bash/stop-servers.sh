#!/usr/bin/env bash
# Stops the build servers dotnet leaves behind (MSBuild nodes, the compiler server) and shows how much
# memory the ones still alive hold. Those belong to a running IDE, which starts its own.
#
# Usage: ./stop-servers.sh
set -euo pipefail

# Prints the number of build server processes and their memory
report() {
    ps -eo rss=,args= | awk -v label="$1" '
        /MSBuild\.dll|VBCSCompiler/ && !/awk/ { count++; rss += $1 }
        END { printf "%s %d processes, %.0f MiB\n", label, count, rss / 1024 }'
}

command -v dotnet >/dev/null || { echo "error: dotnet is not installed" >&2; exit 1; }
report "Before:"
dotnet build-server shutdown >/dev/null 2>&1 || true
report "After: "
