#!/usr/bin/env bash
# Builds the self-contained FlowRate client on Linux, for Linux, Windows and/or Android, or on macOS for
# macOS: the GStreamer runtime, the client with its own .NET runtime, then everything to publish in
# dist/<version>/<platform>/ (platform linux-x64, linux-x64-aot, win-x64, osx-arm64, osx-arm64-aot or
# android). The result needs neither .NET nor GStreamer on the target machine. The Android client is one .apk.
#
# Usage: ./publish.sh                     linux-x64 (the default)
#        ./publish.sh --target win-x64    Windows build
#        ./publish.sh --target android    Android build (.apk, arm64-v8a and x86_64)
#        ./publish.sh --target all        all three
#        ./publish.sh --target osx-arm64  macOS build (Apple silicon), only on macOS
#        ./publish.sh linux-x64           the target also goes without --target
#        ./publish.sh --aot               compiled ahead of time (NativeAOT) instead of the JIT build
#        ./publish.sh --gstreamer         rebuild the linux-x64 or osx-arm64 GStreamer runtime too
#        ./publish.sh --notes FILE        what is new in this version, Markdown, shown by the updater
#        ./publish.sh --dev               a development build: <version>-dev.<UTC yyMMddHHmm>
#        ./publish.sh --dev --stamp STAMP the dev build stamped STAMP (yyMMddHHmm) instead of now: the
#                                         builds of one release on Linux and Windows share it
#
# A desktop platform folder holds two forms of the client:
#  - the installed one, made by vpk (Velopack, a local dotnet tool of the repository), which updates
#    itself: FlowRateClient-linux.AppImage, FlowRateClient-win-Setup.exe or FlowRateClient-osx-Setup.pkg,
#    plus the packages and the releases.<channel>.json its updater takes;
#  - the standalone one, FlowRateClient-<version>-<platform>-standalone.tar.gz or .zip: unpacked and run
#    as is, it does not update itself. On macOS it is FlowRate.app in a .zip.
# On Linux both get flowrate-linux.sh, which adds the client to the application menu: inside the archive,
# and as FlowRateClient-<platform>-install.sh next to the AppImage.
# Every file of the folder goes to the GitHub release v<version> as it is; a dev build is published as a
# pre-release, which only clients set to the dev builds take.
# Every variant has an update channel of its own (linux, linux-aot, win, win-aot, osx, osx-aot), so a client updates to
# the same variant it is. The last build of a channel is kept in dist/.feed/<channel>[-dev] and vpk makes
# a delta package from it; with none there, the latest release of the channel is downloaded from GitHub
# (GITHUB_TOKEN, if set, lets it read a private repository). A dev build is below the release of the
# same number, so raise <Version> in the project after a release, or the dev builds of the next one are
# not newer for the clients.
#
# The linux-x64 runtime is built here in Docker when missing. The win-x64 runtime can only be made on
# Windows (Build/windows/collect.ps1): copy its artifacts/win-x64 folder here first.
# The osx-arm64 runtime is collected on macOS from the installed GStreamer framework
# (Build/mac/collect.sh). The macOS build is signed ad hoc only: on another Mac the first start is
# allowed in System Settings, Privacy & Security, or with xattr -dr com.apple.quarantine FlowRate.app.
# NativeAOT does not compile for another system, so --aot builds linux-x64 on Linux and osx-arm64 on
# macOS; it needs clang or gcc.
# The Android build has no GStreamer runtime and no --aot. It needs the android workload of dotnet
# (dotnet workload install android), the Android SDK and a JDK. With no keystore given the .apk is signed
# with the debug key: it installs, but a store does not take it. A release key goes in the variables
# ANDROID_KEYSTORE (the file), ANDROID_KEY_ALIAS, ANDROID_KEYSTORE_PASSWORD and ANDROID_KEY_PASSWORD, or
# in the file Signing/signing.env of the Android project, which builds from the IDE read as well.
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
ICON=src/Client/Client.Avalonia.Assets/AppIcons/Headphones/flowrate.ico
MAC_ICON=src/Client/Client.Avalonia.Assets/AppIcons/Headphones/png/app-icon-1024.png
APP_ICONS=src/Client/Client.Avalonia.Assets/AppIcons/Headphones
LINUX_SCRIPT=src/Client/Client.Avalonia.Desktop/Linux/flowrate-linux.sh
MAC_PLIST=src/Client/Client.Avalonia.Desktop/macOS/Info.plist
MAC_ENTITLEMENTS=src/Client/Client.Avalonia.Desktop/macOS/FlowRate.entitlements
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
        linux-x64|win-x64|osx-arm64|android|all) target=$1 ;;
        --aot) aot=1 ;;
        --gstreamer) rebuild_gstreamer=1 ;;
        --dev) dev=1 ;;
        --stamp) stamp=${2:-}; shift ;;
        --stamp=*) stamp=${1#*=} ;;
        --notes) notes=${2:-}; shift ;;
        --notes=*) notes=${1#*=} ;;
        -h|--help) sed -n '2,46p' "$script"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
    shift
done

case "$target" in
    linux-x64|win-x64|osx-arm64|android) targets=("$target") ;;
    all) targets=(linux-x64 win-x64 android) ;;
    *) echo "Unknown target '$target', use linux-x64, win-x64, osx-arm64, android or all" >&2; exit 1 ;;
esac

# vpk packs for the system it runs on, and the macOS runtime and bundle need the tools of macOS
if [[ "$(uname -s)" == Darwin ]]; then
    [[ " ${targets[*]} " == " osx-arm64 " ]] || { echo "error: on macOS only --target osx-arm64 is built" >&2; exit 1; }
elif [[ " ${targets[*]} " == *" osx-arm64 "* ]]; then
    echo "error: the osx-arm64 build can only be made on macOS" >&2
    exit 1
fi

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

    if [[ "$rid" == osx-arm64 ]]; then
        if (( rebuild_gstreamer )) || [[ ! -d "$runtime" ]]; then
            echo "######## Collecting the macOS GStreamer runtime"
            "$GSTREAMER_DIR/mac/collect.sh"
        else
            echo "######## Using the existing macOS GStreamer runtime ($runtime), --gstreamer collects it again"
        fi
        return
    fi

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

# Checks the libwebrtc runtime of <rid>: the only transport of the sessions, so a build without it
# cannot join one. It is committed (Client.LibWebRTC/Build builds it by hand), so nothing is built here
prepare_libwebrtc() {
    local rid=$1

    if [[ ! -d "$LIBWEBRTC_DIR/Build/prebuilt/$rid" ]]; then
        echo "error: no libwebrtc runtime for $rid in $LIBWEBRTC_DIR/Build/prebuilt/$rid" >&2
        exit 1
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

# flowrate-linux.sh at <file> with the icons appended after its line ICONS_PAYLOAD, in the hicolor sizes
linux_script() {
    local file=$1 icons size
    icons=$(mktemp -d)
    for size in 16 24 32 48 64 128 256 512; do
        mkdir -p "$icons/hicolor/${size}x$size/apps"
        cp "$APP_ICONS/png/app-icon-$size.png" "$icons/hicolor/${size}x$size/apps/flowrate.png"
    done
    mkdir -p "$icons/hicolor/scalable/apps"
    cp "$APP_ICONS/svg/app-icon.svg" "$icons/hicolor/scalable/apps/flowrate.svg"
    { cat "$LINUX_SCRIPT"; echo ICONS_PAYLOAD; tar -C "$icons" -cz --owner=0 --group=0 hicolor | base64 -w 76; } > "$file"
    chmod 755 "$file"
    rm -rf "$icons"
}

# The standalone form: the published folder in an archive with a single FlowRateClient-<version> folder
pack_standalone() {
    local rid=$1 publish_dir=$2 archive=$3 name="FlowRateClient-$version" extra
    if [[ "$rid" == linux-x64 ]]; then
        # The script goes into the archive only, the published folder stays as vpk packed it
        extra=$(mktemp -d)
        linux_script "$extra/flowrate-linux.sh"
        tar -czf "$archive" --transform "s,^\.,$name," -C "$publish_dir" . -C "$extra" ./flowrate-linux.sh
        rm -rf "$extra"
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
    local rid=$1 publish_dir=$2 os directive=() exe icon channel platform output last token=() release_notes=() pre=() portable=() bundle=()
    case "$rid" in
        linux-x64) os=linux exe=FlowRate ;;
        # Packed here for Windows: vpk builds the installer on Linux as well
        # No portable bundle of vpk there: the standalone archive is the form that is not installed;
        # on Linux the AppImage is the only bundle
        win-x64) os=win exe=FlowRate.exe directive=("[win]") portable=(--noPortable) ;;
        # FlowRate.app is made here (make_app), with the Info.plist that asks for the microphone
        osx-arm64) os=osx exe=FlowRate ;;
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
    elif [[ "$os" == osx ]]; then
        # vpk takes the bundle made here as it is; the icon is only for its options
        make_app "$publish_dir" "$PUBLISH_ROOT/$platform-app"
        publish_dir="$PUBLISH_ROOT/$platform-app/FlowRate.app"
        icon="$publish_dir/Contents/Resources/FlowRate.icns"
        # Signed ad hoc: no Apple certificate, but the bundle is sealed, which the privacy settings
        # of macOS go by. The hardened runtime vpk signs with needs the entitlements of the microphone
        # and the camera, or macOS denies them without asking
        bundle=(--signAppIdentity - --signEntitlements "$MAC_ENTITLEMENTS")
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
        dotnet vpk ${directive[@]+"${directive[@]}"} download github --repoUrl "$REPO_URL" --channel "$channel" -o "$work" \
            ${token[@]+"${token[@]}"} ${pre[@]+"${pre[@]}"} >/dev/null 2>&1 || true
    fi
    # A rebuild of the same version, or of an older one, has nothing to make a delta from
    feed prune "$work" "$channel" "$version"
    [[ -e "$work/releases.$channel.json" ]] ||
        echo "Note: no earlier build of $channel, the release has no delta package"

    dotnet vpk ${directive[@]+"${directive[@]}"} pack --packId "$PACK_ID" --packVersion "$version" --packDir "$publish_dir" \
        --mainExe "$exe" --packTitle FlowRate --packAuthors puffer-bit --icon "$icon" \
        --channel "$channel" -o "$work" ${portable[@]+"${portable[@]}"} ${release_notes[@]+"${release_notes[@]}"} \
        ${bundle[@]+"${bundle[@]}"}

    feed take "$work" "$channel" "$version" "$output"
    # FlowRateClient-<channel>.AppImage, FlowRateClient-<channel>-Setup.exe or FlowRateClient-<channel>-Setup.pkg
    cp "$work"/*.AppImage "$work"/*-Setup.exe "$work"/*-Setup.pkg "$output/" 2>/dev/null || true
    compgen -G "$output/*.AppImage" >/dev/null || compgen -G "$output/*-Setup.exe" >/dev/null ||
        compgen -G "$output/*-Setup.pkg" >/dev/null || { echo "error: vpk made no installer in $work" >&2; exit 1; }
    # On macOS the portable bundle of vpk is the standalone form: FlowRate.app in a .zip
    local standalone="$output/FlowRateClient-$version-$platform-standalone.$([[ "$os" == linux ]] && echo tar.gz || echo zip)"
    [[ "$os" == osx ]] && cp "$work"/*-Portable.zip "$standalone"
    # The next build makes its delta from this one
    rm -rf "$last"
    feed take "$work" "$channel" "$version" "$last"
    rm -rf "$work"
    vpk_work=

    if [[ "$os" != osx ]]; then
        echo "######## Packing the standalone $platform"
        pack_standalone "$rid" "$publish_dir" "$standalone"
    fi
    # Downloaded next to the AppImage, it installs that one
    if [[ "$os" == linux ]]; then
        linux_script "$output/FlowRateClient-$platform-install.sh"
    fi
}

# FlowRate.app in <folder> of a published folder. Contents/MacOS holds the published files, except the
# licenses: codesign takes their folders with a dot in the name (gstreamer-1.0) for nested bundles, so
# they are in Resources, linked from where the client looks for them
make_app() {
    local publish_dir=$1 folder=$2 contents
    contents="$folder/FlowRate.app/Contents"
    rm -rf "$folder"
    mkdir -p "$contents/MacOS" "$contents/Resources"
    cp -R "$publish_dir/." "$contents/MacOS/"
    mv "$contents/MacOS/licenses" "$contents/Resources/licenses"
    ln -s ../Resources/licenses "$contents/MacOS/licenses"
    sed -e "s/@VERSION@/$version/g" -e "s/@SHORT_VERSION@/${version%%-*}/g" "$MAC_PLIST" > "$contents/Info.plist"
    make_icns "$contents/Resources/FlowRate.icns"
}

# The icon of the macOS bundle, with the margin of a macOS app icon around the artwork (824 of 1024)
make_icns() {
    local icns=$1 work size
    work=$(mktemp -d)
    mkdir "$work/FlowRate.iconset"
    sips -Z 824 "$MAC_ICON" --out "$work/artwork.png" >/dev/null
    sips -p 1024 1024 "$work/artwork.png" --out "$work/icon.png" >/dev/null
    for size in 16 32 128 256 512; do
        sips -z $size $size "$work/icon.png" --out "$work/FlowRate.iconset/icon_${size}x${size}.png" >/dev/null
        sips -z $((size * 2)) $((size * 2)) "$work/icon.png" --out "$work/FlowRate.iconset/icon_${size}x${size}@2x.png" >/dev/null
    done
    iconutil -c icns "$work/FlowRate.iconset" -o "$icns"
    rm -rf "$work"
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
    elif [[ -f "$(dirname "$ANDROID_PROJECT")/Signing/signing.env" ]]; then
        echo "Note: the .apk is signed with the key of $(dirname "$ANDROID_PROJECT")/Signing/signing.env"
    else
        echo "Note: neither ANDROID_KEYSTORE nor signing.env is there, the .apk is signed with the debug key"
    fi

    echo "######## Publishing the client for android"
    rm -rf "$ANDROID_PUBLISH_DIR"
    # The signing step is skipped while its output is there, a change of the key alone does not rerun it
    rm -f "$(dirname "$ANDROID_PROJECT")"/bin/Release/*/*-Signed.apk
    dotnet publish "$ANDROID_PROJECT" -c Release -o "$ANDROID_PUBLISH_DIR" -nologo -nodeReuse:false \
        -p:UseSharedCompilation=false ${signing[@]+"${signing[@]}"}

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
    dotnet publish "$PROJECT" -p:PublishProfile="$rid-$mode" -r "$rid" -nologo ${publish_arguments[@]+"${publish_arguments[@]}"}

    release "$rid" "$publish_dir"
    echo "Published: $publish_dir"
    echo "Release:   $version_dir/$rid$suffix"
done

echo "######## Done: $version_dir"
