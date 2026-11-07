# FlowRate Client

Voice, screen sharing, and chat for communities running their own FlowRate server.

FlowRate Client is built with **.NET 10** and **Avalonia** for Windows and Linux. The application combines WebRTC media transport with GStreamer capture, processing, and playback.

This repository contains the client, not the server. You need a running FlowRate server to connect, chat, or join voice sessions.

[Download builds](https://github.com/puffer-bit/FlowRate-Client/releases) · [Report an issue](https://github.com/puffer-bit/FlowRate-Client/issues) · [Third-party notices](THIRD-PARTY-NOTICES.md)

## Contents

- [What you can do](#what-you-can-do)
- [Platforms and packages](#platforms-and-packages)
- [Getting started](#getting-started)
- [Chat and formatting](#chat-and-formatting)
- [Settings and personalization](#settings-and-personalization)
- [Local data and protection](#local-data-and-protection)
- [Desktop launch options](#desktop-launch-options)
- [Building from source](#building-from-source)
- [Tests and development tools](#tests-and-development-tools)
- [Publishing packages](#publishing-packages)
- [Architecture and repository layout](#architecture-and-repository-layout)
- [Troubleshooting](#troubleshooting)
- [Contributing](#contributing)
- [License](#license)

## What you can do

### Voice and screen sharing

- Join voice sessions, mute your microphone, deafen playback, and adjust individual participants' volume.
- Configure microphone and output devices, voice gating, noise suppression, echo cancellation, and automatic gain control.
- Use desktop global hotkeys for common voice actions.
- Share a screen or window from desktop, with audio where supported by the capture backend.
- Watch screen shares on demand and adjust screen sharing and video settings.
- Use hardware video paths where the operating system, driver, codec, and capture backend support them.

On desktop, libwebrtc normally provides the session transport; GStreamer provides media capture, processing, encoding, decoding, and playback. The experimental GStreamer transport is also available and is used as a fallback when the libwebrtc native library is unavailable. OpenAL handles desktop interface and notification sounds separately from voice media.

Webcam calling is not currently implemented.

### Chat and community tools

- Exchange messages with Markdown formatting, mentions, images, and file attachments.
- Play supported audio and video attachments inside the client when media is enabled.
- Edit messages while preserving their original Markdown.
- Track unread messages and mute individual chats.
- Manage sessions, roles, and moderation actions according to the permissions granted by the server.
- Keep saved server identities and local user notes.

### Everyday use

- Switch between English, Russian, Spanish, Japanese, and Chinese.
- Customize appearance, notification sounds, and application behavior.
- Protect the local database with optional encryption and, where available, store its password in the system keyring.
- Receive desktop updates through installed Velopack packages, with separate release and development channels.
- Start with temporary diagnostic options without changing saved preferences.

## Platforms and packages

The packaging scripts target the following platforms:

| Platform | Package forms | Notes |
|---|---|---|
| Windows x64 | `Setup.exe`, standalone `.zip` | Installed packages support automatic updates. |
| Linux x64 | `.AppImage`, standalone `.tar.gz` | Installed packages support automatic updates. |

Choose an asset for your platform from [GitHub Releases](https://github.com/puffer-bit/FlowRate-Client/releases). Available assets depend on the release. Desktop JIT and NativeAOT variants use separate update channels.

Desktop release packages include the .NET runtime or a native executable, together with their media libraries. End users do not need to install .NET or GStreamer separately. Keep the accompanying libraries and runtime folders alongside the executable when unpacking a standalone archive.

The Linux GStreamer bundle is built against glibc 2.35. Its documented baseline includes Ubuntu 22.04+ and Debian 12+. It still depends on the host's graphics drivers and relevant system audio libraries; see the [runtime compatibility notes](src/Client/Client.GStreamer/Build/README.md#compatibility).

Standalone archives do not update themselves. Replace them manually with a newer archive. Application files and user data are stored separately, including when using an installed package.

## Getting started

1. Download and install or extract the package for your platform.
2. Launch FlowRate and open the connection dialog.
3. Enter the server address supplied by your administrator. The client's default coordinator port is `26666`; use the server's configured port if it differs.
4. Select a saved identity or create a new one, and provide a server password if required.
5. Open a chat or join an available voice session. Session creation and administrative actions depend on your server permissions.
6. Check microphone and playback devices in Settings before speaking.

For a server running on the same machine with the default port, the address is `127.0.0.1:26666`.

## Chat and formatting

The message editor supports these Markdown forms:

| Formatting | Syntax | Desktop shortcut |
|---|---|---|
| Bold | `**text**` | `Ctrl+B` |
| Italic | `*text*` | `Ctrl+I` |
| Strikethrough | `~~text~~` | `Ctrl+Shift+X` |
| Inline code | `` `code` `` | `Ctrl+E` |
| Fenced code block | Three backticks around a block | `Ctrl+Shift+E` |
| Link | `[label](https://example.com)` | `Ctrl+K` |

The input flyout exposes the same formatting commands with icons. Commands wrap the selection or insert delimiters at the caret when nothing is selected. The link command selects the destination so you can replace it immediately.

Other Markdown block syntax remains plain text. Messages retain their source Markdown for editing, while selection and copying use the displayed text. Code backgrounds, text, and icons follow the active theme, including theme changes while chat is open.

With `--no-media`, audio and video attachments appear as files to download or open externally instead of playing inside the client.

## Settings and personalization

Settings cover appearance, audio, video, screen sharing, hotkeys, behavior, storage, saved accounts, experimental features, and debug logs.

Use appearance settings to adjust the theme and chat presentation. Audio settings separate voice devices and processing from interface sounds. Storage settings manage locally cached media; saved identities and database protection are managed separately from disposable media cache.

Experimental settings include the desktop GStreamer transport fallback. When diagnosing a media problem, record which transport was selected and whether a fallback occurred; the journal records the engine used for a session.

Options supplied on the command line apply to the current process. Affected settings are disabled in the interface for that launch. The settings header indicates safe mode, disabled media, and software rendering where applicable.

## Local data and protection

### Data directory

The desktop data directory contains `client.db`, logs, media cache, sound packs, and other persistent local state.

| Operating system | Default location |
|---|---|
| Windows | `%LocalAppData%\FlowRate` |
| Linux | The .NET `LocalApplicationData` directory plus `FlowRate`, normally `~/.local/share/FlowRate` |

Choose another directory with `--data-dir`:

```powershell
dotnet run --project src/Client/Client.Avalonia.Desktop -- --data-dir "D:\FlowRateData"
```

Relative paths resolve against the working directory. Missing directories are created, and startup checks that the directory is writable. An invalid or unwritable path stops startup with exit code `1`.

Selecting a different directory does not move existing data. Supply the option on each launch that should use it. Close the client before copying its database or moving the data directory.

### Database encryption

Database encryption is configurable in Settings; it is not a claim that every local file is encrypted. The database implementation uses SQLite3 Multiple Ciphers with ChaCha20-Poly1305. Password storage in the system keyring depends on platform availability. Without a stored password, an encrypted database needs to be unlocked at startup.

Database encryption protects local database contents. It does not encrypt downloaded attachments, logs, or sound packs, and does not imply end-to-end encryption of conversations. Keep access to your database password when backing up encrypted data; copying the database alone does not transfer a password stored in another machine's keyring.

### Multiple instances

By default, a second desktop launch activates the running application. To allow separate processes, start the **first** process with `--allow-multiple-instances`.

For independent test clients, give each process a separate `--data-dir`. Allowing another process to start does not make sharing a database safe or bypass database locks.

## Desktop launch options

| Option | Behavior |
|---|---|
| `--data-dir PATH` | Use a custom data directory. |
| `--minimized` | Start in the tray after initialization; unlock and recovery prompts remain visible. |
| `--allow-multiple-instances` | Allow subsequent launches to open separate instances; set this on the first process. |
| `--no-media` | Disable voice and video media while keeping OpenAL interface sounds available. |
| `--safe-mode` | Disable media, global hotkeys, automatic connections, and session restoration. |
| `--no-auto-connect` | Skip automatic connections and session restoration; manual connections remain available. |
| `--disable-updates` | Disable update checks and controls for this launch. |
| `--no-file-logging` | Disable file logging; keep the in-memory journal. |
| `--log-level LEVEL` | Set `verbose`, `debug`, `information`, `warning`, `error`, or `fatal`; `info` and `warn` are aliases. |
| `--software-rendering` | Render the application interface in software. |
| `--lang CODE` | Override the interface language with `en`, `ru`, `es`, `ja`, or `zh`. |
| `--help`, `-h` | Print help and exit before opening application data. |
| `--version` | Print the application version and exit before opening application data. |

Value options also accept `--name=value`. Options can be combined:

```powershell
dotnet run --project src/Client/Client.Avalonia.Desktop -- --safe-mode --lang en --log-level debug
```

```bash
dotnet run --project src/Client/Client.Avalonia.Desktop -- --no-auto-connect --software-rendering
```

Unknown arguments, repeated options, and invalid or missing values stop startup with exit code `1`. Overrides do not overwrite saved preferences. An application-triggered restart after an update preserves launch options and the absolute data path.

`--no-media` avoids loading GStreamer and libwebrtc. Microphone and video settings are unavailable, voice sessions cannot be joined or restored, and chat media uses downloadable files. Restart without the option to restore media.

Safe mode does not automatically enable software rendering; combine `--safe-mode --software-rendering` when investigating both startup and graphics problems.

## Building from source

Run the following commands from the repository root unless a section says otherwise.

### Prerequisites

- Git, including submodule support.
- The .NET 10 SDK.
- NuGet access for package restore.
- For desktop media: a bundled GStreamer runtime, or a compatible system installation for a Debug build.
- For native media rebuilds and NativeAOT: the platform-specific tools described below and in the linked build guides.

### Clone the repository

```bash
git clone --recurse-submodules https://github.com/puffer-bit/FlowRate-Client.git
cd FlowRate-Client
```

For an existing clone:

```bash
git submodule update --init --recursive
```

`src/Shared` is the [FlowRate Shared](https://github.com/puffer-bit/FlowRate-Shared) submodule containing shared protocol types. Keep it at the revision recorded by this repository when building the client.

### Build and run desktop

Build the desktop application project:

```bash
dotnet build src/Client/Client.Avalonia.Desktop/Client.Avalonia.Desktop.csproj -c Debug
dotnet run --project src/Client/Client.Avalonia.Desktop
```

For UI or chat work without a media runtime:

```bash
dotnet run --project src/Client/Client.Avalonia.Desktop -- --no-media --no-auto-connect
```

A Debug build can fall back to a system GStreamer installation when no bundled runtime is present. A Release desktop build requires the bundled runtime for its runtime identifier. Building managed code successfully does not by itself verify voice or screen capture on the target machine.

### Prepare desktop media libraries

GStreamer artifacts are generated locally and are not tracked in Git. Build or collect the bundle using the platform guide:

- [GStreamer runtime build and verification](src/Client/Client.GStreamer/Build/README.md): Linux Docker build, Windows collection, required plugins, licenses, and output layout.
- [libwebrtc runtime build](src/Client/Client.LibWebRTC/Build/README.md): native sources, pinned dependencies, platform tools, and exported API verification.
- [libwebrtc integration](src/Client/Client.LibWebRTC/README.md): transport, screen streams, and audio behavior.

GStreamer output lives under `src/Client/Client.GStreamer/Build/artifacts/<rid>/`. MSBuild copies the matching runtime alongside the desktop executable. libwebrtc binaries live under `src/Client/Client.LibWebRTC/Build/prebuilt/<rid>/`; consult its build guide before changing the native API or rebuilding those binaries.

## Tests and development tools

### Automated tests

The test projects cover application behavior, infrastructure, WebSocket handling, localization, and the shared protocol. CI runs test projects individually on Windows and Linux without building the application hosts.

Run one project while working on its area:

```bash
dotnet test src/Client/Client.Application.Tests/Client.Application.Tests.csproj -c Release
dotnet test src/Client/Client.Infrastructure.Tests/Client.Infrastructure.Tests.csproj -c Release
dotnet test src/Client/Client.WebSockets.Tests/Client.WebSockets.Tests.csproj -c Release
dotnet test src/Client/Client.Avalonia.Localization.Tests/Client.Avalonia.Localization.Tests.csproj -c Release
dotnet test src/Shared/Shared.Tests/Shared.Tests.csproj -c Release
```

To run every current test project in PowerShell without building the application projects:

```powershell
$testProjects = Get-ChildItem src/Client, src/Shared -Recurse -Filter *.Tests.csproj
foreach ($project in $testProjects) {
    dotnet test $project.FullName -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $($project.FullName)" }
}
```

The repository also has `src/Scripts/bash/test.sh` and `src/Scripts/powershell/test.ps1`. These invoke the whole solution, so they need its workloads and build prerequisites. See [the CI test workflow](.github/workflows/tests.yml) for the project-by-project approach.

### Media checks and test clients

| Tool | Purpose |
|---|---|
| [Client.TestBot](src/Client/Client.TestBot/README.md) | Join a real server with headless users; send test tones or files, exercise chat and screen sharing, and collect load statistics. |
| `Client.Audio.SmokeChecks` | Run media integration checks that require the relevant native runtimes. |
| [Video preview lab](src/Client/Client.Avalonia.Video.Preview/README.md) | Inspect Linux PipeWire capture and the production video renderer without a network session. |
| `Client.IconGenerator` | Repository tooling for application icons. |

For example, start a tone-producing bot against a local test server:

```bash
dotnet run --project src/Client/Client.TestBot -- --server 127.0.0.1:26666
```

The bot needs a reachable server, an available session, and the appropriate media runtime. Its certificate behavior is intended for testing; read its guide before using it outside a local test setup.

## Publishing packages

The publish scripts prepare self-contained packages under `dist/<version>/<platform>/`. Use the script for the host platform.

### Linux

```bash
bash src/Scripts/bash/publish.sh
bash src/Scripts/bash/publish.sh --aot
```

The default target is `linux-x64`. Linux GStreamer runtime construction requires Docker. The script documents additional tools, including Python and tools needed for media preparation.

### Windows

```powershell
powershell -ExecutionPolicy Bypass -File src/Scripts/powershell/publish.ps1
powershell -ExecutionPolicy Bypass -File src/Scripts/powershell/publish.ps1 -Aot
```

The default target is `win-x64`. Follow the script prerequisites for Visual Studio C++ build tools and a Windows SDK when native build steps are needed.

Cross-platform desktop packaging requires the other platform's prepared native runtime. NativeAOT builds must run on their target operating system. Prefer the matching host for each platform.

Use `--dev` / `-Dev` for development builds and `--notes FILE` / `-Notes FILE` to supply release notes. Installed desktop packages use Velopack feeds with separate channels for Windows, Linux, JIT, NativeAOT, and development builds. See the script help and [release notes documentation](release-notes/README.md) before preparing a release.

## Architecture and repository layout

The code separates domain concepts and application use cases from persistence, network transports, media backends, and platform UI composition. Shared view models and reusable Avalonia resources are separated from platform services in the application host.

| Path under `src/` | Responsibility |
|---|---|
| `Client/Client.Core` | Domain models, settings models, and media primitives. |
| `Client/Client.Application` | Application workflows, coordinator/session logic, and service ports. |
| `Client/Client.Infrastructure` | SQLite repositories and migrations, local cache, and release-note retrieval. |
| `Client/Client.Api` | Server API integration. |
| `Client/Client.WebSockets` | WebSocket connection and messaging. |
| `Client/Client.WebRTC` | Shared WebRTC negotiation and protocol support. |
| `Client/Client.LibWebRTC` | libwebrtc transport, native bridge, and runtime build resources. |
| `Client/Client.GStreamer` | Desktop media capture, processing, codec pipelines, and runtime loading. |
| `Client/Client.GStreamer.WebRTC` | GStreamer WebRTC transport implementation. |
| `Client/Client.OpenAL` | Desktop interface and notification audio. |
| `Client/Client.Avalonia.ViewModels` | Shared view models and presentation services. |
| `Client/Client.Avalonia.Desktop` | Desktop entry point, views, platform services, and packaging profiles. |
| `Client/Client.Avalonia.Controls` | Reusable Avalonia controls, including shared menu components. |
| `Client/Client.Avalonia.Styles` | Shared styles and theme resources. |
| `Client/Client.Avalonia.Assets` | Shared icons, fonts, and application artwork. |
| `Client/Client.Avalonia.Localization` | Interface translations and localization support. |
| `Client/Client.Avalonia.Video` | Video frame presentation through GPU or memory-backed paths. |
| `Client/Client.Tools` | Common utilities, launch options, paths, logging, and build information. |
| `Client/*.Tests`, `Shared/Shared.Tests` | Automated test projects. |
| `Shared` | Git submodule with server/client protocol definitions. |
| `Scripts/bash`, `Scripts/powershell` | Build, run, test, publish, and development helper scripts. |

Repository-level files include [third-party notices](THIRD-PARTY-NOTICES.md), [release-note sources](release-notes/README.md), and [GitHub Actions workflows](.github/workflows).

## Troubleshooting

| Symptom | What to check |
|---|---|
| A second launch opens the existing window | This is normal single-instance behavior. Start the first process with `--allow-multiple-instances` for separate test processes. |
| Startup fails before the main window | Check data-directory permissions and command-line values. Try `--safe-mode`; add `--software-rendering` if graphics initialization is involved. |
| Voice or video is unavailable | Remove `--no-media` or `--safe-mode`, check device permissions, and inspect media runtime errors in the journal. |
| Release build reports missing GStreamer | Prepare the runtime for the target RID using the GStreamer build guide. A system installation is only a Debug fallback. |
| Desktop UI runs but voice fails in Debug | Check the bundled/system GStreamer runtime, required plugins, and selected audio devices. |
| Screen sharing fails on Linux | Check PipeWire, the appropriate xdg-desktop-portal backend, capture permissions, graphics drivers, and required GStreamer elements. |
| Software rendering changes UI behavior but capture still fails | `--software-rendering` controls the application interface; media capture and encoding have separate platform requirements. |
| An extracted archive does not update | Standalone archives require manual replacement; use an installed package for automatic updates. |
| An encrypted database cannot be opened | Verify its password and keyring availability, and check that another process is not holding the database. |

For a useful issue report, include the client build, OS, package type, reproduction steps, launch options, selected media transport, and relevant journal output. Review logs for private server addresses, identities, or message content before sharing them.

## Contributing

Start with [AGENTS.md](AGENTS.md) for repository conventions. Write documentation and code comments in English. Keep changes aligned with the existing separation between application logic, platform services, and shared UI resources.

For menu changes, use the shared `Flyout` and `MenuItemButton` components and existing styles. Use icons from the shared icon collection, with theme-aware brushes. Consult the existing [chat message flyout](src/Client/Client.Avalonia.Desktop/Views/Sessions/Chat/ChatMessageFlyout.axaml) before adding a menu.

Run the test projects relevant to your change. UI, capture, audio-device, and native media changes also need manual checks on the affected platform; managed unit tests cannot establish driver or device compatibility.

## License

FlowRate Client is licensed under the [MIT License](LICENSE). Bundled libraries retain their own licenses and distribution requirements; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the notices shipped alongside release packages.
