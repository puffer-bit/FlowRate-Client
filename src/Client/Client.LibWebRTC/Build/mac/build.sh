#!/usr/bin/env bash
# Builds libflowrate_webrtc.dylib for osx-arm64 from the sources of WebRTC: libwebrtc (the fork of
# webrtc-sdk), its C++ wrapper (webrtc-sdk/libwebrtc) and the C interface of flowrate_webrtc.h, linked
# into one library. The result goes into ../prebuilt/osx-arm64/, which is committed: neither CI nor
# publish.sh builds it, they only copy it. The counterpart of ../linux/build.sh, run on a Mac.
#
# Usage: ./build.sh
# The checkout lives in $FLOWRATE_WEBRTC_DIR (default ~/.cache/flowrate-libwebrtc) and takes about
# 30 GB. The first run downloads it and builds everything (half an hour to a couple of hours); the
# next ones only rebuild what changed, e.g. a few seconds after an edit of flowrate_peer.cc.
# Needs Xcode (its macOS SDK; Chromium compiles with its own clang), git, curl and python3;
# depot_tools brings the rest (gn, ninja, clang).
set -euo pipefail

# The sources, pinned. A newer WebRTC is taken by changing them together, with the patches of the
# wrapper made for it (custom_audio_source_m<N>.patch)
WRAPPER_TAG=libwebrtc.m150.7871.06
WEBRTC_COMMIT=a545de45259b47dfac5f3635863e47cb601f1bb2 # webrtc-sdk/webrtc, m150_release
WEBRTC_PATCHES=(custom_audio_source_m150 add_libwebrtc_build_target)
# Our patches to WebRTC, in ../patches
FLOWRATE_PATCHES=(flowrate_keep_sps)

here=$(cd "$(dirname "$0")" && pwd)
native="$here/../../Native"
work=${FLOWRATE_WEBRTC_DIR:-$HOME/.cache/flowrate-libwebrtc}
out_build="$work/out/osx-arm64"
prebuilt="$(dirname "$here")/prebuilt/osx-arm64"
jobs=$(sysctl -n hw.ncpu)
sha256() { shasum -a 256 "$1" | cut -d' ' -f1; }

# Release, without the tests and the parts we do not use: the screens are captured and encoded by
# GStreamer, so neither the desktop capture of the wrapper (ScreenCaptureKit) nor H.264 of WebRTC is
# built, and GStreamer plays and records the sound, so neither are the audio devices of the platform
# (CoreAudio): flowrate_audio.cc is the only one. The C++ library of Chromium is linked in statically
# and hidden, as in the releases of webrtc-sdk, so the library needs only the system. No tests:
# flowrate_target.patch declares the test target of the wrapper only with them.
GN_ARGS='
target_os="mac"
target_cpu="arm64"
is_debug=false
is_component_build=false
symbol_level=0
enable_stripping=true
enable_iterator_debugging=false
treat_warnings_as_errors=false
use_custom_libcxx=true
use_custom_libcxx_for_host=true
use_clang_modules=false
use_rtti=true
rtc_include_tests=false
rtc_build_tools=false
rtc_build_examples=false
rtc_enable_protobuf=false
rtc_include_internal_audio_device=false
libwebrtc_desktop_capture=false
'

step() { echo "######## $*"; }

mkdir -p "$work"
cd "$work"

if [[ ! -d depot_tools ]]; then
    step "Downloading depot_tools"
    git clone --depth 1 https://chromium.googlesource.com/chromium/tools/depot_tools.git
fi
export PATH="$work/depot_tools:$PATH"

# WebRTC itself: fetched at the pinned commit alone, then its dependencies by gclient. A sync is
# needed only when the commit changed; it resets the tree, so the patches go on again after it
if [[ "$(cat .synced 2>/dev/null)" != "$WEBRTC_COMMIT" ]]; then
    step "Fetching WebRTC $WEBRTC_COMMIT"
    if [[ ! -d src/.git ]]; then
        git init -q src
        git -C src remote add origin https://github.com/webrtc-sdk/webrtc.git
    fi
    git -C src fetch --depth 1 origin "$WEBRTC_COMMIT"
    git -C src checkout -q -f FETCH_HEAD
    git -C src clean -q -fd

    cat > .gclient <<EOF
solutions = [
  {
    "name": "src",
    "url": "https://github.com/webrtc-sdk/webrtc.git@$WEBRTC_COMMIT",
    "deps_file": "DEPS",
    "managed": False,
    "custom_deps": {},
  },
]
target_os = ["mac"]
EOF

    step "Syncing the dependencies of WebRTC"
    gclient sync --no-history --jobs "$jobs" -D
    echo "$WEBRTC_COMMIT" > .synced
    rm -f .wrapper
fi

# The wrapper, copied into the tree as src/libwebrtc like its own build does, with our patch that
# links our sources into it. Copied again when the tag or the patch changed
wrapper="$WRAPPER_TAG $(sha256 "$here/../patches/flowrate_target.patch")"
if [[ "$(cat .wrapper 2>/dev/null)" != "$wrapper" ]]; then
    step "Fetching the wrapper $WRAPPER_TAG"
    rm -rf wrapper src/libwebrtc
    git clone -q --depth 1 --branch "$WRAPPER_TAG" https://github.com/webrtc-sdk/libwebrtc.git wrapper
    mkdir -p src/libwebrtc
    cp -r wrapper/{include,src,patches,BUILD.gn,LICENSE} src/libwebrtc/
    git -C src apply "$here/../patches/flowrate_target.patch"
    echo "$wrapper" > .wrapper
fi

# The patches of the wrapper to WebRTC, each applied once: applying them again would touch the files
# and rebuild half of WebRTC
for patch in "${WEBRTC_PATCHES[@]}"; do
    file="src/libwebrtc/patches/$patch.patch"
    if ! git -C src apply --reverse --check --ignore-whitespace "$PWD/$file" 2>/dev/null; then
        step "Applying $patch"
        git -C src apply --ignore-whitespace --whitespace=nowarn "$PWD/$file"
    fi
done

for patch in "${FLOWRATE_PATCHES[@]}"; do
    file="$here/../patches/$patch.patch"
    if ! git -C src apply --reverse --check --ignore-whitespace "$file" 2>/dev/null; then
        step "Applying $patch"
        git -C src apply --ignore-whitespace --whitespace=nowarn "$file"
    fi
done

# Our sources, copied only when they changed so that ninja rebuilds nothing else
mkdir -p src/libwebrtc/flowrate
for source in "$native"/flowrate_*.{cc,h}; do
    file=$(basename "$source")
    cmp -s "$source" "src/libwebrtc/flowrate/$file" || cp "$source" "src/libwebrtc/flowrate/$file"
done
# and one removed or renamed here goes from there too
for file in src/libwebrtc/flowrate/*; do
    [[ -e "$native/$(basename "$file")" ]] || rm -f "$file"
done

if ! xcrun --sdk macosx --show-sdk-path >/dev/null 2>&1; then
    echo "error: no macOS SDK: install Xcode and select it (sudo xcode-select -s /Applications/Xcode.app)" >&2
    exit 1
fi

step "Building"
gn gen "$out_build" --root=src --args="$(echo $GN_ARGS)"
ninja -C "$out_build" libwebrtc

library="$out_build/libflowrate_webrtc.dylib"

# Every function of the header must be exported, and nothing but the system may be needed. Mach-O
# names carry a leading underscore
expected=$(grep -E '^FR_API ' "$native/flowrate_webrtc.h" | grep -oE 'fr_[a-z_0-9]+\(' | tr -d '(' | sort -u)
exported=$(nm -gU "$library" | awk '{print $3}' | sed 's/^_//' | grep -E '^fr_' | sort -u)
if [[ "$expected" != "$exported" ]]; then
    echo "error: the exports differ from flowrate_webrtc.h:" >&2
    diff <(echo "$expected") <(echo "$exported") >&2 || true
    exit 1
fi
echo "Exports: $(echo "$exported" | wc -l) functions of flowrate_webrtc.h"
needs=$(otool -L "$library" | tail -n +2 | awk '{print $1}' | grep -v flowrate_webrtc)
echo "Needs: $(echo "$needs" | tr '\n' ' ')"
if echo "$needs" | grep -vqE '^(/usr/lib/|/System/Library/)'; then
    echo "error: the library needs more than the system" >&2
    exit 1
fi

step "Copying into $prebuilt"
mkdir -p "$prebuilt"
# Loaded by its path from next to the executable. Stripping breaks the signature, without which
# Apple silicon loads nothing, so the library is signed again (ad hoc; the .app signs it for real)
cp "$library" "$prebuilt/libflowrate_webrtc.dylib"
chmod u+w "$prebuilt/libflowrate_webrtc.dylib"
install_name_tool -id @rpath/libflowrate_webrtc.dylib "$prebuilt/libflowrate_webrtc.dylib"
strip -x "$prebuilt/libflowrate_webrtc.dylib"
codesign --force --sign - "$prebuilt/libflowrate_webrtc.dylib"
{
    echo "webrtc-sdk/libwebrtc (the C++ wrapper):"
    echo
    cat src/libwebrtc/LICENSE
    echo
    echo "WebRTC:"
    echo
    cat src/LICENSE
} > "$prebuilt/LICENSE.libwebrtc"

flowrate_commit=$(git -C "$here" rev-parse --short HEAD 2>/dev/null || echo unknown)
dirty=$(git -C "$here" status --porcelain -- ../../Native ../patches . 2>/dev/null | grep -q . && echo " (with uncommitted changes)" || true)
cat > "$prebuilt/BUILD-INFO.txt" <<EOF
libflowrate_webrtc.dylib, built by Build/mac/build.sh
Date:      $(date -u +%Y-%m-%dT%H:%M:%SZ)
WebRTC:    webrtc-sdk/webrtc $WEBRTC_COMMIT
Wrapper:   webrtc-sdk/libwebrtc $WRAPPER_TAG
Audio:     GStreamer capture/playback, external PCM ABI 1
Patches:   ${WEBRTC_PATCHES[*]} ${FLOWRATE_PATCHES[*]} flowrate_target
FlowRate:  $flowrate_commit$dirty
macOS:     $(sw_vers -productVersion), SDK $(xcrun --sdk macosx --show-sdk-version)
SHA-256:   $(sha256 "$prebuilt/libflowrate_webrtc.dylib")
GN args:  $(echo $GN_ARGS)
EOF

step "Done"
ls -la "$prebuilt"
