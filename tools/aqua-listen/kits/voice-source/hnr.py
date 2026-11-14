#!/usr/bin/env python3
"""Harmonics-to-noise ratio of a clip, Boersma (1993) autocorrelation method, so a noise setting can be named in dB instead of in engine units.
  hnr.py CLIP.wav [f0-hz]     prints median HNR over the voiced frames, and the frame count
Frames of 40 ms at a 10 ms hop; a frame counts when its rms is above a tenth of the loudest frame's. r = the window-corrected normalised
autocorrelation peak within +-25 % of f0 (default 140); HNR = 10 log10(r / (1 - r)). Reads 16-bit mono wav; needs numpy only.
It is a measurement of the periodic share of the rendered output, not of the source alone: the tract shapes both.
"""
import sys, wave
import numpy as np


def read(path):
    with wave.open(path) as w:
        assert w.getsampwidth() == 2 and w.getnchannels() == 1
        return np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(float) / 32768, w.getframerate()


def hnr(x, fs, f0=140.0, win=0.04, hop=0.01):
    n, h = int(win * fs), int(hop * fs)
    w = np.hanning(n)
    nfft = 1 << (2 * n - 1).bit_length()
    wac = np.fft.irfft(np.abs(np.fft.rfft(w, nfft)) ** 2)[:n]
    wac /= wac[0]
    lo, hi = int(fs / (f0 * 1.25)), int(fs / (f0 * 0.75))
    frames = [x[i:i + n] for i in range(0, len(x) - n, h)]
    rms = np.array([np.sqrt(np.mean(f * f)) for f in frames])
    out = []
    for f, e in zip(frames, rms):
        if e < 0.1 * rms.max():
            continue
        f = (f - f.mean()) * w
        if not f.any():
            continue
        ac = np.fft.irfft(np.abs(np.fft.rfft(f, nfft)) ** 2)[:n]
        r = float(np.clip((ac[lo:hi] / ac[0] / wac[lo:hi]).max(), 1e-6, 1 - 1e-6))
        out.append(10 * np.log10(r / (1 - r)))
    return float(np.median(out)), len(out)


if __name__ == "__main__":
    x, fs = read(sys.argv[1])
    v, k = hnr(x, fs, float(sys.argv[2]) if len(sys.argv) > 2 else 140.0)
    print(f"{v:.1f} dB over {k} frames")
