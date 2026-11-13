#!/usr/bin/env python3
"""What the formant numbers do not say. Measures a render for the faults Meta heard in tract-weak-spots-1 (2026-11-13) while every
formant matched its target; it does not listen, and a pass here is not a verdict (the listening page is).
  ear_metrics.py FILE.wav [--f0 140] [--steady A B] [--json]

click_ratio   largest pitch-period-window peak over the median window peak. A voiced word with no transient is near 1; the pre-fix
              nibu (a phone boundary stepping an area) was 3.54 with its window at the /b/ closure (0.271 s); after, 1.76 (a vowel's own pulses).
step_db       largest single-sample step, in dB over the median sample step of the voiced part. A cell area that jumps is one sample wide.
bandwidths    LPC pole bandwidths (Hz) of the steady part, F1..F3 as (Hz, bandwidth). Natural vowels run about 40-100 Hz at F1, 50-120 at
              F2, 80-200 at F3 (Hawks & Miller 1995, Fant 1972); a lossless tube rings narrower, which reads as reedy.
hnr_db        harmonics-to-noise ratio of the steady part by autocorrelation at the pitch lag.
hf_db         energy above 4 kHz re the total, dB.
"""
import sys, wave, json, argparse
import numpy as np


def wav(path):
    with wave.open(path) as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(float) / 32768.0, w.getframerate()


def click_ratio(x, sr, f0):
    p = max(8, int(sr / f0))
    peaks = np.array([np.abs(x[i:i + p]).max() for i in range(0, len(x) - p, p // 2)])
    voiced = peaks[peaks > peaks.max() * 0.02]
    if len(voiced) < 4:
        return 0.0, 0.0
    at = int(np.argmax(peaks)) * (p // 2) / sr
    return float(peaks.max() / np.median(voiced)), at


def step_db(x):
    d = np.abs(np.diff(x))
    live = d[d > d.max() * 1e-3]
    return float(20 * np.log10(d.max() / max(np.median(live), 1e-12))) if len(live) else 0.0


def autocorr(s, n=None):
    """Autocorrelation at lags 0..n-1 by FFT (np.correlate is quadratic)."""
    n = n or len(s)
    f = np.fft.rfft(s, 2 * len(s))
    return np.fft.irfft(f * np.conj(f))[:n]


def lpc(s, o):
    s = s * np.hamming(len(s))
    r = autocorr(s, o + 1)
    R = np.array([[r[abs(i - j)] for j in range(o)] for i in range(o)])
    a = np.linalg.solve(R + 1e-9 * r[0] * np.eye(o), r[1:o + 1])
    return np.concatenate([[1], -a])


def bandwidths(y, sr, order=14):
    dec = max(1, int(round(sr / 10000)))
    k = np.hanning(4 * dec + 1)
    k /= k.sum()
    z = np.convolve(y - y.mean(), k, "same")[::dec]
    fs = sr / dec
    z = np.append(z[0], z[1:] - 0.97 * z[:-1])
    rt = np.roots(lpc(z, order))
    rt = rt[np.imag(rt) > 0.001]
    fq = np.angle(rt) * fs / (2 * np.pi)
    bw = -np.log(np.abs(rt)) * fs / np.pi
    # a formant is a narrow pole; broad poles (> 400 Hz) are the spectral tilt the all-pole fit spends order on
    return [(int(f), int(b)) for f, b in sorted(zip(fq, bw)) if 150 < f < 4500 and b < 400][:3]


def hnr_db(y, sr, f0):
    y = y - y.mean()
    ac = autocorr(y)
    ac /= ac[0]
    lo, hi = int(sr / (f0 * 1.3)), int(sr / (f0 * 0.75))
    m = float(ac[lo:hi].max())
    return 10 * np.log10(max(m, 1e-6) / max(1 - m, 1e-6))


def measure(path, f0=140.0, steady=None):
    x, sr = wav(path)
    a, b = steady if steady else (len(x) / sr * 0.35, len(x) / sr * 0.85)
    y = x[int(a * sr):int(b * sr)]
    sp = np.abs(np.fft.rfft((y - y.mean()) * np.hanning(len(y))))
    fr = np.fft.rfftfreq(len(y), 1 / sr)
    cr, at = click_ratio(x, sr, f0)
    return {
        "file": path.split("/")[-1], "click_ratio": round(cr, 2), "click_at_s": round(at, 4), "step_db": round(step_db(x), 1),
        "bandwidths": bandwidths(y, sr), "hnr_db": round(hnr_db(y, sr, f0), 1),
        "hf_db": round(float(10 * np.log10(max((sp[fr > 4000] ** 2).sum(), 1e-20) / (sp ** 2).sum())), 1),
    }


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--f0", type=float, default=140.0)
    ap.add_argument("--steady", nargs=2, type=float)
    ap.add_argument("--json", action="store_true")
    a = ap.parse_args()
    rows = [measure(f, a.f0, a.steady) for f in a.files]
    if a.json:
        print(json.dumps(rows, indent=1))
    else:
        for r in rows:
            print(f"{r['file']:20s} click {r['click_ratio']:5.2f} at {r['click_at_s']:.3f}s  step {r['step_db']:5.1f} dB  hnr {r['hnr_db']:5.1f}  hf {r['hf_db']:6.1f}  F(Hz,bw) {r['bandwidths']}")
