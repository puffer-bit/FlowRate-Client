#!/usr/bin/env bash
# Removes everything dotnet builds: the bin/ and obj/ folders of every project in src/. Stops the
# build servers first, they keep files of obj/ open. The next build starts with a full restore.
#
# Usage: ./clean.sh
#
# Only folders next to a .csproj are removed: the GStreamer runtime in Client.GStreamer/Build/artifacts
# has a bin/ folder of its own (the Windows DLLs) and stays as it is, and so does dist/.
set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")/../.."

command -v dotnet >/dev/null && dotnet build-server shutdown >/dev/null 2>&1 || true

count=0
while IFS= read -r -d '' project; do
    for folder in "$(dirname "$project")"/{bin,obj}; do
        [[ -d "$folder" ]] || continue
        rm -rf "$folder"
        count=$((count + 1))
    done
done < <(find . -name '*.csproj' -not -path '*/Build/artifacts/*' -print0)

echo "Removed $count folders"
