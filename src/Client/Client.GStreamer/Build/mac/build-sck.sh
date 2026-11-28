#!/usr/bin/env bash
# Builds libflowrate_sck.dylib for osx-arm64 from ../../Native/mac: the capture of screens, windows and
# the sound of applications through ScreenCaptureKit, behind the C interface of flowrate_sck.h. The
# result goes into ../prebuilt/osx-arm64/, which is committed: neither CI nor publish build it, they only copy it.
#
# Usage: ./build-sck.sh
# Needs Xcode or its command line tools. Takes a few seconds.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
native="$here/../../Native/mac"
prebuilt="$(dirname "$here")/prebuilt/osx-arm64"
output="$prebuilt/libflowrate_sck.dylib"

mkdir -p "$prebuilt"

# The minimum of FlowRate.app (LSMinimumSystemVersion); only system frameworks are linked
xcrun clang -dynamiclib -arch arm64 -mmacosx-version-min=14.0 -fobjc-arc -O2 -Wall -Wextra -Wno-unused-parameter -Werror \
    -fvisibility=hidden \
    -install_name @rpath/libflowrate_sck.dylib \
    -framework Foundation -framework AppKit -framework CoreGraphics -framework CoreMedia \
    -framework CoreVideo -framework ScreenCaptureKit \
    -o "$output" "$native/flowrate_sck.m"

strip -x "$output"
codesign --force --sign - "$output"

echo "Built $output ($(du -h "$output" | cut -f1))"
