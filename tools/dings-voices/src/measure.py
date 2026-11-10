#!/usr/bin/env python3
"""measure.py <sendersDir> : per-clip duration, onset, true peak and end metrics of decoded oggs (<dir>/<sender>/<motif>.ogg) as JSON on stdout."""
import sys, os, json, numpy as np, soundfile as sf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from metriclib import *
SENDERS = ["aqua", "eureka", "heimdall", "kiko", "libby", "nibu", "void", "weksa"]; MOTIFS = ["mention", "dm", "message", "invite"]
rows = []
for s in SENDERS:
    for m in MOTIFS:
        z, sr = sf.read(os.path.join(sys.argv[1], s, f"{m}.ogg")); assert sr == SR
        rows.append(dict(sender=s, motif=m, durationMs=round(1000 * len(z) / SR, 1), onsetMs=round(onset_time_ms(z), 1), truePeakDbTP=round(true_peak_db(z), 2), **end_metrics(z)))
print(json.dumps(rows))
