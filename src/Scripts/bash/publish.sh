#!/usr/bin/env bash
# Builds the self-contained FlowRate client on Linux, for Linux, Windows and/or Android: the GStreamer
# runtime, the client with its own .NET runtime, then everything to publish in dist/<version>/<platform>/
# (platform linux-x64, linux-x64-aot, win-x64 or android). The result needs neither .NET nor GStreamer on
# the target machine. The Android client is one .apk.
#
# Usage: ./publish.sh                     linux-x64 (the default)
#        ./publish.sh --target win-x64    Windows build
#        ./publish.sh --target android    Android build (.apk, arm64-v8a and x86_64)
#        ./publish.sh --target all        all three
#        ./publish.sh linux-x64           the target also goes without --target
#        ./publish.sh --aot               compiled ahead of time (NativeAOT) instead of the JIT build
#        ./publish.sh --gstreamer         rebuild the linux-x64 GStreamer runtime too
#        ./publish.sh --notes FILE        what is new in this version, Markdown, shown by the updater
#        ./publish.sh --dev               a development build: <version>-dev.<UTC yyMMddHHmm>
#        ./publish.sh --dev --stamp STAMP the dev build stamped STAMP (yyMMddHHmm) instead of now: the
#                                         builds of one release on Linux and Windows share it
#
# A desktop platform folder holds two forms of the client:
#  - the installed one, made by vpk (Velopack, a local dotnet tool of the repository), which updates
#    itself: FlowRateClient-linux.AppImage or FlowRateClient-win-Setup.exe, plus the packages and the
#    releases.<channel>.json its updater takes;
#  - the standalone one, FlowRateClient-<version>-<platform>-standalone.tar.gz or .zip: unpacked and run
#    as is, it does not update itself.
# Every file of the folder goes to the GitHub release v<version> as it is; a dev build is published as a
# pre-release, which only clients set to the dev builds take.
# Every variant has an update channel of its own (linux, linux-aot, win, win-aot), so a client updates to
# the same variant it is. The last build of a channel is kept in dist/.feed/<channel>[-dev] and vpk makes
# a delta package from it; with none there, the latest release of the channel is downloaded from GitHub
# (GITHUB_TOKEN, if set, lets it read a private repository). A dev build is below the release of the
# same number, so raise <Version> in the project after a release, or the dev builds of the next one are
# not newer for the clients.
#
# The linux-x64 runtime is built here in Docker when missing. The win-x64 runtime can only be made on
# Windows (Build/windows/collect.ps1): copy its artifacts/win-x64 folder here first.
# NativeAOT does not compile for another system, so --aot builds linux-x64 only; it needs clang or gcc.
# The Android build has no GStreamer runtime and no --aot. It needs the android workload of dotnet
# (dotnet workload install android), the Android SDK and a JDK. With no keystore given the .apk is signed
# with the debug key: it installs, but a store does not take it. A release key goes in the variables
# ANDROID_KEYSTORE (the file), ANDROID_KEY_ALIAS, ANDROID_KEYSTORE_PASSWORD and ANDROID_KEY_PASSWORD.
# Needs the .NET 10 SDK, Docker for the linux-x64 runtime, python3, and curl, unzip and g++ for libwebrtc.
set -euo pipefail
script=$(readlink -f "$0")
cd "$(dirname "$script")/../../.."

PROJECT=src/Client/Client.Avalonia.Desktop/Client.Avalonia.Desktop.csproj
GSTREAMER_DIR=src/Client/Client.GStreamer/Build
LIBWEBRTC_DIR=src/Client/Client.LibWebRTC
PUBLISH_ROOT=src/Client/Client.Avalonia.Desktop/bin/publish
ANDROID_PROJECT=src/Client/Client.Avalonia.Android/Client.Avalonia.Android.csproj
ANDROID_PUBLISH_DIR=src/Client/Client.Avalonia.Android/bin/publish/android
ICON=src/Client/Client.Avalonia.Desktop/Assets/avalonia-logo.ico
FEED_DIR=dist/.feed
REPO_URL=https://github.com/puffer-bit/FlowRate-Client
# The installed application goes to %LocalAppData%\FlowRateClient on Windows; the data of the user
# are in the FlowRate folder apart from it (AppDirectories), so an update or uninstall leaves them
PACK_ID=FlowRateClient

target=linux-x64
rebuild_gstreamer=0
aot=0
dev=0
notes=
stamp=
while (( $# )); do
    case "$1" in
        --target) target=${2:-}; shift ;;
        --target=*) target=${1#*=} ;;
        linux-x64|win-x64|android|all) target=$1 ;;
        --aot) aot=1 ;;
        --gstreamer) rebuild_gstreamer=1 ;;
        --dev) dev=1 ;;
        --stamp) stamp=${2:-}; shift ;;
        --stamp=*) stamp=${1#*=} ;;
        --notes) notes=${2:-}; shift ;;
        --notes=*) notes=${1#*=} ;;
        -h|--help) sed -n '2,41p' "$script"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
    shift
done

case "$target" in
    linux-x64|win-x64|android) targets=("$target") ;;
    all) targets=(linux-x64 win-x64 android) ;;
    *) echo "Unknown target '$target', use linux-x64, win-x64, android or all" >&2; exit 1 ;;
esac

command -v dotnet >/dev/null || { echo "error: dotnet is not installed" >&2; exit 1; }
if [[ -n "$notes" && ! -f "$notes" ]]; then echo "error: no release notes in $notes" >&2; exit 1; fi
if [[ -n "$stamp" ]]; then
    (( dev )) || { echo "error: --stamp is for a --dev build" >&2; exit 1; }
    [[ "$stamp" =~ ^[0-9]{10}$ ]] || { echo "error: --stamp takes yyMMddHHmm, not '$stamp'" >&2; exit 1; }
fi
if [[ " ${targets[*]} " != " android " ]]; then
    command -v python3 >/dev/null || { echo "error: python3 is needed to pack the releases" >&2; exit 1; }
    # vpk, of the version in dotnet-tools.json: the same as the Velopack library of the client
    dotnet tool restore >/dev/null
fi

# The publish profile is <rid>-jit or <rid>-aot; the channel of the release ends in -aot for the latter
mode=jit
suffix=
publish_arguments=()
if (( aot )); then
    if [[ " ${targets[*]} " == *" win-x64 "* ]]; then
        echo "error: a NativeAOT build for win-x64 can only be made on Windows (publish.ps1 -Aot)" >&2
        exit 1
    fi
    if [[ " ${targets[*]} " == *" android "* ]]; then
        echo "error: --aot is for the desktop client, the Android build has no NativeAOT variant" >&2
        exit 1
    fi
    mode=aot
    suffix=-aot
    # The native linker: clang by default, gcc when there is none
    if ! command -v clang >/dev/null; then
        command -v gcc >/dev/null || { echo "error: NativeAOT needs clang or gcc" >&2; exit 1; }
        publish_arguments+=(-p:CppCompilerAndLinker=gcc)
    fi
fi
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT" | head -n1)
# The moment in UTC, as in the build number: a later dev build has the greater version
(( dev )) && version="$version-dev.${stamp:-$(date -u +%y%m%d%H%M)}"
version_dir="dist/$version"

# Makes sure artifacts/<rid> holds a GStreamer runtime, building the Linux one when needed
prepare_gstreamer() {
    local rid=$1 runtime="$GSTREAMER_DIR/artifacts/$1/gstreamer"

    if [[ "$rid" == win-x64 ]]; then
        if [[ ! -d "$runtime" ]]; then
            echo "error: no Windows GStreamer runtime in $runtime" >&2
            echo "It can only be built on Windows: run $GSTREAMER_DIR/windows/collect.ps1 there and copy" >&2
            echo "its artifacts/win-x64 folder to $GSTREAMER_DIR/artifacts/ here." >&2
            exit 1
        fi
        (( rebuild_gstreamer )) && echo "Note: --gstreamer only rebuilds the linux-x64 runtime, win-x64 is used as is"
        echo "######## Using the Windows GStreamer runtime ($runtime)"
        return
    fi

    if (( rebuild_gstreamer )) || [[ ! -d "$runtime" ]]; then
        command -v docker >/dev/null || { echo "error: docker is needed to build the GStreamer runtime" >&2; exit 1; }
        echo "######## Building the Linux GStreamer runtime"
        "$GSTREAMER_DIR/linux/build.sh"
    else
        echo "######## Using the existing Linux GStreamer runtime ($runtime), --gstreamer rebuilds it"
    fi
}

# Puts the libwebrtc runtime into the build of <rid>: the voice of the experimental setting. Optional,
# the client falls back to GStreamer without it. The Linux one takes seconds, so it is built every time
prepare_libwebrtc() {
    local rid=$1

    if [[ "$rid" == linux-x64 ]]; then
        echo "######## Building the libwebrtc runtime"
        "$LIBWEBRTC_DIR/Native/build.sh"
    elif [[ ! -d "$LIBWEBRTC_DIR/Build/artifacts/$rid/libwebrtc" ]]; then
        echo "Note: no libwebrtc runtime for $rid, its voice goes over GStreamer only"
    fi
}

# The folder vpk works in, removed also when the script stops on an error
vpk_work=
trap 'rm -rf "${vpk_work:-}"' EXIT

# Works on the update feed of a channel (releases.<channel>.json and the packages it lists):
#   feed prune <dir> <channel> <version>        empties <dir> unless all it holds is older than <version>
#   feed take <dir> <channel> <version> <to>    copies the packages of <version> to <to>, with a feed of
#                                               that version alone: the release on GitHub lists only its
#                                               own files
feed() {
    python3 - "$@" <<'PY'
import json, os, shutil, sys

def order(version):
    # SemVer precedence: a pre-release is below its release, numeric identifiers compare as numbers
    main, _, pre = version.partition("-")
    numbers = tuple(int(part) for part in main.split("."))
    if not pre:
        return numbers, 1, ()
    return numbers, 0, tuple((0, int(part), "") if part.isdigit() else (1, 0, part) for part in pre.split("."))

command, folder, channel, version = sys.argv[1:5]
path = os.path.join(folder, f"releases.{channel}.json")
assets = json.load(open(path, encoding="utf-8"))["Assets"] if os.path.exists(path) else []

if command == "prune":
    if any(order(asset["Version"]) >= order(version) for asset in assets):
        for name in os.listdir(folder):
            os.remove(os.path.join(folder, name))
elif command == "take":
    target = sys.argv[5]
    os.makedirs(target, exist_ok=True)
    own = [asset for asset in assets if asset["Version"] == version]
    for asset in own:
        shutil.copy2(os.path.join(folder, asset["FileName"]), target)
    with open(os.path.join(target, f"releases.{channel}.json"), "w", encoding="utf-8") as file:
        json.dump({"Assets": own}, file)
PY
}

# The standalone form: the published folder in an archive with a single FlowRateClient-<version> folder
pack_standalone() {
    local rid=$1 publish_dir=$2 archive=$3 name="FlowRateClient-$version"
    if [[ "$rid" == linux-x64 ]]; then
        tar -C "$publish_dir" -czf "$archive" --transform "s,^\.,$name," .
    else
        python3 - "$publish_dir" "$archive" "$name" <<'PY'
import os, sys, zipfile
source, archive, name = sys.argv[1:]
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
    for folder, _, files in os.walk(source):
        for file in files:
            path = os.path.join(folder, file)
            package.write(path, os.path.join(name, os.path.relpath(path, source)))
PY
    fi
}

# Both forms of a published folder in dist/<version>/<platform>/
release() {
    local rid=$1 publish_dir=$2 os directive=() exe icon channel platform output last token=() release_notes=() pre=() portable=()
    case "$rid" in
        linux-x64) os=linux exe=Client.Avalonia.Desktop ;;
        # Packed here for Windows: vpk builds the installer on Linux as well
        # No portable bundle of vpk there: the standalone archive is the form that is not installed;
        # on Linux the AppImage is the only bundle
        win-x64) os=win exe=Client.Avalonia.Desktop.exe directive=("[win]") portable=(--noPortable) ;;
    esac
    channel=$os$suffix
    platform=$rid$suffix
    output="$version_dir/$platform"
    # Dev builds keep a feed of their own: a release makes its delta from the previous release
    last="$FEED_DIR/$channel"
    (( dev )) && last="$last-dev"
    rm -rf "$output"
    mkdir -p "$output" "$last"
    [[ -n "${GITHUB_TOKEN:-}" ]] && token=(--token "$GITHUB_TOKEN")
    [[ -n "$notes" ]] && release_notes=(--releaseNotes "$notes")
    (( dev )) && pre=(--pre)

    if [[ "$os" == linux ]]; then
        # The AppImage takes a PNG: the 256 px image of the icon is one already
        icon="$PUBLISH_ROOT/icon.png"
        python3 - "$ICON" "$icon" <<'PY'
import struct, sys
data = open(sys.argv[1], "rb").read()
count = struct.unpack_from("<HHH", data, 0)[2]
images = [struct.unpack_from("<BBBBHHII", data, 6 + 16 * i) for i in range(count)]
width, _, _, _, _, _, size, offset = max(images, key=lambda image: image[0] or 256)
if data[offset:offset + 4] != b"\x89PNG":
    sys.exit("the largest image of the icon is not a PNG")
open(sys.argv[2], "wb").write(data[offset:offset + size])
PY
    else
        icon=$ICON
    fi

    echo "######## Packing $platform in the update channel $channel"
    # vpk works in a folder of its own with the previous build alone, for the delta
    vpk_work=$(mktemp -d)
    local work=$vpk_work
    cp -a "$last/." "$work/"
    if [[ ! -e "$work/releases.$channel.json" ]]; then
        # Quiet: no release yet, or a private repository without a token, is the usual failure here
        dotnet vpk "${directive[@]}" download github --repoUrl "$REPO_URL" --channel "$channel" -o "$work" \
            "${token[@]}" "${pre[@]}" >/dev/null 2>&1 || true
    fi
    # A rebuild of the same version, or of an older one, has nothing to make a delta from
    feed prune "$work" "$channel" "$version"
    [[ -e "$work/releases.$channel.json" ]] ||
        echo "Note: no earlier build of $channel, the release has no delta package"

    dotnet vpk "${directive[@]}" pack --packId "$PACK_ID" --packVersion "$version" --packDir "$publish_dir" \
        --mainExe "$exe" --packTitle FlowRate --packAuthors puffer-bit --icon "$icon" \
        --channel "$channel" -o "$work" "${portable[@]}" "${release_notes[@]}"

    feed take "$work" "$channel" "$version" "$output"
    # FlowRateClient-<channel>.AppImage or FlowRateClient-<channel>-Setup.exe
    cp "$work"/*.AppImage "$work"/*-Setup.exe "$output/" 2>/dev/null || true
    compgen -G "$output/*.AppImage" >/dev/null || compgen -G "$output/*-Setup.exe" >/dev/null ||
        { echo "error: vpk made no installer in $work" >&2; exit 1; }
    # The next build makes its delta from this one
    rm -rf "$last"
    feed take "$work" "$channel" "$version" "$last"
    rm -rf "$work"
    vpk_work=

    echo "######## Packing the standalone $platform"
    pack_standalone "$rid" "$publish_dir" \
        "$output/FlowRateClient-$version-$platform-standalone.$([[ "$os" == linux ]] && echo tar.gz || echo zip)"
}

# The Android client: a Release .apk for every ABI of the project, copied to dist/<version>/android/
publish_android() {
    local android_version name signing=()
    android_version=$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$ANDROID_PROJECT" | head -n1)
    name="FlowRateClient-$android_version-android.apk"

    echo "######## FlowRate client $android_version for android"
    dotnet workload list | grep -q '^android ' ||
        { echo "error: the android workload is missing, run: dotnet workload install android" >&2; exit 1; }

    if [[ -n "${ANDROID_KEYSTORE:-}" ]]; then
        [[ -f "$ANDROID_KEYSTORE" ]] || { echo "error: no keystore in $ANDROID_KEYSTORE" >&2; exit 1; }
        : "${ANDROID_KEY_ALIAS:?ANDROID_KEY_ALIAS is not set}"
        : "${ANDROID_KEYSTORE_PASSWORD:?ANDROID_KEYSTORE_PASSWORD is not set}"
        # env: the passwords stay out of the command line
        signing=(-p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=$(readlink -f "$ANDROID_KEYSTORE")"
            "-p:AndroidSigningKeyAlias=$ANDROID_KEY_ALIAS" -p:AndroidSigningStorePass=env:ANDROID_KEYSTORE_PASSWORD
            -p:AndroidSigningKeyPass=env:ANDROID_KEY_PASSWORD)
        export ANDROID_KEYSTORE_PASSWORD ANDROID_KEY_PASSWORD=${ANDROID_KEY_PASSWORD:-$ANDROID_KEYSTORE_PASSWORD}
    else
        echo "Note: ANDROID_KEYSTORE is not set, the .apk is signed with the debug key"
    fi

    echo "######## Publishing the client for android"
    rm -rf "$ANDROID_PUBLISH_DIR"
    # The signing step is skipped while its output is there, a change of the key alone does not rerun it
    rm -f "$(dirname "$ANDROID_PROJECT")"/bin/Release/*/*-Signed.apk
    dotnet publish "$ANDROID_PROJECT" -c Release -o "$ANDROID_PUBLISH_DIR" -nologo -nodeReuse:false \
        -p:UseSharedCompilation=false "${signing[@]}"

    echo "######## Packing android"
    mkdir -p "$version_dir/android"
    archive="$version_dir/android/$name"
    cp "$ANDROID_PUBLISH_DIR"/*-Signed.apk "$archive"
    echo "Folder:  $ANDROID_PUBLISH_DIR"
    echo "Archive: $archive ($(du -h "$archive" | cut -f1))"
}

for rid in "${targets[@]}"; do
    if [[ "$rid" == android ]]; then
        publish_android
        continue
    fi

    echo "######## FlowRate client $version for $rid ($mode)"
    prepare_gstreamer "$rid"
    prepare_libwebrtc "$rid"

    # dotnet publish ignores a missing profile and publishes elsewhere
    profile="$(dirname "$PROJECT")/Properties/PublishProfiles/$rid-$mode.pubxml"
    [[ -f "$profile" ]] || { echo "error: no publish profile $profile" >&2; exit 1; }

    publish_dir="$PUBLISH_ROOT/$rid$suffix"
    echo "######## Publishing the client for $rid"
    rm -rf "$publish_dir"
    # -r as well: the RID of the profile alone does not reach the restore
    dotnet publish "$PROJECT" -p:PublishProfile="$rid-$mode" -r "$rid" -nologo "${publish_arguments[@]}"

    release "$rid" "$publish_dir"
    echo "Published: $publish_dir"
    echo "Release:   $version_dir/$rid$suffix"
done

echo "######## Done: $version_dir"
