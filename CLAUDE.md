# FlowRate Client

Desktop (Linux, Windows) and Android client of FlowRate: voice sessions, screen sharing and chat on a
self-hosted server. .NET 10, Avalonia, ReactiveUI/CommunityToolkit.Mvvm; media over WebRTC through a
bundled GStreamer runtime. Solution: `src/FlowRateClient.sln`.

## Layers

Dependencies point inwards; each outer project registers its services in its own `DependencyInjection.cs`.

| Layer | Projects | Role |
|---|---|---|
| Protocol | `Shared/Shared.Core`, `Shared/Shared.Parsers` (submodule FlowRate-Shared, also used by the server) | messages, models, enums, permissions, validation; wire format in `Shared/Docs/Protocol.md` |
| Domain | `Client.Core` | models of coordinators (servers), sessions, users, chat, media, settings; no I/O |
| Application | `Client.Application` | use cases; `Ports/` holds the interfaces every outer layer implements (gateway, WebRTC transport, audio devices, storage, updater, push, screen capture) |
| Adapters | `Client.Api`, `Client.WebSockets`, `Client.Infrastructure`, `Client.GStreamer`, `Client.OpenAL` | server connection, SQLite storage, media pipelines, audio output |
| Presentation | `Client.Avalonia.ViewModels`, `.Controls`, `.Styles`, `.Assets`, `.Localization` | view models and UI parts shared by both apps |
| Hosts | `Client.Avalonia.Desktop`, `Client.Avalonia.Android` | composition roots and platform services |

`Client.Tools` (logging, environment, hardware id, app info) is referenced from everywhere and depends
on nothing.

## Server connection

- A server is a *coordinator*. `Client.Application/Coordinator/CoordinatorSession` holds the state of
  one connection (users, roles, sessions, messages) and handles the server's messages;
  `CoordinatorFactory` creates one per server.
- `Client.Api/CoordinatorGateway` implements `ICoordinatorGateway`: requests and responses
  (`Protocol/ResponseAwaiter`), heartbeat, mapping of Shared models to Core ones (`Mappers/`).
- Transport is `Client.WebSockets` (a bare WebSocket connection) under `Client.Api/Transport`, with
  its own certificate check (untrusted certificates are confirmed by the user) and an HTTP part for
  media uploads.
- The protocol version is negotiated on connect; a mismatch is reported as `ProtocolMismatch`.
  Reconnects follow `ReconnectBackoff` and `Connectivity/`.

## Media

- `Client.GStreamer` builds the pipelines: `WebRTC/` (negotiation with the server's SFU, incoming and
  outgoing tracks, retransmission), `Audio/` (capture, voice gate, mixing, microphone test), `Video/`
  (screen share encode/decode and rendering), `Interop/` and `Loader/` (loading the GStreamer runtime).
  Design notes: `Client.GStreamer/Docs/VoiceGate.md`, `VideoRendering.md`.
- Release builds ship their own GStreamer runtime, built outside git
  (`Client.GStreamer/Build/README.md`); Debug falls back to the system installation.
- `Client.OpenAL` plays UI sounds and voice output through OpenAL Soft.
- Android has no media pipeline: `Platform/Media` registers no-op implementations of the ports, so the
  app covers chat, moderation and push notifications.

## Storage and platform services

- `Client.Infrastructure/Database`: SQLite encrypted with SQLite3MC, accessed with Dapper (AOT-safe);
  the key is kept in the system keyring (`ISecureStorage`) or behind a user password.
- `Client.Infrastructure/Media/MediaCache`: downloaded chat images and avatars.
- Desktop `Services/`: global hotkeys, keyring (D-Bus Secret Service on Linux, DPAPI on Windows),
  screen capture (`ScreenCast`: the XDG desktop portal on Linux, its own implementation on Windows), updates (Velopack from GitHub Releases, one channel per
  variant: `linux`, `linux-aot`, `win`, `win-aot`, each with a `-dev` feed).
- Android `Platform/`: Firebase push, network monitoring, notifications.

## UI texts

`Client.Avalonia.Localization/Languages/*.arb` (en, ru, es, ja, zh), ICU MessageFormat. English is the
fallback. Markup reads texts through `TextExtension`, code through `Strings.Get`/`Strings.Observe`; the
language can change at runtime.

## Builds and tests

- Test projects: `Client.Application.Tests`, `Client.Infrastructure.Tests`, `Client.WebSockets.Tests`,
  `Client.Avalonia.Localization.Tests`, `Shared/Shared.Tests` (xUnit v3). None needs GStreamer or the
  Android workload.
- Packages: `src/Scripts/bash/publish.sh` / `src/Scripts/powershell/publish.ps1` (JIT or NativeAOT,
  release or dev build), output in `dist/<version>/<platform>/`. CI in `.github/workflows`
  (`tests.yml`, `build.yml`, `release.yml`).
- The version is `<Version>` in `Client.Avalonia.Desktop.csproj`; a dev build is
  `<version>-dev.<UTC yyMMddHHmm>`.
- `Client.TestBot` is a headless client that plays test audio into a session.
