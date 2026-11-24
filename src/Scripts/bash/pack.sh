#!/usr/bin/env bash
# Packs the client libraries as the FlowRate.* NuGet packages, the meta-package FlowRate.Client among
# them, all with one version; see Client/Client.Package/README.md. CI publishes them on a tag sdk-v<version>
# (.github/workflows/packages.yml); locally the output folder serves as a feed for a bot developed
# alongside: dotnet nuget add source <output> --name flowrate-local.
#
# With --gstreamer-runtime it packs instead the GStreamer runtime of one RID as
# FlowRate.Client.GStreamer.Runtime.<rid>, from Client.GStreamer/Build/artifacts/<rid>, which the scripts
# of Client.GStreamer/Build produce; see Client.GStreamer/Build/package/README.md. Its version is that of
# GStreamer, by default the one the Linux runtime is built with.
#
# Usage: ./pack.sh [version] [output]
#        ./pack.sh                       0.0.0-local.<UTC yyMMddHHmm> into artifacts/packages
#        ./pack.sh 0.1.0 /tmp/feed
#        ./pack.sh --gstreamer-runtime linux-x64 [version] [output]
set -euo pipefail
CALLER_DIR=$PWD
cd "$(dirname "$(readlink -f "$0")")/../.."
SRC_DIR=$PWD

# The output folder, made if missing: a path given is from where the script was called, the default is
# artifacts/packages of the repository
output_dir() {
    local dir=${1:-$SRC_DIR/../artifacts/packages}
    (cd "$CALLER_DIR" && mkdir -p "$dir" && cd "$dir" && pwd)
}

GSTREAMER_BUILD=Client/Client.GStreamer/Build

gstreamer_rid=
if [[ "${1:-}" == --gstreamer-runtime ]]; then
    gstreamer_rid=${2:?"--gstreamer-runtime needs a RID: linux-x64, win-x64 or osx-arm64"}
    shift 2
fi

command -v dotnet >/dev/null || { echo "error: dotnet is not installed" >&2; exit 1; }

# Lays FlowRate.Client.GStreamer.Runtime.<rid> out in artifacts/<rid>/package and packs it
pack_gstreamer_runtime() {
    local rid=$1 version=$2 output=$3 os scanner
    case "$rid" in
        linux-x64) os=Linux; scanner=gstreamer/libexec/gstreamer-1.0/gst-plugin-scanner ;;
        osx-arm64) os=OSX; scanner=gstreamer/libexec/scanner/gst-plugin-scanner ;;
        win-x64) os=Windows; scanner= ;;
        *) echo "error: no GStreamer runtime for $rid, only linux-x64, win-x64 and osx-arm64" >&2; exit 1 ;;
    esac

    local runtime="$GSTREAMER_BUILD/artifacts/$rid"
    [[ -d "$runtime/gstreamer" ]] ||
        { echo "error: no GStreamer runtime in $runtime, build it first, see $GSTREAMER_BUILD/README.md" >&2; exit 1; }

    local id="FlowRate.Client.GStreamer.Runtime.$rid" stage="$runtime/package"
    rm -rf "$stage"
    mkdir -p "$stage/runtime" "$stage/buildTransitive"

    # The same files Build/GStreamerRuntime.targets gives the client; tools/ is for the checks only
    cp -R "$runtime/gstreamer" "$runtime/licenses" "$stage/runtime/"
    cp "$runtime/THIRD-PARTY-NOTICES.txt" "$stage/runtime/"
    # The ScreenCaptureKit helper of the macOS captures, committed next to the scripts
    if [[ -d "$GSTREAMER_BUILD/prebuilt/$rid" ]]; then
        cp "$GSTREAMER_BUILD/prebuilt/$rid"/* "$stage/runtime/"
    fi

    sed -e "s|@RID_NAME@|${rid//-/_}|g" -e "s|@RID@|$rid|g" -e "s|@OS@|$os|g" -e "s|@SCANNER@|$scanner|g" \
        "$GSTREAMER_BUILD/package/runtime.targets" > "$stage/buildTransitive/$id.targets"
    cp "$GSTREAMER_BUILD/package/README.md" "$stage/"
    cp "$runtime/THIRD-PARTY-NOTICES.txt" "$stage/"
    cp Client/Client.Avalonia.Assets/AppIcons/Monitor/png/app-icon-128.png "$stage/icon.png"

    echo "Packing $id $version"
    # Client.GStreamer only carries the pack: the nuspec decides every file, none of the library goes in.
    # NU5100: the native DLLs of Windows outside lib/ are meant so
    dotnet pack Client/Client.GStreamer -c Release -nologo -v quiet -o "$output" \
        -p:NuspecFile="$(readlink -f "$GSTREAMER_BUILD/package/runtime.nuspec")" \
        -p:NuspecBasePath="$(readlink -f "$stage")" \
        -p:NuspecProperties="rid=$rid;version=$version" \
        -p:IncludeSymbols=false -p:NoWarn=NU5100

    rm -rf "$stage"
}

if [[ -n "$gstreamer_rid" ]]; then
    default_version=$(sed -n 's/^ARG GST_VERSION=//p' "$GSTREAMER_BUILD/linux/Dockerfile")
    VERSION=${1:-$default_version}
    [[ -n "$VERSION" ]] || { echo "error: no GStreamer version given or found in $GSTREAMER_BUILD/linux/Dockerfile" >&2; exit 1; }
    OUTPUT=$(output_dir "${2:-}")
    pack_gstreamer_runtime "$gstreamer_rid" "$VERSION" "$OUTPUT"
    ls -1 "$OUTPUT" | grep -F "FlowRate.Client.GStreamer.Runtime.$gstreamer_rid.$VERSION." || true
    exit 0
fi

VERSION=${1:-0.0.0-local.$(date -u +%y%m%d%H%M)}
OUTPUT=$(output_dir "${2:-}")

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

for project in "${PROJECTS[@]}"; do
    echo "Packing $project $VERSION"
    dotnet pack "$project" -c Release -nologo -v quiet -o "$OUTPUT" -p:FlowRateSdkVersion="$VERSION"
done

echo "Packages in $OUTPUT:"
ls -1 "$OUTPUT" | grep -F ".$VERSION." || true
