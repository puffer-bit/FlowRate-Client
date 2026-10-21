#!/usr/bin/env bash
# Builds the desktop client and starts it.
#
# Usage: ./run.sh                     Debug build
#        ./run.sh -c Release          another configuration
#        ./run.sh -- <arguments>      everything after -- goes to the client
#
# A Debug build runs on the installed GStreamer when the bundled runtime for this platform is missing.
set -euo pipefail
script=$(readlink -f "$0")
cd "$(dirname "$script")/../.."

PROJECT=Client/Client.Avalonia.Desktop

configuration=Debug
while (( $# )); do
    case "$1" in
        -c|--configuration) configuration=${2:-}; shift ;;
        -h|--help) sed -n '2,8p' "$script"; exit 0 ;;
        --) shift; break ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
    shift
done

command -v dotnet >/dev/null || { echo "error: dotnet is not installed" >&2; exit 1; }
dotnet build "$PROJECT" -c "$configuration" -nologo -v quiet -nodeReuse:false -p:UseSharedCompilation=false
exec dotnet run --project "$PROJECT" -c "$configuration" --no-build -- "$@"
