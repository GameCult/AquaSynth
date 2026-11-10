"""metriclib.py: measurements shared by finish.py and measure.py. SR=48000. Metrics are not verdicts."""
import numpy as np
SR = 48000

def a_weight_db(f):
    f2 = f * f
    ra = (12194.0**2 * f2 * f2) / ((f2 + 20.6**2) * np.sqrt((f2 + 107.7**2) * (f2 + 737.9**2)) * (f2 + 12194.0**2))
    return 20 * np.log10(np.maximum(ra, 1e-12)) + 2.0

def lae(x):
    """A-weighted sound exposure level, dB re full scale, 1 s reference: 10log10(sum(xA^2)/SR). Energy-matched, duration-independent."""
    n = 1 << int(np.ceil(np.log2(len(x) * 2)))
    X = np.fft.rfft(x, n); f = np.fft.rfftfreq(n, 1 / SR)
    w = 10 ** (a_weight_db(np.maximum(f, 1.0)) / 20); w[0] = 0
    xa = np.fft.irfft(X * w, n)
    return 10 * np.log10(max(np.sum(xa**2) / SR, 1e-20))

def lafmax(x, win_ms=125):
    """Maximum A-weighted 125 ms running energy level (dB re full scale), a short-term peak of perceived level."""
    n = 1 << int(np.ceil(np.log2(len(x) * 2)))
    X = np.fft.rfft(x, n); f = np.fft.rfftfreq(n, 1 / SR)
    w = 10 ** (a_weight_db(np.maximum(f, 1.0)) / 20); w[0] = 0
    xa = np.fft.irfft(X * w, n)[: len(x)]
    k = int(SR * win_ms / 1000); c = np.cumsum(np.concatenate([[0], xa**2]))
    m = (c[k:] - c[:-k]) / k if len(xa) >= k else np.array([np.mean(xa**2)])
    return 10 * np.log10(max(m.max(), 1e-20))

def true_peak_db(x, os_=8):
    n = len(x); X = np.fft.rfft(x); Y = np.zeros(n * os_ // 2 + 1, dtype=complex); Y[: len(X)] = X
    y = np.fft.irfft(Y, n * os_) * os_
    return 20 * np.log10(max(np.max(np.abs(y)), 1e-12))

def onset_rise_ms(x):
    """Onset sharpness (reported on the single-note `mention` clip, where the first hit is the whole clip): time between the FIRST crossing
    of 10% and the FIRST crossing of 90% of the 1 ms RMS envelope's peak, ms. Small = sharp/struck; large = swelling."""
    k = SR // 1000; c = np.cumsum(np.concatenate([[0], x**2])); e = np.sqrt((c[k:] - c[:-k]) / k)
    pk = e.max(); i10 = int(np.argmax(e >= 0.1 * pk)); i90 = int(np.argmax(e >= 0.9 * pk))
    return (i90 - i10) / (SR / 1000)

def onset_time_ms(x):
    """Time from clip start to the first 10% crossing of the 1 ms RMS envelope's peak (when the sound audibly starts), ms."""
    k = SR // 1000; c = np.cumsum(np.concatenate([[0], x**2])); e = np.sqrt((c[k:] - c[:-k]) / k)
    return int(np.argmax(e >= 0.1 * e.max())) / (SR / 1000)

def spectral(x):
    X = np.abs(np.fft.rfft(x * np.hanning(len(x)), 1 << int(np.ceil(np.log2(len(x))))))**2; f = np.fft.rfftfreq(2 * (len(X) - 1), 1 / SR)
    m = (f >= 80) & (f <= 16000); P = X[m]; P = np.maximum(P, P.max() * 1e-9); ff = f[m]   # floor at -90 dB re the loudest bin, so leakage below the 16-bit/Vorbis floor does not decide flatness
    centroid = float(np.sum(ff * P) / np.sum(P)); flat = float(np.exp(np.mean(np.log(P))) / np.mean(P))
    return centroid, flat

def logspec(x, N=2048, H=512, bands=32):
    edges = np.geomspace(80, 16000, bands + 1); fr = np.fft.rfftfreq(N, 1 / SR); win = np.hanning(N); cols = []
    pad = np.concatenate([x, np.zeros(max(0, N - len(x)))])
    for s in range(0, max(1, len(pad) - N + 1), H):
        P = np.abs(np.fft.rfft(pad[s:s + N] * win))**2
        cols.append(np.array([10 * np.log10(P[(fr >= edges[b]) & (fr < edges[b + 1])].sum() + 1e-12) for b in range(bands)]))
    return np.array(cols)

def spec_dist(a, b):
    """Mean absolute dB difference over 32 log bands (80 Hz-16 kHz), cells where both sides sit under -60 dB (re the pair's own loudest band-frame) ignored.
    Same construction as the Earcon Workshop's test.mjs sibling check; a spectrogram number, not a perceptual one."""
    A, B = logspec(a), logspec(b); n = max(len(A), len(B))
    pad = lambda M: np.vstack([M, np.full((n - len(M), M.shape[1]), -120.0)])
    A, B = pad(A), pad(B); ref = max(A.max(), B.max())
    keep = ~((A < ref - 60) & (B < ref - 60))
    return float(np.mean(np.abs(A - B)[keep])) if keep.any() else 0.0


def end_metrics(z, ms_tail=5, ms_window=100):
    """The clip's end, as the brief asks: level of the last `ms_tail` ms re the clip peak (dB; very negative = settled), the peak of the last 10 ms re the clip peak (a step at the very end can be no larger than this, so it bounds any click), the mean (DC) over the last `ms_window` ms as % of the peak,
    and the number of samples at or above 0.999 full scale (clipping)."""
    pk = float(np.max(np.abs(z))); k = SR * ms_tail // 1000; w = SR * ms_window // 1000
    tail = 20 * np.log10(max(np.sqrt(np.mean(z[-k:] ** 2)), 1e-12) / pk)
    return dict(tailDb=round(float(tail), 1), endPeakDb=round(float(20 * np.log10(max(float(np.max(np.abs(z[-SR // 100:]))), 1e-12) / pk)), 1),
                dcPct=round(100 * float(np.mean(z[-w:])) / pk, 4), lastSamplePct=round(100 * abs(float(z[-1])) / pk, 4), clipped=int(np.sum(np.abs(z) >= 0.999)))
