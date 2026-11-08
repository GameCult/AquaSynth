#!/usr/bin/env python3
"""Averaged-spectrum peaks and dips of a stretch of a WAV, independent of the AquaSynth code (numpy only).

  spectrum_dips.py WAV T0 T1 [--f0 HZ] [--fmax 4000] [--nperseg 1024]
Welch estimate (Hann, 75 % overlap, zero-padded to 4x) of the mono signal between T0 and T1 seconds. Prints local maxima and minima in dB (relative
to the largest) below --fmax. With --f0 the spectrum of a periodic source is a line spectrum, so the extrema are read from the harmonic envelope
instead: the largest bin within a quarter of f0 of each harmonic k*f0; frequencies are then multiples of f0 and the resolution is f0.
Used on the nasal murmur of a WordRender render against the nostril transfer of tube_formants.py --nasal.
"""
import sys, argparse, wave
import numpy as np

def read_wav(path):
    with wave.open(path) as w:
        n, ch, width, rate = w.getnframes(), w.getnchannels(), w.getsampwidth(), w.getframerate()
        raw = w.readframes(n)
    if width != 2: raise SystemExit(f"{path}: only 16-bit PCM")
    x = np.frombuffer(raw, dtype='<i2').astype(float) / 32768.0
    return x.reshape(-1, ch)[:, 0], rate

def welch(x, rate, nperseg):
    nperseg = min(nperseg, len(x))
    win = np.hanning(nperseg)
    step = max(1, nperseg // 4)
    nfft = 4 * nperseg
    acc = np.zeros(nfft // 2 + 1); count = 0
    for s in range(0, len(x) - nperseg + 1, step):
        seg = x[s:s + nperseg] * win
        acc += np.abs(np.fft.rfft(seg, nfft)) ** 2; count += 1
    return np.fft.rfftfreq(nfft, 1 / rate), acc / max(count, 1), count

def harmonic_envelope(freqs, power, f0, fmax):
    out = []
    k = 1
    while k * f0 < fmax:
        sel = (freqs >= k * f0 - f0 / 4) & (freqs <= k * f0 + f0 / 4)
        out.append((k * f0, power[sel].max()))
        k += 1
    return np.array([o[0] for o in out]), np.array([o[1] for o in out])

def extrema(freqs, db):
    res = []
    for i in range(1, len(db) - 1):
        if db[i] > db[i - 1] and db[i] >= db[i + 1]: res.append((freqs[i], db[i], 'peak'))
        elif db[i] < db[i - 1] and db[i] <= db[i + 1]: res.append((freqs[i], db[i], 'dip'))
    return res

def main():
    p = argparse.ArgumentParser()
    p.add_argument('wav'); p.add_argument('t0', type=float); p.add_argument('t1', type=float)
    p.add_argument('--f0', type=float); p.add_argument('--fmax', type=float, default=4000); p.add_argument('--nperseg', type=int, default=1024)
    a = p.parse_args()
    x, rate = read_wav(a.wav)
    seg = x[int(a.t0 * rate):int(a.t1 * rate)]
    freqs, power, count = welch(seg, rate, a.nperseg)
    keep = (freqs > 30) & (freqs < a.fmax)
    freqs, power = freqs[keep], power[keep]
    if a.f0: freqs, power = harmonic_envelope(freqs, power, a.f0, a.fmax)
    db = 10 * np.log10(np.maximum(power, 1e-30) / power.max())
    ex = extrema(freqs, db)
    print(f"{a.wav} {a.t0}-{a.t1}s rate {rate} Welch segments {count}" + (f" harmonic envelope f0 {a.f0}" if a.f0 else ""))
    print("  peaks: " + "  ".join(f"{f:.0f}({d:.0f}dB)" for f, d, k in ex if k == 'peak'))
    print("  dips:  " + "  ".join(f"{f:.0f}({d:.0f}dB)" for f, d, k in ex if k == 'dip'))

if __name__ == '__main__':
    main()
