# FlowRate client libraries

The libraries of the [FlowRate](https://github.com/puffer-bit/FlowRate-Client) desktop client, without
its interface, for other hosts such as bots. A host connects to a FlowRate server as a regular user:
it logs in, joins voice sessions, talks and shares screens over libwebrtc, and reads and writes chats.

## Packages

| Package | What it is |
|---|---|
| `FlowRate.Client` | All of the below in one reference. |
| `FlowRate.Shared.Core` | The protocol shared with the server: messages, models, permissions, validation. |
| `FlowRate.Client.Platform` | Operating-system helpers: native environment variables, the platform name. |
| `FlowRate.Client.Core` | Domain models, settings, media primitives. |
| `FlowRate.Client.Application` | Coordinator and session workflows; the ports a host implements. |
| `FlowRate.Client.WebSockets` | The WebSocket connection. |
| `FlowRate.Client.Api` | The server API over the protocol. |
| `FlowRate.Client.WebRTC` | WebRTC negotiation shared by the transports. |
| `FlowRate.Client.LibWebRTC` | The libwebrtc transport, with the native library for linux-x64, win-x64 and osx-arm64. |
| `FlowRate.Client.GStreamer` | Audio and video through GStreamer. |

A chat-only host needs `FlowRate.Client.Api` alone; anything with voice takes `FlowRate.Client`.

## Requirements

- .NET 10.
- GStreamer installed on the system (`FlowRate.Client.GStreamer`): the client ships 1.28, older versions
  are not tested. Register it with `GstNativeLibraryResolver.Register(useInstalledRuntime: true)` before
  the first media call.
- The host passes what the libraries do not decide themselves: `ClientInfo` to `AddApi`, its data folder
  to `AddApplication`, `GStreamerSettings`, and its own logging through `Microsoft.Extensions.Logging`.

## Versions

The packages share one version, separate from the version of the desktop client. While the protocol
is 3, the versions are 0.x and a minor version may change the API.
