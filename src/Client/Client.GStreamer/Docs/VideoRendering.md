# Video rendering

How a shared screen gets from the decoder onto the screen of the viewer, and how it is sent at a
high frame rate. The goal is a smooth picture at any rate, up to 1080p at 144 fps, without copying
every frame through memory when the GPU can avoid it.
How the encoders and decoders are chosen, and what the frame counter shows, is in `VideoCodecs.md`.

Without zero copy the frames come in memory. With OpenGL they stay NV12 or I420 as the decoder
wrote them and the shader turns them into RGB; only without OpenGL are they converted to BGRA and
drawn from a bitmap, the way they always were.

## Where it lives

| File | Role |
|------|------|
| `Video/VideoOutputSelector.cs` | Picks the decoder and the chain after it from the settings, the GPU and what the screen supports |
| `Video/VideoFramePump.cs` | Takes samples out of the appsink and hands them over as `RawVideoFrame`, per output kind; `VideoCapsInfo` reads size, DRM format and colorimetry |
| `Video/GstMemoryYuvFrame.cs` | An NV12 or I420 frame in memory; holds the sample with the buffer mapped |
| `Video/GstDmaBufFrame.cs` | A DMA-BUF frame of a VA-API decoder; holds the sample |
| `Video/CudaGlFrame.cs` | A CUDA frame of an NVIDIA decoder, copied into OpenGL textures on the GPU |
| `Video/D3D11SharedTextureWriter.cs` | Copies Direct3D 11 frames into shared textures the compositor opens |
| `Interop/GstVideoNative.cs` | Native reads of a video buffer: PTS, video meta, DMA-BUF fds; the allocation probe |
| `Interop/GstEncodedNative.cs` | Encoded frames in and out of a pipeline, and the keyframe requests that travel upstream |
| `Video/GstVideoMedia.cs` | `IVideoMedia`: what a transport asks for a capture or a decoder |
| `Video/GstVideoDecoder.cs` | The decoding pipeline of a stream, rebuilt in memory when GPU frames fail |
| `Video/GstScreenCapture.cs` | The capture pipeline of a shown screen: its source and `ScreenEncodingBranch` |
| `Video/ScreenEncodingBranch.cs` | Scaling on the GPU for VA-API encoders, the encoder, the preview capped at 30 fps |
| `Video/VideoEncoderSelector.cs` | The encoder, with the one picked in the settings first |
| `Client.Core/Media/RawVideoFrame.cs` | A frame: pixels in memory, or `Gpu`; `Timestamp` in nanoseconds |
| `Client.Core/Media/GpuVideoFrame.cs` | Reference-counted frames: `DmaBufVideoFrame`, `GlCopyVideoFrame`, `D3D11SharedVideoFrame`, and `MemoryYuvVideoFrame` (in memory, held the same way) |
| `Client.Core/Media/VideoSurfaceSupport.cs` | What the screen can show without copying; failures reported back |
| `Client.Core/Media/FramePacer.cs` | Which frame is shown on a refresh |
| `Client.Core/Settings/VideoSettings.cs` | The Video tab of the settings; `VideoSettings.Current` is read when a branch is built |
| `Client.Avalonia.Video/` | `VideoView`, `GlVideoPresenter` + `GlVideoRenderer`, `AngleD3D11Import`, `BitmapVideoPresenter`, `VideoSurfaceProbe` |

## Path of a frame

The transport gives the decoder encoded frames and nothing else (`IVideoDecoder`), so the decoding
does not depend on how the stream arrived. With the GStreamer transport:

```
webrtcbin pad ─ queue ─ rtph264depay ─ h264parse ─ appsink          (Client.GStreamer.WebRTC)
                                                      │
                     IncomingVideo (reader thread) ── EncodedVideoFrame
                                                      │
appsrc ─ h264parse ─ <chain of VideoOutputSelector> ─ appsink        (GstVideoDecoder)
                                                         │
                       VideoFramePump (thread of the sink) ── RawVideoFrame
                                │
   transport → HybridSession → ActiveHybridSessionVM → ScreenStreamVM → HybridScreenTile
                                │
                            VideoView ── GlVideoPresenter, or BitmapVideoPresenter
                                │
                     VideoFrameQueue (FramePacer)
                                │
          GlVideoRenderer on the render thread ── picks a frame on every refresh of the monitor
```

The whole chain up to the presenter is synchronous on the thread of the sink. A frame is only valid
during that call: a presenter copies the pixels, or takes a reference to a GPU frame with
`GpuVideoFrame.Retain()` and gives it back with `Dispose()`. The pump gives back its own reference
right after the call. A GPU frame holds the GstSample, so the decoder does not reuse its buffer while
the screen reads it.

## Output kinds

`VideoOutputSelector.Select` decides per stream:

| Decoder | Screen supports | Chain | Frames |
|---------|-----------------|-------|--------|
| `va*` (AMD, Intel, Linux) | `DmaBuf` | `vah264dec ! video/x-raw(memory:DMABuf),format=DMA_DRM` | `GstDmaBufFrame` |
| `nv*` on Linux | `CudaGl` | `nvh264dec ! video/x-raw(memory:CUDAMemory),format=NV12` | `CudaGlFrame` |
| `d3d11*`, `nv*` on Windows | `D3D11Shared` | `… ! d3d11convert ! video/x-raw(memory:D3D11Memory),format=BGRA` | `D3D11SharedTextureWriter` frames |
| anything else, or zero copy off | `YuvMemory` | `<decoder> ! videoconvert ! video/x-raw,format={NV12,I420}` | `GstMemoryYuvFrame` |
| anything else, or zero copy off | — | `<decoder> ! videoconvert ! video/x-raw,format=BGRA` | pixels in memory |

Hardware decoding off in the settings picks the software decoder (`openh264dec`). A decoder picked
in the settings is used when the system has it. These settings apply to streams opened after the
change; a stream that plays keeps its branch (the settings tab says to watch it again).

The memory path is always the plain one. Converting on the GPU first and downloading BGRA is not
faster: on AMD with VA-API, `vah264dec ! vapostproc ! BGRA` read back into memory gave 28 fps at
1080p, `vah264dec ! videoconvert` 144.

### YUV in memory

With an OpenGL presenter (`YuvMemory`, independent of the zero copy setting) the plain path asks for
NV12 or I420, what decoders output anyway: `videoconvert` passes them through untouched and only
converts anything else (10-bit, 4:4:4). `GstMemoryYuvFrame` keeps the buffer mapped while the frame
is held, so nothing is copied before the upload; the renderer uploads the planes with
`GL_UNPACK_ROW_LENGTH` set to their stride (luma `R8`, chroma `RG8` for NV12 or `R8` twice for I420)
and draws them through the YUV shader. Compared with BGRA the CPU neither converts colors nor copies
the frame, and 1.5 bytes a pixel are uploaded instead of 4 (3.1 MB instead of 8.3 at 1080p).

The pump accepts a video meta on this path too, so a decoder with padded planes hands them over
without copying them into the default layout; without a meta the planes are where
`gst_video_info_set_format` puts them.

### What the screen supports

`VideoSurfaceProbe.RunAsync` runs when the main window opens, before a session is joined, and fills
`VideoSurfaceSupport`:

- A GL context shared with the compositor (`IOpenGlTextureSharingRenderInterfaceContextFeature`)
  means frames are drawn by `GlVideoPresenter`.
- **Linux:** `DmaBuf` when it is an EGL context with `EGL_EXT_image_dma_buf_import` and
  `glEGLImageTargetTexture2DOES`; `CudaGl` when `libcuda.so.1` loads.
- **Windows:** `D3D11Shared` whenever OpenGL draws: Avalonia renders with ANGLE there by default,
  and whether ANGLE opens shared textures (`EGL_ANGLE_d3d_share_handle_client_buffer`,
  `EGL_ANGLE_keyed_mutex`) is found out with the first frame, see "Direct3D 11".
- Any OpenGL presenter adds `YuvMemory`.
- Otherwise nothing; frames come in memory as BGRA and `BitmapVideoPresenter` draws them.

Only an EGL context can import DMA-BUF, so on X11 the desktop starts with
`X11RenderingMode.Egl` first, then GLX and the framebuffer (`Program.cs`).

The test bot never runs the probe, so it receives frames in memory; `--gpu-frames` makes it take GPU
frames to exercise those branches.

### When GPU frames fail

A pump that cannot make a GPU frame, or a presenter that cannot import one, calls
`VideoSurfaceSupport.ReportFailure`. The kind is taken out of the supported ones for good, and every
`GstVideoDecoder` of that kind builds its pipeline anew with the memory chain. The new decoder drops
what comes until a keyframe and raises `KeyFrameNeeded`; the transport asks the sender for one (the
GStreamer one sends the request upstream from its sink, and the RTP session turns it into a PLI), so
the picture comes back after about a second. The pipeline of webrtcbin is not touched, so nothing has
to hold its pad meanwhile. Streams opened later use the memory chain from the start.
`--gpu-frames-fail <sec>` of the test bot triggers this on purpose.

When OpenGL breaks altogether, `GlVideoPresenter` reports every kind still supported, since all of
them need it; `YuvMemory` streams are then rebuilt as BGRA for the bitmap.

## Drawing on the render thread

Avalonia calls `OpenGlControlBase.OnOpenGlRender`, `RequestAnimationFrame` and the updates of a
`CompositionDrawingSurface` on the UI thread. At 144 Hz a frame has 6.9 ms, and anything the
interface does (a menu, a list, a chat) takes longer now and then: frames piled up and were dropped.

`GlVideoPresenter` therefore only creates a `CompositionCustomVisual` and places it. Its handler,
`GlVideoRenderer`, runs on the render thread of the compositor:

- `OnAnimationFrameUpdate` comes on every refresh; while frames wait it invalidates the visual.
- `OnRender` leases Skia (`ISkiaSharpApiLeaseFeature`) and its OpenGL context
  (`TryLeasePlatformGraphicsApi`), picks the frame that is due, brings it into textures, renders it
  through the YUV or RGB shader into an RGBA texture of its own framebuffer, and draws that texture
  with `SKImage.FromTexture` (origin bottom left) into the place `VideoLayout.Fit` gives.
- Before the raw OpenGL calls Skia is flushed and the state it may leave (scissor, blend, unpack
  buffer, color mask) is reset; afterwards `GRContext.ResetContext()` makes Skia read its state again.
- The UI thread talks to the handler only with messages: stretch, clear, start, stop. Stop cannot
  delete OpenGL objects, since no context is current then; they are queued and the next renderer that
  renders deletes them (all contexts of the compositor share one group).
- If no OpenGL context can be leased, the presenter hides itself and `VideoView` hands the frames to
  `BitmapVideoPresenter`.

With the UI thread blocked for 30 ms every 200 ms, 1080p144 still showed 144 of 144 frames.

## DMA-BUF (VA-API)

The decoder exports NV12 (or P010) surfaces as DMA-BUF with a DRM modifier
(`drm-format=NV12:0x0200000020801b04` on AMD). `GstDmaBufFrame` takes the plane offsets and strides
from the video meta and the fd of the memory holding each plane.

`GlVideoPresenter` imports each plane as a separate EGL image of a single-plane format (`R8` for luma,
`GR88` for chroma; `R16` and `GR1616` for P010) with the same modifier, binds them to two textures and
turns YUV into RGB in a shader: BT.709 or BT.601 from the colorimetry, limited or full range. The
images are made anew for every frame and destroyed when the next one replaces it.

### Trap: the video meta

A decoder that outputs `DMA_DRM` caps refuses to negotiate unless downstream announces GstVideoMeta
("DMABuf caps negotiated without the mandatory support of VideoMeta" in `gstvabasedec.c`), since the
tiled layout is only in the meta. appsink refuses the allocation query, so
`GstVideoNative.AcceptVideoMeta` puts a probe on its sink pad that answers the query itself: the
video meta API and nothing else, returned as `GST_PAD_PROBE_HANDLED`. The probe is an
`UnmanagedCallersOnly` function that only makes native calls, so no wrapper is made on the streaming
thread.

`GST_QUERY_ALLOCATION` is `(140 << 8) | 6` = 35846 (downstream and serialized flags), read from the
`type` field right after the 64-byte mini object.

## CUDA (NVIDIA, Linux)

`CudaGlFrame` maps the CUDA memory of the buffer with `GST_MAP_CUDA` for as long as it lives
(`gst_cuda_memory_sync` first, since the decoder may still be writing). `CopyTo` runs on the render
thread with the GL context current: it pushes the CUDA context of the decoder, registers the two
textures with `cuGraphicsGLRegisterImage` once per texture and context, and copies each plane with
`cuMemcpy2D` from device memory into the mapped array. The presenter calls
`GlCopyVideoFrame.NotifyTextureDeleting` before it deletes a texture, which drops its registration.

Not run on hardware yet.

## Direct3D 11 (Windows)

`D3D11SharedTextureWriter` keeps six BGRA textures created on the device of the decoder with
`SHARED_KEYEDMUTEX`, which gives them share handles of the older kind
(`IDXGIResource::GetSharedHandle`): ANGLE opens only those, not NT handles. For every frame it takes
a free one, copies the decoded texture into it on the GPU (`CopySubresourceRegion` under the device
lock) and releases it with key 1. The share handles are no kernel handles and are never closed.

`AngleD3D11Import` hands each texture to ANGLE, which opens it on its own device:

1. Once per texture, `eglCreatePbufferFromClientBuffer(EGL_D3D_TEXTURE_2D_SHARE_HANDLE_ANGLE)`
   makes a pbuffer of it (`EGL_ANGLE_d3d_share_handle_client_buffer`), with any 8-bit RGBA
   configuration that binds to textures. `eglQuerySurfacePointerANGLE(EGL_DXGI_KEYED_MUTEX_ANGLE)`
   gives the keyed mutex ANGLE opened with it (`EGL_ANGLE_keyed_mutex`).
2. For every frame the renderer takes key 1 with a 4 ms timeout and binds the pbuffer to its input
   texture with `eglBindTexImage`; the RGB shader draws it into the texture of its framebuffer
   without swapping channels.
3. Right after the draw it calls `eglReleaseTexImage` and `glFlush` and releases key 0, so the
   decoder writes the texture again while the picture stays in the renderer's own texture.

No call is made on ANGLE's Direct3D device but through EGL. The first version opened the textures
itself on ANGLE's device (`OpenSharedResource1`) and copied them there with `CopyResource`, behind
ANGLE's back: the pictures stayed black and the AMD driver crashed, without an error that would
have fallen back to memory.

ANGLE refuses contexts shared with the compositor, so the presenter draws in the compositor's own
context, and the probe cannot ask that context for the extensions. On Windows it claims
`D3D11Shared` whenever OpenGL draws, and the renderer finds out with the first frame: when an
extension is missing, the pbuffer cannot be made or bound, the kind is reported as failed with the
reason, and the stream is rebuilt in memory. Turning off **Show frames without copying** keeps
the frames in memory from the start.

A frame the pacer dropped, or that could not be taken in 4 ms, was never read, so its texture is still
at key 1. The frame remembers whether the renderer took it (`MarkRead`), and the writer takes such a
texture back with key 1 instead of 0. A texture still held by a frame on screen is skipped. The
pbuffers are kept by share handle, all let go after a size change or past twelve of them (the
decoder made new textures). All COM calls go through vtable function pointers, which NativeAOT
allows.

Written 2026-10-29, not run yet.

## Pacing

Frames arrive unevenly: the jitter buffer, the decoder and the network hold some back and let others
through in a bunch. Drawn the moment they arrive, they stutter.

`FramePacer` places every frame at its timestamp plus a delay: the smallest delay between a timestamp
and the arrival of its frame seen over the last two seconds (the fastest a frame made it), plus room
for `PresentationBufferFrames` frames (the smoothed frame interval times the setting). On every
refresh of the monitor the presenter calls `Pick` and gets the oldest frame whose place has come.
When two are due, the newer one is shown on the next refresh, which a monitor faster than the stream
has to spare; only when more than two are due are the oldest dropped. A timestamp that goes back or jumps by more than a second starts the timing
over. The queue holds at most the buffer plus one frame, so the delay cannot grow and a decoder with a
fixed pool is never left without buffers.

With pacing off in the settings, or for frames without timestamps, the newest frame is shown.

The pacer does not fix a frame rate that does not divide the refresh rate (60 fps on 144 Hz shows
frames for 2 and 3 refreshes in turn); that needs a monitor with variable refresh.

## Sending

The capture is a pipeline of its own (`GstScreenCapture`): the source, then `ScreenEncodingBranch`,
which ends in a sink of encoded frames and one of preview frames. The transport pushes the encoded
frames into an appsrc in front of its payloader, stamped with the clock of its own pipeline, and
passes the keyframe requests that reach that appsrc on to `IScreenCapture.RequestKeyFrame`.

- The rate comes from the dialog (`ScreenShareSettings.Framerates`: 15, 30, 60, 144). Above 60 the
  dialog warns once; it remembers that in `DismissedPrompts` (`screen.highFramerate`).
- With a VA-API encoder the picture is scaled and converted by `vapostproc` into VA memory,
  otherwise by `videoscale ! videoconvert`. At 1080p, `vapostproc + vah264enc` encoded about 196 fps
  on an AMD RX 7700, `videoconvert + vah264enc` about 77.
- **Windows:** a monitor or window captured by `d3d11screencapturesrc` stays a Direct3D 11 texture all
  the way when the encoder takes textures (`nvd3d11h264enc`, `amfh264enc`, `qsvh264enc`, `mfh264enc`):
  `videorate` and the tee work on `memory:D3D11Memory`, `d3d11convert` scales and converts to NV12 for
  the encoder, and the preview is scaled to 720p BGRA by another `d3d11convert` before `d3d11download`.
  With another encoder, or when an element is missing, the branch is the one through memory. The log
  says which (`scaled on the GPU (Direct3D 11)`). Run on 2026-10-29 with `amfh264enc` (AMD): the
  capture of 2560x1440 kept 144 fps.
- **Linux:** PipeWire capture still comes through memory. The bundled `pipewiresrc` is built from
  PipeWire 0.3.65 for libpipewire 0.3.48, and knows no `DMA_DRM` caps, which the VA elements need to
  take a DMA-BUF. At 2560x1440 the capture gave 30-125 fps (KWin also sends a frame only when the
  screen changed). A plugin from PipeWire 1.2+ would allow `pipewiresrc (DMA-BUF) ! vapostproc`.
- The preview leg is capped at 30 fps (`videorate drop-only=true max-rate=30`): only the sharer sees
  it.
- The recommended bitrate grows with the square root of the rate: 8.8 Mbit/s at 1080p144.

## Measured on 2026-10-14

On an AMD RX 7700/7800 with a 165 Hz monitor, KDE:

- Avalonia with EGL on X11 renders about 160 fps.
- A 1080p144 stream through the DMA-BUF path: 144 of 144 frames shown after the first second.
- Two test bots through a local SFU: 144 fps sent and received; the DMA-BUF branch inside webrtcbin
  keeps 144 fps; a rebuild to memory recovers within a second.

## Not done or not verified

- NVIDIA (CUDA), and Windows receiving through the share handles of ANGLE: compiled, never run.
- P010 (10-bit) frames.
- YUV in memory (written 2026-10-26): compiles, never run.
- The desktop client in a real session; the parts were checked separately.
- The SFU announces H.264 `profile-level-id 42e01f` (level 3.1). 1080p144 needs level 5.2 on paper;
  decoders ignore the level today. Changing it needs the server and `WebRTC/Sdp/RtpCodec.cs` together.
