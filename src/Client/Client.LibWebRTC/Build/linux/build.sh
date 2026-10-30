#!/usr/bin/env bash
# Builds libflowrate_webrtc.so for linux-x64 from the sources of WebRTC: libwebrtc (the fork of
# webrtc-sdk), its C++ wrapper (webrtc-sdk/libwebrtc) and the C interface of flowrate_webrtc.h, linked
# into one library. The result goes into ../prebuilt/linux-x64/, which is committed: neither CI nor
# publish.sh builds it, they only copy it. The Windows counterpart is ../windows/build.ps1.
#
# Usage: ./build.sh
# The checkout lives in $FLOWRATE_WEBRTC_DIR (default ~/.cache/flowrate-libwebrtc) and takes about
# 30 GB. The first run downloads it and builds everything (half an hour to a couple of hours); the
# next ones only rebuild what changed, e.g. a few seconds after an edit of flowrate_webrtc.cc.
# Needs git, curl and python3; depot_tools brings the rest (gn, ninja, clang, the sysroot).
set -euo pipefail

# The sources, pinned. A newer WebRTC is taken by changing them together, with the patches of the
# wrapper made for it (custom_audio_source_m<N>.patch)
WRAPPER_TAG=libwebrtc.m150.7871.06
WEBRTC_COMMIT=a545de45259b47dfac5f3635863e47cb601f1bb2 # webrtc-sdk/webrtc, m150_release
WEBRTC_PATCHES=(custom_audio_source_m150 add_libwebrtc_build_target fix_desktop_capture_compile)
# Our patches to WebRTC, in ../patches
FLOWRATE_PATCHES=(flowrate_audio_processing flowrate_keep_sps)

here=$(cd "$(dirname "$0")" && pwd)
native="$here/../../Native"
work=${FLOWRATE_WEBRTC_DIR:-$HOME/.cache/flowrate-libwebrtc}
out_build="$work/out/linux-x64"
prebuilt="$(dirname "$here")/prebuilt/linux-x64"

# Release, without the tests and the parts we do not use: the screens are captured and encoded by
# GStreamer, so neither the desktop capture of the wrapper (PipeWire, X11) nor H.264 of WebRTC is
# built. The C++ library of Chromium is linked in statically and hidden, as in the releases of
# webrtc-sdk, so the library needs only the C runtime of the system. The tests stay declared, as the
# wrapper has a test target that needs them, but only the library is built.
GN_ARGS='
target_os="linux"
target_cpu="x64"
is_debug=false
is_component_build=false
symbol_level=0
enable_stripping=true
enable_iterator_debugging=false
treat_warnings_as_errors=false
use_custom_libcxx=true
use_custom_libcxx_for_host=true
use_llvm_libatomic=false
use_clang_modules=false
use_rtti=true
rtc_include_tests=true
rtc_build_tools=false
rtc_build_examples=false
rtc_enable_protobuf=false
rtc_use_x11=false
rtc_use_pipewire=false
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
target_os = ["linux"]
EOF

    step "Syncing the dependencies of WebRTC"
    gclient sync --no-history --jobs "$(nproc)" -D
    echo "$WEBRTC_COMMIT" > .synced
    rm -f .wrapper
fi

# The wrapper, copied into the tree as src/libwebrtc like its own build does, with our patch that
# links our sources into it. Copied again when the tag or the patch changed
wrapper="$WRAPPER_TAG $(sha256sum "$here/../patches/flowrate_target.patch" | cut -d' ' -f1)"
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

step "Installing the sysroot"
python3 src/build/linux/sysroot_scripts/install-sysroot.py --arch=x64

step "Building"
gn gen "$out_build" --root=src --args="$(echo $GN_ARGS)"
ninja -C "$out_build" libwebrtc

library="$out_build/libflowrate_webrtc.so"

# Every function of the header must be exported, and nothing but the system may be needed
expected=$(grep -E '^FR_API ' "$native/flowrate_webrtc.h" | grep -oE 'fr_[a-z_0-9]+\(' | tr -d '(' | sort -u)
exported=$(nm -D --defined-only "$library" | awk '{print $3}' | grep -E '^fr_' | sort -u)
if [[ "$expected" != "$exported" ]]; then
    echo "error: the exports differ from flowrate_webrtc.h:" >&2
    diff <(echo "$expected") <(echo "$exported") >&2 || true
    exit 1
fi
echo "Exports: $(echo "$exported" | wc -l) functions of flowrate_webrtc.h"
echo "Needs: $(readelf -d "$library" | grep -oP 'NEEDED.*\[\K[^]]+' | tr '\n' ' ')"

step "Copying into $prebuilt"
mkdir -p "$prebuilt"
src/third_party/llvm-build/Release+Asserts/bin/llvm-strip --strip-unneeded -o "$prebuilt/libflowrate_webrtc.so" "$library"
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
libflowrate_webrtc.so, built by Build/linux/build.sh
Date:      $(date -u +%Y-%m-%dT%H:%M:%SZ)
WebRTC:    webrtc-sdk/webrtc $WEBRTC_COMMIT
Wrapper:   webrtc-sdk/libwebrtc $WRAPPER_TAG
Patches:   ${WEBRTC_PATCHES[*]} ${FLOWRATE_PATCHES[*]} flowrate_target
FlowRate:  $flowrate_commit$dirty
SHA-256:   $(sha256sum "$prebuilt/libflowrate_webrtc.so" | cut -d' ' -f1)
GN args:  $(echo $GN_ARGS)
EOF

step "Done"
ls -la "$prebuilt"
