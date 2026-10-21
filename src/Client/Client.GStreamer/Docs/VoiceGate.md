# Voice gate

The microphone is sent only while the user speaks, like the voice activity mode of most voice
chats. Below a loudness threshold nothing leaves the machine: no audio, no packets.

This has two purposes:

- the noise of the room (fan, keyboard, breathing) is never heard by the others;
- the speaking indicator on the avatars lights up only for speech, not for noise (see
  [Speaking indicator](#speaking-indicator), which also works for senders without a gate).

## Where it lives

| File | Role |
|------|------|
| `WebRTC/OutgoingAudioBranch.cs` | Builds the capture branch, with the `level` element and the gate valve in it |
| `Audio/VoiceGate.cs` | Decides from the measured loudness whether the valve is open |
| `WebRTC/OutgoingTracks.cs` | Owns the gates and passes the level messages of the pipeline bus to them |
| `Interop/GstBusWatcher.cs` | Takes bus messages on a managed thread of its own |
| `Loader/GstElementCheck.cs` | Reports at startup whether the runtime has `level` |
| `Client.Core/Settings/AudioSettings.cs` | `IsVoiceGateEnabled`, `VoiceGateThresholdDb` and their limits |
| `Client.Infrastructure/Database/Migrations/001_Initial.sql` | Holds both settings in the `AudioSettings` table |
| `Client.Avalonia.Desktop/.../AudioControlVM.cs`, `AudioControl.axaml` | The settings tab: switch, threshold slider, meter |
| `Build/linux/meson-options.txt`, `Build/windows/collect.ps1` | Put the `level` plugin into the bundled runtime |

## The capture branch

```
capture:   source ─ queue ─ audioconvert ─ audioresample ─ audio/x-raw,rate=48000,channels=1
                  ─ webrtcdsp ─ webrtcdsp   ← microphone only, see MicrophoneProcessing
encoding:
       ─ volume (input_volume)
       ─ level (gate_level)          ← measures, posts a message every 10 ms
       ─ valve (gate)                ← opened and closed by VoiceGate
       ─ valve (mute)                ← the microphone button
       ─ opusenc ─ rtpopuspay ─ queue ─ webrtcbin
```

- The branch is two bins. `capture` holds everything that depends on the device and on the noise
  suppression level, and `OutgoingAudioBranch.ReplaceCapture` swaps it in a running branch when the
  user picks another microphone or level: the old part is set to NULL, which stops its streaming
  thread, then removed, and the new one is linked in. `encoding` stays, so the gate, the mute state
  and the RTP sequence go on. (`webrtcdsp` reads its settings only when it starts, hence the swap.)
- `level` and the gate valve are inserted only for a microphone. A file or a tone gets the branch
  without them, the same as before the gate.
- A microphone keeps them even while the gate is turned off in the settings: the valve then stays
  open and `level` posts nothing (`post-messages=false`). This is what lets the gate be turned on
  again during a call without rebuilding the branch.
- `level` sits after the input volume, so the gate hears exactly what would be sent: turning the
  input volume down makes the gate harder to open.
- The audio is made mono at 48 kHz before `volume`, so `level` always measures a single channel.
- The gate valve starts closed (`drop=true`) with `drop-mode=forward-sticky-events`: buffers are
  dropped, but the stream-start, caps and segment events still pass. webrtcbin and the encoder are
  negotiated before the user says anything.
- The mute valve is separate from the gate valve. Muting does not depend on the threshold, and the
  gate keeps working while muted (it just has no effect).

## How the gate decides

`level` posts an element message named `level` every `VoiceGate.LevelInterval` (10 ms). Its `peak`
field is an array with one value per channel, in dBFS (0 is full scale, silence goes to −∞). The
gate takes the loudest channel.

The gate compares **peak** levels, not RMS: the threshold is set against the microphone test meter,
which shows peaks, and both have to mean the same number.

| Value | Default | Meaning |
|-------|---------|---------|
| Threshold (setting) | −40 dB | A closed gate opens at this peak level or above |
| `HysteresisDb` | 6 dB | An open gate counts anything down to threshold − 6 dB as speech |
| `HoldTime` | 300 ms | How long an open gate waits after the last loud interval before closing |
| `LevelInterval` | 10 ms | How often `level` reports |

```
closed ──(peak ≥ threshold)──► open
open   ──(peak ≥ threshold − 6)──► open, hold timer restarted
open   ──(peak < threshold − 6 for 300 ms)──► closed
```

- Two thresholds (hysteresis): a word that fades out does not switch the gate on and off around a
  single value.
- The hold time covers the pauses between words, so a sentence goes out in one piece instead of
  being chopped into syllables.
- The threshold comes from the settings, see [Settings](#settings).

## Threads

```
streaming thread (queue)       level ──post──► pipeline bus
GstBusWatcher thread           TimedPopFiltered(Element) ──► OutgoingTracks.OnElementMessage
                                                         ──► VoiceGate.Update ──► valve["drop"]
```

- Nothing managed runs per buffer. Audio is measured natively by `level`, and managed code sees
  only 100 small messages per second.
- Messages are taken by `GstBusWatcher`, never by `Bus.SyncMessage`: a sync handler runs on the
  posting thread, and for PulseAudio elements that is a libpulse thread, where managed code
  crashes the process.
- Element messages of inner bins are forwarded to the pipeline bus. The transport finds the gate by
  the handle of the element that posted the message (`_gates`, keyed by the `level` element).
- The watcher also keeps the bus empty. Before the gate nobody read the bus of the transport
  pipeline, and its messages piled up; with 100 level messages per second that would grow without
  bound. Messages of other types are taken off and dropped.
- The watcher is created in `Connect` and disposed in `Disconnect` and `Dispose`, before the
  pipeline goes to `NULL`.
- The threshold is changed from the thread that saved the settings, while `Update` runs on the
  watcher. `VoiceGate` runs both under one lock; without it, turning the gate off could open the
  valve just before a level message that was already in flight closes it again.

## What is gated

Only a microphone (`DeviceAudioInput`): `WebRTCTransport.StartOutbound(outbound, input, volume,
voiceGateThresholdDb, noiseSuppression)` builds the gate for it and passes `isGated: false` for any other input. A
looped file or a tone (the test bot) is meant to be heard as it is, and the gate would cut the quiet
parts of music.

The threshold travels as `double?`, null meaning "gate off, send all the time":
`ActiveHybridSessionVM` → `IHybridSession.StartVoiceShareAsync(deviceId, inputVolume,
voiceGateThresholdDb)` → `IWebRTCTransport.StartOutbound`. The view model passes
`AudioSettings.ActiveVoiceGateThresholdDb`, which is null while the switch in the settings is off.

## Settings

Settings → Voice and audio:

- **Voice activity: "Send the microphone only while I speak"**: `AudioSettings.IsVoiceGateEnabled`,
  on by default. Off sends the microphone all the time, as before the gate.
- **Threshold slider** under the microphone test meter: `AudioSettings.VoiceGateThresholdDb`, from
  −60 to 0 dB, default −40 dB. `AudioService` clamps it to that range on save.

The slider shares the column and the scale of the meter: `GstMicrophoneTester` maps peaks from
`AudioSettings.MinVoiceGateThresholdDb` (−60 dB) to 0 dB onto the bar, and the slider covers the same
range, so the thumb sits where the bar has to reach. While the test runs, the bar turns green
(`IsVoiceDetected`) when the gate would send, with the same 300 ms hold. The hysteresis is not
simulated there.

Both are stored in the `AudioSettings` table (`001_Initial.sql`).

### Applied during a call

The threshold, the switch and the input volume apply to a running microphone at once:

```
AudioControlVM ──save (300 ms after the slider stops)──► AudioService.SaveSettingsAsync
  └─ AudioService.SettingsChanged(AudioSettings)
       └─ ActiveHybridSessionVM.OnAudioSettingsChanged
            ├─ IHybridSession.SetInputVolume ──► IWebRTCTransport.SetOutboundVolume ──► volume (input_volume)
            └─ IHybridSession.SetVoiceGateThreshold ──► IWebRTCTransport.SetVoiceGateThreshold
                                                       ──► VoiceGate.SetThreshold
```

- `SetThreshold(null)` opens the valve at once and silences `level`.
- A new threshold is applied by the next level message, within 10 ms. An open gate first waits out
  its hold time, so raising the threshold above the voice does not cut a word in half.
- Without a running microphone nothing happens; the next start reads the settings anyway.
- The input device is not switched during a call; it is taken on the next start.

Measured on the real branch with a constant sine (peak −10.5 dB), one second per step:

| Step | Packets |
|------|---------|
| Threshold −40 dB | 49 |
| Threshold −2 dB (closes below −8 dB) | 17 (the hold time), then 0 |
| Gate off | 50 |
| Threshold −40 dB again | 50 |
| Input volume 0.01 (peak −50 dB) | 17, then 0 |
| Input volume 1.0 | 49 |

With a threshold of −5 dB the gate would stay open: it closes only below −11 dB, and the sine is
above that. That is the hysteresis working, not a fault.

The test meter reports a peak per 50 ms and the gate per 10 ms. The larger window only makes the
meter react a little more readily than the gate; it does not shift the threshold.

## Latency and what is lost

The gate learns about a loud interval only after that interval has already reached the valve, so
it opens one `LevelInterval` late plus the bus delay. The very beginning of a word, about 10 ms,
is not sent. Measured on the real branch: the gate opens 5–30 ms after the sound starts.

After the last word, 300 ms of quiet audio are still sent (the hold time), then nothing.

Measured on the real branch with a 48 kHz sine switched every second between loud and almost
silent:

| Phase | RTP packets per second |
|-------|------------------------|
| Loud | 48–50 (one per 20 ms frame) |
| Quiet | 18 (the hold time), then 0 |

## Runtime without `level`

The bundled GStreamer is built with `-Dauto_features=disabled`, so every plugin is listed
explicitly. `level` (gst-plugins-good, LGPL like the rest of GStreamer) was added to:

- `Build/linux/meson-options.txt`: `-Dgst-plugins-good:level=enabled`;
- `Build/windows/collect.ps1`: `level` in `$plugins`;
- both `required-elements.txt`: a newly built runtime without it fails verification.

A runtime built before that has no `level`. Then `OutgoingAudioBranch.Build` leaves the gate out and
the microphone is sent ungated, exactly as before. That is why `GstElementCheck` lists `level` as
optional: an old runtime is reported, but the client still starts. **Both runtimes have to be
rebuilt** for the gate to work in a build with the bundled runtime; Debug builds without the bundle
use the installed GStreamer, which normally has `level`.

## Speaking indicator

A green ring lights up on the avatar of whoever speaks, in the sessions list and in the
participants list of the session we are in. Only that session knows who speaks, from the audio
its transport receives and sends.

### What counts as speech

`SpeechActivityMeter` watches the RTP packets of an audio track with a pad probe. A packet counts as
speech by the size of its **payload**, not by the fact that it arrived: opusenc with DTX keeps
sending a packet every 20 ms in silence too, with one to three bytes of payload, while a speech
frame carries 80–140 bytes. Measured on the real branch:

| Input | Packets per second | Packet size (12-byte header included) |
|-------|--------------------|----------------------------------------|
| Digital silence | 50 | 13–15 bytes |
| Speech (a sine) | 50 | 85–137 bytes |

So a payload of 10 bytes or more is speech (`MinSpeechPayloadBytes`). This works for senders with
a voice gate, which send nothing in silence, and without one (gate off, older clients) alike. The
header length varies, since the SFU adds header extensions, so `GetPayloadLength` skips the CSRC
list and the extensions and leaves out the padding.

A track speaks for `HoldTime` (250 ms) after its last speech packet, which covers the jitter between
packets. The sender's gate already holds 300 ms between words, so a sentence lights the ring
without gaps.

The probe only parses the header and stores a timestamp. It runs on a streaming thread of the
pipeline (not a libpulse one), and releases the buffer wrapper at once: the wrapper holds a native
reference of its own, and leaving it to the GC would keep buffers alive.

### Where the meters sit

| Track | Pad | Notes |
|-------|-----|-------|
| Inbound audio | the src pad of webrtcbin, in `IncomingStreamPlayer.Attach` | Before decoding; the user is found by the mline of the section |
| Our outbound audio | the src pad of the capture branch | What really leaves: nothing while muted or gated |

### From the meters to the avatars

```
SpeakingIndicator timer (50 ms) ── reads all meters, maps sections to users
  └─ SpeakingChanged(userId, isSpeaking)            only on a change
       └─ HybridSession ──► IVoiceActivityService.Set(sessionId, userId, isSpeaking)
            └─ Changed(sessionId, userId, isSpeaking)
                 ├─ SessionsListVM ──► HybridSessionVM.UpdateSpeaking ──► PeerVM.IsSpeaking
                 └─ ActiveHybridSessionVM ──► HybridSessionParticipantsListVM.UpdateSpeaking
                                              ──► HybridSessionParticipantVM.IsSpeaking
                                                   └─ UserAvatar.IsSpeaking ──► :speaking ──► ring
```

- An inbound section is matched to its user on every round, not when its pad appears: a section
  changes hands when it is reused by the next track.
- `IVoiceActivityService` (`Client.Application`) is shared by both lists, the way
  `IPeerPlaybackService` is. Its events come from a timer thread; both view models post them to the
  UI thread.
- When the transport disconnects, it reports everyone silent. A replaced or disposed transport is
  ignored by `HybridSession`, which clears the session in the service itself.
- The ring is 2 px thick, around the avatar with a 1 px gap, so the picture keeps its size. It
  reaches 3 px past the bounds of `UserAvatar`; the peer rows clip (`ClipToBounds`, for their
  height animation), so they need at least 3 px of padding around the avatar: the sessions list
  has 4, the participants list 6. `UserAvatar` itself must not clip: a templated control clips to
  its bounds by default in Avalonia 12, which left only the corners of the ring, so it overrides
  `ClipToBounds` to false. It fades in and out in 120 ms.
- A user we muted locally does not light up: we do not hear them, so the ring would point at
  nothing. `HybridSession` keeps who the transport hears speaking (`_speakingUsers`) apart from what
  it publishes, so muting hides the ring at once and unmuting shows it again while they still speak.

### Measured

Real capture branch, gate off, meter read every 50 ms:

| Phase | Polls with speaking |
|-------|---------------------|
| Speech | 20 of 20 |
| Digital silence, first second | 9 of 20 (hold time and the last frames of the encoder) |
| Digital silence, next seconds | 0 of 20 |
| Speech, muted | 4 of 20 (the hold time), then off |

## Future

The transport is planned to move to libwebrtc. It brings the RFC 6464 audio level header extension
(the loudness of every packet, also what mediasoup's active speaker observers use), its own VAD and
noise suppression. The gate logic (threshold, hysteresis, hold time) and its settings carry over,
but the `level` + `valve` implementation will be replaced. The speaking indicator keeps working with
only its source replaced: `IWebRTCTransport.SpeakingChanged` can come from the RFC 6464 levels
instead of `SpeechActivityMeter`, and nothing above the transport changes.
