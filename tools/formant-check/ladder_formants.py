#!/usr/bin/env python3
"""Impulse-response formants of the AquaSynth Kelly-Lochbaum ladder, in numpy, for the Story 2008 area functions. It copies the update
equations of FaustExport.VocalLadder.cs (glottal reflection .75, per-cell loss, one cell per sample, the lip reflection a constant) so
that the ladder's formants can be compared with tube_formants.py (frequency domain, frequency-dependent radiation) without Faust.

  ladder_formants.py CSV [--cells 24] [--lip-reflection -0.82] [--loss 0.998] [--vowel V]
Prints the first three spectral peaks above 150 Hz at 48 kHz. Unheard: this says where resonances sit, not how a vowel sounds.
"""
import argparse, csv
import numpy as np

def load(path):
    rows = [l for l in open(path, encoding='utf-8') if not l.startswith('#')]
    r = list(csv.reader(rows))
    return {h: np.array([float(x[j + 1]) for x in r[1:] if x[0].isdigit()]) for j, h in enumerate(r[0][1:])}

def at(a, p):  # linear in area between section centres, as StoryVowelAreas.AreaAt
    x = np.clip(p * len(a) - 0.5, 0, len(a) - 1); l = int(np.floor(x)); r = min(len(a) - 1, l + 1)
    return a[l] + (a[r] - a[l]) * (x - l)

def impulse(areas, lip, loss, glot=0.75, n=1 << 14):
    N = len(areas)
    k = [0] + [(areas[i - 1] - areas[i]) / (areas[i - 1] + areas[i]) for i in range(1, N)]
    R = np.zeros(N); L = np.zeros(N); out = np.zeros(n)
    for t in range(n):
        Rn = np.zeros(N); Ln = np.zeros(N)
        Rn[0] = (L[0] * glot + (1.0 if t == 0 else 0.0)) * loss
        for i in range(1, N): Rn[i] = (R[i - 1] - k[i] * (R[i - 1] + L[i])) * loss
        for i in range(N - 1): Ln[i] = (L[i + 1] + k[i + 1] * (R[i] + L[i + 1])) * loss
        Ln[N - 1] = R[N - 1] * lip * loss
        R, L = Rn, Ln; out[t] = R[N - 1]
    return out

def peaks(out, fs=48000, n=3):
    S = np.abs(np.fft.rfft(out)); f = np.fft.rfftfreq(len(out), 1 / fs)
    idx = [i for i in range(1, len(S) - 1) if S[i] > S[i - 1] and S[i] >= S[i + 1] and f[i] > 150 and S[i] > 0.05 * S.max()]
    return [round(f[i]) for i in idx][:n]

if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('csv'); p.add_argument('--cells', type=int, default=24)
    p.add_argument('--lip-reflection', type=float, default=-0.82); p.add_argument('--loss', type=float, default=0.998)
    p.add_argument('--vowel')
    a = p.parse_args()
    for v, areas in load(a.csv).items():
        if a.vowel and v != a.vowel: continue
        cells = np.array([at(areas, (i + .5) / a.cells) for i in range(a.cells)])
        print(v, peaks(impulse(cells, a.lip_reflection, a.loss)))
