#!/usr/bin/env bash
# Runs the tests of the whole solution.
#
# Usage: ./test.sh                                  every test project
#        ./test.sh --filter Name~Gate               anything goes to dotnet test as is
#        ./test.sh -c Release
#
# No MSBuild node and no compiler server outlives the run.
set -euo pipefail
script=$(readlink -f "$0")
cd "$(dirname "$script")/../.."

case "${1:-}" in -h|--help) sed -n '2,8p' "$script"; exit 0 ;; esac

command -v dotnet >/dev/null || { echo "error: dotnet is not installed" >&2; exit 1; }
dotnet test FlowRateClient.sln -nologo -nodeReuse:false -p:UseSharedCompilation=false "$@"
