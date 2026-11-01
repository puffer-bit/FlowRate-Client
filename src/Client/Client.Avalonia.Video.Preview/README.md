# Video preview lab

Run on a Linux desktop with PipeWire and xdg-desktop-portal:

```sh
dotnet run --project FlowRate-Client/src/Client/Client.Avalonia.Video.Preview
```

Press **Start PipeWire**, then select a screen or window in the desktop dialog.
**Stop PipeWire** ends the portal session and returns to the pattern scenarios.
Closing the window also releases the capture and portal session.

**DMA-BUF (GPU capture)** is enabled by default. With **Capture only** off,
PipeWire RGB DMA-BUF frames (AR24/XR24/AB24/XB24) go straight to appsink and the
production `VideoView`. The OpenGL presenter imports the original DRM format and
modifier through EGL and draws to its display texture on the GPU. There is no
CPU pixel mapping, conversion or full-frame memory copy. The sample stays alive
while queued/imported. After drawing to the local output texture, the renderer
waits for that GPU read with `glFinish` and immediately releases the capture
buffer, rather than pinning it until the next frame. This synchronous wait is
used to ensure the compositor cannot overwrite an in-flight GPU read. The preview
uses the latest DMA-BUF frame without the network jitter buffer to avoid pinning
the compositor's small buffer pool. Submission to VideoView is capped at 144 FPS;
actual capture and display rates are measured separately. Source rates can be
lower when the desktop has no new content or the compositor is overloaded.

With **DMA-BUF** off, capture uses system memory and a multithreaded BGRA
converter. The source accepts `max-framerate=(fraction)[0/1,240/1]`; the negotiated
value is printed in caps. **Copy PipeWire buffers** adds a source copy and a
bounded queue in this memory mode. In DMA-BUF mode copying is always disabled.
Change modes while capture is stopped. A DMA-BUF import/negotiation failure is
reported as an error instead of silently changing to CPU readback.

**Capture only (no rendering)** connects the source straight to `fakesink`: no
converter, queue, buffer mapping or VideoView. Use it with DMA-BUF on or off to
compare the two capture paths under the same screen activity. It intentionally
leaves the video area blank.
There is no encoder or network transport. Every two seconds the sidebar and
console report source FPS and delivered FPS, plus negotiated output caps;
the console also reports frames skipped before preview and average VideoView handoff time. Keepalive is disabled,
so a still desktop may stop producing frames and statistics updates.

The standard performance overlay shows measured rendering statistics and the
rendering route; click it to collapse/expand. Synthetic network/decoder counters
and issue scenarios are only used in pattern mode. Pause/Next control those
scenarios, not the live capture.

The project copies the same bundled GStreamer runtime as the desktop app;
build it first if missing (see `../Client.GStreamer/Build/README.md`).

For a GPU-only source diagnostic enable both **Capture only** and **DMA-BUF**.
This advertises DMA_DRM formats and modifiers from the existing VA capability
probe, announces GstVideoMeta on fakesink, disables source copying and excludes
system-memory fallback. Compare its FPS with the memory capture-only mode under
the same screen activity. A negotiation failure means these DMA-BUF capabilities
were not accepted; it is not a measurement of capture throughput.
