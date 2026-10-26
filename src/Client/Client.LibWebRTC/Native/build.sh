#!/usr/bin/env bash
# Puts the libwebrtc runtime of the linux-x64 client into ../Build/artifacts/linux-x64/libwebrtc/:
# libwebrtc.so, the prebuilt of webrtc-sdk/libwebrtc, and libflowrate_webrtc.so, the C interface of
# flowrate_webrtc.h built against it. LibWebRTCRuntime.targets copies both next to the executable.
#
# Usage: ./build.sh
# Needs curl, unzip and a C++17 compiler (g++ or clang++).
set -euo pipefail

# The release of webrtc-sdk/libwebrtc and the SHA-256 of its linux-x64 archive. A newer one is taken
# by changing both together
VERSION=m150.7871.06
SHA256=2a5c34427ddf8cef0104ae7cd4fa8c3752eead4b09d32a73bf68956b8e21d3ac

here=$(cd "$(dirname "$0")" && pwd)
build="$here/../Build"
downloads="$build/downloads"
out="$build/artifacts/linux-x64/libwebrtc"
archive="$downloads/libwebrtc-$VERSION-linux-x64-release.zip"
sdk="$downloads/libwebrtc-$VERSION-linux-x64"

mkdir -p "$downloads" "$out"

if [[ ! -f "$archive" ]] || ! echo "$SHA256  $archive" | sha256sum -c --status; then
    echo "######## Downloading libwebrtc $VERSION"
    curl -fL --retry 3 -o "$archive.part" \
        "https://github.com/webrtc-sdk/libwebrtc/releases/download/libwebrtc.$VERSION/libwebrtc-linux-x64-release.zip"
    echo "$SHA256  $archive.part" | sha256sum -c --quiet
    mv "$archive.part" "$archive"
fi

if [[ ! -d "$sdk" ]]; then
    rm -rf "$sdk.part"
    unzip -q "$archive" -d "$sdk.part"
    mv "$sdk.part/linux-x64-release" "$sdk"
    rm -rf "$sdk.part"
fi

echo "######## Building libflowrate_webrtc.so"
cxx=${CXX:-$(command -v g++ || command -v clang++)}

# The library is looked up next to the interface, wherever the client is unpacked
"$cxx" -std=c++17 -O2 -fPIC -shared -fvisibility=hidden -Wall \
    -I"$sdk/include" "$here/flowrate_webrtc.cc" \
    -L"$sdk/lib" -lwebrtc -Wl,-rpath,'$ORIGIN' -Wl,--no-undefined \
    -o "$out/libflowrate_webrtc.so"

cp "$sdk/lib/libwebrtc.so" "$out/"
cp "$sdk/LICENSE" "$out/LICENSE.libwebrtc"
strip --strip-unneeded "$out/libflowrate_webrtc.so"

echo "######## Done: $out"
ls -la "$out"
