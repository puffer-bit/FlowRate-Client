#!/usr/bin/env python3
"""Modern, minimal UI sound kits: one or two short rounded gestures per event.

Usage: make_modern.py <output-dir> [kit ...]   -> <output-dir>/<kit>/<name>.wav
"""
import sys
import wave
from pathlib import Path

import numpy as np

import make_sounds as base

SR = base.SR


def time(dur):
    return np.arange(int(SR * dur)) / SR


def glide(freq, bend, t, glide_time):
    """Phase of a sine that starts `bend` semitones off and settles on freq."""
    f = freq * 2 ** (bend * np.exp(-t / glide_time) / 12)
    return 2 * np.pi * np.cumsum(f) / SR


def attack(t, length):
    return 0.5 - 0.5 * np.cos(np.pi * np.minimum(t / length, 1.0))


# ---------------------------------------------------------------- Voices ----
def soft(freq, bend, vel):
    """Rounded sine drop, like phone and web app notifications."""
    t = time(0.6)
    phase = glide(freq, bend, t, 0.03)
    return vel * (np.sin(phase) + 0.07 * np.sin(2 * phase)) * attack(t, 0.007) * np.exp(-t / 0.10)


def pop(freq, bend, vel):
    """Bubble: every note snaps up into its pitch, short and slightly squashed."""
    t = time(0.4)
    phase = glide(freq, bend if bend else -5, t, 0.014)
    return vel * np.tanh(1.6 * np.sin(phase) * attack(t, 0.004) * np.exp(-t / 0.055))


def tap(freq, bend, vel):
    """Muted knock: more of a touch than a note."""
    t = time(0.25)
    phase = glide(freq, bend, t, 0.02)
    body = np.sin(phase) * np.exp(-t / 0.032) + 0.25 * np.sin(2.4 * phase) * np.exp(-t / 0.010)
    smooth = np.hanning(24)
    click = np.convolve(np.random.default_rng(11).standard_normal(len(t)), smooth / smooth.sum(), mode="same")
    return vel * (body + 0.5 * click * np.exp(-t / 0.004)) * attack(t, 0.0015)


def deep(freq, bend, vel):
    """Low rounded hum: sine with an octave below it, lightly saturated."""
    t = time(0.9)
    phase = glide(freq, bend, t, 0.04)
    out = np.sin(phase) + 0.55 * np.sin(phase / 2) + 0.10 * np.sin(2 * phase) * np.exp(-t / 0.05)
    return vel * np.tanh(1.2 * out * attack(t, 0.010) * np.exp(-t / 0.15))


def felt(freq, bend, vel):
    """Felt piano: dull hammer, warm body, highs gone almost at once."""
    t = time(1.0)
    phase = glide(freq, bend, t, 0.03)
    out = np.zeros_like(t)
    for k, amp in ((1, 1.0), (2, 0.45), (3, 0.20), (4, 0.10), (5, 0.05)):
        out += amp * np.sin(k * phase) * np.exp(-t * k ** 1.5 / 0.26)
    smooth = np.hanning(96)
    hammer = np.convolve(np.random.default_rng(13).standard_normal(len(t)), smooth / smooth.sum(), mode="same")
    return vel * (out + 1.2 * hammer * np.exp(-t / 0.008)) * attack(t, 0.005)


def sub(freq, bend, vel):
    """Bass drop: starts an octave up and falls into a low note, like an 808 tap."""
    t = time(0.7)
    phase = glide(freq, bend + 12, t, 0.012)
    out = np.sin(phase) + 0.30 * np.sin(2 * phase) * np.exp(-t / 0.06)
    return vel * np.tanh(1.5 * out * attack(t, 0.003) * np.exp(-t / 0.12))


def handpan(freq, bend, vel):
    """Handpan / tongue drum: fundamental, octave and twelfth ringing together."""
    t = time(1.2)
    phase = glide(freq, bend, t, 0.03)
    out = np.sin(phase) * np.exp(-t / 0.32)
    out += 0.50 * np.sin(2 * phase) * np.exp(-t / 0.20) + 0.28 * np.sin(3 * phase) * np.exp(-t / 0.12)
    smooth = np.hanning(64)
    hit = np.convolve(np.random.default_rng(17).standard_normal(len(t)), smooth / smooth.sum(), mode="same")
    return vel * (out + 0.8 * hit * np.exp(-t / 0.006)) * attack(t, 0.003)


def synth(freq, bend, vel):
    """Analog synth pluck: bright for an instant, then the filter closes."""
    t = time(0.9)
    out = np.zeros_like(t)
    for cents in (-6, 6):
        phase = glide(freq * 2 ** (cents / 1200), bend, t, 0.03)
        for k in range(1, 19):
            out += np.sin(k * phase) / k * np.exp(-t * (1 + 0.7 * k) / 0.22)
    return vel * 0.5 * out * attack(t, 0.004)


def kalimba(freq, bend, vel):
    """Thumb piano: warm tine with a short metallic tick from the nail."""
    t = time(0.9)
    phase = glide(freq, bend, t, 0.03)
    out = np.sin(phase) * np.exp(-t / 0.22) + 0.14 * np.sin(2 * phase) * np.exp(-t / 0.10)
    out += 0.22 * np.sin(5.4 * phase) * np.exp(-t / 0.018)
    return vel * out * attack(t, 0.002)


def drop(freq, bend, vel):
    """Water drop: a quick rising chirp and a fainter, higher echo of it."""
    t = time(0.4)
    out = np.zeros_like(t)
    for delay, scale, level in ((0.0, 1.0, 1.0), (0.085, 1.5, 0.35)):
        local = np.maximum(t - delay, 0.0)
        f = freq * scale * 2 ** (bend / 12) * (1 + 1.1 * np.minimum(local / 0.07, 1.0) ** 2)
        env = np.where(t >= delay, np.sin(np.pi * np.minimum(local / 0.09, 1.0)) ** 2, 0.0)
        out += level * env * np.sin(2 * np.pi * np.cumsum(np.where(t >= delay, f, 0.0)) / SR)
    return vel * out


def chord(freq, bend, vel):
    """Lush chord: every gesture is root, fifth and ninth strummed together."""
    t = time(1.2)
    out = np.zeros_like(t)
    for i, (ratio, amp) in enumerate(((1.0, 1.0), (1.5, 0.7), (2.25, 0.5))):
        local = np.maximum(t - 0.012 * i, 0.0)
        phase = glide(freq * ratio, bend, t, 0.03)
        shine = 0.28 * np.sin(2 * phase) + 0.14 * np.sin(3 * phase) + 0.06 * np.sin(4 * phase)
        out += amp * (np.sin(phase) + shine * np.exp(-local / 0.095)) * attack(local, 0.007) * np.exp(-local / 0.17)
    return vel * 0.6 * out


def reverse(freq, bend, vel):
    """Reversed note: swells out of nothing and stops on a soft edge."""
    t = time(0.5)
    peak = 0.20
    env = np.where(t < peak, np.exp((t - peak) / 0.06), np.exp(-(t - peak) / 0.022))
    phase = glide(freq, bend, t, 0.05)
    out = np.sin(phase) + 0.25 * np.sin(2 * phase) + 0.10 * np.sin(3 * phase)
    return vel * out * env * attack(t, 0.01)


def vox(freq, bend, vel):
    """Synthetic voice: a short hummed "oh" with a little vibrato."""
    t = time(0.9)
    vibrato = 0.10 * np.sin(2 * np.pi * 5.5 * t) * np.minimum(t / 0.12, 1.0)
    phase = glide(freq, bend, t, 0.04) + 2 * np.pi * np.cumsum(freq * (2 ** (vibrato / 12) - 1)) / SR
    out = np.zeros_like(t)
    for k in range(1, int(3800 / freq) + 1):
        f = k * freq
        gain = sum(a * np.exp(-((f - c) / w) ** 2) for c, w, a in ((480, 160, 1.0), (850, 170, 0.6), (2600, 300, 0.12)))
        out += gain / k ** 0.5 * np.sin(k * phase)
    return vel * out * attack(t, 0.03) * np.exp(-np.maximum(t - 0.05, 0) / 0.15)


def swoosh(direction, dur=0.22):
    """Soft air sweep; direction +1 rises, -1 falls."""
    n = int(SR * dur)
    t = np.arange(n) / SR
    sweep = np.linspace(0.0, 1.0, n) if direction > 0 else np.linspace(1.0, 0.0, n)
    cutoff = 500.0 * (3200.0 / 500.0) ** sweep
    noise = np.random.default_rng(21).standard_normal(n)
    out = np.zeros(n)
    low = band = 0.0
    for i in range(n):
        f = 2 * np.sin(np.pi * cutoff[i] / SR)
        low += f * band
        band += f * (noise[i] - low - 0.9 * band)
        out[i] = band
    return out / np.max(np.abs(out)) * np.sin(np.pi * t / dur) ** 2


# ----------------------------------------------------------------- Events ----
# name: ([(start in beats, semitones from the kit root, velocity, bend), ...], swoosh direction or 0)
EVENTS = {
    "join":               ([(0, 0, 0.9, 0), (1, 5, 1.0, 0)], 0),
    "leave":              ([(0, 5, 0.9, 0), (1, 0, 1.0, 0)], 0),
    "connected":          ([(0, 0, 0.8, 0), (1, 7, 1.0, 0), (1, 12, 0.35, 0)], 0),
    "disconnected":       ([(0, 7, 0.8, 0), (1, 0, 1.0, 0), (1, -5, 0.35, 0)], 0),
    "message":            ([(0, 7, 1.0, 0), (1, 10, 0.8, 0)], 0),
    "message_sent":       ([(0, 12, 0.55, -3)], 0),
    "mute":               ([(0, 0, 1.0, 3)], 0),
    "unmute":             ([(0, 3, 1.0, -3)], 0),
    "deafen":             ([(0, 0, 1.0, 3), (1, -4, 1.0, 3)], 0),
    "undeafen":           ([(0, -4, 1.0, -3), (1, 0, 1.0, -3)], 0),
    "screen_share_start": ([(1.5, 9, 1.0, 0)], +1),
    "screen_share_stop":  ([(1.5, 2, 1.0, 0)], -1),
    "error":              ([(0, -5, 1.0, 0), (0, -4, 0.5, 0), (1.4, -5, 1.0, 0), (1.4, -4, 0.5, 0)], 0),
    "call_incoming":      ([(0, 0, 0.9, 0), (0, 7, 0.7, 0), (1.6, 0, 0.9, 0), (1.6, 7, 0.7, 0),
                            (6, 0, 0.9, 0), (6, 7, 0.7, 0), (7.6, 0, 0.9, 0), (7.6, 7, 0.7, 0)], 0),
}

# kit: (voice, root Hz, beat seconds, lowpass Hz, reverb time, reverb wet)
KITS = {
    "soft": (soft, 523.25, 0.085, 5000, 0.09, 0.10),
    "pop":  (pop,  392.00, 0.105, 6000, 0.06, 0.06),
    "tap":  (tap,  329.63, 0.095, 4500, 0.05, 0.05),
    "deep":    (deep,    220.00, 0.120, 2200, 0.16, 0.14),
    "felt":    (felt,    220.00, 0.130, 2800, 0.18, 0.14),
    "sub":     (sub,     146.83, 0.130, 2500, 0.10, 0.08),
    "handpan": (handpan, 196.00, 0.140, 3500, 0.22, 0.16),
    "synth":   (synth,   220.00, 0.110, 3500, 0.14, 0.12),
    "kalimba": (kalimba, 293.66, 0.120, 4500, 0.16, 0.12),
    "drop":    (drop,    440.00, 0.150, 5000, 0.14, 0.14),
    "chord":   (chord,   196.00, 0.142, 8000, 0.12, 0.08),
    "reverse": (reverse, 261.63, 0.200, 3500, 0.16, 0.14),
    "vox":     (vox,     220.00, 0.150, 4000, 0.18, 0.14),
}


def lowpass(mono, cutoff):
    spectrum = np.fft.rfft(mono)
    freqs = np.fft.rfftfreq(len(mono), 1 / SR)
    return np.fft.irfft(spectrum / np.sqrt(1 + (freqs / cutoff) ** 4), len(mono))


def render(notes, sweep, voice, root, beat):
    out = np.zeros(int(SR * (max(n[0] for n in notes) * beat + 1.3)))
    if sweep:
        air = 0.22 * swoosh(sweep)
        out[:len(air)] += air
    for start, semis, vel, bend in notes:
        n = voice(root * 2 ** (semis / 12), bend, vel)
        i = int(SR * start * beat)
        out[i:i + len(n)] += n[:len(out) - i]
    return out


def main():
    root_dir = Path(sys.argv[1])
    for kit in sys.argv[2:] or list(KITS):
        voice, root, beat, cutoff, base.REVERB_TIME, base.REVERB_WET = KITS[kit]
        out_dir = root_dir / kit
        out_dir.mkdir(parents=True, exist_ok=True)
        lengths = []
        for name, (notes, sweep) in EVENTS.items():
            audio = base.finish(base.reverb(lowpass(render(notes, sweep, voice, root, beat), cutoff)))
            pcm = (np.clip(audio, -1.0, 1.0) * 32767).astype("<i2")
            with wave.open(str(out_dir / f"{name}.wav"), "wb") as f:
                f.setnchannels(2)
                f.setsampwidth(2)
                f.setframerate(SR)
                f.writeframes(pcm.tobytes())
            lengths.append(len(audio) / SR)
        print(f"{kit:8s} {len(lengths)} files, {min(lengths):.2f}-{max(lengths):.2f} s")


if __name__ == "__main__":
    main()
