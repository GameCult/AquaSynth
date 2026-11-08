#!/usr/bin/env python3
"""Independent check of the WordRender outputs against published formants and the stress/burst cues. Shares no code with the C#
tests or the ladder; formants come from burg_formants.py (Burg LPC), f0 from autocorrelation, levels from the WAV.
  verify_renders.py RENDER_DIR [--json OUT.json]
RENDER_DIR holds one subdirectory per word (v_<id>, ama, ana, nibu, nibu_flat, bin, bun) as written by the WordRender runs of
work/b3-render-all.sh. Prints a table: per vowel Burg F1/F2/F3 at the vowel's steady part against Hillenbrand 1995 male means and
Peterson & Barney 1952 male means (docs/research/articulatory-vocal-tract-sources.md) and the Story tube-chain prediction
(tube_formants.py, 17.52 cm), then the nasal murmur, the stress cue and the b burst. It measures; it does not listen.
"""
import sys, os, json, wave, subprocess, argparse
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
CSV = os.path.join(HERE, "..", "..", "docs", "research", "data", "story2008-area-functions.csv")
# Male means, F1/F2/F3 Hz (docs/research/articulatory-vocal-tract-sources.md).
HILL = {"i": (343, 2323, 3001), "ih": (429, 2034, 2687), "e": (476, 2090, 2692), "eh": (588, 1803, 2604), "ae": (591, 1930, 2595),
        "uh": (621, 1181, 2548), "ah": (757, 1309, 2535), "aw": (656, 1023, 2521), "o": (498, 910, 2459), "oo_short": (469, 1123, 2435), "u": (380, 992, 2355)}
PB = {"i": (267, 2294, 2937), "ih": (392, 1993, 2569), "eh": (526, 1854, 2481), "ae": (664, 1727, 2420), "uh": (631, 1192, 2377),
      "ah": (718, 1091, 2442), "aw": (568, 836, 2403), "oo_short": (437, 1023, 2245), "u": (307, 876, 2239)}
STORY_IDS = {"i": "i", "ih": "ɪ", "e": "e", "eh": "ɛ", "ae": "æ", "uh": "ʌ", "ah": "ɑ", "aw": "ɔ", "o": "o", "oo_short": "ʊ", "u": "u"}

def wav(path):
    with wave.open(path) as w:
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(float) / 32768.0
        return x, w.getframerate()

def burg(path, t0, t1):
    out = subprocess.run([sys.executable, os.path.join(HERE, "burg_formants.py"), path, str(t0), str(t1), "--json"], capture_output=True, text=True, check=True).stdout
    j = json.loads(out)
    return [j[f"F{i}"]["median"] if j[f"F{i}"] else None for i in (1, 2, 3)]

def tube(vowel):
    out = subprocess.run([sys.executable, os.path.join(HERE, "tube_formants.py"), CSV, "--vowel", STORY_IDS[vowel], "--length-cm", "17.52"], capture_output=True, text=True).stdout
    nums = [float(tok) for line in out.splitlines() for tok in line.replace("=", " ").replace("Hz", " ").split() if tok.replace(".", "", 1).isdigit()]
    return out.strip().splitlines()[-1] if out.strip() else "?"

def rms(x, sr, a, b):
    s = x[int(a * sr):int(b * sr)]
    return float(np.sqrt((s ** 2).mean())) if len(s) else 0.0

def f0(x, sr, a, b):
    s = x[int(a * sr):int(b * sr)]; s = s - s.mean()
    lo, hi = sr // 400, sr // 60
    c = [np.dot(s[:-l], s[l:]) for l in range(lo, hi)]
    return sr / (lo + int(np.argmax(c)))

def pct(v, ref): return None if v is None else round(100.0 * (v - ref) / ref, 1)

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("dir"); ap.add_argument("--json"); a = ap.parse_args()
    res = {"vowels": {}}
    print("vowel  Burg F1/F2/F3 (window)   | Hillenbrand F1/F2 err%  | P&B F1/F2 err%   | tube chain")
    for v in HILL:
        p = os.path.join(a.dir, f"v_{v}", f"v_{v}.wav")
        if not os.path.exists(p): print(v, "MISSING"); continue
        f = burg(p, 0.07, 0.15)  # 180 ms vowel; the onset ramp is excluded
        h = HILL[v]; pb = PB.get(v)
        res["vowels"][v] = {"burg": f, "hillenbrand": h, "pb": pb, "err_hill_pct": [pct(f[0], h[0]), pct(f[1], h[1])], "err_pb_pct": [pct(f[0], pb[0]), pct(f[1], pb[1])] if pb else None}
        print(f"{v:9s}{f}  | {h[:2]} {res['vowels'][v]['err_hill_pct']} | {pb[:2] if pb else '-'} {res['vowels'][v]['err_pb_pct'] if pb else ''} | {tube(v)}")
    # nasal murmur: low first formant and weaker level than the flanking vowels
    for w, c in (("ama", "m"), ("ana", "n")):
        p = os.path.join(a.dir, w, f"{w}.wav")
        if not os.path.exists(p): continue
        x, sr = wav(p)
        seg = [(0, .18), (.18, .275), (.275, .455)]
        r = [rms(x, sr, *s) for s in seg]
        res[w] = {"rms": r, "murmur_db_re_vowel": round(20 * np.log10(r[1] / max(r[0], 1e-9)), 1), "burg_murmur": burg(p, .20, .26)}
        print(f"{w}: rms V/{c}/V {['%.4f' % q for q in r]} murmur {res[w]['murmur_db_re_vowel']} dB re first vowel; Burg of the murmur {res[w]['burg_murmur']}")
    # stress
    p0, p1 = os.path.join(a.dir, "nibu_flat", "nibu_flat.wav"), os.path.join(a.dir, "nibu", "nibu.wav")
    if os.path.exists(p0) and os.path.exists(p1):
        x0, sr = wav(p0); x1, _ = wav(p1)
        d = 0.18 * 220 / 160 - 0.18
        # vowel i spans 0.095-0.275 (flat) / 0.095-0.3225 (primary); compare steady parts
        rf, rp = rms(x0, sr, .15, .25), rms(x1, sr, .15, .25)
        ff, fp = f0(x0, sr, .15, .25), f0(x1, sr, .15, .25)
        res["stress"] = {"rms_flat": rf, "rms_primary": rp, "level_db": round(20 * np.log10(rp / rf), 2), "f0_flat": ff, "f0_primary": fp, "f0_ratio": round(fp / ff, 3),
                         "length_flat_s": len(x0) / sr, "length_primary_s": len(x1) / sr, "length_diff_s": round(len(x1) / sr - len(x0) / sr, 3), "expected_diff_s": round(d, 3)}
        print("stress:", res["stress"])
    # b release in bin: closure/ burst / vowel
    p = os.path.join(a.dir, "bin", "bin.wav")
    if os.path.exists(p):
        x, sr = wav(p)
        step = int(sr * 0.002); env = [(i / sr, float(np.abs(x[i:i + step]).max())) for i in range(0, len(x) - step, step)]
        res["bin_env_peak_per_2ms"] = [(round(t * 1000), round(v, 4)) for t, v in env[:70]]
        pk = max(v for _, v in env)
        print("bin: peak", round(pk, 4), "b closure (0-60 ms) max", round(max(v for t, v in env if t < .06), 4), "release 60-95 ms max", round(max(v for t, v in env if .06 <= t < .1), 4), "vowel 110-260 ms max", round(max(v for t, v in env if .11 <= t < .26), 4))
    if a.json: json.dump(res, open(a.json, "w"), indent=1, default=float)

if __name__ == "__main__": main()
