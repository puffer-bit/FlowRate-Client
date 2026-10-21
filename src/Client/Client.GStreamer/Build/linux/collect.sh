#!/usr/bin/env bash
# Runs inside the build image. Copies the GStreamer installed in $PREFIX into <out>/gstreamer with
# the layout the client expects, adds every non-system library it needs, makes the tree relocatable
# through RPATH=$ORIGIN and collects the license texts.
#
#   <out>/gstreamer/lib/                     libraries
#   <out>/gstreamer/lib/gstreamer-1.0/       plugins
#   <out>/gstreamer/lib/gstreamer-1.0-va/            va plugin for a system libva < 2.21
#   <out>/gstreamer/lib/gstreamer-1.0-va-modern/     va plugin and its library for libva 2.21+
#   <out>/gstreamer/lib/gstreamer-1.0-pipewire/        pipewire plugin for libpipewire 0.3.48+
#   <out>/gstreamer/lib/gstreamer-1.0-pipewire-modern/ pipewire plugin for libpipewire 1.2+
#   <out>/gstreamer/libexec/gstreamer-1.0/   gst-plugin-scanner
#   <out>/licenses/, <out>/THIRD-PARTY-NOTICES.txt
#   <out>/tools/gst-inspect-1.0              for verify.sh only, not shipped
set -euo pipefail

PREFIX=${PREFIX:-/opt/gst}
OUT=${1:-/out}
DEST="$OUT/gstreamer"
SRC=/src

# Libraries that belong to the user's system: the C runtime, and everything that has to match the
# installed daemons and drivers (PipeWire, PulseAudio, ALSA, VA-API, DRM, X11/Wayland/GL).
EXCLUDED='^(linux-vdso|ld-linux-x86-64|libc|libm|libmvec|libdl|libpthread|librt|libresolv|libutil|libstdc\+\+|libgcc_s|libz|libpipewire-0\.3|libpulse[a-z-]*|libasound|libva[a-z-]*|libdrm|libX11|libxcb[a-z-]*|libGL[A-Za-z]*|libEGL|libwayland-[a-z]+|libudev|libdbus-1|libsystemd|libcap)\.so'

# Never allowed in the bundle: GPL codecs and libav
FORBIDDEN='^lib(x264|x265|avcodec|avformat|avfilter|avutil|swscale|swresample|postproc|a52|mpeg2|dvdread|faad|mad)\.so'

rm -rf "$DEST" "$OUT/licenses" "$OUT/tools" "$OUT/THIRD-PARTY-NOTICES.txt"
mkdir -p "$DEST/lib/gstreamer-1.0" "$DEST/libexec/gstreamer-1.0" "$OUT/licenses" "$OUT/tools"

declare -A ORIGIN

echo "== Copying GStreamer"
for plugin in "$PREFIX"/lib/gstreamer-1.0/*.so; do
    install -m 644 "$plugin" "$DEST/lib/gstreamer-1.0/"
done
for library in "$PREFIX"/lib/libgst*-1.0.so.0; do
    # The unit test library, built only to satisfy the docs setup of the plugin modules
    [[ "$(basename "$library")" == libgstcheck-1.0.so.0 ]] && continue
    cp -L "$library" "$DEST/lib/"
    ORIGIN[$(basename "$library")]=gstreamer
done
install -m 755 "$PREFIX/libexec/gstreamer-1.0/gst-plugin-scanner" "$DEST/libexec/gstreamer-1.0/"

# Only one va variant may be on the plugin path, Loader/GstLoader.cs adds the one matching the system
mkdir -p "$DEST/lib/gstreamer-1.0-va" "$DEST/lib/gstreamer-1.0-va-modern"
mv "$DEST/lib/gstreamer-1.0/libgstva.so" "$DEST/lib/gstreamer-1.0-va/"
install -m 644 "$PREFIX"/lib/gstreamer-1.0-va-modern/* "$DEST/lib/gstreamer-1.0-va-modern/"
mkdir -p "$DEST/lib/gstreamer-1.0-pipewire-modern"
install -m 755 "$PREFIX"/lib/gstreamer-1.0-pipewire-modern/* "$DEST/lib/gstreamer-1.0-pipewire-modern/"
# Only one of the two builds may be on the plugin path, the loader picks it
mkdir -p "$DEST/lib/gstreamer-1.0-pipewire"
mv "$DEST/lib/gstreamer-1.0/libgstpipewire.so" "$DEST/lib/gstreamer-1.0-pipewire/"
install -m 755 "$PREFIX/bin/gst-inspect-1.0" "$OUT/tools/"

resolve() {
    local dir
    for dir in "$PREFIX/lib" /usr/lib/x86_64-linux-gnu /lib/x86_64-linux-gnu; do
        if [[ -e "$dir/$1" ]]; then
            readlink -f "$dir/$1"
            return 0
        fi
    done
    return 1
}

elf_files() {
    find "$DEST" "$OUT/tools" -type f \( -name '*.so*' -o -perm -u+x \) -exec sh -c 'file -b "$1" | grep -q ^ELF' _ {} \; -print
}

echo "== Resolving dependencies"
changed=1
while (( changed )); do
    changed=0
    while read -r file; do
        while read -r needed; do
            [[ "$needed" =~ $EXCLUDED ]] && continue
            [[ -e "$DEST/lib/$needed" ]] && continue

            if ! path=$(resolve "$needed"); then
                echo "error: $needed needed by $(basename "$file") not found" >&2
                exit 1
            fi

            cp "$path" "$DEST/lib/$needed"
            chmod 644 "$DEST/lib/$needed"
            ORIGIN[$needed]=$path
            echo "  + $needed ($path)"
            changed=1
        done < <(readelf -d "$file" | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p')
    done < <(elf_files)
done

echo "== Checking for forbidden libraries"
if forbidden=$(ls "$DEST/lib" "$DEST/lib/gstreamer-1.0" | grep -E "$FORBIDDEN"); then
    echo "error: GPL or libav libraries in the bundle: $forbidden" >&2
    exit 1
fi

echo "== Making the tree relocatable"
for file in "$DEST"/lib/*.so*; do
    patchelf --set-rpath '$ORIGIN' "$file"
done
for file in "$DEST"/lib/gstreamer-1.0/*.so; do
    patchelf --set-rpath '$ORIGIN/..' "$file"
done
patchelf --set-rpath '$ORIGIN/..' "$DEST/lib/gstreamer-1.0-va/libgstva.so"
patchelf --set-rpath '$ORIGIN/..' "$DEST/lib/gstreamer-1.0-pipewire-modern/libgstpipewire.so"
patchelf --set-rpath '$ORIGIN/..' "$DEST/lib/gstreamer-1.0-pipewire/libgstpipewire.so"
# Its own libgstva-1.0 first: the one in lib/ is built against the older libva
for file in "$DEST"/lib/gstreamer-1.0-va-modern/*; do
    patchelf --set-rpath '$ORIGIN:$ORIGIN/..' "$file"
done
patchelf --set-rpath '$ORIGIN/../../lib' "$DEST/libexec/gstreamer-1.0/gst-plugin-scanner"
patchelf --set-rpath '$ORIGIN/../gstreamer/lib' "$OUT/tools/gst-inspect-1.0"

elf_files | xargs strip --strip-unneeded

echo "== Collecting licenses"
copy_licenses() {
    local name=$1 dir=$2
    mkdir -p "$OUT/licenses/$name"
    find "$dir" -maxdepth 1 -type f \( -iname 'COPYING*' -o -iname 'LICENSE*' -o -iname 'PATENTS*' \) \
        -exec cp {} "$OUT/licenses/$name/" \;
}

copy_licenses gstreamer "$SRC/gstreamer"
copy_licenses usrsctp "$SRC/gstreamer/subprojects/gst-plugins-bad/ext/sctp/usrsctp"
for subproject in libnice openh264 orc webrtc-audio-processing abseil-cpp; do
    for dir in "$SRC"/gstreamer/subprojects/"$subproject"*/; do
        [[ -d "$dir" ]] && copy_licenses "$subproject" "$dir"
    done
done
copy_licenses dav1d "$SRC/dav1d"
copy_licenses svt-av1 "$SRC/svtav1"
copy_licenses pipewire "$SRC/pipewire"
copy_licenses gst-plugins-rs "$SRC/gst-plugins-rs"

{
    echo "Third-party components of the FlowRate client media runtime"
    echo "==========================================================="
    echo
    echo "GStreamer ${GST_VERSION:-} and its plugins: LGPL-2.1-or-later"
    echo "  source: https://gitlab.freedesktop.org/gstreamer/gstreamer"
    echo "gst-plugins-rs (dav1d, rtp): MIT/Apache-2.0 and MPL-2.0"
    echo "  source: https://gitlab.freedesktop.org/gstreamer/gst-plugins-rs"
    echo "PipeWire GStreamer plugin: MIT      source: https://gitlab.freedesktop.org/pipewire/pipewire"
    echo "libnice: LGPL-2.1 / MPL-1.1         source: https://libnice.freedesktop.org"
    echo "OpenH264: BSD-2-Clause              source: https://github.com/cisco/openh264"
    echo "dav1d: BSD-2-Clause                 source: https://code.videolan.org/videolan/dav1d"
    echo "SVT-AV1: BSD-3-Clause-Clear, AOM patent license   source: https://gitlab.com/AOMediaCodec/SVT-AV1"
    echo "orc: BSD-2-Clause, usrsctp: BSD-3-Clause"
    echo "webrtc-audio-processing: BSD-3-Clause   source: https://gitlab.freedesktop.org/pulseaudio/webrtc-audio-processing"
    echo "Abseil: Apache-2.0                  source: https://github.com/abseil/abseil-cpp"
    echo
    echo "The libraries are unmodified and loaded dynamically; any of them can be replaced by the user."
    echo "License texts are in the 'licenses' folder."
    echo
    echo "Bundled libraries from Ubuntu 22.04 (license in licenses/<package>/copyright):"
    for library in $(printf '%s\n' "${!ORIGIN[@]}" | sort); do
        origin=${ORIGIN[$library]}
        [[ "$origin" == gstreamer || "$origin" == "$PREFIX"/* ]] && continue
        # Ubuntu merged /lib into /usr/lib, dpkg may know the file under either path
        package=$( (dpkg -S "$origin" 2>/dev/null || dpkg -S "${origin#/usr}" 2>/dev/null) | head -n1 | cut -d: -f1 || true)
        if [[ -n "$package" ]]; then
            mkdir -p "$OUT/licenses/$package"
            cp "/usr/share/doc/$package/copyright" "$OUT/licenses/$package/" 2>/dev/null || true
            echo "  $library  ($package $(dpkg-query -W -f='${Version}' "$package"))"
        else
            echo "  $library  ($origin)"
        fi
    done
} > "$OUT/THIRD-PARTY-NOTICES.txt"

echo "== Done: $(du -sh "$DEST" | cut -f1) in $DEST"
