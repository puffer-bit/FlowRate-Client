#!/usr/bin/env bash
# Builds the GStreamer runtime shipped with the client for osx-arm64 from the official macOS framework
# (https://gstreamer.freedesktop.org/download, the runtime .pkg), installed in /Library/Frameworks.
# Copies the plugins the client needs together with the libraries they link, keeps only their arm64
# code, signs them ad hoc and checks the result in isolation from the installed framework.
#
# The libraries of the framework link each other through @rpath, and the rpaths of a plugin and of
# the scanner point at ../lib from where they are: the layout below keeps those paths valid. The
# folders of the plugins and of the scanner have no dot in their names, unlike those of the framework:
# codesign takes a folder with an extension inside FlowRate.app for a nested bundle and fails.
#
# Output in ../artifacts:
#   osx-arm64/gstreamer/{lib, lib/plugins, libexec/scanner}   shipped with the client
#   osx-arm64/licenses, osx-arm64/THIRD-PARTY-NOTICES.txt
#   osx-arm64/tools/gst-inspect-1.0                                        for the check only, not shipped
#
# Usage: ./collect.sh [framework root, default /Library/Frameworks/GStreamer.framework/Versions/1.0]
set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
ROOT=${1:-/Library/Frameworks/GStreamer.framework/Versions/1.0}
OUT="$SCRIPT_DIR/../artifacts/osx-arm64"
DEST="$OUT/gstreamer"
ARCH=arm64

[[ "$(uname -s)" == Darwin ]] || { echo "error: the macOS runtime is collected on macOS" >&2; exit 1; }
[[ -f "$ROOT/lib/libgstreamer-1.0.0.dylib" ]] ||
    { echo "error: no GStreamer framework in $ROOT, install the runtime .pkg of gstreamer.freedesktop.org" >&2; exit 1; }

# Plugins the client builds its pipelines from (file libgst<name>.dylib). No GPL plugin is on the list.
plugins=(
    isomp4 matroska avi playback typefindfunctions id3demux audioparsers wavparse mpg123
    coreelements app audioconvert audioresample audiomixer audiofx audiotestsrc volume level rawparse
    audiobuffersplit webrtcdsp autodetect videoconvertscale videorate
    opus videoparsersbad openh264 vpx svtav1 dav1d rsaudiofx
    jpeg osxaudio applemedia
)

# The binding imports these by name, even when no plugin needs them
bound=(
    libgstreamer-1.0.0 libgstbase-1.0.0 libgstapp-1.0.0 libgstvideo-1.0.0 libgstaudio-1.0.0 libgstrtp-1.0.0
    libgstpbutils-1.0.0 libgstcontroller-1.0.0 libgstnet-1.0.0 libgsttag-1.0.0
    libglib-2.0.0 libgobject-2.0.0 libgmodule-2.0.0 libgio-2.0.0
)

# License folders (share/licenses/<name>) of the components in the bundle
license_packages=(
    gstreamer-1.0 gst-plugins-base-1.0 gst-plugins-bad-1.0 gst-plugins-rs
    libjpeg-turbo mpg123 glib libffi proxy-libintl pcre2 zlib orc
    opus webrtc-audio-processing abseil-cpp openh264 libvpx dav1d svt-av1 bzip2 moltenvk
)

# Never allowed in the bundle
forbidden='^(lib)?(x264|x265|avcodec|avformat|avfilter|avutil|swscale|swresample|postproc|a52|mpeg2|dvdread|faad|mad)'

version=$("$ROOT/bin/gst-inspect-1.0" --version | sed -n 's/^GStreamer //p')
echo "== GStreamer $version from $ROOT"

rm -rf "$OUT"
mkdir -p "$DEST/lib/plugins" "$DEST/libexec/scanner" "$OUT/tools"

echo "== Copying plugins"
for plugin in "${plugins[@]}"; do
    file="$ROOT/lib/gstreamer-1.0/libgst$plugin.dylib"
    [[ -f "$file" ]] || { echo "error: plugin $plugin is not in the framework" >&2; exit 1; }
    cp "$file" "$DEST/lib/plugins/"
done
cp "$ROOT/libexec/gstreamer-1.0/gst-plugin-scanner" "$DEST/libexec/scanner/"
for library in "${bound[@]}"; do
    cp "$ROOT/lib/$library.dylib" "$DEST/lib/"
done
# Next to the bundle, it finds its libraries there through an rpath added below
cp "$ROOT/bin/gst-inspect-1.0" "$OUT/tools/"

echo "== Resolving dependencies"
# Everything a file links through @rpath comes from lib of the framework; the rest is the system
while :; do
    added=0
    while read -r file; do
        while read -r name; do
            # A library lists its own install name too
            [[ "$name" == "$(basename "$file")" || -e "$DEST/lib/$name" ]] && continue
            [[ -f "$ROOT/lib/$name" ]] || { echo "error: $(basename "$file") links $name, not in $ROOT/lib" >&2; exit 1; }
            cp "$ROOT/lib/$name" "$DEST/lib/"
            echo "  + $name"
            added=$((added + 1))
        done < <(otool -L "$file" | sed -n 's|^[[:space:]]*@rpath/\([^ ]*\) .*|\1|p')
        if otool -L "$file" | grep -q '/Library/Frameworks/GStreamer.framework'; then
            echo "error: $(basename "$file") links the installed framework by an absolute path" >&2
            exit 1
        fi
    done < <(find "$DEST" "$OUT/tools" -type f)
    (( added == 0 )) && break
done

bad=$(find "$DEST" -type f | xargs -n1 basename | grep -E "$forbidden" || true)
[[ -z "$bad" ]] || { echo "error: GPL or libav libraries in the bundle: $bad" >&2; exit 1; }

echo "== Keeping $ARCH only and signing ad hoc"
install_name_tool -add_rpath @executable_path/../gstreamer/lib "$OUT/tools/gst-inspect-1.0" 2>/dev/null
while read -r file; do
    if [[ "$(lipo -archs "$file")" != "$ARCH" ]]; then
        lipo -thin "$ARCH" "$file" -output "$file.thin"
        mv "$file.thin" "$file"
    fi
    # The signature of the framework does not survive thinning, and arm64 runs no unsigned code
    codesign --force --sign - "$file" 2>/dev/null
done < <(find "$DEST" "$OUT/tools" -type f)

echo "== Collecting licenses"
mkdir -p "$OUT/licenses"
missing=()
for package in "${license_packages[@]}"; do
    if [[ -d "$ROOT/share/licenses/$package" ]]; then
        cp -R "$ROOT/share/licenses/$package" "$OUT/licenses/"
    else
        missing+=("$package")
    fi
done
# Abseil is linked statically into webrtcdsp, and the framework carries no license folder for it
if [[ " ${missing[*]} " == *" abseil-cpp "* ]]; then
    mkdir -p "$OUT/licenses/abseil-cpp"
    curl -fsSL https://raw.githubusercontent.com/abseil/abseil-cpp/master/LICENSE -o "$OUT/licenses/abseil-cpp/LICENSE"
    missing=("${missing[@]/abseil-cpp}")
    missing=(${missing[@]})
fi
(( ${#missing[@]} == 0 )) || echo "Note: license folders not in the framework: ${missing[*]}"

{
    echo "Third-party components of the FlowRate client media runtime"
    echo "==========================================================="
    echo
    echo "GStreamer $version and its plugins (LGPL-2.1-or-later), taken from the official macOS"
    echo "framework of https://gstreamer.freedesktop.org/download, reduced to their arm64 code"
    echo "Sources: https://gitlab.freedesktop.org/gstreamer/gstreamer, build recipes:"
    echo "https://gitlab.freedesktop.org/gstreamer/cerbero (tag $version)"
    echo
    echo "The libraries are loaded dynamically; any of them can be replaced by the user."
    echo "License texts are in the 'licenses' folder."
    echo
    echo "Bundled files:"
    (cd "$DEST" && find . -type f | sed 's|^\./|  |' | sort)
} > "$OUT/THIRD-PARTY-NOTICES.txt"

echo "== Verifying in isolation"
# The environment Loader/GstLoader.cs sets, so nothing of the installed framework is used
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/gio-modules"
export GST_PLUGIN_SYSTEM_PATH_1_0="$DEST/lib/plugins"
export GST_PLUGIN_PATH_1_0=""
export GST_PLUGIN_SCANNER_1_0="$DEST/libexec/scanner/gst-plugin-scanner"
export GST_REGISTRY_1_0="$work/registry.bin"
export GIO_MODULE_DIR="$work/gio-modules"
inspect="$OUT/tools/gst-inspect-1.0"

failed=0
while read -r line; do
    line=${line%%#*}
    line=$(echo "$line" | xargs)
    [[ -z "$line" ]] && continue
    if [[ "$line" == \?* ]]; then
        "$inspect" --exists "${line#\?}" || echo "  optional element missing: ${line#\?}"
    else
        "$inspect" --exists "$line" || { echo "FAIL: required element missing: $line" >&2; failed=1; }
    fi
done < "$SCRIPT_DIR/required-elements.txt"

for file in "$DEST"/lib/plugins/*.dylib; do
    filename=$("$inspect" "$file" 2>&1 | sed -n 's/^[[:space:]]*Filename[[:space:]]*//p')
    [[ "$filename" == "$DEST"/* ]] || { echo "FAIL: $(basename "$file") loaded from ${filename:-nowhere}" >&2; failed=1; }
done
# Every library comes from the bundle, none from the installed framework
if DYLD_PRINT_LIBRARIES=1 "$inspect" coreelements 2>&1 >/dev/null | grep -q '/Library/Frameworks/GStreamer.framework'; then
    echo "FAIL: the installed framework was loaded" >&2
    failed=1
fi
(( failed == 0 )) || { echo "error: the bundle failed the check" >&2; exit 1; }

echo "== Done: $OUT ($(du -sh "$DEST" | cut -f1))"
