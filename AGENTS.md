# FlowRate Client

FlowRate Client is a Windows, Linux, and Android application for voice sessions, screen sharing, and chat on self-hosted FlowRate servers. The client is built with .NET 10 and Avalonia, with ReactiveUI and CommunityToolkit.Mvvm for presentation logic.

This repository contains the client. Shared protocol definitions are maintained in the `src/Shared` Git submodule. The main solution is `src/FlowRateClient.sln`.

## Project conventions

- Write all project documentation in English, including instruction files and code comments.
- Follow the naming, formatting, and implementation patterns of the surrounding code.
- Keep documentation consistent with implemented behavior. Distinguish platform-specific features and optional functionality from features available on every platform.
- Keep changes focused on the requested behavior and preserve unrelated work.

## Architecture

Project paths in this table are relative to `src/`.

| Layer | Projects | Responsibility |
|---|---|---|
| Protocol | `Shared/Shared.Core` | Messages, shared models, permissions, and validation used by the client and server. |
| Domain | `Client/Client.Core` | Domain models, settings, and media primitives. |
| Application | `Client/Client.Application` | Coordinator and session workflows, application state, and service interfaces in `Ports/`. |
| Network | `Client/Client.Api`, `Client/Client.WebSockets`, `Client/Client.WebRTC` | Server API, WebSocket messaging, and shared WebRTC negotiation support. |
| Persistence | `Client/Client.Infrastructure` | SQLite storage, repositories, migrations, media cache, and release-note retrieval. |
| Media | `Client/Client.GStreamer`, `Client/Client.GStreamer.WebRTC`, `Client/Client.LibWebRTC`, `Client/Client.OpenAL` | Media pipelines, WebRTC transports, native integration, and interface sounds. |
| Presentation | `Client/Client.Avalonia.ViewModels`, `Client/Client.Avalonia.Controls`, `Client/Client.Avalonia.Styles`, `Client/Client.Avalonia.Assets`, `Client/Client.Avalonia.Localization`, `Client/Client.Avalonia.Video` | Shared presentation logic, controls, themes, resources, translations, and video rendering. |
| Hosts | `Client/Client.Avalonia.Desktop`, `Client/Client.Avalonia.Android` | Application entry points, composition, views, and platform services. |
| Utilities | `Client/Client.Tools` | Logging, launch options, application paths, and common helpers. |

- Keep domain and application logic independent of platform UI and concrete infrastructure implementations.
- Implement external services through the existing application ports and register adapters through the established dependency injection patterns.
- Share presentation behavior where practical; keep operating-system integrations in the appropriate host or platform adapter.
- Treat `src/Shared` as a separately versioned protocol dependency. Preserve client/server compatibility when changing protocol usage.

## Server connections

- A server connection is represented as a coordinator. `Client.Application` owns coordinator state and session workflows.
- `Client.Api` maps shared protocol models to client models and handles requests, responses, heartbeat, and server events.
- `Client.WebSockets` provides the underlying WebSocket connection; API transport services handle certificate checks and HTTP media transfers.
- Preserve protocol negotiation, certificate confirmation, reconnection behavior, and permission checks when changing connection flows.
- Use [the shared protocol specification](src/Shared/Docs/Protocol.md) when working on wire behavior.

## Media and platform boundaries

- Desktop normally uses libwebrtc for session transport, with GStreamer providing capture, processing, encoding, decoding, and playback.
- The GStreamer WebRTC transport is available as an experimental alternative and as a fallback when the native libwebrtc library is unavailable.
- `IWebRTCTransport`, `IVideoMedia`, and related application ports separate transport from media implementation. Preserve those boundaries when changing audio or video behavior.
- `Client.OpenAL` provides desktop interface and notification sounds independently of voice and video media.
- Android uses libwebrtc and platform media services, without the desktop GStreamer or OpenAL runtime. Android supports watching shared screens; screen sharing originates from desktop.
- Preserve media-disabled behavior: voice and video must not initialize when disabled, while interface sounds remain independent.
- Treat native frame ownership, disposal, threading, and GPU resource lifetimes as explicit contracts. Preserve supported memory-backed rendering paths when changing accelerated rendering.
- Changes to native interfaces must remain consistent with managed bindings and the distributed native libraries.

Consult the relevant design and runtime documentation before changing media behavior:

- [libwebrtc integration](src/Client/Client.LibWebRTC/README.md)
- [libwebrtc runtime](src/Client/Client.LibWebRTC/Build/README.md)
- [GStreamer runtime](src/Client/Client.GStreamer/Build/README.md)
- [Voice gate](src/Client/Client.GStreamer/Docs/VoiceGate.md)
- [Audio latency](src/Client/Client.GStreamer/Docs/AudioLatency.md)
- [Video rendering](src/Client/Client.GStreamer/Docs/VideoRendering.md)
- [Video codecs](src/Client/Client.GStreamer/Docs/VideoCodecs.md)

## Persistence and account data

- Use the existing SQLite repositories and migration system for persistent state. Preserve compatibility with existing databases.
- Keep database access compatible with NativeAOT and the project's Dapper AOT integration.
- Database encryption is optional and uses SQLite3 Multiple Ciphers. Password storage uses platform secure storage where available.
- Preserve the distinction between database protection, cached files, and network security. Database encryption does not cover every stored file or imply end-to-end message encryption.
- Treat identities, authentication material, saved credentials, and database passwords as sensitive data. Do not expose them through logs or diagnostic UI.
- Preserve database locking, unlock, migration, and recovery behavior when changing storage workflows.

## Client interface

- New and modified menus must strictly match the existing flyout style, including icons, colors, spacing, dimensions, corner radii, and hover, pressed, focus, and disabled states.
- Use the shared `Flyout` and `MenuItemButton` components and the existing styles in `src/Client/Client.Avalonia.Styles/Styles/FlyoutStyle.axaml` and `src/Client/Client.Avalonia.Controls/Controls/Menus/MenuItemButton.axaml`. Do not replace them with standard `ContextMenu` / `MenuItem` controls or separate styling.
- Use icons from the shared `src/Client/Client.Avalonia.Assets/IconImages.axaml` collection. Add new icons to that collection following its conventions; neutral colors must follow the theme through shared brush resources.
- Before implementing a menu, consult existing client flyouts, such as `src/Client/Client.Avalonia.Desktop/Views/Sessions/Chat/ChatMessageFlyout.axaml`.
- Reuse shared controls and theme resources for other interface changes. Preserve runtime theme switching, keyboard navigation, and the existing layout conventions.
- Keep user-facing text focused on user actions and outcomes; expose implementation details only where they help explain a diagnostic or a meaningful choice.

## Localization

- Interface translations are stored in `src/Client/Client.Avalonia.Localization/Languages/*.arb` for English, Russian, Spanish, Japanese, and Chinese.
- English is the fallback language. Use the existing localization keys and ICU MessageFormat conventions.
- Read localized text through `TextExtension` in markup and `Strings.Get` or `Strings.Observe` in code, following the surrounding implementation.
- Preserve runtime language switching. Update affected translations together and keep placeholders and plural forms consistent.

## Validation

- Validate changed behavior with the relevant existing test projects: `Client.Application.Tests`, `Client.Infrastructure.Tests`, `Client.WebSockets.Tests`, `Client.Avalonia.Localization.Tests`, and `Shared.Tests`.
- Keep automated tests independent of desktop media runtimes and Android services unless a check explicitly targets those integrations.
- Use regression tests for meaningful changes to protocol handling, persistence, application state, and localization behavior.
- UI, audio, capture, rendering, and native interoperability changes also require checks on the affected platforms. Unit tests alone do not establish device or driver compatibility.
- [Client.TestBot](src/Client/Client.TestBot/README.md), `Client.Audio.SmokeChecks`, and the [video preview lab](src/Client/Client.Avalonia.Video.Preview/README.md) provide specialized integration checks.
- Report what was verified and any remaining validation limits accurately.

## Distribution

- Desktop distribution supports Windows x64 and Linux x64, with JIT and NativeAOT variants. Android is distributed as an APK.
- Installed desktop packages use Velopack updates. Standalone archives require manual replacement.
- Preserve platform, runtime variant, and release/development channel separation in update behavior.
- Keep launch options temporary: they must not overwrite saved preferences, and application-triggered restarts must preserve applicable overrides.
- Preserve third-party notices and the distribution requirements of bundled native components.
- Keep user-facing documentation in [README.md](README.md), release-note guidance in [release-notes/README.md](release-notes/README.md), and licensing details in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
