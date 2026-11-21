#!/usr/bin/env bash
# Packs the client libraries as the FlowRate.* NuGet packages, the meta-package FlowRate.Client among
# them, all with one version; see Client/Client.Package/README.md. CI publishes them on a tag sdk-v<version>
# (.github/workflows/packages.yml); locally the output folder serves as a feed for a bot developed
# alongside: dotnet nuget add source <output> --name flowrate-local.
#
# Usage: ./pack.sh [version] [output]
#        ./pack.sh                       0.0.0-local.<UTC yyMMddHHmm> into artifacts/packages
#        ./pack.sh 0.1.0 /tmp/feed
set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")/../.."

VERSION=${1:-0.0.0-local.$(date -u +%y%m%d%H%M)}
OUTPUT=$(readlink -f "${2:-../artifacts/packages}")

PROJECTS=(
    Shared/Shared.Core
    Client/Client.Platform
    Client/Client.Core
    Client/Client.Application
    Client/Client.WebSockets
    Client/Client.Api
    Client/Client.WebRTC
    Client/Client.LibWebRTC
    Client/Client.GStreamer
    Client/Client.Package
)

command -v dotnet >/dev/null || { echo "error: dotnet is not installed" >&2; exit 1; }
mkdir -p "$OUTPUT"

for project in "${PROJECTS[@]}"; do
    echo "Packing $project $VERSION"
    dotnet pack "$project" -c Release -nologo -v quiet -o "$OUTPUT" -p:FlowRateSdkVersion="$VERSION"
done

echo "Packages in $OUTPUT:"
ls -1 "$OUTPUT" | grep -F ".$VERSION." || true
