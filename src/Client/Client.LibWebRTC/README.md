# Client.LibWebRTC

The voice of hybrid sessions over libwebrtc, the WebRTC engine of Chrome: ICE, DTLS-SRTP, the jitter
buffer (NetEq), the processing of the microphone (APM) and the estimate of the network. On the desktop
it is an experimental setting next to GStreamer (Settings → Experimental features → Voice); Android
has no other engine. Screens are neither shared nor watched with it.

## Layout

| Path | Role |
|---|---|
| `Voice/VoiceTransport.cs` | `IWebRTCTransport` of every platform: the sections of the SFU, the offer/answer rounds, the volumes, the speaking indicator, the statistics |
| `Voice/IVoicePeer.cs` | What a platform gives it: one peer connection that answers the offer of the SFU |
| `Native/` | The peer of the desktop: `flowrate_webrtc.h`/`.cc`, a C interface over the C++ one of [webrtc-sdk/libwebrtc](https://github.com/webrtc-sdk/libwebrtc), and its P/Invoke bindings |
| `Build/LibWebRTCRuntime.targets` | Copies the native libraries next to the executable |
| `Client.Avalonia.Android/Platform/Media/WebRTC/AndroidVoicePeer.cs` | The peer of Android, over the Java bindings of libwebrtc (`VoicePeer.java`) |
| `Client.Avalonia.Desktop/Services/Media/MediaEngineTransportFactory.cs` | Picks the engine of each session on the desktop |

The SFU is described exactly as for webrtcbin (`Client.WebRTC/Sdp`); libwebrtc answers, and the SSRC
the SFU created each producer with is written into the answer, since mediasoup knows our streams by it
alone. The format parameters of the sections we send in set FEC, DTX and the 64 kbit/s of the voice.

## Building

```bash
src/Client/Client.LibWebRTC/Native/build.sh
```

Downloads the pinned release of webrtc-sdk/libwebrtc (checked by its SHA-256) and builds
`libflowrate_webrtc.so` against it with the g++ of the system, into `Build/artifacts/linux-x64/libwebrtc/`.
Takes seconds; `publish.sh` runs it for every linux-x64 build. Without it the client builds and runs,
and the setting falls back to GStreamer. A Windows build is not there yet.

## What differs from the GStreamer voice

- The microphone and the speakers are the devices of libwebrtc (PulseAudio, WASAPI). The device of the
  settings is found by its name, as PulseAudio gives libwebrtc no ids.
- Noise suppression is the one of libwebrtc, on for every level but off; RNNoise is not used.
- The voice gate is `VoiceGateDecision` on the peaks of the processed microphone, with the pre-roll and
  the ramps of the GStreamer gate; it has no voice detection.
- The levels of the voices we receive are taken before the playback volume, so a voice turned down or
  muted still lights its speaking indicator.
