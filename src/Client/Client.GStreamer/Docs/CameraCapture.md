# Camera capture

Desktop hybrid sessions publish camera video through `CameraVideoInput` and the existing
`IVideoMedia` capture/encoding path. This does not capture audio: the microphone remains a
separate voice track. Camera and screen producers coexist on the same transport.

`GstCameraDeviceProvider` uses GStreamer device discovery and creates the element from the chosen
device. Windows uses the Media Foundation provider; Linux uses Video4Linux2 or an available
PipeWire provider. Camera identifiers and capture choices are persisted in `CameraPreferences`.
The broadcast button remembers the last successfully started source kind across app restarts;
remembering that kind does not automatically publish a camera when joining a session.

The source prefers the requested native size and frame rate, then the requested frame rate, then
another supported raw or MJPEG mode. `decodebin` converts MJPEG into raw frames, and the shared
encoding branch limits output size and rate. A camera start waits for an encoded frame before
reporting success. A later capture error stops the camera producer without disconnecting voice
or screen sharing.

Incoming cameras are routed to connected peers automatically, including peers joining later.
Each camera gets its own tile. Hiding a camera stops presentation; incoming media remains routed
and decoded, so showing it again does not require negotiation. Hiding the local camera preview
closes its preview branch while transmission continues. Camera tiles expose neither sound
controls nor viewers. Android can receive these tracks through its existing video path; Android
camera capture is not implemented here. Direct calls retain their existing media restrictions.

The Video settings page selects Screen or Camera. Encoder, decoder, and codec property overrides
are stored independently in database profiles 1 and 2. Existing screen preferences survive the
migration. Presentation settings remain shared. Both libwebrtc and GStreamer transports choose
the profile from the track source, not its codec or user ID.

## Runtime and checks

The Windows bundle includes `mediafoundation` and `jpeg`; the Linux build enables `v4l2` and
`jpeg`. An older bundle without these elements may not discover cameras or decode MJPEG.
Build the updated runtime using the scripts in `../Build` before distributing camera capture.

Run the raw/MJPEG bridge and native mode selection checks without opening a camera:

```bash
dotnet run --project src/Client/Client.Audio.SmokeChecks -- --camera
```

Application tests exercise state, failure rollback, automatic viewing, and independent codec
preferences. Infrastructure tests cover migration and persistence. Server application tests cover
routing to current/late peers, independent screen capture, and cleanup.

Real-device checks are still needed on Windows and Linux: busy or denied devices, USB removal,
repeated start/stop, multiple cameras, both transports, and concurrent camera/screen sharing.
