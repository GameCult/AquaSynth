#!/usr/bin/env python3
"""Independent formant estimator for AquaSynth renders. Shares no code with the C# tests or with the ladder.

Burg linear prediction (not autocorrelation LPC, not the tract's own impulse response): stable on short windows and
unbiased by the window edge. Usage:
  burg_formants.py FILE.wav T0 T1 [--order N] [--frame-ms 30] [--json]
Resamples to 10 kHz (FFT-domain), pre-emphasises 0.97, Hamming 30 ms frames hop 10 ms in [T0,T1], Burg order 12 default,
poles with bandwidth < 400 Hz and 150 Hz < f < 4.5 kHz are formants. Prints the per-formant median over frames and the
interquartile spread. It measures; it does not listen.
"""
import sys, json, wave, argparse
import numpy as np

def read_wav(path):
    with wave.open(path) as w:
        sr, n, ch, sw = w.getframerate(), w.getnframes(), w.getnchannels(), w.getsampwidth()
        raw = w.readframes(n)
    assert sw == 2, "16-bit PCM only"
    x = np.frombuffer(raw, dtype=np.int16).astype(np.float64) / 32768.0
    return (x.reshape(-1, ch).mean(axis=1) if ch > 1 else x), sr

def resample(x, sr, target):
    if sr == target:
        return x
    n = int(round(len(x) * target / sr))
    X = np.fft.rfft(x)
    m = n // 2 + 1
    Y = np.zeros(m, dtype=complex)
    k = min(m, len(X))
    Y[:k] = X[:k]
    return np.fft.irfft(Y, n) * (n / len(x))

def burg(x, order):
    f = x.copy(); b = x.copy(); a = np.array([1.0])
    for _ in range(order):
        fp, bp = f[1:], b[:-1]
        k = -2.0 * np.dot(fp, bp) / (np.dot(fp, fp) + np.dot(bp, bp) + 1e-30)
        a = np.concatenate([a, [0.0]]); a = a + k * a[::-1]
        f, b = fp + k * bp, bp + k * fp
    return a

def formants(frame, fs, order):
    a = burg(frame * np.hamming(len(frame)), order)
    r = np.roots(a); r = r[np.imag(r) > 1e-4]
    fq = np.angle(r) * fs / (2 * np.pi); bw = -np.log(np.abs(r)) * fs / np.pi
    return sorted(float(f) for f, w in zip(fq, bw) if 150 < f < 4500 and w < 400)

def main():
    p = argparse.ArgumentParser()
    p.add_argument("wav"); p.add_argument("t0", type=float); p.add_argument("t1", type=float)
    p.add_argument("--order", type=int, default=12); p.add_argument("--frame-ms", type=float, default=30)
    p.add_argument("--json", action="store_true")
    a = p.parse_args()
    x, sr = read_wav(a.wav)
    fs = 10000
    y = resample(x, sr, fs)
    y = np.append(y[0], y[1:] - 0.97 * y[:-1])
    n = int(a.frame_ms * fs / 1000); hop = int(0.010 * fs)
    rows = []
    for s in range(int(a.t0 * fs), int(a.t1 * fs) - n, hop):
        fr = y[s:s + n]
        if fr.std() < 1e-6: continue
        rows.append(formants(fr, fs, a.order)[:4])
    out = {"file": a.wav, "t0": a.t0, "t1": a.t1, "order": a.order, "frames": len(rows)}
    for i in range(4):
        v = [r[i] for r in rows if len(r) > i]
        out[f"F{i+1}"] = None if len(v) < max(3, len(rows) // 2) else {"median": round(float(np.median(v)), 1), "q1": round(float(np.percentile(v, 25)), 1), "q3": round(float(np.percentile(v, 75)), 1), "n": len(v)}
    if a.json: print(json.dumps(out))
    else:
        print(f"{a.wav} [{a.t0},{a.t1}] order {a.order} frames {len(rows)}: " + "  ".join(f"F{i+1}={out[f'F{i+1}']['median']} ({out[f'F{i+1}']['q1']}-{out[f'F{i+1}']['q3']})" if out[f"F{i+1}"] else f"F{i+1}=-" for i in range(4)))

if __name__ == "__main__":
    main()
