# Encoders and decoders

How the encoder of a shown screen and the decoder of a watched one are chosen, what they are set
to, and what the user sees of it. The way of the frames after the decoder is in `VideoRendering.md`.

Both transports use the same elements: the screen is captured and encoded by `GstScreenCapture`
and decoded by `GstVideoDecoder` (`IVideoMedia`), whether the packets travel through webrtcbin or
libwebrtc.

## Buffer ownership and queue budgets

`GstEncodedNative.Read` lends the source `Gst.Buffer` through `EncodedVideoFrame.NativeBuffer`
for the duration of the synchronous callback. A GST destination copies the buffer header and
references its payload memory, keeping timestamps independent and the payload alive after the
callback. GStreamer copies non-shareable memory when necessary. Frames from other backends keep
using the byte-copy path. Neither the frame nor its borrowed native wrapper may be kept after the
callback without retaining ownership separately.

The decoder accepts up to 60 queued encoded frames, with an 8 MiB payload budget and a 500 ms
queued-time threshold. `EncodedFrameGate` checks all budgets before pushing; appsrc does not
silently discard frames. An overflow drops dependent frames and requests a keyframe, with requests
limited to once per second. A single oversized keyframe is allowed into an empty queue so that
recovery cannot be blocked by the byte budget. Time is checked against the currently queued span,
so one incoming frame can cross the threshold before the following push detects it.

Raw frame sinks retain one pending frame and disable `last-sample`; encoded frame sinks also
disable `last-sample` but keep their non-dropping queues. The CPU screen branch limits the frame
rate before scaling and color conversion in the individual encoder and preview legs.

Run the native ownership, queue overflow/recovery, requested keyframes, software H.264/AV1 encoding
and decoding, and GStreamer RTP round-trip checks with:

```powershell
dotnet run --project src/Client/Client.Audio.SmokeChecks -- --video-buffers
```

## Choosing by the registry

No vendor is detected. The hardware plugins of GStreamer (nvcodec, va, qsv, amfcodec,
mediafoundation, d3d11) register their elements only when the device and its driver can run
them, so an element that `ElementFactory.Find` finds is one this system can use.
`VideoEncoderSelector` and `VideoDecoderSelector` walk a fixed list in order of preference and
take the first element that is there. Hardware comes first, and the software element is the last
resort. Every candidate is free of GPL code (no x264, no avdec).

### Encoders (H.264)

| Windows | Linux | Kind |
|---|---|---|
| `nvd3d11h264enc` | `nvcudah264enc` | NVIDIA NVENC |
| `nvh264enc` | `nvh264enc` | NVIDIA NVENC, older plugin |
| `qsvh264enc` | `vah264enc` | Intel Quick Sync / VA-API |
| `amfh264enc` | `vah264lpenc` | AMD AMF / VA-API low power |
| `mfh264enc` | | Media Foundation |
| `openh264enc` | `openh264enc` | software |

The session selects H.264 (the default), VP8 or AV1 for new screen tracks. The transport uses
the codec the SFU announces, rather than substituting H.264 for an unknown codec. VP9 has encoder
and decoder candidates but is not offered in session settings.

`VideoCodecPipeline` defines the encoded caps, parser, RTP payloader and depayloader together.
Capture, decoding and the GStreamer transport use that description. AV1 travels as parsed
OBU streams aligned to temporal units; `av1parse` assembles the depayloader's OBU output before
decoding. The AV1 encoder candidates are NVIDIA, Intel and AMD hardware, then SVT-AV1; the
decoder candidates are platform hardware, NVIDIA and finally dav1d. SVT-AV1 uses low-delay CBR
without lookahead, so it does not retain a random-access group before sending frames.

Both senders attach a single-layer dependency descriptor, which mediasoup needs to recognize AV1
keyframes. The SFU and the senders reserve extension id 6 for it. libwebrtc generates it from the
passthrough encoder's generic frame information. The GStreamer transport adds it after packetization
with `Av1RtpDependencyDescriptor` and `GstPacketTransform`; only RTP packets are copied, and buffer
metadata is preserved. Buffers and buffer lists are both supported. The payloader reserves 16 bytes
for the extension so packets stay within the existing 1200-byte MTU. This path requires GStreamer 1.28+.
The SFU's `npm test` verifies an actual mediasoup worker forwards the client's AV1 keyframe fixture.

AV1 requires an updated coordinator and SFU, matching Shared definitions, and updated clients
for every viewer. The desktop libwebrtc runtime must be rebuilt from the updated native sources
on both Windows and Linux; older binaries advertise only H.264 and VP8. The GStreamer transport
requires the AV1 runtime elements checked by `GstElementCheck`. Android viewing depends on the
AV1 decoders exposed by its libwebrtc runtime and device; desktop remains the source of screen sharing.

### Decoders (H.264)

| Windows | Linux |
|---|---|
| `d3d11h264dec` | `vah264dec` |
| `nvh264dec` | `nvh264dec` |
| `openh264dec` | `openh264dec` |

### The settings

- **Encoder** (Video tab): the element picked there is moved to the front of the list when this
  system has it; otherwise the usual order applies. Used from the next share.
- **Decoder** (Video tab): selects a software or hardware decoder directly, when this system has
  it. Applies to screens opened after the change. The former hardware-decoding switch is no longer
  shown; its disabled state is represented by the software decoder when loading settings.
- The lists show the elements this system has, in the order above, as "NVIDIA NVENC, video card
  (nvd3d11h264enc)" (`VideoElementNames`). The automatic choice names the element it comes to: the
  first of the list.

## Tuning

`VideoEncoderSelector` gives every encoder the bitrate and the properties of low latency. A
property that only some versions of an element have (the `Tuning` of a candidate) is tried on an
element of its own first (`Accepts`), since a property the element does not know fails the parse of
the whole branch.

| Encoder | Bitrate | Low latency | GOP | Frame size bound |
|---|---|---|---|---|
| NVENC (`nvd3d11`, `nvcuda`) | kbit/s | `rate-control=cbr` / `rc-mode=cbr`, `tune=ultra-low-latency`, `zero-reorder-delay=true` / `zerolatency=true` (names depend on plugin version) | `gop-size=-1` | `vbv-buffer-size` |
| NVENC, older (`nvh264enc`) | kbit/s | `preset=low-latency-hq`, `zerolatency=true`, `rc-mode=cbr` | `gop-size=-1` | `vbv-buffer-size` |
| AMF | kbit/s | `usage=ultra-low-latency`, `rate-control=cbr` | only when asked in this usage; `gop-size=0` (an IDR at the start alone) where the usage is not taken | |
| VA-API | kbit/s | `rate-control=cbr` | `key-int-max=1024` | `cpb-size` |
| Quick Sync | kbit/s | `rate-control=cbr`, `low-latency=true` | `gop-size=65535` | `max-frame-size-i` |
| Media Foundation | kbit/s | `low-latency=true`, `rc-mode=cbr` | the encoder's own | |
| OpenH264 | bit/s | `usage-type=screen`, `rate-control=bitrate`, `complexity=low` | `gop-size=2147483647`, `scene-change-detection=false` | |

Left to their defaults, NVENC and AMF are tuned for quality: a variable rate that sends a keyframe
as a burst of several times the average frame, and frames held back for reordering.

NVENC plugin versions use different names for rate control and zero latency. Both spellings are
probed independently so a renamed property does not silently leave the encoder at its default.
Quick Sync's low-latency mode is also enabled explicitly rather than relying on its default of off.

The GOP is the longest each encoder takes: a keyframe is made when a viewer asks for one (below), and
the default GOPs (30 to 90 frames) made several of them a second at a high rate. The frame size bound
is what the bitrate carries in 100 ms (`KeyFrameWindowMs`): left alone, a keyframe of a screen of text
is ten to fifty times the average frame, up to half a second of the uplink in one burst, which loses
packets, and each loss brings a PLI and one more keyframe. Bounded, the keyframe comes out blurred
and the next few frames sharpen it. The GOP and the bound are used where the element has them, the
same way as the tuning above; `SetBitrate` changes the bound with the bitrate.

Whatever the encoder, the stream is constrained baseline (what the SFU announces,
`profile-level-id 42e01f`, and what OpenH264 of a viewer always decodes), and `h264parse
config-interval=-1` puts the parameter sets in front of every keyframe, so a viewer can start at
any of them.

## The branch of the encoder

`ScreenEncodingBranch.Build` tries descriptions from the one with the fewest copies down, and takes
the first that parses. An element that does not take what comes before it fails the parse, so the
next one is tried (Media Foundation without a GPU refuses textures, for one).

1. **Direct3D 11** (Windows): the capture is `d3d11screencapturesrc` and the encoder takes textures
   (`nvd3d11h264enc`, `amfh264enc`, `qsvh264enc`, `mfh264enc`). `videorate` and the tee work on
   `memory:D3D11Memory`, `d3d11convert` scales and converts to NV12 for the encoder. Nothing goes
   through memory but the preview.
2. **VA-API with DMA-BUF** (Linux): the capture hands out DMA-BUF and the encoder is a VA-API one;
   `pipewiresrc` uses `min-buffers=1`, `always-copy=false` and negotiates RGB DRM formats
   including GPU modifiers. Before the bridge and its queues, `vapostproc` imports and converts
   into owned NV12 VA surfaces. This returns the scarce compositor buffers without waiting for
   encoding or presentation. The bridge limits the rate after this GPU conversion with a fractional monotonic-clock
   schedule, without retaining skipped frames. This enforces the configured ceiling even with
   variable-rate caps (`framerate=0/1`), independently of caps negotiation. The VA capture branch tees directly into the encoder
   and preview queues, without another input queue or videorate holding GPU surfaces.
   After enforcing the ceiling, the bridge advertises the configured nominal frame rate to the
   encoder in a separate sample header. PipeWire keeps its original VFR caps: the change is confined
   to the appsrc side of the bridge. GPU memory, timestamps, segment, colorimetry and memory
   features are preserved; cached caps are rebuilt when the capture format or size changes.
   This matters for VA H.264: with an unknown frame rate the encoder substitutes 30 fps for rate
   control and output timing, even when the bridge forwards 144 frames/s
   ([GStreamer 1.28.7 source](https://github.com/GStreamer/gstreamer/blob/1.28.7/subprojects/gst-plugins-bad/sys/va/gstvah264enc.c#L1507)).
   Before starting, the encoder is opened in READY to query its VA display context, then closed.
   That context is applied to the whole pipeline while it is still NULL. The appsink/appsrc
   bridge otherwise breaks upstream context discovery, allowing separate VA displays and
   expensive transfers between them. Startup logs confirm that the shared display was prepared.
   VA H.264 also requests target-usage=7, one reference, four slices and no B frames where the
   installed encoder accepts those properties. This favors throughput and latency over compression
   efficiency, without reducing the capture resolution or configured frame rate.
3. **VA-API**: the frames come in memory, `vapostproc` uploads, scales and converts them.
4. **CPU**: `videoscale ! videoconvert` and then the encoder, whatever it is.

The log names the way (`scaled on the GPU (Direct3D 11)`, `GPU (VA-API)`, `CPU`) in the line the
capture starts with.

Both legs drop late frames instead of queueing them, except the sink of the encoded frames: a lost
frame there would break every one up to the next keyframe, so it holds the encoder back instead.
The preview leg keeps the size and rate of the capture. On the Linux VA capture path, when the
presenter supports DMA-BUF, it exports NV12 DMA-BUF to OpenGL without CPU readback. Local preview
frames have no presentation timestamp (`-1`): the presenter uses the latest frame instead of
retaining a network jitter queue. The preview sink retains at most one queued frame and no last
sample. The shared renderer also supports RGB DMA-BUF and releases those buffers after GPU reads
complete, as exercised by `Client.Avalonia.Video.Preview`.

Other paths deliver BGRA memory as before. The preview valve (`preview_valve`) is closed while
the sharer does not watch their own screen (`IScreenCapture.SetPreviewShown`); a share starts
unwatched. The DMA-BUF capture path is selected for VA encoders with advertised RGB import caps;
software encoders retain the memory capture path. Live capture, encoder and compositor rates
remain available in the existing capture logs and media statistics.

### Keyframes and bitrate

- A viewer who joins, or whose decoder lost frames, asks for a keyframe; the transport passes the
  request on to `IScreenCapture.RequestKeyFrame`, which sends a force-key-unit event from the sink of the
  encoded frames up to the encoder.
- Keyframes are made only when asked for: the SFU asks when a viewer joins and repeats a request
  until the keyframe reaches it. `GstScreenCapture` answers one request of a wait, and one that comes
  less than 500 ms after a keyframe waits until then. A keyframe every 2 seconds on top of that was a
  burst every 2 seconds, which lost packets on a slow uplink.
- `SetBitrate` sets the bitrate properties the encoder got when it was made, and the frame size
  bound, for the new rate. An encoder that does not take them while it runs keeps its bitrate.

## The decoder

`VideoOutputSelector.Select` picks the decoder (above) and the form its frames leave in: a GPU
surface where the screen can import it and **Show frames without copying** is on, otherwise planes
or BGRA in memory (`VideoRendering.md`, "Output kinds"). The software decoders always deliver in
memory.

When a GPU surface fails, the decoder builds its pipeline anew in memory and asks for a keyframe.
Before the first frame of every pipeline it raises `IVideoDecoder.DecodingChanged` with a
`VideoDecoding`: the element, whether the GPU decodes, the form of the frames, and the GPU kind that
failed before, with the reason when it is known.

## What the user sees

- **Statistics of the media** ("Logging and debug" → "Media"): drawn over every screen, or only over
  the expanded one, by `StreamPerfOverlay` (`Client.Avalonia.Controls`, shared with Android). A line
  of the main values (frames shown/received, the delay on this device, loss, bitrate) expands with a
  click, for every screen at once, into the network, the transport, the encoder or decoder, the
  drawing and the sound of the screen, with graphs of the last 30 s. Only what this device measured
  is shown; nothing goes through the server.

  The drawing part has the frame counters and the way of the frames: who decodes (from
  `DecodingChanged`) and the way the frames really arrive in (from the frames themselves), "drawn
  without OpenGL" when the bitmap presenter copies them, and after a fallback the kind that failed and
  why (`VideoView.Route`).

  The decoding report travels `IVideoDecoder.DecodingChanged` → the transport
  (`IWebRTCTransport.VideoDecodingChanged`, by user and source) → `IHybridSession` →
  `ScreenStreamVM.Decoding` → `VideoView.SetDecoding`. Android decodes in libwebrtc, without an
  `IVideoDecoder`, and reports nothing; its decoder is named by the statistics of libwebrtc
  (`decoderImplementation`).

  The statistics come from `IWebRTCTransport.PerfSampled` (`MediaPerfReport`), read only while the
  overlay or the journal of the media is on: the encoder and the decoder are timed from outside by
  `GstCodecTimer` (probes on their pads, matched by timestamp, or in order for the VA-API encoders,
  which count their own timestamps), libwebrtc adds the RTCStats of its streams and, on the desktop,
  the timings of `flowrate_video.cc` (`fr_video_source_take_stats`, `fr_video_take_receive_stats`).
  `MediaPerfJournal` writes a summary of every stream and the anomalies into the journal, and every
  reading into `media-yyyyMMdd.jsonl` when asked.
- **On this computer** (under "Show frames without copying"): the ways without copying the screen
  found when the window opened (`VideoSurfaceSupport.Kinds`), "Not available" when there are none,
  or the kinds that failed in this run (`VideoSurfaceSupport.FailedKinds`, with the reason the first
  failure was reported with, `FailureReason`), which come back only after a restart. On Windows the
  probe claims Direct3D 11 whenever OpenGL draws, so the line says "Direct3D 11" until the first
  frame shows whether ANGLE takes the textures.
