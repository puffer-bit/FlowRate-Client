#!/usr/bin/env bash
# Adds FlowRate to the desktop of the current user: the application menu entry, the icon, the window of the
# running client grouped under that icon in the dock (StartupWMClass), and on request the start at login.
# Nothing goes outside the home folder and no root is needed.
#
# Usage: ./flowrate-linux.sh                     installs the AppImage next to the script, or the
#                                                standalone client when the script is in its folder
#        ./flowrate-linux.sh --appimage FILE     installs that AppImage
#        ./flowrate-linux.sh --dir DIR           installs to DIR instead of ~/.local/opt/flowrate
#        ./flowrate-linux.sh --autostart         also starts FlowRate (minimized) at login
#        ./flowrate-linux.sh --no-autostart      stops that again; otherwise an earlier choice stays
#        ./flowrate-linux.sh --uninstall         removes all of it again; the data of the client stays
#        ./flowrate-linux.sh --uninstall --purge also removes the data: ~/.local/share/FlowRate
#
# The client is copied to the install folder, together with this script, which is then the one to run
# for --uninstall. The AppImage updates itself in place there, so the menu entry stays valid. Running the
# script again, for example with a newer standalone client, replaces the previous install.
#
# publish.sh appends the icons to the published copy of the script, after the line ICONS_PAYLOAD (a
# base64 tar.gz of hicolor/); a copy without them installs the menu entry with no icon of its own.
set -euo pipefail

APP_ID=flowrate
EXECUTABLE=FlowRate
# The X11 class of the client window, which is the name of the entry assembly
WM_CLASS=FlowRate
MARKER=.flowrate-install

data_home=${XDG_DATA_HOME:-$HOME/.local/share}
config_home=${XDG_CONFIG_HOME:-$HOME/.config}
applications="$data_home/applications"
icons="$data_home/icons/hicolor"
desktop_file="$applications/$APP_ID.desktop"
autostart_file="$config_home/autostart/$APP_ID.desktop"
client_data="$data_home/FlowRate"
default_dir="$HOME/.local/opt/$APP_ID"

script=$(readlink -f "$0")
script_dir=$(dirname "$script")

usage() { sed -n '6,13p' "$script" | sed 's/^# \{0,1\}//'; }
fail() { echo "error: $*" >&2; exit 1; }

appimage= dir= autostart= uninstall=0 purge=0
while (( $# )); do
    case "$1" in
        --appimage) appimage=${2:-}; [[ -n "$appimage" ]] || fail "--appimage needs a file"; shift ;;
        --appimage=*) appimage=${1#*=} ;;
        --dir) dir=${2:-}; [[ -n "$dir" ]] || fail "--dir needs a folder"; shift ;;
        --dir=*) dir=${1#*=} ;;
        --autostart) autostart=1 ;;
        --no-autostart) autostart=0 ;;
        --uninstall) uninstall=1 ;;
        --purge) purge=1 ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; fail "unknown option '$1'" ;;
    esac
    shift
done
(( purge && !uninstall )) && fail "--purge goes with --uninstall"
(( EUID == 0 )) && fail "run it as the user FlowRate is for, not as root: it installs into the home folder"

# The cache updates are optional: desktops without the tools pick the changes up anyway, a bit later
refresh_caches() {
    command -v update-desktop-database >/dev/null && update-desktop-database -q "$applications" 2>/dev/null || true
    # Only a cache that is there already: a new one would hide the icons other programs add later
    if [[ -f "$icons/icon-theme.cache" ]] && command -v gtk-update-icon-cache >/dev/null; then
        gtk-update-icon-cache -q -t -f "$icons" 2>/dev/null || true
    fi
}

# The folder of the previous install: the one in the menu entry, else the default one
installed_dir() {
    local exec
    if [[ -f "$desktop_file" ]]; then
        exec=$(sed -n 's/^TryExec=//p' "$desktop_file" | head -n 1)
        [[ -n "$exec" ]] && { dirname "$exec"; return; }
    fi
    echo "$default_dir"
}

# Removes an install folder only when it is one this script made, never an arbitrary --dir
remove_dir() {
    local target=$1
    [[ -e "$target" ]] || return 0
    if [[ ! -f "$target/$MARKER" ]]; then
        echo "Note: $target was not made by this script, it is left as it is"
        return 0
    fi
    rm -rf -- "$target"
}

if (( uninstall )); then
    target=${dir:-$(installed_dir)}
    rm -f -- "$desktop_file" "$autostart_file"
    rm -f -- "$icons"/*/apps/"$APP_ID".png "$icons/scalable/apps/$APP_ID.svg"
    remove_dir "$target"
    refresh_caches
    echo "FlowRate is removed from the desktop"
    if (( purge )); then
        rm -rf -- "$client_data"
        echo "The data of the client is removed: $client_data"
    else
        echo "The data of the client stays in $client_data (--uninstall --purge removes it)"
    fi
    exit 0
fi

dir=${dir:-$default_dir}
[[ "$dir" == /* ]] || dir="$PWD/$dir"
dir=${dir%/}

# What to install: the standalone client is the folder the script is in, else an AppImage
if [[ -z "$appimage" && -x "$script_dir/$EXECUTABLE" && ! -f "$script_dir/$MARKER" ]]; then
    source_kind=standalone
elif [[ -z "$appimage" && -f "$script_dir/$MARKER" ]]; then
    # The installed copy of the script: only the menu entry is made again
    source_kind=installed
    dir=$script_dir
else
    source_kind=appimage
    if [[ -z "$appimage" ]]; then
        # The newest AppImage of FlowRate next to the script, as it is downloaded from the release
        appimage=$(ls -t "$script_dir"/FlowRate*.AppImage 2>/dev/null | head -n 1 || true)
        [[ -n "$appimage" ]] || fail "no FlowRate*.AppImage next to the script, give it with --appimage FILE"
    fi
    [[ -f "$appimage" ]] || fail "no such file: $appimage"
    appimage=$(readlink -f "$appimage")
fi

# The new install is put together next to the old one and then swapped in, so a running client keeps
# its files (an AppImage overwritten in place breaks the one that runs from it)
if [[ "$source_kind" != installed ]]; then
    if [[ -e "$dir" && ! -f "$dir/$MARKER" ]]; then
        [[ -z "$(ls -A "$dir" 2>/dev/null)" ]] || fail "$dir is not empty and was not made by this script, choose another --dir"
    fi
    mkdir -p "$(dirname "$dir")"
    staging=$(mktemp -d "$dir.new.XXXXXX")
    trap 'rm -rf -- "${staging:-}"' EXIT
    if [[ "$source_kind" == standalone ]]; then
        cp -a "$script_dir/." "$staging/"
    else
        cp "$appimage" "$staging/$EXECUTABLE.AppImage"
        chmod +x "$staging/$EXECUTABLE.AppImage"
        cp "$script" "$staging/flowrate-linux.sh"
    fi
    chmod +x "$staging/flowrate-linux.sh"
    : > "$staging/$MARKER"
    chmod 755 "$staging"
    old=
    if [[ -e "$dir" ]]; then
        old="$dir.old.$$"
        mv -- "$dir" "$old"
    fi
    mv -- "$staging" "$dir"
    staging=
    [[ -n "$old" ]] && rm -rf -- "$old"
fi

if [[ -x "$dir/$EXECUTABLE.AppImage" ]]; then
    executable="$dir/$EXECUTABLE.AppImage"
else
    executable="$dir/$EXECUTABLE"
fi
[[ -x "$executable" ]] || fail "no client in $dir"

# The icons from the payload of the script, when it has one
payload=$(awk 'found { print } /^ICONS_PAYLOAD$/ { found = 1 }' "$script")
if [[ -n "$payload" ]]; then
    mkdir -p "$icons"
    base64 -d <<< "$payload" | tar -xz -C "$icons" --strip-components=1
    icon=$APP_ID
else
    echo "Note: this copy of the script has no icons, the menu entry gets a generic one"
    icon=application-x-executable
fi

# The Exec value, quoted as the Desktop Entry spec wants: inside double quotes \ " ` $ are escaped
quoted=$(printf '%s' "$executable" | sed -e 's/[\\"`$]/\\&/g')

write_entry() {
    local file=$1 arguments=$2 extra=${3:-}
    mkdir -p "$(dirname "$file")"
    cat > "$file" <<EOF
[Desktop Entry]
Type=Application
Name=FlowRate
GenericName=Voice and Text Chat
Comment=Voice, video and text chat with your friends
Exec="$quoted"$arguments
TryExec=$executable
Icon=$icon
Terminal=false
Categories=Network;InstantMessaging;Chat;
Keywords=voice;chat;call;messenger;
StartupWMClass=$WM_CLASS
StartupNotify=true
$extra
EOF
}

# The menu entry opens flowrate:// links too: the link comes as the argument (%u) and goes on to a running client
write_entry "$desktop_file" " %u" "MimeType=x-scheme-handler/flowrate;"
command -v xdg-mime >/dev/null && xdg-mime default "$APP_ID.desktop" x-scheme-handler/flowrate 2>/dev/null || true
# With neither option the start at login of an earlier install stays, pointing at the new client
[[ -z "$autostart" && -f "$autostart_file" ]] && autostart=1
if [[ "$autostart" == 1 ]]; then
    write_entry "$autostart_file" " --minimized"
    echo "X-GNOME-Autostart-enabled=true" >> "$autostart_file"
else
    rm -f -- "$autostart_file"
fi
refresh_caches

echo "FlowRate is installed in $dir and added to the application menu"
[[ -f "$autostart_file" ]] && echo "It starts minimized at login ($autostart_file)"
echo "To remove it: $dir/flowrate-linux.sh --uninstall"
if [[ "$executable" == *.AppImage ]] && ! command -v fusermount >/dev/null && ! command -v fusermount3 >/dev/null; then
    echo "Note: no FUSE found; an AppImage needs it (the package is usually fuse or libfuse2)"
fi
exit 0
