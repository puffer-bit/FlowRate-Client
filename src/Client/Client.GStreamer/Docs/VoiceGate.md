# Voice gate

The microphone is sent only while the user speaks, like the voice activity mode of most voice
chats. Below a loudness threshold nothing leaves the machine: no audio, no packets. Optionally the
gate also waits for RNNoise to hear a voice, and raises its threshold above the noise of the room.

This has two purposes:

- the noise of the room (fan, keyboard, breathing) is never heard by the others;
- the speaking indicator on the avatars lights up only for speech, not for noise (see
  [Speaking indicator](#speaking-indicator), which also works for senders without a gate).

## Where it lives

| File | Role |
|------|------|
| `WebRTC/OutgoingAudioBranch.cs` | Builds the capture branch, with the `level` element, the pre-roll, the gate valve and the voice detection in it |
| `Audio/MicrophoneProcessing.cs` | The noise suppression in front of the gate: webrtcdsp and RNNoise |
| `Client.Core/Media/VoiceGateDecision.cs` | Decides from the readings whether the gate is open: threshold, hysteresis, hold, voice detection, adaptive threshold. Knows nothing of GStreamer, unit tested in `Client.Application.Tests/VoiceGateDecisionTests.cs` |
| `Audio/VoiceGate.cs` | Feeds the level messages to the decision and moves the valve; the pre-roll and RNNoise constants |
| `Audio/VoiceGateFade.cs` | Ramps the valve open and closed over several buffers, so the voice starts without a click and its tail fades |
| `WebRTC/OutgoingTracks.cs` | Owns the gates and passes the level messages of the pipeline bus to them |
| `Interop/GstBusWatcher.cs` | Takes bus messages on a managed thread of its own |
| `Loader/GstElementCheck.cs` | Reports at startup whether the runtime has `level`, `webrtcdsp` and `audiornnoise` |
| `Client.Core/Settings/AudioSettings.cs`, `VoiceGateOptions.cs` | The settings of the gate and their limits; the record the gate travels as |
| `Client.Infrastructure/Database/Migrations/001_Initial.sql`, `012_VoiceGateOptions.sql` | Hold the settings in the `AudioSettings` table |
| `Client.Avalonia.Desktop/.../AudioControlVM.cs`, `AudioControl.axaml` | The settings tab: switches, threshold slider, meter |
| `Build/linux/meson-options.txt`, `Build/linux/Dockerfile`, `Build/windows/collect.ps1` | Put `level` and `audiornnoise` into the bundled runtime |

## The capture branch

```
capture:   source ─ queue ─ audioconvert ─ audioresample ─ audio/x-raw,rate=48000,channels=1
                  [─ audioconvert ─ F32 ─ audiornnoise ─ audioconvert]   ← neural levels only
                  ─ webrtcdsp ─ webrtcdsp                                ← microphone only, see MicrophoneProcessing
encoding:
       ─ volume (input_volume)
       ─ level (gate_level)          ← measures, posts a message every 10 ms
       ─ tee (gate_split) ──────────────────────────────┐   ← only with RNNoise in the runtime
       ─ queue (gate_delay)          ← the pre-roll     │
       ─ volume (gate)               ← the gate valve   queue (leaky) ─ valve (voice_valve)
       ─ volume (mute_fade)                             ─ audioconvert ─ F32 ─ audiornnoise
       ─ volume (mute)               ← the button       ─ level (voice_level) ─ fakesink
       ─ opusenc ─ rtpopuspay ─ queue ─ webrtcbin
```

- The branch is two bins. `capture` holds everything that depends on the device and on the noise
  suppression level, and `OutgoingAudioBranch.ReplaceCapture` swaps it in a running branch when the
  user picks another microphone or level: the old part is set to NULL, which stops its streaming
  thread, then removed, and the new one is linked in. `encoding` stays, so the gate, the mute state
  and the RTP sequence go on. (`webrtcdsp` reads its settings only when it starts, hence the swap.)
- `level`, the pre-roll, the gate valve and the voice detection are inserted only for a microphone.
  A file or a tone gets the branch without them, the same as before the gate.
- A microphone keeps them even while the gate is turned off in the settings: the valve then stays
  open and `level` posts nothing (`post-messages=false`). This is what lets the gate be turned on
  again during a call without rebuilding the branch.
- `level` sits after the input volume, so the gate hears exactly what would be sent: turning the
  input volume down makes the gate harder to open.
- The audio is made mono at 48 kHz before `volume`, so `level` always measures a single channel.
- The gate valve is a `volume` element that starts muted: the silence it lets out keeps the RTP
  clock running, and webrtcbin and the encoder are negotiated before the user says anything.
- The mute valve is separate from the gate valve. Muting does not depend on the threshold, and the
  gate keeps working while muted (it just has no effect).

## Noise suppression

Settings → Voice and audio → Noise suppression, `AudioSettings.NoiseSuppression`
(`NoiseSuppressionLevel`, stored as its number, so new levels are only appended):

| Level | Number | What runs |
|-------|--------|-----------|
| Off | 0 | webrtcdsp: high-pass filter and gain only |
| Low, Moderate, High, Very high | 1–4 | webrtcdsp with its noise suppression at that level |
| Neural | 5 | RNNoise (`audiornnoise`), then webrtcdsp without its noise suppression (high-pass and gain) |
| Neural + moderate | 6 | RNNoise, then webrtcdsp with moderate noise suppression for the steady noise RNNoise leaves |

- RNNoise is a small neural network that takes out noise that is not steady, keys, clicks and
  knocks, which the webrtcdsp levels let through. It sits in front of webrtcdsp, so the automatic
  gain is set on the voice and not raised along with the noise.
- `audiornnoise` takes only interleaved F32 at 48 kHz (its `allowed_caps` in gst-plugins-rs), so
  an `audioconvert` and a caps filter go in front of it and another `audioconvert` behind it. The
  rate and the channels are already those of the capture caps.
- Its `voice-activity-threshold` stays at 0 here: every frame comes out denoised and none is
  silenced; silencing is the job of the gate.
- A runtime without `audiornnoise` runs both neural levels as Moderate.
  `MicrophoneProcessing.Resolve` decides it, and the transport and the microphone test log a
  warning that the level was replaced.
- The microphone test builds its pipeline from the same fragment, so it follows the level chosen,
  the neural ones included.

## How the gate decides

`level` posts an element message named `level` every `VoiceGate.LevelInterval` (10 ms). Its `peak`
field is an array with one value per channel, in dBFS (0 is full scale, silence goes to −∞). The
gate takes the loudest channel. `VoiceGate` hands every reading with its time to
`VoiceGateDecision`, which decides; the same class drives the meter of the microphone test.

The gate compares **peak** levels, not RMS: the threshold is set against the microphone test meter,
which shows peaks, and both have to mean the same number.

| Value | Default | Meaning |
|-------|---------|---------|
| Threshold (setting) | −32 dB | A closed gate opens at this peak level or above |
| `HysteresisDb` | 6 dB | An open gate counts anything down to threshold − 6 dB as speech |
| `MinHoldTime` | 100 ms | The hold after a burst that opened the gate for a moment |
| `MaxHoldTime` | 400 ms | The hold once the gate has spoken for 600 ms or more |
| `LevelInterval` | 10 ms | How often `level` reports |
| `PreRoll` | 20 ms | How long the audio is held back in front of the valve |

```
closed ──(speech)──► open
open   ──(speech)──► open, hold restarted
open   ──(no speech for hold(spoken))──► closed

speech  = peak ≥ threshold (closed) or ≥ threshold − 6 (open)
          [and, with voice detection, RNNoise heard a voice within 50 ms]
hold    = 100 ms + half the time from opening to the last speech, at most 400 ms
```

- Two thresholds (hysteresis): a word that fades out does not switch the gate on and off around a
  single value.
- The hold covers the pauses between words, so a sentence goes out in one piece instead of being
  chopped into syllables. It grows with how long the gate has been speaking: a knock, a cough or a
  click that opened it is cut off after 100 ms, while a sentence keeps up to 400 ms between its
  words. The gate used to hold a fixed 300 ms, which carried a third of a second of the room after
  every click.
- The threshold is the one of the settings, or the adaptive one above it, see
  [Adaptive threshold](#adaptive-threshold).

### Voice detection

With `IsVoiceDetectionEnabled` the gate opens and holds only while the level is loud enough **and**
RNNoise takes the sound for a voice, so keys, clicks and knocks as loud as the voice stay out.

- The encoding branch splits after `level` (`tee gate_split`) into a branch that is never sent:
  a leaky queue, a valve, `audiornnoise voice-activity-threshold=0.8`, a second `level`
  (`voice_level`) and a `fakesink`. RNNoise fills every 10 ms frame it rates below the threshold
  with zeros, so `voice_level` reads −∞ for anything that is no voice, and a real level otherwise.
- `VoiceGate` gets the messages of both level elements (`OutgoingTracks._gates` holds the gate under
  both handles) and passes those of `voice_level` to `VoiceGateDecision.OnVoice`. Any peak above
  the floor of −100 dB counts as a voice.
- The detector hears a sound later than `level`, and the two message streams are apart, so a loud
  reading and a voice reading count together when they are at most `VoiceWindow` (50 ms) apart: the
  gate opens on whichever comes second.
- Loudness still matters: someone talking in the next room is a voice, but below the threshold.
- The threshold of 0.8 was chosen by measurement, see [Measured](#measured-on-the-real-branch-with-rnnoise).
- RNNoise runs whenever the gate is on, whether voice detection is on or not: it costs about 0.7 %
  of one core (measured: 14 s of audio in 94 ms of CPU), and it tells a voice from a key far better
  once it has heard the room for a while. Turned on in the middle of typing right after the start
  of RNNoise, the gate opened on four keys; with RNNoise already running, on one. The valve in front
  of it drops only while the gate is off (`drop-mode=forward-sticky-events`, so the caps are known
  at once), and `voice_level` posts only while voice detection is on.
- The switch applies to a running call without rebuilding the branch, like the threshold.
- The queue of the detection is leaky (100 ms): a detector that falls behind never holds up the
  voice, which goes through `tee` first. While its valve drops, `tee` fails the allocation query on
  that branch; the elements in front then allocate their buffers themselves.
- A runtime without `audiornnoise` gets the branch without the detection and a pre-roll of 20 ms;
  the gate then goes by the level alone whatever the switch says.

### Pre-roll

The gate learns of a loud interval only once `level` has measured all of it (10 ms) and the message
has reached the bus watcher, a few milliseconds more: the valve used to open 5–30 ms after a word
began, and the first syllable was cut. `queue gate_delay` between `level` and the valve now lets
nothing out until it holds `PreRoll`, so the valve opens before the sound that opened it arrives,
and its 8 ms fade-in runs over the quiet in front of the word.

| Branch | Pre-roll | Why |
|--------|----------|-----|
| Without RNNoise | 20 ms | One level interval plus the way to the bus watcher |
| With RNNoise | 50 ms | 20 ms plus `VoiceDetectionDelay` (30 ms): RNNoise fills 10 ms frames and needs a frame or more of a voice to rate it. Measured, it heard most onsets 0–10 ms after `level`, a soft "h" 60–70 ms after |

- The pre-roll is fixed when the branch is built, by whether the runtime has RNNoise, not by the
  switch: changed while the voice flows it would leave a hole or a burst in it.
- `queue` adds its `min-threshold-time` to the latency it reports (`gstqueue.c`, "adjust for
  min-threshold"); measured on the branch, the latency query below the valve answers 20 or 50 ms.
- At the end of the stream the queue clears its thresholds and lets out all it holds; a flush
  empties it and restores them. Serialized events and queries pass it without waiting for the
  threshold.
- Its upper limit (200 ms) only guards against a stalled valve: the queue behind the encoder is
  leaky, so the branch never backs up.
- The voice reaches the others 20 or 50 ms later for it.

### Adaptive threshold

With `IsAdaptiveThresholdEnabled` the gate follows the noise of the room and opens only clearly
above it:

```
effective threshold = max(threshold, min(floor + 12 dB, threshold + 15 dB, 0 dB))
```

| Value | | Meaning |
|-------|---|---------|
| `NoiseMarginDb` | 12 dB | How far above the floor the gate opens. Peaks of a steady noise spread over about 6 dB, and an open gate closes 6 dB lower still |
| `MaxAdaptiveRiseDb` | 15 dB | The cap above the threshold of the user: a long loud noise cannot shut the gate for good |
| Floor fall | 18 dB/s | A room that gets quieter is followed down within a second |
| Floor rise, closed gate | 2 dB/s | The follower settles near the quiet end of the noise (below it a tenth of the time); a word below the threshold moves it by less than a decibel |
| Floor rise, open gate | 1 dB/s | See below |

- The floor starts at threshold − 12 dB at most, so a first reading that happens to be loud raises
  nothing.
- The floor is measured mainly while the gate is closed. While it is open the follower may still
  fall, and rise at half the speed: a fan loud enough to open the gate before it was learned would
  otherwise hold the gate open through the hysteresis for good. On the real branch a fan of −38 dB
  peaks under a threshold of −40 dB held a plain gate open for the whole 26 s; the adaptive one
  closed after 10 s and opened again on speech over the fan. Speech pauses more than a nineteenth
  of the time, so an open gate still keeps the floor with the room (unit tested over a minute of
  talking).
- With voice detection a noise never opens the gate, and the closed gate learns it directly.
- Turning the adaptive threshold on starts the floor anew; it is not stored.

## Logged

Every minute `OutgoingTracks.ReportGates` logs, per gate: the threshold, the threshold it opens from
now and the noise floor (null without the adaptive threshold), how much of the time it was open
and how often it opened, the loudest peak and the average peak while open, and how much of the time
RNNoise heard a voice (null without voice detection).


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
- The options are changed from the thread that saved the settings, while `Update` runs on the
  watcher. `VoiceGate` runs both under one lock; without it, turning the gate off could open the
  valve just before a level message that was already in flight closes it again.
- `VoiceGateDecision` takes the time of each reading from the caller (`VoiceGate` uses a
  `Stopwatch`), so it runs the same in the transport, in the microphone test and in the unit tests.

## What is gated

Only a microphone (`DeviceAudioInput`): `WebRTCTransport.StartOutbound(outbound, input, volume,
voiceGate, noiseSuppression)` builds the gate for it and leaves it out for any other input. A
looped file or a tone (the test bot) is meant to be heard as it is, and the gate would cut the quiet
parts of music.

The gate travels as one `VoiceGateOptions(ThresholdDb, IsVoiceDetectionEnabled,
IsAdaptiveThresholdEnabled)`, null meaning "gate off, send all the time": `ActiveHybridSessionVM` →
`IHybridSession.StartVoiceShareAsync(deviceId, inputVolume, voiceGate, noiseSuppression)` →
`IWebRTCTransport.StartOutbound` → `OutgoingTracks.Start` → `VoiceGate`. The view model passes
`AudioSettings.ActiveVoiceGate`, which is null while the switch in the settings is off.

## Settings

Settings → Voice and audio:

- **Voice activity: "Send the microphone only while I speak"**: `AudioSettings.IsVoiceGateEnabled`,
  on by default. Off sends the microphone all the time, as before the gate.
- **"Open only for a voice"**: `AudioSettings.IsVoiceDetectionEnabled`, off by default, see
  [Voice detection](#voice-detection).
- **"Raise the threshold above the room noise"**: `AudioSettings.IsAdaptiveThresholdEnabled`, off by
  default, see [Adaptive threshold](#adaptive-threshold).
- Both switches are greyed out while the gate is off.
- **Threshold slider** under the microphone test meter: `AudioSettings.VoiceGateThresholdDb`, from
  −60 to 0 dB, default −32 dB. `AudioService` clamps it to that range on save.

The slider shares the column and the scale of the meter: `GstMicrophoneTester` maps peaks from
`AudioSettings.MinVoiceGateThresholdDb` (−60 dB) to 0 dB onto the bar, and the slider covers the same
range, so the thumb sits where the bar has to reach. While the test runs, the bar turns green
(`IsVoiceDetected`) when the gate would send: `AudioControlVM` feeds the peaks of the test to a
`VoiceGateDecision` of its own, so the hysteresis, the growing hold and the adaptive threshold
show as in a call. Voice detection is not simulated there: the test pipeline has no RNNoise
branch, and the meter shows the loudness the threshold is set against.

The gate settings are stored in the `AudioSettings` table: the switch and the threshold since
`001_Initial.sql`, voice detection and the adaptive threshold since `012_VoiceGateOptions.sql`.

### Applied during a call

The threshold, the switches and the input volume apply to a running microphone at once:

```
AudioControlVM ──save (300 ms after the slider stops)──► AudioService.SaveSettingsAsync
  └─ AudioService.SettingsChanged(AudioSettings)
       └─ ActiveHybridSessionVM.OnAudioSettingsChanged
            ├─ IHybridSession.SetInputVolume ──► IWebRTCTransport.SetOutboundVolume ──► volume (input_volume)
            └─ IHybridSession.SetVoiceGate ──► IWebRTCTransport.SetVoiceGate
                                              ──► VoiceGate.SetOptions
```

- `SetOptions(null)` opens the valve at once, silences both level elements and stops RNNoise.
- New options are applied by the next level message, within 10 ms. An open gate first waits out
  its hold time, so raising the threshold above the voice does not cut a word in half.
- Without a running microphone nothing happens; the next start reads the settings anyway.
- The input device is not switched during a call; it is taken on the next start.

Measured on the real branch with a constant sine (peak −10.5 dB), one second per step, before the
hold grew with the speech and before the pre-roll (the hold was a fixed 300 ms then):

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

Without the pre-roll the gate opened 5–30 ms after a word began, and that much of it was lost. The
pre-roll holds the voice back 20 ms (50 ms with RNNoise in the runtime), so the valve is open in
time for most onsets; the latest ones lose a few milliseconds instead of all of them. With voice
detection a soft onset that RNNoise hears late can still lose some of its start, about 35 ms on the
synthetic "h" measured below.

After the last word, 100–400 ms of quiet audio are still sent (the hold), then the release ramp.

## Fading at the edges

A valve switched at once makes the sound jump from silence to the middle of a wave, and the
listeners hear a click at the start of every sentence; a tail cut off short sounds like the gate
chopping the end of every word. `VoiceGateFade` ramps the valve instead, with a smoothstep:

- Opening unmutes the valve at once and adds a probe on its src pad. The probe skips the buffers
  the valve still marked as a gap (muted before it opened) and ramps up over 8 ms.
- Closing ramps down over 200 ms in the same kind of probe and mutes the valve right after the
  buffer that reached silence, on the streaming thread. The release is longer than the shortest
  hold, so after a short burst the fade does most of the closing, and a word that only just ends
  there decays instead of stopping.
- The ramp goes on from where it is when the gate turns around midway.
- The probe is gone once the ramp has finished, so managed code touches only the buffers at the
  edges of a sentence.
- Only S16LE and F32LE buffers that are writable are faded. Anything else is switched at once, the
  way it was before.
- Turning the gate off opens the valve at once; a probe still waiting then finds nothing to do.

Measured with a sine of half the full scale, when the fade took one buffer: the largest step
between two samples at the edges was 15847 switched at once, and 943 faded, which is the slope of
the sine itself.

Measured on the real branch with a 48 kHz sine switched every second between loud and almost
silent:

| Phase | RTP packets per second |
|-------|------------------------|
| Loud | 48–50 (one per 20 ms frame) |
| Quiet | 18 (the hold time), then 0 |

## Measured on the real branch with RNNoise

No GStreamer runtime of the project and no microphone were at hand, so the branch was checked with
GStreamer 1.24 of Ubuntu 24.04 and `libgstrsaudiofx.so` built from the `gstreamer-1.28.7` tag of
gst-plugins-rs. A harness built the branch with `OutgoingAudioBranch.Build`, ran the real
`VoiceGate` on its bus messages, and fed it a file in real time (`clocksync`): 1 s of quiet, a
synthetic sentence (espeak-ng), 1 s of quiet, 3 s of typing (21 clicks of 5 ms), 1 s of quiet, a
second sentence. These are synthetic sounds; real voices and keyboards have to confirm the numbers.

RNNoise alone, share of the 10 ms intervals it let through:

| `voice-activity-threshold` | Speech | Typing |
|----------------------------|--------|--------|
| 0.5 | 75 % | 6 % |
| 0.6 | 73 % | 4 % |
| 0.7 | 72 % | 2 % |
| 0.8 | 70 % | 0.3 % |
| 0.9 | 68 % | 0 % |

The speech that does not pass lies between syllables and words, which the hold covers.

The gate at −40 dB:

| Voice detection | Openings in the typing | Opening on the sentences |
|-----------------|------------------------|--------------------------|
| Off | 22 (every key) | Before both reached the valve |
| On | 1 | The second as without; the first, which starts with a soft "h", 85 ms later than without, so about 35 ms of it fell behind the pre-roll |
| Turned on in the call before the typing | 1 | As with it on from the start |

The latency query below the valve answered 50 ms with RNNoise and 20 ms without, and the stream
reached its end through the pre-roll.

## Runtime without `level` or `audiornnoise`

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

`audiornnoise` comes from gst-plugins-rs (the audiofx crate, plugin `rsaudiofx`, MPL-2.0; RNNoise
in pure Rust, nnnoiseless, so it needs no library beyond GStreamer):

- `Build/linux/Dockerfile`: `-p gst-plugin-audiofx` next to dav1d and rtp; `collect.sh` copies
  `libgstrsaudiofx.so` with the other plugins;
- `Build/windows/collect.ps1`: `rsaudiofx` in `$plugins`, from the official installer;
- both `required-elements.txt` and `GstElementCheck`: optional (`?audiornnoise`).

Without it the neural noise suppression runs as Moderate and the gate has no voice detection; the
client starts and logs it.

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
packets. The sender's gate already holds 100–400 ms between words, so a sentence lights the ring
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
but the `level` + `valve` implementation will be replaced. `VoiceGateDecision` does not depend on
GStreamer and carries over as it is. The speaking indicator keeps working with
only its source replaced: `IWebRTCTransport.SpeakingChanged` can come from the RFC 6464 levels
instead of `SpeechActivityMeter`, and nothing above the transport changes.
