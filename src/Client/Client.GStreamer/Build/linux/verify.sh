#!/usr/bin/env bash
# Checks a collected bundle the way the client uses it. Meant for a clean container without any
# GStreamer installed (see build.sh), but works on any machine: the environment below isolates the
# bundle from an installed GStreamer exactly like Loader/GstLoader.cs does.
set -euo pipefail

OUT=${1:-/out}
DEST="$OUT/gstreamer"
SCRIPT_DIR=$(dirname "$(readlink -f "$0")")
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

failed=0
fail() { echo "FAIL: $*" >&2; failed=1; }

# The PipeWire plugin needs the system libpipewire; without it, it may fail to load
has_pipewire=0
ldconfig -p | grep -q 'libpipewire-0\.3\.so\.0' && has_pipewire=1

# The va variant the client would pick here: the modern one needs vaMapBuffer2 from libva 2.21+
va_dir="$DEST/lib/gstreamer-1.0-va"
libva=$(ldconfig -p | awk '/libva\.so\.2 /{print $NF; exit}')
if [[ -n "$libva" ]] && grep -q vaMapBuffer2 "$libva"; then
    va_dir="$DEST/lib/gstreamer-1.0-va-modern"
fi
echo "== va variant: $(basename "$va_dir") (system libva: ${libva:-none})"

mkdir -p "$WORK/gio-modules"
export GST_PLUGIN_SYSTEM_PATH_1_0="$DEST/lib/gstreamer-1.0:$va_dir"
export GST_PLUGIN_PATH_1_0=""
export GST_PLUGIN_SCANNER_1_0="$DEST/libexec/gstreamer-1.0/gst-plugin-scanner"
export GST_REGISTRY_1_0="$WORK/registry.bin"
export GIO_MODULE_DIR="$WORK/gio-modules"
unset LD_LIBRARY_PATH
inspect="$OUT/tools/gst-inspect-1.0"

echo "== Unresolved libraries"
while read -r file; do
    missing=$(ldd "$file" 2>/dev/null | grep 'not found' || true)
    if [[ -n "$missing" && ( "$(basename "$file")" != libgstpipewire.so || $has_pipewire == 1 ) ]]; then
        fail "$(basename "$file"): $missing"
    fi
done < <(find "$DEST" -type f -name '*.so*'; echo "$DEST/libexec/gstreamer-1.0/gst-plugin-scanner")

echo "== Version"
"$inspect" --version | head -n2

echo "== Elements"
while read -r line; do
    line=${line%%#*}
    line=$(echo "$line" | xargs)
    [[ -z "$line" ]] && continue

    if [[ "$line" == \?* ]]; then
        "$inspect" --exists "${line#\?}" || echo "  optional element missing: ${line#\?}"
    else
        "$inspect" --exists "$line" || fail "required element missing: $line"
    fi
done < "$SCRIPT_DIR/required-elements.txt"

echo "== Plugins: load from the bundle, licenses"
# By file rather than by registry name: hardware plugins without their device register no
# elements, and gst-inspect leaves them out of its list, but they must still load.
for file in "$DEST"/lib/gstreamer-1.0/*.so "$va_dir/libgstva.so"; do
    plugin=$(basename "$file" .so)
    plugin=${plugin#libgst}

    if ! details=$("$inspect" "$file" 2>&1); then
        if [[ "$plugin" == pipewire && $has_pipewire == 0 ]]; then
            echo "  $plugin: not loadable, no libpipewire on this system"
        else
            fail "plugin $plugin does not load: $(tail -n1 <<< "$details")"
        fi
        continue
    fi

    filename=$(sed -n 's/^\s*Filename\s*//p' <<< "$details")
    license=$(sed -n 's/^\s*License\s*//p' <<< "$details")

    [[ "$filename" != "$DEST"/* ]] && fail "plugin $plugin loaded from outside the bundle: $filename"
    if [[ "$license" == *GPL* && "$license" != *LGPL* ]]; then
        fail "plugin $plugin has license $license"
    fi
    printf '  %-20s %s\n' "$plugin" "$license"
done

if (( failed )); then
    echo "== Verification FAILED" >&2
    exit 1
fi
echo "== Verification passed"
