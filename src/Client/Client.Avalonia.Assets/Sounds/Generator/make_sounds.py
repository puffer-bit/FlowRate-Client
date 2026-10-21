#!/usr/bin/env python3
"""Generates a draft set of UI sounds (soft FM mallet + short room reverb).

Usage: make_sounds.py <output-dir>
Everything worth tweaking is in the "Sound set" and "Voice" sections.
"""
import sys
import wave
from pathlib import Path

import numpy as np

SR = 48000

# ---------------------------------------------------------------- Voice ----
ATTACK = 0.014        # seconds; longer = softer onset
DECAY = 0.21          # seconds; base decay time of a note at 440 Hz
FM_RATIO = 2.0        # modulator/carrier ratio: 2 = warm, 3 = marimba, 3.5+ = bell
FM_INDEX = 0.45       # brightness of the strike
FM_INDEX_DECAY = 0.07
REVERB_TIME = 0.22    # seconds; decay constant of the room tail
REVERB_WET = 0.20
TRANSPOSE = -12       # semitones; shifts the whole set
SLOWDOWN = 1.3        # stretches the gaps between notes and the lengths
TARGET_RMS_DB = -23.0
PEAK_LIMIT_DB = -3.0

# D major pentatonic, so every sound belongs to the same family.
N = {
    "A4": 440.00, "B4": 493.88,
    "D5": 587.33, "E5": 659.25, "F#5": 739.99, "A5": 880.00, "B5": 987.77,
    "D6": 1174.66, "E6": 1318.51, "F#6": 1479.98, "A6": 1760.00,
    "Bb4": 466.16,
}


def note(freq, dur=0.5, vel=1.0, bright=1.0):
    freq *= 2 ** (TRANSPOSE / 12)
    t = np.arange(int(SR * dur)) / SR
    tau = DECAY * (440.0 / freq) ** 0.35
    env = np.exp(-t / tau) * np.minimum(t / ATTACK, 1.0)
    index = FM_INDEX * bright * np.exp(-t / FM_INDEX_DECAY)
    body = np.sin(2 * np.pi * freq * t + index * np.sin(2 * np.pi * freq * FM_RATIO * t))
    octave = 0.05 * np.sin(2 * np.pi * 2 * freq * t) * np.exp(-t / (tau * 0.5))
    return vel * env * (body + octave)


def sequence(events, length):
    """events: (start_seconds, note_name, velocity, brightness)"""
    length *= SLOWDOWN
    out = np.zeros(int(SR * length))
    for start, name, vel, bright in events:
        start *= SLOWDOWN
        n = note(N[name], dur=length - start, vel=vel, bright=bright)
        i = int(SR * start)
        out[i:i + len(n)] += n[:len(out) - i]
    return out


def reverb(mono):
    rng = np.random.default_rng(7)
    n = int(SR * REVERB_TIME * 6)
    t = np.arange(n) / SR
    smooth = np.hanning(48)
    smooth /= smooth.sum()
    size = len(mono) + n + int(SR * 0.02)
    spectrum = np.fft.rfft(mono, size)
    channels = []
    for _ in range(2):
        ir = np.convolve(rng.standard_normal(n), smooth, mode="same") * np.exp(-t / REVERB_TIME)
        ir = np.concatenate([np.zeros(int(SR * 0.012)), ir])
        wet = np.fft.irfft(spectrum * np.fft.rfft(ir, size), size)
        wet *= np.sqrt(np.sum(mono ** 2) / max(np.sum(wet ** 2), 1e-12))
        dry = np.concatenate([mono, np.zeros(size - len(mono))])
        channels.append(dry + REVERB_WET * wet)
    return np.stack(channels, axis=1)


def finish(stereo):
    level = np.max(np.abs(stereo), axis=1)
    audible = np.nonzero(level > np.max(level) * 10 ** (-42 / 20))[0]
    stereo = stereo[:audible[-1] + int(SR * 0.03)]
    fade = int(SR * 0.03)
    stereo[-fade:] *= np.linspace(1.0, 0.0, fade)[:, None]
    rms = np.sqrt(np.mean(stereo ** 2))
    stereo = stereo * (10 ** (TARGET_RMS_DB / 20) / rms)
    peak = np.max(np.abs(stereo))
    limit = 10 ** (PEAK_LIMIT_DB / 20)
    if peak > limit:
        stereo = stereo * (limit / peak)
    return stereo


# ------------------------------------------------------------ Sound set ----
# name: (length, [(start, note, velocity, brightness), ...])
SOUNDS = {
    "join":               (0.60, [(0.00, "A5", 0.9, 1.0), (0.09, "D6", 1.0, 1.0)]),
    "leave":              (0.60, [(0.00, "D6", 0.9, 1.0), (0.09, "A5", 1.0, 0.8)]),
    "connected":          (0.90, [(0.00, "D5", 0.8, 0.9), (0.10, "A5", 0.9, 1.0), (0.20, "D6", 1.0, 1.0)]),
    "disconnected":       (0.90, [(0.00, "D6", 0.9, 0.9), (0.10, "A5", 0.9, 0.8), (0.20, "D5", 1.0, 0.7)]),
    "message":            (0.55, [(0.00, "B5", 0.9, 1.2), (0.07, "E6", 1.0, 1.2)]),
    "message_sent":       (0.35, [(0.00, "D6", 0.7, 0.6)]),
    "mute":               (0.40, [(0.00, "A5", 0.9, 0.6), (0.06, "F#5", 1.0, 0.5)]),
    "unmute":             (0.40, [(0.00, "F#5", 0.9, 0.7), (0.06, "A5", 1.0, 0.8)]),
    "deafen":             (0.50, [(0.00, "A5", 0.9, 0.5), (0.07, "D5", 1.0, 0.4)]),
    "undeafen":           (0.50, [(0.00, "D5", 0.9, 0.6), (0.07, "A5", 1.0, 0.8)]),
    "screen_share_start": (0.70, [(0.00, "E5", 0.8, 1.4), (0.08, "B5", 0.9, 1.4), (0.16, "E6", 1.0, 1.4)]),
    "screen_share_stop":  (0.70, [(0.00, "E6", 0.8, 1.2), (0.08, "B5", 0.9, 1.1), (0.16, "E5", 1.0, 1.0)]),
    "error":              (0.60, [(0.00, "A4", 1.0, 1.3), (0.00, "Bb4", 0.8, 1.3),
                                  (0.14, "A4", 1.0, 1.3), (0.14, "Bb4", 0.8, 1.3)]),
    "call_incoming":      (1.60, [(0.00, "D6", 0.9, 1.0), (0.12, "F#6", 0.9, 1.0), (0.24, "A6", 1.0, 1.0),
                                  (0.48, "D6", 0.9, 1.0), (0.60, "F#6", 0.9, 1.0), (0.72, "A6", 1.0, 1.0)]),
}


def main():
    out_dir = Path(sys.argv[1])
    out_dir.mkdir(parents=True, exist_ok=True)
    for name, (length, events) in SOUNDS.items():
        audio = finish(reverb(sequence(events, length)))
        pcm = (np.clip(audio, -1.0, 1.0) * 32767).astype("<i2")
        with wave.open(str(out_dir / f"{name}.wav"), "wb") as f:
            f.setnchannels(2)
            f.setsampwidth(2)
            f.setframerate(SR)
            f.writeframes(pcm.tobytes())
        peak_db = 20 * np.log10(np.max(np.abs(audio)))
        print(f"{name:20s} {len(audio) / SR:5.2f} s  peak {peak_db:6.1f} dBFS")


if __name__ == "__main__":
    main()
