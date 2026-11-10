#!/usr/bin/env python3
"""inspect_clips.py <rawDir> <outDir> <tag>: the looking-at-it tool behind the v4 findings. Writes, into outDir,
void-<motif>-<tag>.png (spectrogram, waveform, 1 ms Hilbert envelope with detected onsets) for every Void clip, onsets-<tag>.json (onset list per Void clip, in the
full band and above 1.5 kHz), and nibu-<motif>-<tag>.png (spectrogram) plus nibu-spectrum-<tag>.png (invite vs mention spectrum) and nibu-<tag>.json
(energy above 4 kHz per note window, tail level before the next onset, image-band level). Reads the float WAVs build.mjs wrote (unit scale). Measurements, not verdicts."""
import sys, os, json
import numpy as np, soundfile as sf, matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from scipy.signal import spectrogram, hilbert, butter, sosfiltfilt
SR = 48000
MOTIFS = ["mention", "dm", "message", "invite"]
RHYTHM_MS = {"mention": [0], "message": [0, 230], "dm": [0, 240], "invite": [0, 200, 300]}
HP = butter(4, 1500, "hp", fs=SR, output="sos")
RISE_DB, FLOOR_DB, HP_RISE_DB, HP_FLOOR_DB = 6.0, -45.0, 10.0, -40.0   # an onset: 1 ms envelope up by RISE_DB over the quietest of the 15 ms before, above FLOOR re its band's peak

def env_db(x):
    a = np.abs(hilbert(x)); m = len(x) // 48; e = 20 * np.log10(a[:m * 48].reshape(m, 48).mean(1) + 1e-9); return e - e.max()
def onsets(x, rise, floor):
    e = env_db(x); out = []
    for i in range(1, len(e) - 1):
        lo = e[max(0, i - 15):i].min()
        if e[i] > floor and e[i] - lo >= rise and e[i] == e[max(0, i - 3):i + 4].max(): out.append((i, round(float(e[i] - lo), 1)))
    return out
def cluster(*lists):   # onsets within 20 ms of the previous one are one event (a note's own attack spans ~9 ms)
    allv = sorted(i for l in lists for i, _ in l); out = []
    for i in allv:
        if out and i - out[-1][-1] <= 20: out[-1].append(i)
        else: out.append([i])
    return [c[0] for c in out]
def panel(x, title, path, marks):
    fig, ax = plt.subplots(3, 1, figsize=(11, 8)); f, t, S = spectrogram(x, SR, nperseg=512, noverlap=480, nfft=2048)
    ax[0].pcolormesh(t * 1000, f, 10 * np.log10(S + 1e-14), vmin=-110, vmax=-30, shading="auto"); ax[0].set_ylim(0, 6000); ax[0].set_ylabel("Hz"); ax[0].set_title(title)
    ax[1].plot(np.arange(len(x)) / SR * 1000, x, lw=.5); ax[1].set_ylabel("amplitude (unit scale)")
    e = env_db(x); ax[2].plot(np.arange(len(e)), e, lw=1); ax[2].set_ylim(-60, 2); ax[2].set_ylabel("1 ms envelope, dB re peak"); ax[2].grid(alpha=.3); ax[2].set_xlabel("ms")
    for i in marks: ax[2].axvline(i, color="r", alpha=.5); ax[2].text(i, -8, f"{i} ms", color="r", fontsize=8)
    for a in ax[:2]: a.set_xlim(0, len(x) / 48)
    ax[2].set_xlim(0, len(x) / 48); fig.tight_layout(); fig.savefig(path, dpi=90); plt.close(fig)
def spec(x):
    S = np.abs(np.fft.rfft(x * np.hanning(len(x)), 32768)) ** 2; return np.fft.rfftfreq(32768, 1 / SR), S
def hf_db(x): f, S = spec(x); return float(10 * np.log10(S[f > 4000].sum() / S.sum() + 1e-20))

raw, out, tag = sys.argv[1:4]; os.makedirs(out, exist_ok=True)
ons = {}
for m in MOTIFS:
    x = sf.read(os.path.join(raw, f"void-{m}.wav"))[0]; full, hp = onsets(x, RISE_DB, FLOOR_DB), onsets(sosfiltfilt(HP, x), HP_RISE_DB, HP_FLOOR_DB); ev = cluster(full, hp)
    ons[m] = dict(noteMs=RHYTHM_MS[m], fullBand=full, above1500Hz=hp, events=ev); panel(x, f"void {m} ({tag}); red = onset events", os.path.join(out, f"void-{m}-{tag}.png"), ev)
json.dump(ons, open(os.path.join(out, f"onsets-{tag}.json"), "w"), indent=1)
nib = {}; men = sf.read(os.path.join(raw, "nibu-mention.wav"))[0]
for m in ["message", "dm", "invite"]:
    x = sf.read(os.path.join(raw, f"nibu-{m}.wav"))[0]; on = RHYTHM_MS[m]; ends = on[1:] + [len(x) / 48]; notes = []
    for i, (a, b) in enumerate(zip(on, ends)):
        s = x[int(a * 48):int(b * 48)]; notes.append(dict(startMs=a, endMs=round(b), above4kHzDb=round(hf_db(s), 1), last5msDbRePeak=round(float(20 * np.log10(np.sqrt((s[-240:] ** 2).mean()) / np.abs(s).max() + 1e-12)), 1)))
    f, S = spec(x); band = (f > 6500) & (f < 9500); w = int(on[1] * 48)   # note 1 against the single note (same pitch) over the same window
    nib[m] = dict(notes=notes, note1VsMention=dict(windowMs=on[1], peakHz=[round(float(spec(a)[0][spec(a)[1].argmax()])) for a in (x[:w], men[:w])], above4kHzDb=[round(hf_db(a), 1) for a in (x[:w], men[:w])]), imageBandPeakDbRePeak=round(float(10 * np.log10(S[band].max() / S.max())), 1), clipEvents=cluster(onsets(x, RISE_DB, FLOOR_DB)))
    fig, a = plt.subplots(1, 1, figsize=(10, 4)); ff, t, SS = spectrogram(x, SR, nperseg=1024, noverlap=900, nfft=4096)
    a.pcolormesh(t * 1000, ff, 10 * np.log10(SS + 1e-14), vmin=-120, vmax=-40, shading="auto"); a.set_ylim(0, 12000); a.set_xlabel("ms"); a.set_title(f"nibu (3) {m} ({tag}), 0-12 kHz"); fig.tight_layout(); fig.savefig(os.path.join(out, f"nibu-{m}-{tag}.png"), dpi=90); plt.close(fig)
f, S = spec(men); nib["mention"] = dict(imageBandPeakDbRePeak=round(float(10 * np.log10(S[(f > 6500) & (f < 9500)].max() / S.max())), 1))
fig, a = plt.subplots(1, 1, figsize=(10, 4))
for name, x in [("invite (3 notes)", sf.read(os.path.join(raw, "nibu-invite.wav"))[0]), ("mention (1 note)", men)]:
    f, S = spec(x); a.semilogx(f[1:], 10 * np.log10(S[1:] / S.max() + 1e-14), label=name, lw=.8)
a.set_xlim(100, 24000); a.set_ylim(-100, 3); a.legend(); a.grid(alpha=.3); a.set_xlabel("Hz"); a.set_title(f"nibu (3) full-clip spectrum, dB re its peak ({tag})"); fig.tight_layout(); fig.savefig(os.path.join(out, f"nibu-spectrum-{tag}.png"), dpi=90)
json.dump(nib, open(os.path.join(out, f"nibu-{tag}.json"), "w"), indent=1); print(json.dumps(dict(void={m: v["events"] for m, v in ons.items()}, nibuImageBandDb={m: v["imageBandPeakDbRePeak"] for m, v in nib.items()})))
