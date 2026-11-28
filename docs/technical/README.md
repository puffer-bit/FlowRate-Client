# Technical documentation

FlowRate is a self-hosted communication system with text chats, voice sessions, and desktop screen sharing. Its repositories cover the client, server, and shared protocol. This guide describes the runtime components, implementation boundaries, and tools for checking behavior.

- [System architecture and tradeoffs](architecture.md)
- [Chat, voice, screen sharing, and recovery walkthrough](demo.md)
- [Client setup](../../README.md)
- [Source builds, tests, and repository layout](../../DEVELOPMENT.md)

## Implementation overview

FlowRate lets a community run its own chat, voice, and screen-sharing service. The system combines a C# desktop client, a .NET server for accounts and session control, and a mediasoup SFU for forwarding real-time media.

| Area | Implementation |
|---|---|
| Desktop | .NET 10, Avalonia, shared presentation logic, platform adapters |
| Communication | WebSocket control protocol, WebRTC media, gRPC between server and SFU |
| Media | libwebrtc transport and GStreamer desktop media pipelines |
| Storage | SQLite repositories and migrations; optional client database encryption |
| Verification | Application, persistence, protocol, and transport tests; headless media and load-test tools |
| Delivery | GitHub Actions; Windows and Linux packages with JIT and NativeAOT variants |

## Implementation references

| Topic | Starting point | Behavior and responsibilities |
|---|---|---|
| Application boundaries | [Client application ports](../../src/Client/Client.Application/Ports) | How workflows use interfaces while hosts compose concrete services. |
| SFU recovery | [Client waiting tests](../../src/Client/Client.Application.Tests/HybridSessionNodeWaitingTests.cs) | Retaining participants, resetting media state, waiting for an available node, and avoiding unnecessary moves after recovery. |
| Native media integration | [libwebrtc integration](../../src/Client/Client.LibWebRTC/README.md) and [video rendering](../../src/Client/Client.GStreamer/Docs/VideoRendering.md) | Transport versus capture/codec responsibilities, frame ownership, and platform differences. |
| Protocol compatibility | [Shared protocol specification](../../src/Shared/Docs/Protocol.md) and [schema tests](../../src/Shared/Shared.Tests/ProtocolSchemaTests.cs) | Versioned shared types, validation, and compatibility between independently built components. |
| Reproducible measurements | [Client.TestBot](../../src/Client/Client.TestBot/README.md) | How to generate traffic, interpret delivery counters, and distinguish a client bottleneck from a server bottleneck. |

## Measurement report outline

Use the existing test bot guide to choose a scenario. Record the date, exact revisions, OS, CPU/RAM, network topology, transport backend, bot count, active speakers, screen watchers, ramp interval, test duration, and exact command. Record server/SFU and load-generator CPU and memory separately, along with failures and available latency percentiles.

Explain what each timing measures. Chat delivery and stamped screen-frame timings have clock constraints documented in the bot guide. Drawing-to-decoding time does not measure microphone-to-speaker latency or final display presentation. Example counters in documentation are not benchmark results.

## Validation

Use the [functional walkthrough](demo.md) to check chat, voice, screen sharing, and SFU recovery with two clients. Record client and server revisions, environment details, observed results, and relevant logs. Automated tests cover specific contracts; device access, capture, rendering, and recovery also need checks in the target environment.
