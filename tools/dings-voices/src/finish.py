#!/usr/bin/env python3
"""finish.py <rawDir> <outDir>: loudness-match, encode ogg (Vorbis, 48 kHz mono), decode back, measure, write manifest + metrics.
Needs numpy and soundfile (libsndfile has Vorbis); no oggenc/ffmpeg on the host. Metrics are not verdicts; nothing here was heard."""
import sys, os, json, itertools
import numpy as np, soundfile as sf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from metriclib import *

SR = 48000
SENDERS = ["aqua", "eureka", "heimdall", "kiko", "libby", "nibu", "void", "weksa"]
MOTIFS = ["mention", "dm", "message", "invite"]
PEAK_LIMIT_DBTP = -1.0          # brief: true peak under -1 dBTP (measured on the DECODED ogg)
TARGET_PEAK_DBTP = -9.0         # the loudest clip is placed here, so there is 8 dB of headroom before the limit
raw, out = sys.argv[1], sys.argv[2]

os.makedirs(out, exist_ok=True)
clips = {}
for s in SENDERS:
    for m in MOTIFS:
        x, sr = sf.read(os.path.join(raw, f"{s}-{m}.wav")); assert sr == SR and x.ndim == 1
        clips[(s, m)] = x
# One loudness target: every clip is scaled so its A-weighted exposure equals the target. The target is the highest value at which
# the clip with the highest peak-to-exposure ratio lands at TARGET_PEAK_DBTP (so no clip exceeds it).
need = {k: TARGET_PEAK_DBTP - (true_peak_db(x) - lae(x)) for k, x in clips.items()}   # exposure target that puts clip k's true peak at TARGET_PEAK_DBTP
target = min(need.values()); limiting = min(need, key=need.get)
rows = []; dec = {}
for (s, m), x in clips.items():
    g = 10 ** ((target - lae(x)) / 20); y = x * g
    d = os.path.join(out, "senders", s); os.makedirs(d, exist_ok=True)
    path = os.path.join(d, f"{m}.ogg"); sf.write(path, y.astype(np.float32), SR, format="OGG", subtype="VORBIS")
    z, sr2 = sf.read(path); assert sr2 == SR and z.ndim == 1; dec[(s, m)] = z
    cen, flat = spectral(z)
    rows.append(dict(sender=s, motif=m, file=f"senders/{s}/{m}.ogg", bytes=os.path.getsize(path), durationMs=round(1000 * len(z) / SR, 1),
        laeDb=round(lae(z), 2), lafmaxDb=round(lafmax(z), 2), samplePeakDb=round(20 * np.log10(np.max(np.abs(z))), 2), truePeakDbTP=round(true_peak_db(z), 2),
        onsetRiseMs=round(onset_rise_ms(z), 2) if m == "mention" else None, onsetMs=round(onset_time_ms(z), 1), **end_metrics(z), centroidHz=round(cen), flatnessDb=round(10 * np.log10(flat), 1)))
# neutral fallback, measured as shipped (not modified)
neutral = []
for m in MOTIFS:
    z, sr3 = sf.read(os.path.join(out, "neutral", f"{m}.ogg")); z = z if z.ndim == 1 else z[:, 0]
    neutral.append(dict(motif=m, durationMs=round(1000 * len(z) / sr3, 1), laeDb=round(lae(z), 2), truePeakDbTP=round(true_peak_db(z), 2), onsetRiseMs=round(onset_rise_ms(z), 2) if m == "mention" else None, centroidHz=round(spectral(z)[0]), flatnessDb=round(10 * np.log10(spectral(z)[1]), 1)))
# pair distinctness, per motif, over decoded clips
pairs = []
for m in MOTIFS:
    for a, b in itertools.combinations(SENDERS, 2):
        pairs.append(dict(motif=m, a=a, b=b, distDb=round(spec_dist(dec[(a, m)], dec[(b, m)]), 2)))
manifest = {"neutral": {m: f"neutral/{m}.ogg" for m in MOTIFS}, "senders": {s: {m: f"senders/{s}/{m}.ogg" for m in MOTIFS} for s in SENDERS}}
json.dump(manifest, open(os.path.join(out, "manifest.json"), "w"), indent=2)
summary = dict(targetLaeDb=round(target, 2), limitingClip=f"{limiting[0]}/{limiting[1]}", targetPeakDbTP=TARGET_PEAK_DBTP, peakLimitDbTP=PEAK_LIMIT_DBTP, heard=False,
               maxTruePeakDbTP=max(r["truePeakDbTP"] for r in rows), laeSpreadDb=round(max(r["laeDb"] for r in rows) - min(r["laeDb"] for r in rows), 2),
               lafmaxSpreadWithinMotifDb={m: round(max(r["lafmaxDb"] for r in rows if r["motif"] == m) - min(r["lafmaxDb"] for r in rows if r["motif"] == m), 2) for m in MOTIFS},
               maxMessageMs=max(r["durationMs"] for r in rows if r["motif"] == "message"), maxAnyMs=max(r["durationMs"] for r in rows))
json.dump(dict(summary=summary, clips=rows, neutral=neutral, pairs=pairs), open(os.path.join(out, "..", "metrics.json"), "w"), indent=1)
print(json.dumps(summary, indent=1))
