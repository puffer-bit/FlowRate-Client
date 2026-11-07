# Voice audio latency

The desktop libwebrtc transport uses GStreamer for both microphone processing and hardware
playback. The RNNoise integration changed both paths, so a regression introduced with that
integration is not necessarily a delay inside the denoiser.

## Buffer budgets

- Hardware capture and playback request a 60 ms buffer with a 10 ms period. Windows uses
  `wasapi2src` and `wasapi2sink` with `low-latency=false`: the minimum hardware period
  selected by `low-latency=true` can be shorter than the requested 10 ms and leaves too
  little scheduling margin for the managed bridge. Hardware and sound servers can
  negotiate different actual sizes. In GStreamer 1.28.7, WASAPI2 allocates an internal
  500 ms ringbuffer for device switching, independently of the requested buffer time;
  the request does not bound how far ahead a producer can fill that ringbuffer.
- The capture queue drops the oldest sound after 100 ms instead of blocking hardware capture.
  This is an upper bound during a stall, not a minimum amount held in normal operation.
- Microphone DSP receives 10 ms buffers. RNNoise and webrtcdsp can still add their own
  processing delay.
- The gate holds no audio while disabled, 20 ms when using the level alone, and 50 ms when
  voice detection is enabled. Its queue is bounded to 80 ms and drops the oldest audio.
- Processed microphone output holds at most two 10 ms buffers. Capture age is measured
  but does not cause additional frame drops: the custom native source cannot communicate
  skipped capture time to the RTP encoder.
- Native playout appsrc holds at most two 10 ms buffers. A `clocksync` immediately after
  appsrc paces buffers against the pipeline clock, without an independent managed timer.
- A non-leaky queue after clocksync provides a 20 ms scheduling cushion, bounded to 60 ms.
  It supplies the output processing deadline without letting the producer run ahead of
  clocksync or dropping PCM in normal playback.

These are local buffer limits, not a measured end-to-end latency guarantee. Codec framing,
the receiver's jitter buffer, the network and device processing also contribute.

## Recovery and synchronization

`need-data` reports available appsrc queue space, not a 10 ms hardware tick. Without
explicit pacing, an audio sink can repeatedly request PCM ahead of real time and fill
its internal ringbuffer. The appsrc queue limit does not limit that downstream buffer.
The bridge's cumulative PTS then keeps scheduling subsequent decoded sound ahead of the
clock, creating latency outside NetEq's reported jitter buffer.

`clocksync sync=true sync-to-first=false` now waits for each buffer's pipeline running
time before forwarding it. It runs on the appsrc streaming thread, so the next
`need-data` callback cannot pull another frame while that wait is in progress. There
must be no queue between appsrc and clocksync. With 10 ms buffers, the bridge can pull
one frame ahead while clocksync waits, rather than fill the hardware ringbuffer ahead.

When output is over 50 ms late, its next PTS is moved to the current running time. Each
output callback pulls one frame from NetEq; it never bulk-pulls and discards frames to
catch up. Such bursts disturb NetEq's real-time cadence and skip samples in every received
voice. NetEq retains responsibility for adapting its network jitter buffer.

The scheduling queue is deliberately after clocksync. Without a scheduling cushion,
the combination of clock pacing and WASAPI's minimum hardware period can underrun and
produce crackles. The affected release logged repeated "Not enough buffering available
for the processing deadline" warnings. Merely suppressing that warning or setting the
deadline to zero would not supply the missing audio headroom.

Latency bus messages are handled explicitly because the client has no GLib main loop.
This keeps playback scheduling and the echo probe updated when capture or gate latency changes.
Changing the gate's delay can discard a short portion of queued audio when it is disabled,
or introduce a short pause when it is enabled; it does not retain the old delay indefinitely.

## Diagnostics

`GstVoiceAudioDevice` logs the configured pipeline output latency at Debug level and reports
late output timestamp resynchronization at Warning level.
Every five seconds it also logs the playout frame rate and current/maximum PTS lead
relative to the pipeline clock. Normal pacing is approximately 100 frames per second
with at most one 10 ms frame of lead. The existing smoke checks use a clocked fakesink;
their frame-rate assertions alone cannot establish real hardware output latency.
`GstProcessedMicrophone` logs current and maximum PCM age and the number of frames older
than 150 ms every five seconds at Debug level. Existing libwebrtc media performance statistics
report the receiver's actual and target jitter-buffer delay separately.

The settings microphone loopback intentionally delays playback by one second. That path is
not used for a conversation and should not be used to measure conversation latency.
