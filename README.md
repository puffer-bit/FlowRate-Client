# FlowRate Client

A desktop and Android client for FlowRate: voice sessions, screen sharing and chat on a server you
run yourself. Built with .NET 10 and Avalonia; media goes over WebRTC through a bundled GStreamer
runtime, so nothing has to be installed on the user's machine.

The client needs a FlowRate server to connect to.

## Features

- Voice sessions with noise gate, mute and deafen, per-user volume and global hotkeys
- Screen sharing with system audio, watched on request
- Chat with images, mentions and unread counters
- Roles and moderation
- Encrypted local database, with the key kept in the system keyring or behind a password
- Automatic updates from GitHub Releases (desktop)
- Interface in English, Russian, Spanish, Japanese and Chinese

## Download

Builds for Linux (x64), Windows (x64) and Android are on the
[Releases](https://github.com/puffer-bit/FlowRate-Client/releases) page. Each desktop platform comes in
two forms: an installed one that updates itself (`.AppImage` / `Setup.exe`) and a standalone archive.

## Building from source

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
