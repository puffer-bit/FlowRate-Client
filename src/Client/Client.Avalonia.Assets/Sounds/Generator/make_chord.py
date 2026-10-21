#!/usr/bin/env python3
"""The "chord" kit with a separate identity for every event.

Usage: make_chord.py <output-dir>
Events differ in register, number of hits, chord shape, strum and brightness,
not only in direction, so they can be told apart without seeing the screen.
"""
import sys
import wave
from pathlib import Path

import numpy as np

import make_sounds as base
from make_modern import attack, glide, lowpass

SR = base.SR
ROOT = 196.00         # G3; every "semis" value below is relative to it
LOWPASS = 8000
base.REVERB_TIME = 0.12
base.REVERB_WET = 0.08


def hit(semis, intervals, vel, bend, decay, bright, strum, glide_time=0.03):
    """One gesture: the given intervals over a base note, strummed up (or down if strum < 0)."""
    t = np.arange(int(SR * (decay * 7 + abs(strum) * len(intervals)))) / SR
    order = range(len(intervals)) if strum >= 0 else reversed(range(len(intervals)))
    out = np.zeros_like(t)
    for position, i in enumerate(order):
        local = np.maximum(t - abs(strum) * position, 0.0)
        ratio = 2 ** (intervals[i] / 12)
        phase = glide(ROOT * 2 ** (semis / 12) * ratio, bend, local, glide_time)
        shine = bright * (0.28 * np.sin(2 * phase) + 0.14 * np.sin(3 * phase) + 0.06 * np.sin(4 * phase))
        tail = decay * ratio ** -0.3
        voice = (np.sin(phase) + shine * np.exp(-local / (tail * 0.56))) * attack(local, 0.007) * np.exp(-local / tail)
        out += 0.8 ** i * np.where(t >= abs(strum) * position, voice, 0.0)
    return vel * out


OPEN = [0, 7, 14]     # root, fifth, ninth: the neutral shape of the kit

# name: [(start seconds, semis, intervals, velocity, bend, decay, brightness, strum seconds[, glide seconds]), ...]
EVENTS = {
    # People: two full chords in the middle, stepping up or down.
    "join":               [(0.00, 0, OPEN, 0.9, 0, 0.17, 1.0, 0.012), (0.142, 5, OPEN, 1.0, 0, 0.17, 1.0, 0.012)],
    "leave":              [(0.00, 5, OPEN, 0.9, 0, 0.17, 1.0, 0.012), (0.142, 0, OPEN, 1.0, 0, 0.17, 0.5, 0.012)],
    # Connection: short-short-long, two single notes climbing into a major chord / a minor chord falling apart.
    "connected":          [(0.00, 0, [0], 0.7, 0, 0.08, 1.0, 0), (0.09, 7, [0], 0.8, 0, 0.08, 1.0, 0),
                           (0.18, 12, [0, 4, 7], 1.0, 0, 0.20, 1.0, 0.010)],
    "disconnected":       [(0.00, 12, [0, 3, 7], 0.9, 0, 0.12, 0.9, 0.010), (0.16, 7, [0], 0.8, 0, 0.08, 0.7, 0),
                           (0.25, 0, [0], 1.0, 0, 0.16, 0.5, 0)],
    # Messages: single notes, no chord. Incoming is two bubbles snapping up into pitch, sent is two quick quiet notes.
    "message":            [(0.00, 12, [0], 0.9, -5, 0.07, 1.0, 0, 0.014), (0.10, 17, [0], 1.0, -5, 0.09, 1.0, 0, 0.014)],
    "message_sent":       [(0.00, 12, [0], 0.4, 0, 0.05, 0.8, 0), (0.05, 19, [0], 0.5, 0, 0.06, 0.8, 0)],
    # Mention: the incoming bubbles with a third one on top, ending on a bright fifth, so it is heard as "for you".
    "mention":            [(0.00, 12, [0], 0.9, -5, 0.07, 1.1, 0, 0.014), (0.09, 17, [0], 1.0, -5, 0.07, 1.1, 0, 0.014),
                           (0.18, 24, [0, 7], 1.0, -5, 0.12, 1.2, 0.008, 0.014)],
    # Kicked: three quick chords stepping down, like footsteps going away; darker than leave.
    "user_kicked":        [(0.00, 7, OPEN, 0.8, 0, 0.08, 1.0, 0.008), (0.09, 3, OPEN, 0.9, 0, 0.08, 0.8, 0.008),
                           (0.18, -2, OPEN, 1.0, 0, 0.16, 0.5, 0.010)],
    # Microphone: one low note whose pitch falls (off) or rises (on).
    "mute":               [(0.00, 0, [0], 1.0, 5, 0.09, 0.8, 0)],
    "unmute":             [(0.00, 7, [0, 7], 1.0, -5, 0.11, 1.0, 0)],
    # Sound: the same idea, but two hits.
    "deafen":             [(0.00, 3, [0], 1.0, 4, 0.09, 0.7, 0), (0.13, -2, [0], 1.0, 4, 0.11, 0.7, 0)],
    "undeafen":           [(0.00, -2, [0], 1.0, -4, 0.09, 0.8, 0), (0.13, 5, [0, 7], 1.0, -4, 0.12, 1.0, 0)],
    # Screen share: a fast run of three single notes.
    "screen_share_start": [(0.000, 0, [0], 0.7, 0, 0.09, 1.1, 0), (0.065, 7, [0], 0.8, 0, 0.09, 1.1, 0),
                           (0.130, 14, [0, 7], 1.0, 0, 0.17, 1.1, 0.010)],
    "screen_share_stop":  [(0.000, 14, [0], 0.7, 0, 0.09, 1.1, 0), (0.065, 7, [0], 0.8, 0, 0.09, 0.9, 0),
                           (0.130, 0, [0, 7], 1.0, 0, 0.17, 0.7, 0.010)],
    # Error: a low dissonant cluster, twice.
    "error":              [(0.00, -2, [0, 1, 6], 1.0, 0, 0.10, 1.4, 0), (0.17, -2, [0, 1, 6], 1.0, 0, 0.10, 1.4, 0)],
    # Call: a bubble melody, up and back down, played twice.
    "call_incoming":      [(0.13 * i + 0.72 * r, n, [0], 1.0, -5, 0.11, 1.0, 0, 0.014)
                           for r in range(2) for i, n in enumerate((12, 19, 24, 19))],
}


def main():
    out_dir = Path(sys.argv[1])
    out_dir.mkdir(parents=True, exist_ok=True)
    for name, hits in EVENTS.items():
        parts = [(start, hit(*rest)) for start, *rest in hits]
        mono = np.zeros(max(int(SR * start) + len(p) for start, p in parts))
        for start, p in parts:
            i = int(SR * start)
            mono[i:i + len(p)] += p
        audio = base.finish(base.reverb(lowpass(mono, LOWPASS)))
        pcm = (np.clip(audio, -1.0, 1.0) * 32767).astype("<i2")
        with wave.open(str(out_dir / f"{name}.wav"), "wb") as f:
            f.setnchannels(2)
            f.setsampwidth(2)
            f.setframerate(SR)
            f.writeframes(pcm.tobytes())
        print(f"{name:20s} {len(audio) / SR:5.2f} s  peak {20 * np.log10(np.max(np.abs(audio))):6.1f} dBFS")


if __name__ == "__main__":
    main()
