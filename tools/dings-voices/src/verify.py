#!/usr/bin/env python3
"""verify.py: checks the staged set against the brief's limits and the manifest schema, from the files on disk (not from metrics.json)."""
import json, os, re, sys, itertools
import numpy as np, soundfile as sf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
root = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SENDERS = ["aqua", "eureka", "heimdall", "kiko", "libby", "nibu", "void", "weksa"]; MOTIFS = ["mention", "dm", "message", "invite"]
PATH = re.compile(r"^[A-Za-z0-9_-]+(/[A-Za-z0-9_.-]+)*\.(ogg|mp3)$")
man = json.load(open(os.path.join(root, "out", "manifest.json"))); bad = []
assert set(man) == {"neutral", "senders"} and set(man["senders"]) == set(SENDERS)
for grp in [man["neutral"], *man["senders"].values()]:
    assert set(grp) == set(MOTIFS)
    for p in grp.values():
        if not PATH.match(p) or not os.path.exists(os.path.join(root, "out", p)): bad.append(("path", p))
def _unused(x, k=8):
    X = np.fft.rfft(x); Y = np.zeros(len(x) * k // 2 + 1, dtype=complex); Y[: len(X)] = X; return 20 * np.log10(np.max(np.abs(np.fft.irfft(Y, len(x) * k) * k)))
laes = []; SR = 48000
from metriclib import lae, true_peak_db, end_metrics
for s in SENDERS:
    for m in MOTIFS:
        x, sr = sf.read(os.path.join(root, "out", man["senders"][s][m])); info = sf.info(os.path.join(root, "out", man["senders"][s][m]))
        if sr != 48000 or x.ndim != 1 or info.format != "OGG" or info.subtype != "VORBIS": bad.append(("format", s, m))
        if true_peak_db(x) >= -1.0: bad.append(("truepeak", s, m, true_peak_db(x)))
        if m == "message" and 1000 * len(x) / sr > 400: bad.append(("message>400ms", s, 1000 * len(x) / sr))
        if len(x) / sr > 0.6: bad.append(("over 600ms", s, m))
        em = end_metrics(x)
        if em['clipped'] or em['tailDb'] > -30 or abs(em['dcPct']) > 1: bad.append(('end', s, m, em))
        laes.append(lae(x))
for p in ["nibu1", "nibu2", "nibu3"]:
    for m in MOTIFS:
        x, sr = sf.read(os.path.join(root, "out", "proposals", p, f"{m}.ogg")); em = end_metrics(x)
        if sr != 48000 or x.ndim != 1 or true_peak_db(x) >= -1.0 or (m == "message" and len(x) / sr > 0.4) or len(x) / sr > 0.6 or em['clipped'] or em['tailDb'] > -30 or abs(em['dcPct']) > 1: bad.append(("proposal", p, m, em))
        laes.append(lae(x))
print("loudness spread (dB):", round(max(laes) - min(laes), 2), "| files checked:", len(laes))
if max(laes) - min(laes) > 0.5: bad.append(("loudness spread", max(laes) - min(laes)))
print("FAIL" if bad else "OK", bad); sys.exit(1 if bad else 0)
