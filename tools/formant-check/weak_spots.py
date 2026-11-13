#!/usr/bin/env python3
"""Re-measures the tract voice's four listening weak spots from WordRender outputs. Measures; it does not listen.
  weak_spots.py ROOT [--json OUT.json]
ROOT holds: out/<word>/<word>.wav (140 Hz renders: v_<vowel>, v_<vowel>_s with primary stress, ama, ana, bin, bun, ba, nibu, nibu_flat),
out148/v_<vowel> (the five stress vowels unstressed at 148.4 Hz: the f0 rise alone), outnb/<word> (the same word with --no-burst); --burst-sub names the directory of the with-burst bin/ba/bun renders.
A  stress: level of the steady part (100-170 ms) of stressed against unstressed vowels, split into the f0 rise alone and the rest.
B  /b/ release: burst alone (render minus --no-burst render), its peak and decay against the vowel peak, the closure, the tail.
C  nasals: murmur level against the flanking vowels (the steady-part method of verify_renders.py), Burg N1, band levels of m against n.
D  lax vowels: Burg F1/F2 of /ɪ/ /ʊ/ against the tube chain and Hillenbrand 1995 / Peterson & Barney 1952 male means.
"""
import sys, os, json, wave, argparse
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import burg_formants as bf

def wav(path):
    with wave.open(path) as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(float) / 32768.0, w.getframerate()

def rms(x, sr, a, b):
    s = x[int(a * sr):int(b * sr)]
    return float(np.sqrt((s ** 2).mean())) if len(s) else 0.0

def peak(x, sr, a, b):
    s = x[int(a * sr):int(b * sr)]
    return float(np.abs(s).max()) if len(s) else 0.0

def db(a, b): return round(20 * np.log10(max(a, 1e-12) / max(b, 1e-12)), 2)

def load(root, sub, name):
    return wav(os.path.join(root, sub, name, name + ".wav"))

def burg(root, name, t0, t1):
    x, sr = load(root, "out", name)
    y = bf.resample(x, sr, 10000); y = np.append(y[0], y[1:] - 0.97 * y[:-1]); fs = 10000
    n = int(0.03 * fs); hop = int(0.01 * fs); rows = []
    for s in range(int(t0 * fs), int(t1 * fs) - n, hop):
        fr = y[s:s + n]
        if fr.std() < 1e-6: continue
        rows.append(bf.formants(fr, fs, 12)[:4])
    return [round(float(np.median([r[i] for r in rows if len(r) > i])), 1) if sum(len(r) > i for r in rows) >= max(3, len(rows) // 2) else None for i in range(3)]

FIVE = ["i", "ah", "u", "e", "o"]  # the vowels the test PrimaryStressRendersAboutTwoDecibelsLouderOnAverageOverVowels holds the mean over
HILL = {"ih": (429, 2034), "oo_short": (469, 1123)}
PB = {"ih": (392, 1993), "oo_short": (437, 1023)}
TUBE = {"ih": (346, 1987), "oo_short": (478, 850)}  # tube_formants.py at 17.52 cm

def stress(root):
    out = {}
    for v in FIVE:
        x0, sr = load(root, "out", f"v_{v}"); xf, _ = load(root, "out148", f"v_{v}"); xs, _ = load(root, "out", f"v_{v}_s")
        l0, lf, ls = (rms(q, sr, .10, .17) for q in (x0, xf, xs))
        out[v] = {"total_db": db(ls, l0), "f0_rise_only_db": db(lf, l0), "rest_db": round(db(ls, l0) - db(lf, l0), 2)}
    for tag, sel in (("mean_five", FIVE),):
        out[tag] = {k: round(float(np.mean([out[v][k] for v in sel])), 2) for k in ("total_db", "f0_rise_only_db", "rest_db")}
        out[tag]["total_sd"] = round(float(np.std([out[v]["total_db"] for v in sel])), 2)
    xa, sr = load(root, "out", "nibu_flat"); xb, _ = load(root, "out", "nibu")
    out["nibu_i_150_250ms_db"] = db(rms(xb, sr, .15, .25), rms(xa, sr, .15, .25))
    return out

def release_at(c0, c1):
    """Where a stop's articulators begin to open: StopReleaseStartSeconds in PatchScript.cs (the end less min(12 ms, 0.3 of the gesture))."""
    return c1 - min(0.012, 0.3 * (c1 - c0))

def burst(root, name, c0, c1, sub="out"):
    """c0..c1: the b gesture's start and end in seconds; v1: the vowel's steady part after it."""
    x, sr = load(root, sub, name); nb, _ = load(root, "outnb", name)
    n = min(len(x), len(nb)); d = x[:n] - nb[:n]
    vp = peak(x, sr, c1 + .06, c1 + .17)
    i = int(np.argmax(np.abs(d))); tpk = i / sr
    env = np.array([np.abs(d[k:k + int(.002 * sr)]).max() for k in range(0, n - int(.002 * sr), int(.002 * sr))])
    pk = float(np.abs(d).max())
    below = [k for k in range(int(tpk * 500), len(env)) if env[k] < pk * .1]
    decay_ms = round((below[0] / 500 - tpk) * 1000, 0) if below else None
    last = x[int((c1 + .06) * sr):]
    rel = release_at(c0, c1)
    return {"closure_peak": round(peak(x, sr, c0, rel), 4), "release_peak_full": round(peak(x, sr, rel, c1 + .005), 4),
            "burst_peak": round(pk, 4), "burst_peak_ms": round(tpk * 1000), "burst_to_tenth_ms": decay_ms, "vowel_peak": round(vp, 4),
            "burst_re_vowel_db": db(pk, vp), "release_peak_re_vowel_db": db(peak(x, sr, rel, c1 + .005), vp),
            "burst_rms_in_30ms_after_peak_re_vowel_rms_db": db(rms(d, sr, tpk, tpk + .03), rms(x, sr, c1 + .06, c1 + .17)),
            "burst_peak_from_120ms_after_release_to_end": round(peak(d, sr, c1 + .12, n / sr), 4), "burst_diff_samples_nonzero_before_closure_end": int(np.count_nonzero(np.abs(d[:int(rel * sr)]) > 1e-9)),
            "file_end_rms_last_100ms": round(rms(x, sr, len(x) / sr - .1, len(x) / sr), 5), "vowel_rms": round(rms(x, sr, c1 + .06, c1 + .17), 4)}

BURST_BANDS = ((0, 1000), (1000, 2000), (2000, 3500), (3500, 7000), (7000, 24000))

def burst_spectrum(root, word, release_s, window_ms=20, sub="out", smooth_hz=400, min_hz=1000):
    """The isolated burst's spectrum: the difference of out/<word> and outnb/<word> (renders with and without release_flow), window_ms from
    release_s, Hann windowed. peak_hz is the maximum of the power spectrum smoothed over smooth_hz and searched from min_hz up (a noise
    burst has no single line; below 1 kHz the release carries the tract's own low resonance, 430-480 Hz in /ata/ and /apa/, which is not
    the place cue: DESIGN choice of the floor), peak_hz_all the same without the floor, centroid_hz the power-weighted mean frequency from
    min_hz up, band_db the power in BURST_BANDS (dB, absolute), window_peak the largest sample in the window, outside_peak the largest
    sample outside release_s - 5 ms .. release_s + 120 ms (the closure's residue: 0 for a burst that exists only in the window)."""
    x, sr = load(root, sub, word); nb, _ = load(root, "outnb", word)
    n = min(len(x), len(nb)); d = x[:n] - nb[:n]
    a = int(release_s * sr); m = int(window_ms / 1000 * sr); s = d[a:a + m] * np.hanning(m)
    nfft = 1 << 14
    P = np.abs(np.fft.rfft(s, nfft)) ** 2; f = np.fft.rfftfreq(nfft, 1 / sr)
    k = max(1, int(smooth_hz / (sr / nfft))); Ps = np.convolve(P, np.ones(k) / k, mode="same")
    up = f >= min_hz
    out_mask = np.ones(n, bool); out_mask[max(0, int((release_s - .005) * sr)):int((release_s + .12) * sr)] = False
    return {"peak_hz": round(float(f[up][np.argmax(Ps[up])]), 0), "peak_hz_all": round(float(f[int(np.argmax(Ps))]), 0),
            "centroid_hz": round(float((f[up] * P[up]).sum() / max(P[up].sum(), 1e-30)), 0),
            "band_db": {f"{lo}_{hi}": round(float(10 * np.log10(P[(f >= lo) & (f < hi)].sum() + 1e-30)), 1) for lo, hi in BURST_BANDS},
            "window_peak": round(float(np.abs(d[a:a + m]).max()), 5), "outside_peak": float(np.abs(d[out_mask]).max()) if out_mask.any() else 0.0}

def bands(x, sr, a, b, edges=((0, 400), (400, 800), (800, 1600), (1600, 3200))):
    s = x[int(a * sr):int(b * sr)]; s = s * np.hanning(len(s))
    P = np.abs(np.fft.rfft(s, 1 << 15)) ** 2; f = np.fft.rfftfreq(1 << 15, 1 / sr)
    return [float(10 * np.log10(P[(f >= lo) & (f < hi)].sum() + 1e-30)) for lo, hi in edges]

def nasals(root):
    out = {}
    for w in ("ama", "ana"):
        x, sr = load(root, "out", w); c0, c1 = .18, .275
        v1, v2 = rms(x, sr, c0 - .08, c0 - .01), rms(x, sr, c1 + .03, c1 + .12)
        m = rms(x, sr, c0 + .2 * (c1 - c0), c1 - .2 * (c1 - c0))
        out[w] = {"murmur_db": db(m, np.sqrt((v1 ** 2 + v2 ** 2) / 2)), "N1_N2_N3": burg(root, w, .20, .26),
                  "band_db_0_400_400_800_800_1600_1600_3200": [round(q, 1) for q in bands(x, sr, c0 + .2 * (c1 - c0), c1 - .2 * (c1 - c0))],
                  "vowel_band_db": [round(q, 1) for q in bands(x, sr, c0 - .08, c0 - .01)]}
    out["n_minus_m_band_db"] = [round(a - b, 1) for a, b in zip(out["ana"]["band_db_0_400_400_800_800_1600_1600_3200"], out["ama"]["band_db_0_400_400_800_800_1600_1600_3200"])]
    out["n_minus_m_vowel_band_db"] = [round(a - b, 1) for a, b in zip(out["ana"]["vowel_band_db"], out["ama"]["vowel_band_db"])]
    return out

def lax(root):
    out = {}
    for v in ("ih", "oo_short"):
        f = burg(root, f"v_{v}", .07, .15)
        pct = lambda a, r: round(100 * (a - r) / r, 1)
        out[v] = {"burg_F1_F2": f[:2], "tube": TUBE[v], "hillenbrand": HILL[v], "pb": PB[v],
                  "vs_tube_pct": [pct(f[0], TUBE[v][0]), pct(f[1], TUBE[v][1])], "vs_hillenbrand_pct": [pct(f[0], HILL[v][0]), pct(f[1], HILL[v][1])],
                  "vs_pb_pct": [pct(f[0], PB[v][0]), pct(f[1], PB[v][1])]}
    return out

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("root"); ap.add_argument("--json"); ap.add_argument("--burst-sub", default="out", help="subdirectory of ROOT holding the with-burst renders of bin, ba, bun (outpost after the release_flow fix)"); a = ap.parse_args()
    res = {"A_stress": stress(a.root), "B_release": {"bin": burst(a.root, "bin", 0, .095, a.burst_sub), "ba": burst(a.root, "ba", 0, .095, a.burst_sub),
                                                   "bun": burst(a.root, "bun", 0, .095, a.burst_sub)},
           "C_nasals": nasals(a.root), "D_lax": lax(a.root)}
    s = json.dumps(res, indent=1, default=float, ensure_ascii=False)
    print(s)
    if a.json: open(a.json, "w").write(s + "\n")

if __name__ == "__main__": main()
