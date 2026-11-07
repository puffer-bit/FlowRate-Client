# FlowRate Client

A desktop and Android client for FlowRate: voice sessions, screen sharing and chat on a server you
run yourself. Built with .NET 10 and Avalonia; media goes over WebRTC through a bundled GStreamer
runtime, so nothing has to be installed on the user's machine.

The client needs a FlowRate server to connect to.

## Features

- Voice sessions with noise gate, mute and deafen, per-user volume and global hotkeys
- Screen sharing with system audio, watched on request
- Chat with Markdown formatting, images, mentions and unread counters
- Roles and moderation
- Encrypted local database, with the key kept in the system keyring or behind a password
- Automatic updates from GitHub Releases (desktop)
- Interface in English, Russian, Spanish, Japanese and Chinese

## Chat formatting

Messages support `**bold**`, `*italic*`, `~~strikethrough~~`, inline code, fenced code blocks
and `[label](https://example.com)` links. Other block syntax stays as plain text.
The input context menu includes formatting commands with icons, also available as shortcuts:
Ctrl+B (bold), Ctrl+I (italic), Ctrl+Shift+X (strikethrough), Ctrl+E (inline code),
Ctrl+Shift+E (code block), and Ctrl+K (link). With no selection, commands insert delimiters
at the caret. The link command selects the URL for replacement.

Messages keep their original Markdown for editing. Selection and copying use the displayed text.
Code and icons follow the current light or dark theme, including changes while the chat is open.

## Download

Builds for Linux (x64), Windows (x64) and Android are on the
[Releases](https://github.com/puffer-bit/FlowRate-Client/releases) page. Each desktop platform comes in
two forms: an installed one that updates itself (`.AppImage` / `Setup.exe`) and a standalone archive.

## Building from source

### Desktop launch options

Options apply only to the current launch and do not overwrite saved settings. Settings controlled
by launch options are disabled in the interface. The settings header shows safe mode or media-disabled
mode, plus a separate indicator for software rendering. Hover an indicator for an explanation.

| Option | Behavior |
|---|---|
| `--data-dir PATH` | Select the directory for local application data (details below). |
| `--minimized` | Start in the tray after initialization; unlock and recovery prompts remain visible. |
| `--no-media` | Disable voice and video media; notification sounds remain available. |
| `--safe-mode` | Disable media, global hotkeys, automatic connections and session restoration. |
| `--no-auto-connect` | Skip automatic connections and session restoration; manual connections remain available. |
| `--disable-updates` | Disable update checks and update controls for this launch. |
| `--no-file-logging` | Disable log files; the in-memory journal remains available. |
| `--log-level LEVEL` | Override the journal level: `verbose`, `debug`, `information`, `warning`, `error`, `fatal` (`info` and `warn` also work). |
| `--software-rendering` | Use software rendering for the application interface. |
| `--lang CODE` | Override the interface language: `en`, `ru`, `es`, `ja`, `zh`. |
| `--help`, `-h` | Print available options and exit before opening application data. |
| `--version` | Print the application version and exit before opening application data. |

Options taking a value also accept `--name=value`. Options can be combined:

```powershell
dotnet run --project src/Client/Client.Avalonia.Desktop -- --safe-mode --lang ru --log-level debug
```

Unknown options, repeated options and missing or invalid values stop startup with exit code 1.
An application-triggered restart after an update keeps the launch options and the absolute data path.

### Choosing the data directory (desktop)

Pass `--data-dir "D:\FlowRateData"` (or `--data-dir="D:\FlowRateData"`) to select the
directory for the database, logs, media cache, sound packs and other local data:

```powershell
dotnet run --project src/Client/Client.Avalonia.Desktop -- --data-dir "D:\FlowRateData"
```

Relative paths are resolved against the working directory. Missing directories are created.
An invalid or unwritable path stops startup with an error and exit code 1.
Without this option, the client uses `%LocalAppData%\FlowRate` on Windows or
`~/.local/share/FlowRate` on Linux. Existing data is not moved automatically; the option
must be supplied on each launch that should use the custom directory.

### Running without media (desktop)

Pass `--no-media` to start without loading GStreamer or libwebrtc:

```powershell
dotnet run --project src/Client/Client.Avalonia.Desktop -- --no-media
```

OpenAL interface and notification sounds and their settings remain available. Microphone and video
settings are disabled for this launch. Voice sessions cannot be joined or restored automatically. Chat audio
and video attachments are shown as files that can be downloaded or opened externally. Restart
without the option to restore media; saved settings are preserved.

### Build requirements

Requirements: the .NET 10 SDK and Git. The protocol types are in a submodule, so clone with it:

```bash
git clone --recurse-submodules https://github.com/puffer-bit/FlowRate-Client
cd FlowRateClient/src
dotnet build FlowRateClient.sln
```

A Debug build falls back to a GStreamer installed on the system. A release build ships its own
runtime, built by the scripts in [`src/Client/Client.GStreamer/Build`](src/Client/Client.GStreamer/Build/README.md)
(Docker on Linux).

The release packages are made by the publish scripts:

```bash
src/Scripts/bash/publish.sh                     # linux-x64
src/Scripts/bash/publish.sh --aot               # linux-x64, NativeAOT
src/Scripts/bash/publish.sh --target all        # linux-x64, win-x64 and android
```

On Windows use `src/Scripts/powershell/publish.ps1` (`-Aot` for NativeAOT). The output is in
`dist/<version>/<platform>/`.

## Project layout

| Project | Contents |
|---|---|
| `Client.Core`, `Client.Application` | domain model and use cases, no UI |
| `Client.Infrastructure` | SQLite storage, settings, keyring |
| `Client.Api`, `Client.WebSockets` | server connection and protocol |
| `Client.GStreamer`, `Client.OpenAL` | media pipelines and audio output |
| `Client.Avalonia.ViewModels` | view models shared by desktop and Android |
| `Client.Avalonia.Desktop`, `Client.Avalonia.Android` | the applications |
| `Client.Avalonia.Controls`, `.Styles`, `.Assets`, `.Localization` | UI parts |
| `Client.Avalonia.Video` | the watched screen: frames drawn with OpenGL, ANGLE (D3D11) or from memory |
| `Client.TestBot` | a headless client that plays test audio into a session |
| `Shared` (submodule) | protocol types shared with the server |

## License

[MIT](LICENSE). Third-party components and their licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
