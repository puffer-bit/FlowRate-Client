#!/usr/bin/env bash
# Builds and runs the checks of the native sources that need nothing of WebRTC, with the compiler of
# the system. Usage: ./run.sh
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT

c++ -std=c++17 -Wall -Wextra -Werror -o "$out/flowrate_sdp_test" "$here/flowrate_sdp_test.cc" "$here/../flowrate_sdp.cc"
"$out/flowrate_sdp_test"
