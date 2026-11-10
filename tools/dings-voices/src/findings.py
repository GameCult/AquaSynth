#!/usr/bin/env python3
"""findings.py <v2senders> <v3out> : the v2-vs-v3 numbers quoted by the README and page (void ends and tick, libby per-note level and tail, kiko onset and partials,
proposal distances). Reads decoded oggs; prints JSON."""
import sys, os, json, numpy as np, soundfile as sf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from metriclib import *
v2, v3 = sys.argv[1], sys.argv[2]
rd = lambda p: sf.read(p)[0]
db = lambda x: float(20 * np.log10(max(x, 1e-12)))
out = {}
# Void: last 50 ms, and the tick (largest 1-sample step over the clip, as a multiple of the median step in the 20 ms before the tick)
void = {}
for m in ["mention", "dm", "message", "invite"]:
    row = {}
    for tag, root in [("v2", v2 + "/void"), ("v3", v3 + "/senders/void")]:
        z = rd(f"{root}/{m}.ogg"); k = SR // 20; t = z[-k:]; step = np.abs(np.diff(z)); j = int(np.argmax(step[SR // 20:])) + SR // 20
        base = np.median(step[max(0, j - SR // 50):j]) if j > 0 else 1e-9
        row[tag] = dict(last50TruePeakDbTP=round(true_peak_db(t), 2), last50ClippedSamples=int(np.sum(np.abs(t) >= 0.999)), clipClippedSamples=int(np.sum(np.abs(z) >= 0.999)),
                        clipTruePeakDbTP=round(true_peak_db(z), 2), maxStepAtMs=round(j / 48, 1), maxStepVsMedianStepX=round(float(step[j] / max(base, 1e-12)), 1))
    void[m] = row
out["void"] = void
# Libby: per-note peak and the level of the previous note when the next lands, plus note-1 first-120ms centroid
ons = {"mention": [0], "message": [0, 230], "dm": [0, 240], "invite": [0, 200, 300]}; lib = {}
for m in ons:
    row = {}
    for tag, root in [("v2", v2 + "/libby"), ("v3", v3 + "/senders/libby")]:
        z = rd(f"{root}/{m}.ogg"); notes = []
        for j, o in enumerate(ons[m]):
            seg = z[o * 48:(o + 120) * 48]; X = np.abs(np.fft.rfft(seg * np.hanning(len(seg)), 8192)) ** 2; f = np.fft.rfftfreq(8192, 1 / SR)
            prior = round(db(np.max(np.abs(z[(o - 10) * 48:o * 48])) / np.max(np.abs(z))), 1) if o else None
            notes.append(dict(onsetMs=o, peakDbFS=round(db(np.max(np.abs(seg))), 1), centroidHz=round(float(np.sum(f * X) / np.sum(X))), priorTailDbReClipPeak=prior))
        row[tag] = dict(durationMs=round(1000 * len(z) / SR, 1), notes=notes)
    lib[m] = row
out["libby"] = lib
# Kiko: onset rise (mention), first-5-ms envelope, partial ratios from the first 60 ms spectrum of the mention, decay of each partial
def partials(z):
    seg = z[int(0.004 * SR):int(0.20 * SR)]; N = 1 << 17; X = np.abs(np.fft.rfft(seg * np.hanning(len(seg)), N)); f = np.fft.rfftfreq(N, 1 / SR)
    ps = []; used = np.zeros(len(X), bool)
    for _ in range(4):
        m = np.where(used, 0, X); i = int(np.argmax(m)); 
        if m[i] < X.max() * 10 ** (-50 / 20): break
        ps.append((float(f[i]), db(X[i] / X.max()))); used[max(0, i - 400):i + 400] = True
    ps.sort(); return [dict(hz=round(h, 1), ratio=round(h / ps[0][0], 3), dbRe1stPartial=round(a - ps[0][1], 1)) for h, a in ps]
k = {}
for tag, root in [("v2", v2 + "/kiko"), ("v3", v3 + "/senders/kiko")]:
    z = rd(f"{root}/mention.ogg"); k[tag] = dict(onsetRiseMs=round(onset_rise_ms(z), 2), onsetMs=round(onset_time_ms(z), 1), centroidHz=round(spectral(z)[0]), partials=partials(z))
out["kiko"] = k
# fundamentals of the bell/glass/FM families, same measure (peaks in the first 200 ms of each mention)
fam = {}
for name, root in [("libby", v3 + "/senders/libby"), ("kiko", v3 + "/senders/kiko"), ("nibu2", v3 + "/proposals/nibu2")]:
    z = rd(f"{root}/mention.ogg"); fam[name] = dict(onsetRiseMs=round(onset_rise_ms(z), 2), partials=partials(z))
out["families"] = fam
print(json.dumps(out, indent=1))
