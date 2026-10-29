# Encoders and decoders

How the encoder of a shown screen and the decoder of a watched one are chosen, what they are set
to, and what the user sees of it. The way of the frames after the decoder is in `VideoRendering.md`.

Both transports use the same elements: the screen is captured and encoded by `GstScreenCapture`
and decoded by `GstVideoDecoder` (`IVideoMedia`), whether the packets travel through webrtcbin or
libwebrtc.

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

AV1, VP8 and VP9 have lists of their own (`GetCandidates`), but screens are sent in H.264.

### Decoders (H.264)

| Windows | Linux |
|---|---|
| `d3d11h264dec` | `vah264dec` |
| `nvh264dec` | `nvh264dec` |
| `openh264dec` | `openh264dec` |

### The settings

- **Encoder** (Video tab): the element picked there is moved to the front of the list when this
  system has it; otherwise the usual order applies. Used from the next share.
- **Decode on the video card** off: the software decoder is taken. **Decoder**: the element picked
  there, when this system has it. Both apply to screens opened after the change.
- The lists show the elements this system has, in the order above, as "NVIDIA NVENC, video card
  (nvd3d11h264enc)" (`VideoElementNames`). The automatic choice names the element it comes to: the
  first of the list.

## Tuning

`VideoEncoderSelector` gives every encoder the bitrate and the properties of low latency. A
property that only some versions of an element have (the `Tuning` of a candidate) is tried on an
element of its own first (`Accepts`), since a property the element does not know fails the parse of
the whole branch.

| Encoder | Bitrate | Low latency |
|---|---|---|
| NVENC (`nvd3d11`, `nvcuda`) | kbit/s | `rate-control=cbr`, `tune=ultra-low-latency`, `zero-reorder-delay=true` |
| NVENC, older (`nvh264enc`) | kbit/s | `preset=low-latency-hq`, `zerolatency=true`, `rc-mode=cbr` |
| AMF | kbit/s | `usage=ultra-low-latency`, `rate-control=cbr` |
| VA-API, Quick Sync | kbit/s | `rate-control=cbr` |
| Media Foundation | kbit/s | `low-latency=true`, `rc-mode=cbr` |
| OpenH264 | bit/s | `usage-type=screen`, `rate-control=bitrate`, `complexity=low` |

Left to their defaults, NVENC and AMF are tuned for quality: a variable rate that sends a keyframe
as a burst of several times the average frame, and frames held back for reordering.

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
   `vapostproc` takes the buffers as they are.
3. **VA-API**: the frames come in memory, `vapostproc` uploads, scales and converts them.
4. **CPU**: `videoscale ! videoconvert` and then the encoder, whatever it is.

The log names the way (`scaled on the GPU (Direct3D 11)`, `GPU (VA-API)`, `CPU`) in the line the
capture starts with.

Both legs drop late frames instead of queueing them, except the sink of the encoded frames: a lost
frame there would break every one up to the next keyframe, so it holds the encoder back instead.
The preview leg keeps the size and rate of the capture and is downloaded into memory as BGRA, on
Windows as well: at 2560x1440 and 144 fps that is about 2 GB/s read back from the GPU and uploaded
again by the presenter, the most of what sharing costs the CPU. So a valve at the head of the leg
(`preview_valve`) is closed while the sharer does not watch their own screen
(`IScreenCapture.SetPreviewShown`, set by `HybridSession` with the watch of that screen); a share
starts unwatched, so with the preview closed.

### Keyframes and bitrate

- A viewer who joins, or whose decoder lost frames, asks for a keyframe; the transport passes the
  request on to `IScreenCapture.RequestKeyFrame`, which sends a force-key-unit event from the sink of the
  encoded frames up to the encoder.
- AMF in its low-latency usages makes keyframes only when asked, whatever its GOP size, and requests
  of the SFU may get lost, so `GstScreenCapture` asks for one itself after 2 seconds without one.
- `SetBitrate` sets the bitrate properties the encoder got when it was made, for the new rate. An
  encoder that does not take them while it runs keeps its bitrate.

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

- **Frame counter** (Video tab, "Frame counter on screens"): over every watched screen,

  ```
  144 / 144 fps · 0 dropped
  Video card (Direct3D 11) · without copying, Direct3D 11
  ```

  The second line is green when the frames do not pass through memory. It names who decodes (from
  `DecodingChanged`) and the way the frames really arrive in (from the frames themselves), and says
  "drawn without OpenGL" when the bitmap presenter copies them. After a fallback a third line, in
  the warning colour, names the kind that failed and why.

  The report travels `IVideoDecoder.DecodingChanged` → the transport
  (`IWebRTCTransport.VideoDecodingChanged`, by user and source) → `IHybridSession` →
  `ScreenStreamVM.Decoding` → `VideoView.SetDecoding`. Android decodes in libwebrtc, without an
  `IVideoDecoder`, and reports nothing.
- **On this computer** (under "Show frames without copying"): the ways without copying the screen
  found when the window opened (`VideoSurfaceSupport.Kinds`), "Not available" when there are none,
  or the kinds that failed in this run (`VideoSurfaceSupport.FailedKinds`, with the reason the first
  failure was reported with, `FailureReason`), which come back only after a restart. On Windows the
  probe claims Direct3D 11 whenever OpenGL draws, so the line says "Direct3D 11" until the first
  frame shows whether ANGLE takes the textures.
