#!/usr/bin/env bash
# Builds the whole solution, tests included. The GStreamer runtime is not built here, see
# Client/Client.GStreamer/Build/README.md; the release archives are made by publish.sh next to this script.
#
# Usage: ./build.sh                   Debug build
#        ./build.sh -c Release        another configuration
#        ./build.sh --no-restore      anything else goes to dotnet build as is
#
# No MSBuild node and no compiler server outlives the build.
set -euo pipefail
script=$(readlink -f "$0")
cd "$(dirname "$script")/../.."

configuration=Debug
arguments=()
while (( $# )); do
    case "$1" in
        -c|--configuration) configuration=${2:-}; shift ;;
        -h|--help) sed -n '2,9p' "$script"; exit 0 ;;
        *) arguments+=("$1") ;;
    esac
    shift
done

command -v dotnet >/dev/null || { echo "error: dotnet is not installed" >&2; exit 1; }
dotnet build FlowRateClient.sln -c "$configuration" -nologo -nodeReuse:false -p:UseSharedCompilation=false "${arguments[@]}"
