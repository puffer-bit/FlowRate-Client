# Client.LibWebRTC

The voice of hybrid sessions over libwebrtc, the WebRTC engine of Chrome: ICE, DTLS-SRTP, the jitter
buffer (NetEq), the processing of the microphone (APM) and the estimate of the network. On the desktop
it is an experimental setting next to GStreamer (Settings → Experimental features → Voice); Android
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
each consumer of its own.

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
tenth. The estimate starts at the bitrate the user set, never goes above it and goes down on loss: the
section carries them as `x-google-start-bitrate` and `x-google-max-bitrate`.

So that libwebrtc sends every picture it gets, the encoder says it keeps to the bitrate itself (a
trusted rate controller) and takes native frames, the sender degrades nothing, and its frame rate is
the one of the setting (144 goes through). The track is not marked as a screen: libwebrtc would then
repeat the last frame of a still screen, which an encoder that only passes pictures on cannot do. A
picture it gives again is dropped.

Lost packets are resent as RTX, on the payload type and SSRC the SFU gave the producer: the section
offers the RTX codec, and the answer gets the SSRC of the SFU in its group FID next to the one of the
stream. Where the SFU reads the transport-wide sequence number (it says so with each outbound track),
every section we send in carries it, and libwebrtc estimates the bandwidth by the delays of its
transport-cc feedback; an SFU that reads none is offered none, as libwebrtc would take the missing
feedback for a network that delivers nothing. A screen that starts after the voice is probed up to its
bitrate within a second or so. One that starts in the very round of the microphone used to be probed
while only the voice was allocated, at twice its bitrate, and the result of that probe replaced the
start bitrate: the field trial `WebRTC-Bwe-ProbingConfiguration/abort_further:true/` stops that probe.

A received picture goes to the decoder as soon as it is whole (`WebRTC-ForcePlayoutDelay` of 0), as
in the GStreamer transport: the timing of libwebrtc held pictures 10-20 ms longer on a clean network
and ran away to seconds on one that reorders packets. Every section the SFU sends in has a stream
(`a=msid`) of its own, so libwebrtc keeps no screen in step with a voice. The SPS of H.264 goes as the
encoder wrote it (`patches/flowrate_keep_sps.patch`). With all of it a screen arrives as fast as over
GStreamer: 7 ms from drawing to decoding on one machine, measured with the stamped test picture of
`Client.TestBot`.

`FLOWRATE_WEBRTC_LOG=verbose|info|warning|error` prints the log of libwebrtc to stderr.
`FLOWRATE_WEBRTC_FIELD_TRIALS=Name/Value/...` replaces the field trials of `fr_initialize`.

A screen that stops keeps its section, which goes on sending, nothing: the SFU only pauses its
producer, and the stream goes on with the next sequence number when it is back.

The sound of a shown screen is captured by `IAudioMedia` (GStreamer: the process loopback of Windows,
the streams of an application on PipeWire), cut into 10 ms and pushed into an `fr_audio_source` of its
own section (`Voice/OutgoingSound.cs`). libwebrtc encodes it as stereo Opus at 128 kbit/s without DTX,
as the GStreamer transport does; nothing of the processing of the microphone touches it.

H.264 and VP8 go both ways, which are what the clients send. Android neither shows nor watches screens,
as it has no `IVideoMedia`.

## Building

The native library is built from source by hand and committed: `Build/prebuilt/<rid>/`. Neither CI nor
the publish scripts build it. How to build it, on Linux and on Windows, is in `Build/README.md`.

## What differs from the GStreamer voice

- The microphone and the speakers are the devices of libwebrtc (PulseAudio, WASAPI). The device of the
  settings is found by its name, as PulseAudio gives libwebrtc no ids.
- Noise suppression is the one of libwebrtc at the moderate level, on for every level but off; RNNoise
  is not used. The audio settings show it as a single switch while libwebrtc is chosen, and hide the
  voice detection; their microphone test, which runs on GStreamer, then uses the moderate WebRTC level
  and no detection.
- The automatic gain control is the adaptive digital one of libwebrtc, switched by the same setting as
  the one of GStreamer. WebRTC itself takes the analog one on the desktop, which moves the volume of
  the microphone in the system; `Build/patches/flowrate_audio_processing.patch` replaces it, and lets
  the level of noise suppression be ours instead of always the high one.
- The processing runs at 48 kHz, so the voice keeps its band above 16 kHz, as with GStreamer.
- The voice gate is `VoiceGateDecision` on the peaks of the processed microphone, with the pre-roll and
  the ramps of the GStreamer gate; it has no voice detection.
- The levels of the voices we receive are taken before the playback volume, so a voice turned down or
  muted still lights its speaking indicator.
