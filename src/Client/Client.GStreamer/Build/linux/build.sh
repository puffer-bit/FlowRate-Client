#!/usr/bin/env bash
# Builds the GStreamer runtime for linux-x64 in Docker and checks it in a clean container.
# Nothing is installed on this machine; the result lands in ../artifacts:
#   artifacts/linux-x64/                              unpacked bundle (used by the client build)
#   artifacts/gstreamer-<version>-linux-x64.tar.zst   the same, packed
#
# Usage: ./build.sh           build (repeated runs reuse the Docker layer cache)
#        ./build.sh --clean   remove the build image and the Docker build cache
set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"

GST_VERSION=$(sed -n 's/^ARG GST_VERSION=//p' Dockerfile)
IMAGE="flowrate-gstreamer-build:$GST_VERSION"

if [[ "${1:-}" == "--clean" ]]; then
    docker image rm "$IMAGE" 2>/dev/null || true
    docker builder prune -f
    exit 0
fi

ARTIFACTS=$(readlink -f ../artifacts)
OUT="$ARTIFACTS/linux-x64"
ARCHIVE="$ARTIFACTS/gstreamer-$GST_VERSION-linux-x64.tar.zst"

echo "==== Building $IMAGE (the first run takes a while)"
docker build -t "$IMAGE" .

echo "==== Collecting the bundle"
rm -rf "$OUT"
mkdir -p "$OUT"
docker run --rm --user "$(id -u):$(id -g)" -v "$OUT:/out" "$IMAGE" /build/collect.sh /out

# Only what a desktop system has anyway: the sound, VA-API and PipeWire client libraries.
# Ubuntu 22.04 is the oldest supported system (libva 2.14, libpipewire 0.3.48, base va variant);
# Debian 13 is a current one (libva 2.22, libpipewire 1.4, modern va variant).
verify_in() {
    local image=$1 packages=$2
    echo "==== Verifying in a clean $image"
    docker run --rm -v "$OUT:/out:ro" -v "$PWD:/scripts:ro" "$image" bash -c "
        set -e
        apt-get update -qq
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends $packages file >/dev/null
        /scripts/verify.sh /out"
}

verify_in ubuntu:22.04 "libpulse0 libasound2 libva2 libva-drm2 libdrm2 libpipewire-0.3-0"
verify_in debian:trixie "libpulse0 libasound2t64 libva2 libva-drm2 libdrm2 libpipewire-0.3-0t64"

echo "==== Packing"
tar -C "$OUT" -I 'zstd -19 -T0' -cf "$ARCHIVE" gstreamer licenses THIRD-PARTY-NOTICES.txt

echo "==== Done"
echo "Bundle:  $OUT"
echo "Archive: $ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"
