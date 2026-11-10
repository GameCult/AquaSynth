#!/usr/bin/env python3
"""aqua-ear: audio perception tool. Measures signal facts in a wav/ogg/flac and draws pictures a vision model can read.
It does not hear anything: no detector is a verdict of good or bad. See README.md."""
import argparse, json, os, sys, tempfile

os.environ.setdefault("MPLCONFIGDIR", os.path.join(tempfile.gettempdir(), "aqua-ear-mpl"))
import numpy as np
import soundfile as sf
import scipy.signal as ss
from scipy.ndimage import uniform_filter1d
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

# ---- thresholds: every detector's boundary lives here, stated in the report evidence ---------------------------------
TH = dict(
    floor_dbfs=-60.0,        # onsets below this absolute level are ignored
    rel_floor_db=-45.0,      # ... and below this many dB under the file peak
    onset_rise_db=4.0,       # envelope must rise this far above its recent trough to count as an onset
    trough_window_ms=20,     # how far back the trough is looked for
    flam_gap_ms=80.0,        # two onsets closer than this may be a flam/double hit
    flam_balance_db=20.0,    # ... and the two onsets' peaks are within this many dB (a much quieter first onset is a precursor, reported not fired)
    one_note_db=15.0,        # ... if the envelope between them never fell more than this below the first onset's peak
    dc_abs=0.005,            # |mean| at or above this is a DC offset (-46 dBFS)
    step_abs=0.02,           # a discontinuity must jump at least this much (second difference, -34 dBFS) ...
    step_ratio=25.0,         # ... and be this many times the local mean |second difference| (+-2.5 ms)
    clip_run=3,              # consecutive plateau samples at the ceiling that make a clipped run
    clip_level=0.99,         # plateau must be at or above this |x|
    tp_limit_dbtp=0.0,       # true peak above this is an inter-sample over
    overlap_gap_ms=80.0,     # onsets this far apart or more are separate notes ...
    overlap_tail_db=-40.0,   # ... and overlap if the previous note's tail is above this many dB re its peak at the new onset
    end_decay_db=6.0,        # ... unless it has fallen at least this far in the preceding 9 ms (a fade)
    end_tail_db=-40.0,       # abrupt end: the last 3 ms is louder than this many dB re file peak ...
    end_abs_dbfs=-70.0,      # ... and louder than this absolute level
    hf_cut_hz=2000.0,        # transient channel: onsets also detected on the signal above this (4th-order Butterworth, zero-phase)
    hf_floor_dbfs=-75.0,     # ... ignoring anything whose high band peaks below this
    match_ms=3.0,            # compare mode: onsets within this are the same onset
)
FONT = 15
plt.rcParams.update({"font.size": FONT, "axes.titlesize": FONT + 3, "axes.labelsize": FONT + 1,
                     "xtick.labelsize": FONT, "ytick.labelsize": FONT, "legend.fontsize": FONT - 1,
                     "axes.grid": True, "grid.alpha": 0.25})

def db(x): return 20 * np.log10(np.maximum(x, 1e-9))

# ---- loading ----------------------------------------------------------------------------------------------------------
def load(path):
    x, sr = sf.read(path, dtype="float64", always_2d=True)
    ch = x.shape[1]
    return x.mean(axis=1), sr, ch

# ---- ported from tools/aqua-feel/metrics.js (JS lines ported, not called: keeps this tool one language, no node at run time)
def true_peak(x, sr):
    """4x oversampled true peak, same Hann-windowed 48-tap sinc as metrics.js truePeak (linear). Returns (tp, sample_peak)."""
    T = 12
    k = np.arange(2 * T) - T + 1
    peak = float(np.max(np.abs(x))) if len(x) else 0.0
    tp = peak
    for ph in (0.25, 0.5, 0.75):
        t = k - ph
        h = np.sinc(t) * (0.5 + 0.5 * np.cos(np.pi * t / T))
        y = _corr(x, h, T)  # metrics.js: y[n] = sum_i x[n+i-T+1] h[i]
        tp = max(tp, float(np.max(np.abs(y))) if len(y) else 0.0)
    return tp, peak

def _corr(x, h, T):
    full = np.correlate(x, h, mode="full")  # full[j] = sum_i x[j-(len(h)-1)+i] h[i], so y[n] = full[n+T]
    return full[T:T + len(x)]

K1 = ([1.53512485958697, -2.69169618940638, 1.19839281085285], [1, -1.69065929318241, 0.73248077421585])
K2 = ([1, -2, 1], [1, -1.99004745483398, 0.99007225036621])

def loudness_lufs(x, sr):
    """BS.1770-4 integrated, mono, as metrics.js integratedLoudness. Coefficients are 48 kHz, so other rates are resampled."""
    if sr != 48000:
        from math import gcd
        g = gcd(48000, sr); x = ss.resample_poly(x, 48000 // g, sr // g)
    k = ss.lfilter(*K2, ss.lfilter(*K1, x))
    B, H = 19200, 4800
    n = max(1, int(np.ceil(max(0, len(k) - B) / H)) + 1)
    ms = []
    for i in range(n):
        seg = k[i * H:i * H + B]
        ms.append(float(np.sum(seg * seg)) / B)
    ms = np.array(ms)
    l = lambda e: -0.691 + 10 * np.log10(np.maximum(e, 1e-30))
    p1 = ms[l(ms) > -70]
    if not len(p1): return None
    rel = l(p1.mean()) - 10
    p2 = p1[l(p1) > rel]
    return float(l(p2.mean())) if len(p2) else None

# ---- envelope and onsets ------------------------------------------------------------------------------------------------
def envelope_1ms(x, sr):
    """Hilbert magnitude, 1 ms moving average, then max per 1 ms block: env_db[k] is the level at k ms. Ripple-free for tones."""
    if len(x) == 0: return np.array([-180.0])
    pad = np.zeros(int(sr * 0.05))   # zero-pad: the FFT Hilbert is circular and rings at both ends otherwise
    a = np.abs(ss.hilbert(np.concatenate([pad, x, pad])))[len(pad):len(pad) + len(x)]
    a = uniform_filter1d(a, max(1, int(round(sr * 0.001))), mode="nearest")
    blk = sr / 1000.0
    nb = int(np.ceil(len(a) / blk))
    idx = np.minimum((np.arange(len(a)) / blk).astype(int), nb - 1)
    env = np.zeros(nb); np.maximum.at(env, idx, a)
    return db(env)

def detect_onsets(env_db, peak_db, abs_floor=None):
    """Rise detector on the 1 ms envelope. Onset = first ms (within 10 ms before the trigger) at trough+2 dB or the floor."""
    floor = max(TH["floor_dbfs"] if abs_floor is None else abs_floor, peak_db + TH["rel_floor_db"])
    W, rise = TH["trough_window_ms"], TH["onset_rise_db"]
    out, in_event, ev_peak, since, low = [], False, -1e9, 0, 0
    for i in range(len(env_db)):
        e = env_db[i]
        if in_event:
            low = low + 1 if e - env_db[i - 1] < rise / W else 0   # rise/W = 0.2 dB/ms: slower than that cannot reach a trigger inside the trough window
            if low >= 2: in_event, since = False, i - 1             # the event is over once the envelope has stopped rising for 2 ms
            continue
        lo = max(since, i - W)
        trough = max(env_db[lo:i + 1].min(), floor)
        if e >= floor and e - trough >= rise:
            lvl = trough + 2.0
            j = i
            while j > max(lo, i - 10) and env_db[j - 1] >= lvl: j -= 1
            out.append(j); in_event, ev_peak, low = True, e, 0
    return out

def onset_table(env_db, onsets, band="full"):
    rows = []
    for n, t in enumerate(onsets):
        nxt = onsets[n + 1] if n + 1 < len(onsets) else len(env_db)
        seg = env_db[t:min(nxt, t + 80) + 1]
        rows.append(dict(band=band, ms=int(t), peak_dbfs=round(float(seg.max()), 1), peak_ms=int(t + seg.argmax()),
                         level_at_onset_dbfs=round(float(env_db[t]), 1)))
    return rows

# ---- detectors: each returns dict(name, fired, verdict, evidence) -----------------------------------------------------------
def det_flam(env_db, rows, band="full"):
    """Pairs of onsets in one band closer than flam_gap_ms. 'One note' is judged on the full-band envelope between them."""
    tag = "" if band == "full" else f"[{band}] "
    pairs = []
    for a, b in zip(rows, rows[1:]):
        gap = b["ms"] - a["ms"]
        if gap >= TH["flam_gap_ms"]: continue
        between = env_db[a["ms"]:b["ms"] + 1]
        ref = a["peak_dbfs"] if band == "full" else float(between.max())
        floor_db = float(between.min()) - ref
        diff = round(abs(a["peak_dbfs"] - b["peak_dbfs"]), 1)
        pairs.append(dict(band=band, first_ms=a["ms"], second_ms=b["ms"], gap_ms=gap, min_between_db_re_first_peak=round(floor_db, 1),
                          one_note=bool(floor_db >= -TH["one_note_db"]), peak_difference_db=diff, comparable=bool(diff <= TH["flam_balance_db"])))
    hits = [p for p in pairs if p["one_note"] and p["comparable"]]
    if hits:
        verdict = "FIRES double hit / flam: " + "; ".join(f"{tag}onsets at {p['first_ms']} and {p['second_ms']} ms, {p['gap_ms']} ms apart, level between never below {p['min_between_db_re_first_peak']} dB, peaks {p['peak_difference_db']} dB apart" for p in hits)
    elif pairs:
        verdict = f"no flam: {tag}close onsets exist but " + "; ".join(
            (f"onsets at {p['first_ms']} and {p['second_ms']} ms differ by {p['peak_difference_db']} dB in peak (precursor or grace swell, not a second hit)" if p["one_note"]
             else f"the envelope fell to {p['min_between_db_re_first_peak']} dB between {p['first_ms']} and {p['second_ms']} ms (separate notes)") for p in pairs)
    else:
        verdict = f"no flam: {tag}no two onsets closer than {TH['flam_gap_ms']:g} ms ({len(rows)} onset(s))"
    return dict(name="double_hit", fired=bool(hits), verdict=verdict, evidence=pairs)

def det_click_dc(x, sr):
    m = float(np.mean(x)) if len(x) else 0.0
    dc_fire = abs(m) >= TH["dc_abs"]
    e = np.abs(x[2:] - 2 * x[1:-1] + x[:-2]) if len(x) > 3 else np.zeros(0)
    ev = []
    if len(e):
        w = max(3, int(sr * 0.005) | 1)
        loc = uniform_filter1d(e, w, mode="nearest")
        cand = np.where((e >= TH["step_abs"]) & (e >= TH["step_ratio"] * loc))[0]
        groups = []
        for c in cand:
            if groups and c - groups[-1][-1] <= 2: groups[-1].append(c)
            else: groups.append([c])
        h = max(2, int(sr * 0.002))
        for g in groups[:50]:
            c = g[int(np.argmax(e[g]))] + 1  # sample index of the middle of the second difference
            before = x[max(0, c - h):c - 1]; after = x[c + 1:c + 1 + h]
            lvl = float(after.mean() - before.mean()) if len(before) and len(after) else 0.0
            jump = float(abs(x[c] - x[c - 1]))
            kind = "step" if abs(lvl) >= 0.5 * jump else "click"
            ev.append(dict(ms=round(c * 1000 / sr, 2), kind=kind, jump=round(jump, 4), level_shift=round(lvl, 4)))
    fired = dc_fire or bool(ev)
    parts = []
    parts.append(f"DC offset {m:+.5f} ({db(abs(m)):.1f} dBFS) {'AT/ABOVE' if dc_fire else 'below'} {TH['dc_abs']} limit")
    parts.append(f"{len(ev)} discontinuity(ies)" + (": " + ", ".join(f"{d['kind']} at {d['ms']} ms jump {d['jump']}" for d in ev[:6]) if ev else ""))
    return dict(name="click_step_dc", fired=fired, verdict=("FIRES " if fired else "none: ") + "; ".join(parts) +
                f"  [assumption: discontinuity = second difference >= {TH['step_abs']} and >= {TH['step_ratio']:g}x its local mean]",
                evidence=dict(dc=m, discontinuities=ev))

def det_clip(x, sr):
    tp, sp = true_peak(x, sr)
    over = int(np.sum(np.abs(x) > 1.0))
    runs, run = 0, 0
    ax = np.abs(x)
    for i in range(len(x)):
        if ax[i] >= TH["clip_level"] and (run == 0 or abs(x[i] - x[i - 1]) < 1e-6 and ax[i - 1] >= TH["clip_level"]):
            run += 1
            if run == TH["clip_run"]: runs += 1
        else:
            run = 1 if ax[i] >= TH["clip_level"] else 0
    tpdb = float(db(tp))
    fired = over > 0 or runs > 0 or tpdb > TH["tp_limit_dbtp"]
    v = (f"sample peak {db(sp):.2f} dBFS, true peak {tpdb:.2f} dBTP, {over} sample(s) over 1.0, {runs} clipped plateau run(s) "
         f"of >={TH['clip_run']} equal samples >= {TH['clip_level']}")
    return dict(name="clipping_true_peak", fired=fired, verdict=("FIRES " if fired else "none: ") + v +
                f"  [fires on any over, any plateau run, or true peak > {TH['tp_limit_dbtp']:g} dBTP]",
                evidence=dict(sample_peak_dbfs=float(db(sp)), true_peak_dbtp=tpdb, over_samples=over, plateau_runs=runs))

def det_overlap(env_db, rows):
    hits, near = [], []
    for a, b in zip(rows, rows[1:]):
        gap = b["ms"] - a["ms"]
        if gap < TH["overlap_gap_ms"]: continue
        tail = float(env_db[max(0, b["ms"] - 5):b["ms"] + 1].min()) - a["peak_dbfs"]   # lowest level in the 5 ms before the new onset
        (hits if tail > TH["overlap_tail_db"] else near).append(dict(prev_ms=a["ms"], next_ms=b["ms"], gap_ms=gap, tail_db_re_prev_peak=round(tail, 1)))
    if hits:
        v = "FIRES overlapping tails: " + "; ".join(f"onset at {h['next_ms']} ms arrives while the note from {h['prev_ms']} ms is still at {h['tail_db_re_prev_peak']} dB re its peak" for h in hits)
    else:
        v = f"none: {len(near)} later onset(s) arrived on a tail below {TH['overlap_tail_db']:g} dB" if near else "none: fewer than two separate notes"
    return dict(name="overlapping_tails", fired=bool(hits), verdict=v + f"  [assumption: separate notes >= {TH['overlap_gap_ms']:g} ms apart; overlap = tail above {TH['overlap_tail_db']:g} dB re previous peak (lowest level in the 5 ms before the new onset)]", evidence=hits + near)

def det_end(x, sr, peak):
    n = max(1, int(sr * 0.003))
    tail = float(np.max(np.abs(x[-n:]))) if len(x) else 0.0
    ref = float(np.max(np.abs(x[-4 * n:-3 * n]))) if len(x) >= 4 * n else tail   # the 3 ms window ending 9 ms before the end
    drop = float(db(ref) - db(tail))
    last = float(x[-1]) if len(x) else 0.0
    rel = float(db(tail) - db(peak))
    fired = peak > 0 and rel > TH["end_tail_db"] and db(tail) > TH["end_abs_dbfs"] and drop < TH["end_decay_db"]
    v = f"last 3 ms peak {db(tail):.1f} dBFS ({rel:.1f} dB re file peak), {drop:.1f} dB below the same window 9 ms earlier, final sample {last:+.5f}"
    return dict(name="abrupt_end", fired=bool(fired), verdict=("FIRES abrupt end cut: " if fired else "none: ") + v +
                f"  [assumption: abrupt if the last 3 ms is above {TH['end_tail_db']:g} dB re file peak and above {TH['end_abs_dbfs']:g} dBFS and has not fallen {TH['end_decay_db']:g} dB since 9 ms earlier]",
                evidence=dict(tail_dbfs=float(db(tail)), tail_db_re_peak=rel, drop_db=drop, final_sample=last))

# ---- analysis ---------------------------------------------------------------------------------------------------------------
def analyze(path):
    x, sr, ch = load(path)
    tp, sp = true_peak(x, sr)
    env = envelope_1ms(x, sr)
    peak_db = float(env.max())
    onsets = detect_onsets(env, peak_db) if sp > 1e-6 else []
    rows = onset_table(env, onsets)
    hf_rows = []
    if sr / 2 > TH["hf_cut_hz"] * 1.2 and sp > 1e-6:
        hf = ss.sosfiltfilt(ss.butter(4, TH["hf_cut_hz"], "highpass", fs=sr, output="sos"), x)
        env_hf = envelope_1ms(hf, sr)
        if env_hf.max() > TH["hf_floor_dbfs"]:
            hf_rows = onset_table(env_hf, detect_onsets(env_hf, float(env_hf.max()), TH["hf_floor_dbfs"]), "hf")
    f_full, f_hf = det_flam(env, rows), det_flam(env, hf_rows, "hf")
    flam = dict(name="double_hit", fired=f_full["fired"] or f_hf["fired"], evidence=f_full["evidence"] + f_hf["evidence"],
                verdict=f_full["verdict"] + " | " + f_hf["verdict"] + f"  [assumption: one note = full-band level between the two onsets never below -{TH['one_note_db']:g} dB re the first (hf: re the level there); peaks within {TH['flam_balance_db']:g} dB; hf = above {TH['hf_cut_hz']:g} Hz]")
    dets = [flam, det_click_dc(x, sr), det_clip(x, sr), det_overlap(env, rows), det_end(x, sr, sp)]
    lufs = loudness_lufs(x, sr)
    info = dict(file=os.path.basename(path), sample_rate=sr, channels=ch, duration_ms=round(len(x) * 1000 / sr, 1),
                sample_peak_dbfs=round(float(db(sp)), 2), true_peak_dbtp=round(float(db(tp)), 2),
                loudness_lufs=None if lufs is None else round(lufs, 2), rms_dbfs=round(float(db(np.sqrt(np.mean(x * x)))), 2) if len(x) else None)
    return dict(info=info, onsets=rows, onsets_hf=hf_rows, detectors=dets, thresholds=TH), x, sr, env

# ---- pictures ---------------------------------------------------------------------------------------------------------------
def stft_db(x, sr, nperseg, hop):
    w = ss.windows.hann(nperseg)
    f, t, Z = ss.stft(x, sr, window=w, nperseg=nperseg, noverlap=nperseg - hop, boundary="zeros", padded=True)
    mag = np.abs(Z) * 2   # scipy's default 'spectrum' scaling is 1/sum(window), so a full-scale sine reads 0 dBFS
    return f, t * 1000.0, db(mag)

def draw_spec(ax, x, sr, nperseg, hop, fmin=40, label=True):
    f, tms, S = stft_db(x, sr, nperseg, hop)
    keep = f >= fmin
    top = S.max()
    m = ax.pcolormesh(tms, f[keep], S[keep], shading="auto", cmap="magma", vmin=top - 80, vmax=top)
    ax.set_xlim(0, len(x) * 1000 / sr)
    ax.set_yscale("log"); ax.set_ylim(fmin, sr / 2)
    ticks = [t for t in (50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000) if fmin <= t <= sr / 2]
    ax.set_yticks(ticks); ax.set_yticklabels([f"{t/1000:g}k" if t >= 1000 else str(t) for t in ticks])
    ax.grid(False)
    if label: ax.set_ylabel("frequency (Hz, log)")
    return m

COL = {"spec": {"full": "#00e5ff", "hf": "#ffd400"}, "plain": {"full": "#c0392b", "hf": "#d35400"}}
def items(rep): return sorted([(o["ms"], o["band"]) for o in rep["onsets"] + rep.get("onsets_hf", [])])

def mark_onsets(ax, its, bg="spec", lo=None, hi=None):
    """its: [(ms, band)]. Dashed line plus a big boxed label per onset; hf (transient channel) onsets are yellow/orange and say 'hf'."""
    for k, (t, band) in enumerate(its):
        if lo is not None and not (lo <= t <= hi): continue
        c = COL[bg][band]
        ax.axvline(t, color=c, lw=1.8, ls="--" if band == "full" else ":")
        ax.annotate((f"{t:.0f} ms" if band == "full" else f"hf {t:.0f} ms"), (t, 1.0), xycoords=("data", "axes fraction"), xytext=(3, -6 - 19 * (k % 3)),
                    textcoords="offset points", color="white" if (bg == "plain" or band == "full") else "black", fontsize=FONT, fontweight="bold", va="top",
                    bbox=dict(facecolor=c, alpha=0.9, pad=2, lw=0))

def title_for(rep): 
    i = rep["info"]
    return f"{i['file']}  {i['duration_ms']:g} ms  {i['sample_rate']} Hz  peak {i['sample_peak_dbfs']} dBFS  TP {i['true_peak_dbtp']} dBTP"

def plot_spectrogram(rep, x, sr, path):
    ons = items(rep)
    dur = len(x) * 1000 / sr
    fig, ax = plt.subplots(figsize=(16, 7.5), dpi=100)
    m = draw_spec(ax, x, sr, 2048 if dur > 400 else 1024, 128)
    mark_onsets(ax, ons)
    ax.set_xlabel("time (ms)"); ax.set_title("Spectrogram (dBFS, onsets dashed)\n" + title_for(rep), loc="left")
    cb = fig.colorbar(m, ax=ax, pad=0.01); cb.set_label("dBFS per bin (80 dB range)", fontsize=FONT)
    fig.tight_layout(); fig.savefig(path); plt.close(fig)

def zoom_windows(ons, dur, pre=15, post=85, cap=6):
    wins = []
    for t in ons:
        a, b = max(0.0, t - pre), min(dur, t + post)
        if wins and a <= wins[-1][1]: wins[-1][1] = b; wins[-1][2].append(t)
        else: wins.append([a, b, [t]])
    return wins[:cap], max(0, len(wins) - cap)

def plot_zoom(rep, x, sr, path):
    ons = items(rep)
    wins, dropped = zoom_windows([t for t, _ in ons], len(x) * 1000 / sr)
    if not wins: return False
    fig, axes = plt.subplots(len(wins), 2, figsize=(16, 4.2 * len(wins)), dpi=100, squeeze=False, gridspec_kw=dict(width_ratios=[1, 1.3]))
    for r, (a, b, ts) in enumerate(wins):
        seg = x[int(a * sr / 1000):int(b * sr / 1000)]
        tt = a + np.arange(len(seg)) * 1000 / sr
        pad_ms = 8.0   # analyse a wider slice so the spectrogram has no zero-padded edge frames inside the window
        a2 = max(0.0, a - pad_ms); wide = x[int(a2 * sr / 1000):int((b + pad_ms) * sr / 1000)]
        axw, axs = axes[r]
        axw.plot(tt, seg, color="#1f4e9c", lw=1.2); axw.set_xlim(a, b); axw.set_ylim(-1.05 * max(np.abs(seg).max(), 1e-3), 1.05 * max(np.abs(seg).max(), 1e-3))
        mark_onsets(axw, ons, "plain", a, b); axw.set_ylabel("amplitude"); axw.set_title(f"waveform {a:.0f}-{b:.0f} ms", loc="left")
        ff, tm, SS = stft_db(wide, sr, 256, 16); k = ff >= 100
        axs.pcolormesh(tm + a2, ff[k], SS[k], shading="auto", cmap="magma", vmin=SS.max() - 70, vmax=SS.max())
        axs.set_yscale("log"); axs.set_ylim(100, sr / 2); axs.set_xlim(a, b); axs.grid(False)
        tk = [t for t in (200, 500, 1000, 2000, 5000, 10000, 20000) if t <= sr / 2]
        axs.set_yticks(tk); axs.set_yticklabels([f"{t/1000:g}k" if t >= 1000 else str(t) for t in tk])
        mark_onsets(axs, ons, "spec", a, b); axs.set_title("spectrogram (5.3 ms frames)", loc="left")
        if r == len(wins) - 1: axw.set_xlabel("time (ms)"); axs.set_xlabel("time (ms)")
    note = f"   ({dropped} further window(s) not drawn)" if dropped else ""
    fig.suptitle("Zoom on each onset" + note, fontsize=FONT + 4, x=0.01, ha="left")
    fig.tight_layout(rect=(0, 0, 1, 0.97)); fig.savefig(path); plt.close(fig)
    return True

def plot_envelope(rep, env, path, other=None, labels=("", "")):
    fig, ax = plt.subplots(figsize=(16, 6), dpi=100)
    ax.plot(np.arange(len(env)), env, color="#1f4e9c", lw=2, label=labels[0] or "level")
    if other is not None: ax.plot(np.arange(len(other[0])), other[0], color="#e67e22", lw=2, label=labels[1])
    lo = max(-100, min(env.min(), -80)); ax.set_ylim(lo, 3)
    mark_onsets(ax, items(rep), "plain")
    if other is not None:
        for t in other[1]: ax.axvline(t, color="#e67e22", ls=":", lw=2)
    ax.axhline(0, color="k", lw=1)
    ax.set_xlabel("time (ms)"); ax.set_ylabel("level (dBFS, 1 ms envelope)")
    ax.set_title("Level envelope (onsets dashed)\n" + title_for(rep), loc="left")
    if other is not None or labels[0]: ax.legend(loc="lower right")
    fig.tight_layout(); fig.savefig(path); plt.close(fig)

# ---- text ---------------------------------------------------------------------------------------------------------------
def report_text(rep):
    i = rep["info"]
    L = [title_for(rep), f"loudness {i['loudness_lufs']} LUFS (mono, BS.1770 gated)  rms {i['rms_dbfs']} dBFS  channels {i['channels']} (mixed to mono)", "",
         f"onsets ({len(rep['onsets'])}), 1 ms resolution:"]
    L += [f"  {o['ms']:6d} ms   peak {o['peak_dbfs']:6.1f} dBFS at {o['peak_ms']} ms" for o in rep["onsets"]] or ["  none"]
    L += ["", f"transient (hf, above {TH['hf_cut_hz']:g} Hz) onsets ({len(rep['onsets_hf'])}):"]
    L += [f"  {o['ms']:6d} ms   peak {o['peak_dbfs']:6.1f} dBFS (hf level) at {o['peak_ms']} ms" for o in rep["onsets_hf"]] or ["  none"]
    L += ["", "detectors (signal facts, not verdicts of good or bad):"]
    L += [f"  [{d['name']}] {d['verdict']}" for d in rep["detectors"]]
    return "\n".join(L) + "\n"

def write_single(path, outdir, quiet=False):
    os.makedirs(outdir, exist_ok=True)
    rep, x, sr, env = analyze(path)
    plot_spectrogram(rep, x, sr, os.path.join(outdir, "spectrogram.png"))
    plot_zoom(rep, x, sr, os.path.join(outdir, "zoom.png"))
    plot_envelope(rep, env, os.path.join(outdir, "envelope.png"))
    json.dump(rep, open(os.path.join(outdir, "report.json"), "w"), indent=1, default=float)
    json.dump(rep["onsets"] + rep["onsets_hf"], open(os.path.join(outdir, "onsets.json"), "w"), indent=1)
    open(os.path.join(outdir, "onsets.txt"), "w").write("".join(f"{o['ms']} ms\t{o['band']}\tpeak {o['peak_dbfs']} dBFS\n" for o in sorted(rep["onsets"] + rep["onsets_hf"], key=lambda o: o["ms"])))
    txt = report_text(rep)
    open(os.path.join(outdir, "report.txt"), "w").write(txt)
    return rep, x, sr, env, txt

def match_onsets(a, b):
    tol, used, pairs, only_a = TH["match_ms"], set(), [], []
    for t in a:
        c = [(abs(t - u), j, u) for j, u in enumerate(b) if j not in used and abs(t - u) <= tol]
        if c: _, j, u = min(c); used.add(j); pairs.append((t, u))
        else: only_a.append(t)
    return pairs, only_a, [u for j, u in enumerate(b) if j not in used]

def compare(pa, pb, outdir):
    os.makedirs(outdir, exist_ok=True)
    ra, xa, sra, ea, _ = write_single(pa, os.path.join(outdir, "a"))
    rb, xb, srb, eb, _ = write_single(pb, os.path.join(outdir, "b"))
    oa, ob = [o["ms"] for o in ra["onsets"]], [o["ms"] for o in rb["onsets"]]
    pairs, only_a, only_b = match_onsets(oa, ob)
    ia, ib = ra["info"], rb["info"]
    ha, hb = [o["ms"] for o in ra["onsets_hf"]], [o["ms"] for o in rb["onsets_hf"]]
    hpairs, honly_a, honly_b = match_onsets(ha, hb)
    deltas = {k: (None if ia[k] is None or ib[k] is None else round(ib[k] - ia[k], 2)) for k in ("sample_peak_dbfs", "true_peak_dbtp", "loudness_lufs", "rms_dbfs", "duration_ms")}
    ddiff = [dict(name=da["name"], a=da["fired"], b=db_["fired"], changed=da["fired"] != db_["fired"], a_verdict=da["verdict"], b_verdict=db_["verdict"])
             for da, db_ in zip(ra["detectors"], rb["detectors"])]
    cmp = dict(a=ia["file"], b=ib["file"], onsets_matched=[dict(a_ms=p, b_ms=q, shift_ms=q - p) for p, q in pairs], onsets_only_in_a=only_a, onsets_only_in_b=only_b,
               hf_onsets_matched=[dict(a_ms=p, b_ms=q, shift_ms=q - p) for p, q in hpairs], hf_onsets_only_in_a=honly_a, hf_onsets_only_in_b=honly_b,
               deltas_b_minus_a=deltas, detector_diff=ddiff)
    json.dump(cmp, open(os.path.join(outdir, "compare.json"), "w"), indent=1)
    L = [f"A = {ia['file']}   B = {ib['file']}", "",
         f"onsets A {oa}", f"onsets B {ob}",
         f"matched (within {TH['match_ms']:g} ms): " + (", ".join(f"{p}->{q}" for p, q in pairs) or "none"),
         f"only in A: {only_a}   only in B: {only_b}", "",
         f"transient (hf) onsets A {ha}", f"transient (hf) onsets B {hb}",
         f"hf matched: " + (", ".join(f"{p}->{q}" for p, q in hpairs) or "none") + f"   only in A: {honly_a}   only in B: {honly_b}", "",
         "deltas B-A: " + "  ".join(f"{k} {v:+}" if v is not None else f"{k} n/a" for k, v in deltas.items()), "", "detector diff:"]
    for d in ddiff:
        L.append(f"  [{d['name']}] A {'FIRES' if d['a'] else 'clear'} | B {'FIRES' if d['b'] else 'clear'}" + ("   <-- CHANGED" if d["changed"] else ""))
        L.append(f"      A: {d['a_verdict']}"); L.append(f"      B: {d['b_verdict']}")
    open(os.path.join(outdir, "compare.txt"), "w").write("\n".join(L) + "\n")
    # picture: stacked spectrograms on one time axis, envelopes overlaid
    dmax = max(len(xa) / sra, len(xb) / srb) * 1000
    fig, axes = plt.subplots(3, 1, figsize=(16, 15), dpi=100, gridspec_kw=dict(height_ratios=[3, 3, 2.4]))
    for ax, x, sr, r, nm in ((axes[0], xa, sra, ra, "A"), (axes[1], xb, srb, rb, "B")):
        m = draw_spec(ax, x, sr, 1024, 64)
        mark_onsets(ax, items(r)); ax.set_xlim(0, dmax)
        ax.set_title(f"{nm}: {title_for(r)}", loc="left")
    axes[1].set_xlabel("time (ms)")
    e = axes[2]
    e.plot(np.arange(len(ea)), ea, color="#1f4e9c", lw=2, label="A " + ia["file"]); e.plot(np.arange(len(eb)), eb, color="#e67e22", lw=2, label="B " + ib["file"])
    for t in oa: e.axvline(t, color="#1f4e9c", ls="--", lw=1.6)
    for t in ob: e.axvline(t, color="#e67e22", ls=":", lw=2.2)
    e.set_xlim(0, dmax); e.set_ylim(-100, 3); e.set_xlabel("time (ms)"); e.set_ylabel("level (dBFS)"); e.legend(loc="lower right")
    e.set_title("Level envelopes (A dashed onsets, B dotted onsets)", loc="left")
    fig.tight_layout(); fig.savefig(os.path.join(outdir, "compare.png")); plt.close(fig)
    return ra, rb, "\n".join(L) + "\n"

# ---- CLI ------------------------------------------------------------------------------------------------------------------------
def main(argv=None):
    p = argparse.ArgumentParser(prog="aqua_ear.py", description="Audio perception tool: spectrogram + onset zooms, onset list, level envelope and signal-fact detectors for one wav/ogg/flac, or a comparison of two. Measures; does not hear.")
    sub = p.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("analyze", help="analyze one file"); a.add_argument("file"); a.add_argument("--out", help="output dir (default ./aqua-ear-out/<name>)")
    c = sub.add_parser("compare", help="compare two files (B relative to A)"); c.add_argument("a"); c.add_argument("b"); c.add_argument("--out", help="output dir (default ./aqua-ear-out/compare)")
    for s in (a, c): s.add_argument("--strict", action="store_true", help="exit 3 if any detector fires (default: findings never change the exit code)")
    args = p.parse_args(argv)
    try:
        if args.cmd == "analyze":
            out = args.out or os.path.join("aqua-ear-out", os.path.splitext(os.path.basename(args.file))[0])
            rep, _, _, _, txt = write_single(args.file, out); print(txt + f"wrote {out}/ (spectrogram.png zoom.png envelope.png onsets.json onsets.txt report.json report.txt)")
            fired = any(d["fired"] for d in rep["detectors"])
        else:
            out = args.out or os.path.join("aqua-ear-out", "compare")
            ra, rb, txt = compare(args.a, args.b, out); print(txt + f"wrote {out}/ (compare.png compare.txt compare.json, a/ and b/ single reports)")
            fired = any(d["fired"] for r in (ra, rb) for d in r["detectors"])
    except Exception as e:
        print(f"aqua-ear: error: {e}", file=sys.stderr); return 2
    return 3 if args.strict and fired else 0

if __name__ == "__main__":
    sys.exit(main())
