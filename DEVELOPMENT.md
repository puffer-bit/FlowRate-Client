# Client development

For runtime components and implementation boundaries, see the [technical documentation](docs/technical/README.md).

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

Use `--dev` / `-Dev` for development builds and `--notes FILE` / `-Notes FILE` to supply release notes. Installed desktop packages use Velopack feeds with separate channels for Windows, Linux, JIT, NativeAOT, and development builds. See the script help before preparing a release.

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

Repository-level files include [third-party notices](THIRD-PARTY-NOTICES.md), release-note sources supplied when publishing, and [GitHub Actions workflows](.github/workflows).

## Contributing

Start with [AGENTS.md](AGENTS.md) for repository conventions. Write documentation and code comments in English. Keep changes aligned with the existing separation between application logic, platform services, and shared UI resources.

For menu changes, use the shared `Flyout` and `MenuItemButton` components and existing styles. Use icons from the shared icon collection, with theme-aware brushes. Consult the existing [chat message flyout](src/Client/Client.Avalonia.Desktop/Views/Sessions/Chat/ChatMessageFlyout.axaml) before adding a menu.

Run the test projects relevant to your change. UI, capture, audio-device, and native media changes also need manual checks on the affected platform; managed unit tests cannot establish driver or device compatibility.

