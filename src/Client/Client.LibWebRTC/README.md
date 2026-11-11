# Client.LibWebRTC

The voice of hybrid sessions over libwebrtc, the WebRTC engine of Chrome: ICE, DTLS-SRTP, the jitter
buffer (NetEq), codecs and the estimate of the network. GStreamer captures, processes and plays
all desktop audio; Android keeps its platform audio processing. On the desktop it is an experimental setting next to GStreamer (Settings → Experimental features → Voice); Android
has no other engine. On the desktop it also carries the screens, shown and watched, with their sound;
Android watches them only, decoded by libwebrtc itself (see below).

## Layout

| Path | Role |
|---|---|
| `Voice/VoiceTransport.cs` | `IWebRTCTransport` of every platform: the sections of the SFU, the offer/answer rounds, the volumes, the speaking indicator, the statistics |
| `Voice/IVoicePeer.cs` | What a platform gives it: one peer connection that answers the offer of the SFU |
| `Voice/IncomingVideoStream.cs` | One screen we watch: its encoded pictures go to a decoder of `IVideoMedia`, its keyframe requests back to the sender |
| `Voice/OutgoingSound.cs` | The sound of the screen we show: captured by `IAudioMedia`, pushed into libwebrtc 10 ms at a time |
| `Voice/OutgoingVideo.cs` | The screen we show: the capture of `IVideoMedia` encodes it, libwebrtc sends it and asks for keyframes and the bitrate |
| `Native/` | The peer of the desktop: `flowrate_webrtc.h`/`.cc`, a C interface over the C++ one of [webrtc-sdk/libwebrtc](https://github.com/webrtc-sdk/libwebrtc), `flowrate_video.cc`, the video codecs that pass the pictures through and the source of encoded ones, and the P/Invoke bindings |
| `Build/` | The scripts that build libwebrtc with that interface into one library, the committed libraries (`prebuilt/<rid>/`) and `LibWebRTCRuntime.targets`, which copies them next to the executable |
| `Client.Avalonia.Android/Platform/Media/WebRTC/AndroidVoicePeer.cs` | The peer of Android, over the Java bindings of libwebrtc (`VoicePeer.java`) |
| `Client.Avalonia.Desktop/Services/Media/MediaEngineTransportFactory.cs` | Picks the engine of each session on the desktop |

The SFU is described exactly as for webrtcbin (`Client.WebRTC/Sdp`); libwebrtc answers, and the SSRC
the SFU created each producer with is written into the answer, since mediasoup knows our streams by it
alone. The format parameters of the sections we send in set FEC, DTX and the 64 kbit/s of the voice.

## Screens

libwebrtc carries the screens and codes none of them: `IVideoMedia` captures, encodes and decodes them,
on the GPU, the same as for the GStreamer transport. Both video codec factories of libwebrtc are
replaced (`Native/flowrate_video.cc`, put in place by `Build/patches/flowrate_target.patch`), which
also leaves the software codecs of WebRTC out of the library.

### Watched

libwebrtc receives the screens we watch: the transport, the jitter buffer, the retransmissions and the
keyframe requests. It does not decode them. Its video decoders are replaced by ones that pass the
pictures through (`Native/flowrate_video.cc`, put in place by `Build/patches/flowrate_target.patch`):
each hands its encoded picture to `fr_set_video_receiver`, and gives libwebrtc a blank 2×2 picture back
so that it keeps its timing. The pictures go to the decoders of `IVideoMedia`, the same as for the
GStreamer transport, which decode on the GPU. libwebrtc only hands on pictures it could decode, so a
stream starts at a keyframe and has no gaps. A decoder that needs a keyframe anyway asks for it through
`fr_video_request_keyframe`: the next picture returns `WEBRTC_VIDEO_CODEC_OK_REQUEST_KEYFRAME`, and
libwebrtc sends a PLI. The pictures are told apart by the SSRC of their packets, which the SFU gives
each consumer of its own. For the statistics of the media each decoder also times its pictures: from
the first packet of one to it leaving libwebrtc, and the handing over itself
(`fr_video_take_receive_stats`, by SSRC).

On Android the Java libwebrtc decodes the screens itself, with MediaCodec where the device can
(`DefaultVideoDecoderFactory` without an EGL context, so the pictures come into memory). `VoicePeer.java`
hands each picture out as I420 planes, `AndroidVoicePeer` wraps them in a `MemoryYuvVideoFrame` that
gives them back with its last reference, and the transport (`isVideoDecodedByPeer`) passes them on by
the mid of their section. `Views/PictureView` of Android draws them with a Skia shader.

### Shown

The capture pushes its encoded pictures into an `fr_video_source`, which hands each to libwebrtc as
the native buffer of a frame. libwebrtc carries it to the encoder of the stream as it would carry a
texture, and that encoder sends the picture as it is: libwebrtc packetizes, paces and resends it. What
libwebrtc wants of an encoder goes back to the capture: a keyframe when the frame it asked to be one is
not, and the bitrate of its estimate (`SetRates`), which `OutgoingVideo` passes on when it moved by a
tenth. For the statistics the source counts the pictures pushed and the time each waited for the
encoder (`fr_video_source_take_stats`); a library built before these returns null timings
(`NativeTimings.IsAvailable`). The estimate starts at the bitrate the user set, never goes above it and goes down on loss: the
section carries them as `x-google-start-bitrate` and `x-google-max-bitrate`.

So that libwebrtc sends every picture it gets, the encoder says it keeps to the bitrate itself (a
trusted rate controller) and takes native frames, the sender degrades nothing, and its frame rate is
the one of the setting (144 goes through). The track is not marked as a screen: libwebrtc would then
repeat the last frame of a still screen, which an encoder that only passes pictures on cannot do. A
picture it gives again is dropped.

Session media settings are applied by replacing tracks inside the existing transport. The server
recreates paused screen producers and consumers when their settings changed, including buffering
settings. Screen tiles retain their last frame and expanded state while replacement tracks start;
switching codecs can briefly pause the picture until a new keyframe arrives.

An emptied section is reused only for a track with the same codec and NACK setting.
libwebrtc can keep the previous answer's feedback when generating the next one: reusing a section
that negotiated NACK off for a track with NACK on leaves the receive stream's NACK history at zero,
even though the new remote offer contains `a=rtcp-fb:102 nack`. A section with compatible feedback
can be reused; incompatible historical sections remain inactive.
The native negotiation regression check replaces disabled and enabled tracks on the production
transport, verifies the actual answers, and verifies compatible sections do not grow indefinitely:

```bash
dotnet run --project src/Client/Client.Audio.SmokeChecks -- --video-negotiation
```

This section selection fix is managed code and applies on both desktop platforms without a native
ABI change. The viewer needs the updated client to apply it.

Lost packets are resent as RTX, on the payload type and SSRC the SFU gave the producer: the section
offers the RTX codec, and the answer gets the SSRC of the SFU in its group FID next to the one of the
stream. Where the SFU reads the transport-wide sequence number (it says so with each outbound track),
every section we send in carries it, and libwebrtc estimates the bandwidth by the delays of its
transport-cc feedback; an SFU that reads none is offered none, as libwebrtc would take the missing
feedback for a network that delivers nothing. A screen that starts after the voice is probed up to its
bitrate within a second or so. One that starts in the very round of the microphone used to be probed
while only the voice was allocated, at twice its bitrate, and the result of that probe replaced the
start bitrate: the field trial `WebRTC-Bwe-ProbingConfiguration/abort_further:true/` stops that probe.
The same trial skips a probe when the estimate already exceeds its maximum: otherwise the default
5 Mbit/s probe limit can lower an estimate that is already higher, even on a fast connection.

A received picture goes to the decoder as soon as it is whole (`WebRTC-ForcePlayoutDelay` of 0), as
in the GStreamer transport: the timing of libwebrtc held pictures 10-20 ms longer on a clean network
and ran away to seconds on one that reorders packets. Every section the SFU sends in has a stream
(`a=msid`) of its own, so libwebrtc keeps no screen in step with a voice. The SPS of H.264 goes as the
encoder wrote it (`patches/flowrate_keep_sps.patch`). With all of it a screen arrives as fast as over
GStreamer: 7 ms from drawing to decoding on one machine, measured with the stamped test picture of
`Client.TestBot`.

The log of libwebrtc goes into the journal of the application through `fr_set_log`, as much of it as
the Logging and debug tab asks for, in a file of its own (`libwebrtc-*.txt`); on Android its libwebrtc
takes the level only when it starts. `FLOWRATE_WEBRTC_LOG=verbose|info|warning|error` prints it to
stderr besides, as the library has nothing else to print there.
`FLOWRATE_WEBRTC_FIELD_TRIALS=Name/Value/...` replaces the field trials of `fr_initialize`.

A screen that stops keeps its section, which goes on sending, nothing: the SFU only pauses its
producer, and the stream goes on with the next sequence number when it is back.

The sound of a shown screen is captured by `IAudioMedia` (GStreamer: the process loopback of Windows,
the streams of an application on PipeWire), cut into 10 ms and pushed into an `fr_audio_source` of its
own section (`Voice/OutgoingSound.cs`). libwebrtc encodes it as stereo Opus at 128 kbit/s without DTX,
as the GStreamer transport does; nothing of the processing of the microphone touches it.
Its source leaves audio processing options unset through `flowrate_screen_audio_options.patch`:
the legacy native capture processor is shared between senders. Desktop microphone processing now
runs independently in GStreamer, so screen sender options cannot override the user's gain or echo
cancellation settings.

To check this regression on Windows, use two clients with libwebrtc and enable AGC and echo
cancellation on the client sharing its screen. Compare its microphone volume before screen sharing,
while sharing with audio, and after stopping. Repeat start/stop and test sharing without audio as a
control. With speakers, check that the remote client's voice does not return through the sharing
client's microphone. Repeat with headphones to distinguish acoustic echo from duplicate delivery.
Also repeat with AGC and echo cancellation disabled: sharing must preserve these settings too.

The native source advertises H.264, VP8 and AV1 in both directions. AV1 is single-layer passthrough:
GStreamer provides the encoded OBU stream and libwebrtc packetizes it. Rebuild the Windows and Linux
prebuilt libraries after this change; older binaries advertise only H.264 and VP8. Check AV1 receive
negotiation with `Client.Audio.SmokeChecks --video-negotiation` and encoding, requested keyframes,
decoding and GStreamer RTP round trips with `--video-buffers`. Android viewing uses the codecs its
Java libwebrtc decoder factory and device expose; Android does not originate screen shares.

## Building

The native library is built from source by hand and committed: `Build/prebuilt/<rid>/`. Neither CI nor
the publish scripts build it. How to build it, on Linux and on Windows, is in `Build/README.md`.

## Desktop audio

The desktop uses the existing GStreamer microphone processing and playback sink with libwebrtc's
codecs and network handling. No second GStreamer RTP jitter buffer or decoder is involved.

```text
GStreamer microphone -> RNNoise / webrtcdsp -> input gain -> voice gate -> 48 kHz mono S16 PCM
    -> custom libwebrtc source -> Opus -> network
network -> libwebrtc / NetEq / PLC -> decode and mix, including track volumes -> 48 kHz stereo S16 PCM
    -> GStreamer playback shaping -> webrtcechoprobe -> audio sink
```

- `IVoiceAudioDevice` is the media boundary. `GstVoiceAudioDevice` owns one playback pipeline and
  one processed microphone for all native peers. It shares the microphone frames between callers;
  the last caller to configure it controls its settings until released, then its predecessor resumes.
  Owner handoffs apply volume and voice-gate options in place, preserving the learned RNNoise state.
  Capture is rebuilt when the input, suppression mode, echo cancellation or automatic gain changes,
  and stops when the last microphone owner is released.
- Capture reuses `MicrophoneChain`, `MicrophoneProcessing`, `VoiceGate` and the same settings as the
  microphone test. The neural modes and speech detection use the bundled `audiornnoise` plugin.
  An older GStreamer runtime without it keeps the existing Moderate fallback.
- Playback and microphone DSP share a top-level GStreamer pipeline and clock. The existing echo
  probe sees the actual mixed playback, after shaping and per-track volume. The microphone names
  this probe only when echo cancellation is enabled. Native APM capture processing is bypassed.
- `Native/flowrate_audio.cc` implements a device without platform hardware. GStreamer's clocked
  sink requests each 10 ms block through `fr_pull_playout`; this calls `NeedMorePlayData`, retaining
  NetEq, packet-loss concealment and native mixing. The source queue holds at most two blocks.
  A playback stall rebases timestamps instead of replaying a burst of old PCM.
- Capture ends in 10 ms frames and a two-buffer, dropping appsink. A slow transport cannot build
  an unbounded capture backlog. Screen audio remains an independent custom source, without
  microphone processing.
- Speaking indicators use processed capture peaks and remote levels before playback volume.
  Voice-gate summaries come from the shared GStreamer gate.
- The native ABI is checked before initialization. An old DLL/SO without external audio support is
  rejected, so the desktop factory falls back to GStreamer rather than opening a second audio device.
  **Both platform prebuilts must be rebuilt before release.** The external audio bridge does not
  change Android's Java peer.

## Audio bridge checks

With the bundled GStreamer runtime and a matching native prebuilt, run:

```bash
dotnet run --project src/Client/Client.Audio.SmokeChecks/Client.Audio.SmokeChecks.csproj
```

This uses synthetic capture and a clocked fakesink, without opening the microphone or speakers.
It checks playout cadence and recovery after a 300 ms output stall, 10 ms capture, gain, gate,
both neural modes with the echo probe, shared capture ownership and native peer restart. It does
not measure acoustic echo reduction or replace an end-to-end network test.

Before release, compare against native playback with packet loss, jitter and a short outage:
check intelligibility, recovery without growing playout delay, per-user volume and muting,
then test echo cancellation on speakers and device changes during a call.
