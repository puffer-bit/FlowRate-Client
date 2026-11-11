# RNNoise keyboard noise investigation

Session date: 2026-11-11. Working branch at handoff: `0.7.1/Audio-fixes`.
HEAD at the initial handoff: `dceb2280`. The branch was initially named `0.7.1/RNoise-fixes`.

## Resumed investigation: user clarification and non-neural processing

The user clarified on 2026-11-11 that both neural modes suppress keyboard sounds acceptably.
The audible neural processing character is normal to them, not a reported voice-quality defect.
The requested focus is now the **mode without neural noise suppression**. The user subsequently
confirmed **Moderate, automatic gain enabled, and speech detection enabled**. The manual gate
threshold, input volume and adaptive-threshold setting have not been confirmed. The earlier observations
below are historical; do not assume that the current complaint is poor standalone RNNoise
suppression, damaged speech, or digital crackling.

Code inspection confirms:

- Non-neural modes use `webrtcdsp`, with high-pass filtering, optional echo cancellation,
  mode-dependent noise suppression and optional automatic gain. Enabling automatic gain adds a
  second `webrtcdsp` gain stage. Input volume and the gate follow both stages.
- Even Off retains the high-pass filter and, if enabled, automatic gain. Off means noise
  suppression is disabled, not that the microphone signal bypasses all processing.
- The separate RNNoise detector is built when available and runs while the gate is enabled,
  including in non-neural suppression modes. It only influences gate decisions when
  `IsVoiceDetectionEnabled` is true; its processed audio is never transmitted.
- Defaults in `AudioSettings` are Moderate, automatic gain enabled, gate enabled at -32 dBFS,
  speech detection disabled, and adaptive threshold disabled. These defaults do not establish
  the user's saved settings.

### Identical transient input through the real processing fragment

A temporary harness at `/tmp/flowrate-gate-output-check/` was adapted to call the production
`MicrophoneProcessing.Fragment` with the bundled Linux runtime. It passed the exact same WAV
through Off, Moderate and High, each with automatic gain enabled and disabled. No microphone,
speakers, gate, echo probe, manual input gain or RNNoise processing was involved.

The WAV contains twelve repetitions of an identical one-second waveform at 48 kHz mono S16:
seeded low-level noise plus a 50 ms decaying broadband transient beginning at 500 ms.
The random generator seed is 123, background range is +/-0.0001, transient range is +/-0.012
times `exp(-sample / 350)`. The same full one-second waveform, including the background, is
repeated byte-for-byte. This is a controlled transient, not a keyboard recording or speech.

| Mode | First-second output peak, gain off | First-second output peak, gain on | Last-second output peak, gain off | Last-second output peak, gain on |
|---|---:|---:|---:|---:|
| Off | -39.3 dBFS | -23.9 dBFS | -39.3 dBFS | -23.9 dBFS |
| Moderate | -42.9 dBFS | -27.5 dBFS | -41.5 dBFS | -26.4 dBFS |
| High | -43.3 dBFS | -28.0 dBFS | -41.9 dBFS | -26.8 dBFS |

All six pipelines completed without GStreamer errors and produced exactly 576,000 S16 samples
(12 seconds). The harness built with zero warnings and errors. Results are stored in
`/tmp/flowrate-gate-output-check/comparison.json`; temporary artifacts may not survive cleanup.
The old gate experiment source was preserved as `GateProgram.cs.saved` before adaptation.

This confirms about 15 dB of amplification of this residual transient. With an input volume of
1, a fixed -32 dBFS gate and speech detection disabled, these output peaks fall below the
opening threshold with gain off and above it with gain on. That threshold comparison is an
inference from the measured PCM and gate code; this experiment did not exercise live gate
timing. Identical repeated input also changes slightly over time under WebRTC suppression,
consistent with stateful processing; it does not establish the mechanism of that variation.

This is evidence for investigating gain and gate settings in the ordinary modes. It does not
prove the cause on the user's microphone, establish speech quality, or justify removing gain:
quiet speech needs a separate comparison. No additional production code changes were made.

### Full gate check after confirmation that speech detection is enabled

The same twelve-second input was then processed through the real `MicrophoneChain.BuildMonitor`
and `VoiceGate`, with Moderate, input volume 1, a -32 dBFS gate, speech detection enabled,
adaptive threshold disabled and no echo probe. Both automatic-gain settings were tested. Input
was paced with `identity sync=true`; each pipeline completed in approximately 11,992 ms.

- Gain off: loudest pre-gate peak -41.49 dBFS, zero voice detections, zero gate openings.
- Gain on: loudest pre-gate peak -26.40 dBFS, zero voice detections, zero gate openings.
- Both output files contained exactly 576,000 samples, all zero.

Thus the detector blocked this synthetic transient despite gain pushing it above the level
threshold. Automatic gain alone is **not a sufficient explanation** of an isolated sound opening
the user's gate with speech detection enabled. No false opening was reproduced with this input.
The -32 dBFS threshold and other unconfirmed settings above are experimental settings, not a
claim about the user's saved configuration.

Next distinguish isolated keyboard sounds that open a closed gate from sounds heard while the
gate is open for speech or during its hold/fade. In the first case, detector classification and
message timing remain candidates; in the second, the detector does not denoise transmitted
audio, so residual noise from Moderate can accompany speech. A real recording is needed to
evaluate the user's keyboard and speech; the synthetic check does not establish either.

Full-chain source is currently `Program.cs` in the temporary harness directory. The fragment-only
comparison source is preserved as `FragmentProgram.cs.saved`; the result log is
`gated-comparison.log`. No additional production changes were made.

### Isolated transient false opening reproduced after further clarification

The user confirmed that the keyboard is heard **on its own after silence**, not only while
speaking or during the closing tail. This focuses the investigation on false opening or a
missing/inactive detector, rather than ordinary residual noise while speech keeps the gate open.

Thirty additional synthetic impacts (26 seconds; varying level, duration and mechanical ringing)
were blocked by the full Moderate + gain + speech-detection chain: no voice frames, no openings,
and 1,248,000 zero output samples. This narrower negative result was followed by a wider scan.

A 123-second synthetic recording contains 240 impacts spaced 500 ms apart, starting after two
seconds of background noise. Seed 347; background uniform +/-0.0003; impact amplitude is
`10**uniform(-2.7, -0.3)`, decay `uniform(0.001, 0.018)` seconds, and duration 90 ms. Impacts mix
broadband noise with two inharmonic damped resonances (`f1` 180–3500 Hz, `f2` 1500–9000 Hz).
This is a synthetic mechanical-impact stress test, **not a real keyboard recording**.

The serial offline detector scan uses the real production Moderate processing fragment followed
by conversion to NeuralCaps and `audiornnoise voice-activity-threshold=0.8`. It omits the leaky
detector queue and live gate timing to preserve every sample while scanning. All runs produce
5,904,000 float samples. Counting 480-sample frames with peak amplitude above 0.00001 (the gate's
-100 dBFS floor) gives:

| Automatic gain stages | Frames classified as voice in the 123-second scan |
|---|---:|
| None | 0 |
| First stage only (experimental fragment with the trailing gain stage removed) | 20 |
| Both production stages | 457 |

In the first twelve seconds, either gain-enabled variant produces exactly one voice frame at
audio time 9.00 seconds. Therefore removing only the second stage does **not** eliminate this
particular false detection, although it sharply reduces total detections in this synthetic scan.
This is not a general false-positive-rate benchmark or an evaluation of speech recognition.

The first twelve seconds were then replayed in real time through the real `MicrophoneChain`
and `VoiceGate`, Moderate, input volume 1, gate -32 dBFS, speech detection on, adaptive threshold
off, no echo probe, with the production zero/two gain-stage options:

- Gain off: no voice frames, no gate openings, 576,000 zero output samples.
- Gain on: one detector reading at about 9003 ms (detector output peak -57.71 dBFS), gate opened
  at about 9005 ms and decided to close at 9174 ms. One opening and one voice frame were reported.
- Gain-on output had 12,897 nonzero S16 samples, first at audio time 9001.42 ms and last at
  9314.27 ms. Output peak reached 32,767. Sample times and wall-clock decision times use different
  timelines; do not subtract them to estimate onset delay.
- Both runs completed in about twelve seconds, with 576,000 output samples each. Builds completed
  without warnings/errors and no GStreamer error was reported.

This reproduces a **real false gate opening on isolated synthetic non-speech**, not merely a
UI/audio mismatch. The mechanism is a gain-dependent detector false positive plus the decision
accepting one positive frame within its 50 ms loudness window. The subsequent hold and fade let
more than that single frame through. It is a plausible explanation of the user's observation,
not proof that their keyboard has the same acoustic or level characteristics.

Potential changes require speech comparisons: moving the detector before automatic gain may
avoid gain-induced false detections while keeping transmitted speech gain, but quiet speech may
be detected less reliably. Requiring several voice frames could reject the isolated frame but
may delay or clip soft word onsets, and the wider scan includes longer false runs. Removing gain
indiscriminately is not justified. No additional production behavior was changed.

Additional diagnostic limitation: `VoiceTransport.LogGateSummary` in the normal libwebrtc path
does not log the already-available `VoicePercent`; the older GStreamer transport does. Existing
normal voice-gate summary logs alone cannot distinguish detector positives from a level-only
fallback. This observation was not changed in production code.

Temporary artifacts in `/tmp/flowrate-gate-output-check/`: `scan-clicks.wav`, `scan-events.json`,
`scan-prefix.wav`, `scan-results.json`, `stage-results.json`, `prefix-comparison.log`, and
`prefix-gated-{False,True}.pcm`. Full real-time reproduction source: `PrefixProgram.cs.saved`;
30-impact check: `VariedProgram.cs.saved`; serial scan: `ScanProgram.cs.saved`; current
`Program.cs` compares zero/one/two gain stages. Preserve these if continuing locally; they may
not survive temporary-directory cleanup.

## Historical observations at the initial handoff

- Keyboard presses of apparently equal loudness are sometimes suppressed and sometimes audible.
- Preserving RNNoise state during microphone owner handoffs did not eliminate all audible clicks.
- The user observed the chat speaking indicator activating while the microphone test said sound
  was blocked, and confirmed hearing clicks in the test despite that label.
- The user then compared modes and reported that **Neural + moderate works perfectly**. The
  problematic comparison is standalone Neural (RNNoise).
- Current practical workaround: keep **Neural + moderate** selected. The extra WebRTC suppression
  appears to remove noise left by RNNoise in this user's setup. This is a listening observation,
  not a quantitative comparison of identical recorded input.
- Do not treat all remaining clicks as a confirmed gate bug. The mode comparison shifts attention
  to residual noise in standalone RNNoise and amplification after denoising.
- The exact state of the separate voice-detection toggle was not established. Its Russian label
  is `Не реагировать на посторонние шумы`. Selecting Neural does not automatically enable it.

## Scope and working tree

The initial request authorized investigation without edits. The user subsequently created a
branch and authorized starting with the first candidate: unnecessary capture rebuilds.
Only that production fix has been implemented. Later observations were investigated without
additional production changes. The user requested saving the findings and continuing later.

Implementation prepared at the initial handoff:

1. `src/Client/Client.GStreamer/Audio/GstVoiceAudioDevice.cs`
   - Tracks the configuration of the actual capture.
   - Adds `ConfigureCapture`, used for activation, predecessor restoration and rollback.
   - Reuses capture when input, suppression, echo cancellation and automatic gain are unchanged.
   - Applies volume and gate options in place, preserving both RNNoise instances.
   - Rebuilds capture for changes to those processing parameters.
   - Clears tracked settings when capture stops.
   - Exposes an internal capture generation counter for integration checks.
2. `src/Client/Client.Audio.SmokeChecks/Program.cs`
   - Checks volume-only owner handoffs without rebuilding and actual shared-output muting.
   - Checks restoration of an inactive predecessor's updated gate and volume.
   - Checks preservation of capture in both neural modes during volume/gate handoffs and restores
     voice detection on return to the previous owner.
   - Checks necessary rebuilds for input, echo and automatic gain changes.
   - Checks native-peer gate restoration without rebuilding capture.
3. `src/Client/Client.LibWebRTC/README.md`
   - Documents capture reuse and the settings that still require rebuilding.

No commit or push had been made at that handoff. The implementation and this investigation note
are now included together for review.

## Verified architecture and suspicious paths

### Two independent RNNoise instances

`Audio/MicrophoneProcessing.cs` in `Client.GStreamer` puts RNNoise before WebRTC processing in
the main audio path. Its voice-activity threshold is left at the plugin default of zero:
frames are denoised, but low speech probability does not itself mute the transmitted audio.
Neural disables WebRTC noise suppression; NeuralAndWebRtc enables moderate suppression after
RNNoise. Both modes can run automatic gain afterwards.

`Audio/MicrophoneChain.cs` also builds a separate voice-detection branch after processing and
input volume. It has another RNNoise instance with a voice-activity threshold of `0.8`.
This branch detects speech for the gate; its audio is not transmitted.

### Capture rebuilds and warm-up: implemented fix

Previously all owner settings were compared, including volume and gate. A difference caused a
complete capture rebuild and reset both RNNoise instances, including learned room context.
Returning to a predecessor could also rebuild unnecessarily. The fix described above avoids
those resets for live-adjustable settings. Actual processing changes and releasing the final
owner still stop/recreate capture as necessary; this does not keep the microphone recording
after the final owner leaves.

Existing `Client.GStreamer/Docs/VoiceGate.md` reports a synthetic experiment with four typing
activations when detection was cold versus one when warmed up. This is historical project
evidence, not a measurement on the user's keyboard.

Relevant history: `22df6f4c` introduced RNNoise-based gate detection; `613e5073` (2026-11-06)
introduced the current GStreamer capture bridge shared by native peers.

### Gate timing and false detections: not changed

`Client.Core/Media/Audio/VoiceGateDecision.cs` accepts one non-silent detector reading as voice.
With voice detection enabled, recent loudness and voice readings within a `50 ms` window can
open the gate. There is no requirement for sustained voice over multiple consecutive frames.
A single wrongly classified frame can therefore open the gate; subsequent keyboard residuals
can pass while it remains open. Hold time grows from `100 ms` to `400 ms`.

`Client.GStreamer/Audio/VoiceGateFade.cs` then takes another `200 ms` to fade closed. The
microphone-test label reflects the gate decision, rather than completion of that fade.

The chat speaking indicator is derived from peak PCM after the gate, with a separate `400 ms`
speaking hold (`Client.LibWebRTC/Voice/VoiceTransport.cs`). It is not a direct gate-state display.

### Test indicator versus audible output: independently reproduced

`Client.GStreamer/Audio/GstMicrophoneTester.cs` intentionally delays loopback playback by
one second. Gate events and the label update immediately. A sound heard while the label says
blocked can therefore have passed earlier, or belong to the closing fade. This explains a
possible display/audio mismatch, but does not establish the cause of every keyboard leak.

A temporary harness exercised the real MicrophoneChain, VoiceGate and output PCM using the
bundled Linux runtime, without microphone hardware or speakers. It used suppression Off and
automatic gain disabled to isolate gate behavior, with a 440 Hz tone at volume `0.001`, raised
to `0.25` for 300 ms after 600 ms, then returned to `0.001`.

Observed output:

- Gate opened at about `643 ms` and decided to close at `1197 ms`.
- **19 output frames contained nonzero audio while the decision was already closed**, with
  decreasing peaks through about `1389 ms`.
- After settling (past `1800 ms`), no frames contained nonzero audio.

This confirms the closing-fade discrepancy, not RNNoise classification accuracy. The temporary
harness is at `/tmp/flowrate-gate-output-check/` and may not survive cleanup. An initial file-input
attempt failed because this bundled runtime did not provide `multifilesrc`; the successful
measurement used `audiotestsrc` instead. No repository harness was added for this experiment.

### Other candidates: not proven or changed

- `MicrophoneProcessing.cs` applies two WebRTC automatic-gain stages when enabled. These may
  make residual clicks more audible after RNNoise. Their contribution has not been measured.
- `VoiceGate.Update` timestamps readings with `_clock.Elapsed` when bus messages are handled,
  rather than the audio interval's timestamp. Separate branches and message backlogs can distort
  the 50 ms coincidence window. No load-dependent reproduction has been obtained.
- Missing `audiornnoise` silently resolves neural modes to Moderate at the processing helper;
  voice detection is absent too. Warning coverage depends on the caller. Historical commit
  `0f1bae93` addressed reporting this condition. Not demonstrated in the user's current build.

## Validation already completed

- Smoke-check project build passed. A full build emitted existing LibWebRTC warnings; the
  subsequent incremental build had zero warnings/errors.
- Audio smoke checks passed with the new ownership scenarios and bundled Linux GStreamer/native
  libraries. These prove capture lifetime and settings behavior, not real keyboard denoising.
- All **635 Client.Application.Tests passed**, with no failures or skipped tests.
- `git diff --check` passed for the implementation changes.
- VSTest initially could not create its local communication socket inside the sandbox. Re-running
  with approved escalation succeeded. This was an environment restriction, not a test failure.
- `dotnet run` with an implicit parallel build returned an unhelpful build failure. Building
  explicitly with `-m:1` and running with `--no-build --no-restore` succeeded.

Commands (from the client repository):

```bash
dotnet build src/Client/Client.Audio.SmokeChecks --no-restore --verbosity quiet -m:1
dotnet run --project src/Client/Client.Audio.SmokeChecks --no-build --no-restore
dotnet test src/Client/Client.Application.Tests --no-restore --verbosity quiet -m:1
git diff --check
```

Temporary logs: `/tmp/flowrate-rnnoise-ownership-smoke.log` and
`/tmp/flowrate-rnnoise-application-tests.log`. They may not survive cleanup.
No Windows or physical microphone/keyboard checks were performed by the agent.

## Suggested next investigation

1. Preserve the user-confirmed working Neural + moderate configuration as the comparison.
2. Establish the separate voice-detection and automatic-gain settings before interpreting the
   gate's behavior. The initial report did not establish whether remaining clicks were during
   speech, immediately afterwards, or isolated after several seconds of silence.
3. Compare identical raw audio through Neural and NeuralAndWebRtc, with automatic gain both on
   and off. Measure input, post-denoise, post-gain and post-gate levels. A captured keyboard/speech
   sample would be stronger evidence than synthetic clicks.
4. Distinguish residual denoising noise from voice-detector false positives and UI timing.
   Do not shorten the speech hold, raise detection thresholds or remove gain indiscriminately:
   effects on quiet speech and word onsets have not been tested.
5. If indicator accuracy is addressed, account for both the physical fade and the intentional
   one-second test playback delay. Independent test and call captures also have separate states.

There is no confirmed complete fix for standalone RNNoise yet. The implemented lifetime fix is
validated; the user-confirmed effective workaround is Neural + moderate.
