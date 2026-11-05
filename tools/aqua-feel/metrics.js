// Pure signal metrics for aqua-feel. Dependency-free; every function takes mono Float32Array/number[] at SR.
// These are measurements of a rendered buffer. Nothing here hears anything, and no number is a verdict.
export const SR = 48000;
export const SILENT_PEAK = 10 ** (-70 / 20); // a buffer whose peak is below -70 dBFS counts as silent
const db = (x) => (x > 0 ? 20 * Math.log10(x) : -Infinity);
const pdb = (x) => (x > 0 ? 10 * Math.log10(x) : -Infinity);
const median = (xs) => { const s = Float64Array.from(xs).sort(); return s.length ? (s.length % 2 ? s[s.length >> 1] : (s[s.length / 2 - 1] + s[s.length / 2]) / 2) : NaN; };
const mean = (xs) => (xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : NaN);
const sd = (xs) => (xs.length < 2 ? 0 : Math.sqrt(xs.reduce((a, x) => a + (x - mean(xs)) ** 2, 0) / (xs.length - 1)));
const quantile = (xs, q) => { const s = Float64Array.from(xs).sort(); return s.length ? s[Math.min(s.length - 1, Math.floor(q * s.length))] : NaN; };

// ---- levels ------------------------------------------------------------------------------------------------------
export function samplePeak(x) { let p = 0; for (const v of x) { const a = Math.abs(v); if (a > p) p = a; } return p; }

// True peak: 4x oversampled with a Hann-windowed sinc (BS.1770 Annex 2 style, 48 taps). Returns linear peak.
export function truePeak(x) {
  const T = 12, phases = [0.25, 0.5, 0.75];
  const taps = phases.map((ph) => Array.from({ length: 2 * T }, (_, i) => {
    const k = i - T + 1, t = k - ph, s = Math.abs(t) < 1e-12 ? 1 : Math.sin(Math.PI * t) / (Math.PI * t);
    return s * (0.5 + 0.5 * Math.cos((Math.PI * t) / T));
  }));
  let peak = samplePeak(x);
  for (let n = 0; n < x.length; n++) {
    for (let p = 0; p < 3; p++) {
      let acc = 0;
      for (let i = 0; i < 2 * T; i++) { const j = n + i - T + 1; if (j >= 0 && j < x.length) acc += x[j] * taps[p][i]; }
      if (Math.abs(acc) > peak) peak = Math.abs(acc);
    }
  }
  return peak;
}

// Over = would clip at a +/-1.0 converter. Flat tops = runs of >=3 consecutive samples at >=0.999 (a clipped waveform's signature).
export function clipping(x) {
  let over = 0, flat = 0, run = 0;
  for (const v of x) {
    const a = Math.abs(v);
    if (a > 1) over++;
    if (a >= 0.999) { run++; if (run === 3) flat++; } else run = 0;
  }
  return { overSamples: over, overFraction: x.length ? over / x.length : 0, flatTopRuns: flat };
}

const K1 = { b: [1.53512485958697, -2.69169618940638, 1.19839281085285], a: [1, -1.69065929318241, 0.73248077421585] };
const K2 = { b: [1, -2, 1], a: [1, -1.99004745483398, 0.99007225036621] };
function biquad(x, { b, a }) {
  const y = new Float64Array(x.length); let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
  for (let n = 0; n < x.length; n++) {
    const v = b[0] * x[n] + b[1] * x1 + b[2] * x2 - a[1] * y1 - a[2] * y2;
    x2 = x1; x1 = x[n]; y2 = y1; y1 = v; y[n] = v;
  }
  return y;
}
// ITU-R BS.1770-4 integrated loudness, mono, 48 kHz coefficients, 400 ms blocks at 75% overlap, -70 LUFS absolute gate,
// -10 LU relative gate. A buffer shorter than 400 ms is zero-padded to one block. Returns LUFS, or null when silent.
export function integratedLoudness(x) {
  const k = biquad(biquad(x, K1), K2), B = 19200, H = 4800;
  const n = Math.max(1, Math.ceil(Math.max(0, k.length - B) / H) + 1), ms = [];
  for (let i = 0; i < n; i++) {
    let s = 0; for (let j = i * H; j < i * H + B && j < k.length; j++) s += k[j] * k[j];
    ms.push(s / B);
  }
  const lufs = (e) => -0.691 + pdb(e);
  const pass1 = ms.filter((e) => lufs(e) > -70);
  if (!pass1.length) return null;
  const rel = lufs(mean(pass1)) - 10, pass2 = pass1.filter((e) => lufs(e) > rel);
  return pass2.length ? lufs(mean(pass2)) : null;
}

// ---- time structure ----------------------------------------------------------------------------------------------
const HOP = 8; // envelope hop in samples (0.167 ms)
// Envelope: max |x| per hop. With held=true it also decays with a 30 ms release so the zero crossings of a low tone do not
// read as dips; only the attack uses that (the hold smears decays, so activeEnd, crest and the gallery plot use the raw one).
const RELEASE_S = 0.03;
export function envelope(x, held = false) {
  const env = new Float64Array(Math.ceil(x.length / HOP)), hold = Math.exp(-HOP / SR / RELEASE_S);
  for (let i = 0; i < x.length; i++) { const a = Math.abs(x[i]); if (a > env[(i / HOP) | 0]) env[(i / HOP) | 0] = a; }
  if (held) for (let i = 1; i < env.length; i++) env[i] = Math.max(env[i], env[i - 1] * hold);
  return env;
}
const maxIndex = (a) => { let k = 0; for (let i = 1; i < a.length; i++) if (a[i] > a[k]) k = i; return k; };
const hopMs = (HOP / SR) * 1000;

// Silent = peak under -70 dBFS. Onset: first sample at or above max(-40 dB re peak, -70 dBFS). Attack is measured inside
// the first 150 ms after the onset (shorter than the 180 ms minimum gap between Ding notes, so a later, louder note cannot
// stretch it): 10% -> 90% of the envelope's maximum within that window. If that maximum falls in the last tenth of the
// window the sound is still swelling and attackMs is only a lower bound (attackWindowLimited). The envelope hop is
// 0.167 ms, so an attack faster than that is reported as one hop (resolutionLimited). activeEnd = last envelope hop
// within 50 dB of the peak.
const ATTACK_WINDOW_S = 0.15;
export function timing(x) {
  const peak = samplePeak(x);
  if (!(peak >= SILENT_PEAK)) return { silent: true };
  const thr = Math.max(peak * 0.01, SILENT_PEAK);
  let onsetSample = 0; while (onsetSample < x.length && Math.abs(x[onsetSample]) < thr) onsetSample++;
  const env = envelope(x, true), raw = envelope(x), start = Math.floor(onsetSample / HOP), end = Math.min(env.length - 1, start + Math.round(ATTACK_WINDOW_S * SR / HOP));
  let top = start; for (let i = start; i <= end; i++) if (env[i] > env[top]) top = i;
  const first = env[top], at = (f) => { let i = start; while (i < top && env[i] < first * f) i++; return i; };
  const i10 = at(0.1), i90 = at(0.9);
  const dt = Math.max(hopMs, (i90 - i10) * hopMs), floor = peak * 10 ** (-50 / 20);
  let last = raw.length - 1; while (last > 0 && raw[last] < floor) last--;
  return {
    silent: false, onsetMs: (onsetSample / SR) * 1000, attackMs: dt, attackSlopeDbPerMs: db(0.9 / 0.1) / dt,
    resolutionLimited: i90 - i10 <= 1, attackWindowLimited: top - start > 0.9 * (end - start) && end - start > 10,
    activeStartSample: onsetSample, activeEndSample: Math.min(x.length, (last + 1) * HOP),
    peakAtMs: (maxIndex(raw) * HOP / SR) * 1000, firstPeak: first, firstPeakAtMs: top * hopMs,
  };
}

export function crest(x, t) {
  const seg = x.subarray ? x.subarray(t.activeStartSample, t.activeEndSample) : x.slice(t.activeStartSample, t.activeEndSample);
  let s = 0; for (const v of seg) s += v * v;
  const rms = Math.sqrt(s / Math.max(1, seg.length)), peak = samplePeak(seg);
  return { rms, rmsDb: db(rms), crestDb: db(peak / rms) };
}

// ---- spectrum ----------------------------------------------------------------------------------------------------
export function fft(re, im) {
  const n = re.length;
  for (let i = 1, j = 0; i < n; i++) {
    let bit = n >> 1; for (; j & bit; bit >>= 1) j ^= bit; j ^= bit;
    if (i < j) { [re[i], re[j]] = [re[j], re[i]]; [im[i], im[j]] = [im[j], im[i]]; }
  }
  for (let len = 2; len <= n; len <<= 1) {
    const ang = (-2 * Math.PI) / len, wr = Math.cos(ang), wi = Math.sin(ang);
    for (let i = 0; i < n; i += len) {
      let cr = 1, ci = 0;
      for (let k = 0; k < len / 2; k++) {
        const a = i + k, b = a + len / 2, tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
        [cr, ci] = [cr * wr - ci * wi, cr * wi + ci * wr];
      }
    }
  }
}

const N = 2048, SHOP = 512;
const hanns = new Map();
const hann = (n) => hanns.get(n) ?? (hanns.set(n, Float64Array.from({ length: n }, (_, i) => 0.5 - 0.5 * Math.cos((2 * Math.PI * i) / n))), hanns.get(n));
export function stft(x, N = 2048, SHOP = 512) {
  const frames = [], HANN = hann(N);
  for (let s = 0; s + N <= Math.max(N, x.length); s += SHOP) {
    const re = new Float64Array(N), im = new Float64Array(N);
    for (let i = 0; i < N; i++) re[i] = (x[s + i] ?? 0) * HANN[i];
    fft(re, im);
    const mag = new Float64Array(N / 2); let e = 0;
    for (let k = 0; k < N / 2; k++) { mag[k] = Math.hypot(re[k], im[k]); e += mag[k] * mag[k]; }
    frames.push({ t: (s + N / 2) / SR, mag, energy: e, n: N });
  }
  return frames;
}

// Tone vs noise, per time-frequency cell. A cell (bin k, frame t) is TONAL when (1) bin k is a local maximum at least
// 12 dB (power x16) above the median of its +/-12 neighbours in frame t, (2) that holds for a run of at least 4
// consecutive frames, and (3) over that run the level is temporally smooth: the median absolute second difference of
// the dB level (max over k-1..k+1, so slow drift stays inside) is under 6 dB. A steady or exponentially decaying partial
// passes; a noise-excited resonance (a rattle, a vocal formant) passes (1) but fails (3), because noise gives each
// frame a Rayleigh-random level (about 9 dB median second difference). Tonal cells take +/-2 bins either side (the Hann
// main lobe). Everything else is noise. Limits: a gliding or strongly vibrato partial moves bins and reads as noise;
// with under 5 frames only (1) applies.
// A-weighting as a power factor per FFT bin (IEC 61672 curve, 0 dB at 1 kHz).
const aWeightPower = (hz) => {
  const f2 = hz * hz, ra = (12194 ** 2 * f2 * f2) / ((f2 + 20.6 ** 2) * Math.sqrt((f2 + 107.7 ** 2) * (f2 + 737.9 ** 2)) * (f2 + 12194 ** 2));
  return (ra * 1.2589) ** 2;
};
const prominent = (p, k) => {
  const nb = []; for (let j = Math.max(1, k - 12); j <= Math.min(p.length - 1, k + 12); j++) if (j !== k) nb.push(p[j]);
  return p[k] >= p[k - 1] && p[k] >= p[k + 1] && p[k] >= 16 * median(nb);
};
function toneNoiseSpan(frames) {
  const bins = frames[0].mag.length, T = frames.length, P = frames.map((f) => Float64Array.from(f.mag, (m) => m * m));
  const tonal = P.map(() => new Uint8Array(bins)), prom = P.map((p) => { const r = new Uint8Array(bins); for (let k = 2; k < bins - 2; k++) r[k] = prominent(p, k) ? 1 : 0; return r; });
  const mark = (t, k) => { for (let j = -2; j <= 2; j++) tonal[t][k + j] = 1; };
  for (let k = 2; k < bins - 2; k++) {
    for (let t = 0; t < T;) {
      if (!prom[t][k]) { t++; continue; }
      let e = t; while (e + 1 < T && prom[e + 1][k]) e++;
      if (T < 5) { for (let i = t; i <= e; i++) mark(i, k); }
      else if (e - t + 1 >= 4) {
        const lv = []; for (let i = t; i <= e; i++) lv.push(20 * Math.log10(Math.max(frames[i].mag[k - 1], frames[i].mag[k], frames[i].mag[k + 1], 1e-12)));
        const d2 = []; for (let i = 1; i < lv.length - 1; i++) d2.push(Math.abs(lv[i + 1] - 2 * lv[i] + lv[i - 1]));
        if (median(d2) < 6) for (let i = t; i <= e; i++) mark(i, k);
      }
      t = e + 1;
    }
  }
  const aw = Float64Array.from({ length: bins }, (_, k) => aWeightPower((k * SR) / (2 * bins)));
  let tone = 0, noise = 0, toneA = 0, noiseA = 0;
  for (let t = 0; t < T; t++) for (let k = 1; k < bins; k++) { const v = P[t][k]; if (tonal[t][k]) { tone += v; toneA += v * aw[k]; } else { noise += v; noiseA += v * aw[k]; } }
  return { tone, noise, toneA, noiseA };
}

// Noise-to-tone ratio in dB over the active span (energy-weighted across frames), flat and A-weighted. Flat power is
// dominated by whatever is loudest and lowest (a 35 Hz rumble hides a 1 kHz hiss); the A-weighted ratio is closer to what
// a listener could notice, which is a reason to look at it, not proof of what they notice. Uses an 8192-point window
// (5.9 Hz bins) when the active span is 1 s or longer, so a 35 Hz rumble resolves into lines; shorter sounds use 2048
// (23 Hz bins). Compare it between renders of the same sound, not across sounds of different lengths. Clamped to +/-60 dB.
export function noiseToTone(x, t) {
  const spanS = (t.activeEndSample - t.activeStartSample) / SR, n = spanS >= 1 ? 8192 : 2048;
  const frames = stft(x.slice(t.activeStartSample, t.activeEndSample), n, n / 4), maxE = frames.reduce((m, f) => Math.max(m, f.energy), 0);
  const live = frames.filter((f) => f.energy >= maxE * 1e-5), r = toneNoiseSpan(live);
  const ratio = (nz, tn) => Math.max(-60, Math.min(60, pdb(nz) - pdb(tn || 1e-30)));
  return { db: ratio(r.noise, r.tone), dbA: ratio(r.noiseA, r.toneA), window: n };
}

const downsample = (pts, n) => {
  if (pts.length <= n) return pts;
  return Array.from({ length: n }, (_, i) => {
    const a = Math.floor((i * pts.length) / n), b = Math.max(a + 1, Math.floor(((i + 1) * pts.length) / n)), seg = pts.slice(a, b);
    return { t: mean(seg.map((s) => s.t)), v: mean(seg.map((s) => s.v)) };
  });
};

// Centroid and flux over time, noise-to-tone ratio, and the low-motion flag, over frames within 50 dB of the loudest
// frame and inside the active span. Flux is the half-wave-rectified L2 change between consecutive unit-norm magnitude
// spectra, so it tracks timbre change and not loudness change.
export function spectral(x, t, curvePoints = 48) {
  const all = stft(x), maxE = all.reduce((m, f) => Math.max(m, f.energy), 0), t0 = t.activeStartSample / SR, t1 = t.activeEndSample / SR;
  const idx = all.map((f, i) => (f.energy >= maxE * 1e-5 && f.t >= t0 - N / 2 / SR && f.t <= t1 + N / 2 / SR ? i : -1)).filter((i) => i >= 0);
  if (idx.length < 2) return { frames: idx.length, centroidMedianHz: NaN, centroidCurve: [], fluxCurve: [], fluxMean: 0, fluxP95: 0, centroidSpreadOct: 0, envRangeDb: 0, motion: 0, lowMotion: false, lowMotionReason: "fewer than 2 active frames" };
  const cent = [], flux = [], hz = (k) => (k * SR) / N;
  let prev = null;
  for (const i of idx) {
    const f = all[i]; let num = 0, den = 0;
    for (let k = 1; k < f.mag.length; k++) { num += hz(k) * f.mag[k]; den += f.mag[k]; }
    cent.push({ t: f.t, v: den > 0 ? num / den : 0 });
    const norm = Math.sqrt(f.energy) || 1;
    if (prev) { let s = 0; for (let k = 1; k < f.mag.length; k++) { const d = Math.max(0, f.mag[k] / norm - prev[k]); s += d * d; } flux.push({ t: f.t, v: Math.sqrt(s) }); }
    prev = Float64Array.from(f.mag, (m) => m / norm);
  }
  const cs = cent.map((c) => Math.log2(Math.max(c.v, 20))), fl = flux.slice(3).map((f) => f.v), fe = idx.map((i) => pdb(all[i].energy));
  const centroidSpreadOct = sd(cs), envRangeDb = fe.reduce((a, b) => Math.max(a, b), -Infinity) - fe.reduce((a, b) => Math.min(a, b), Infinity), fluxMean = mean(fl.length ? fl : [0]);
  // Low motion = the sound stays put: timbre barely moves AND level barely moves, over a span long enough to matter.
  // A decaying bell has a large level range, so it is not flagged; a held drone or a pink wash is. Flux is reported
  // but not used: stationary noise has high flux (periodogram scatter) while going nowhere.
  const spanS = t1 - t0, still = centroidSpreadOct < 0.08 && envRangeDb < 6;
  return {
    frames: idx.length, centroidMedianHz: median(cent.map((c) => c.v)), centroidCurve: downsample(cent, curvePoints), fluxCurve: downsample(flux, curvePoints),
    fluxMean, fluxP95: quantile(fl.length ? fl : [0], 0.95), 
    centroidSpreadOct, envRangeDb, motion: Math.min(1, centroidSpreadOct / 0.5 + envRangeDb / 30 + fluxMean / 0.5),
    lowMotion: spanS >= 1 && still, lowMotionReason: spanS < 1 ? "active span under 1 s: not judged" : still ? `centroid spread ${centroidSpreadOct.toFixed(3)} oct, level range ${envRangeDb.toFixed(1)} dB` : "",
  };
}

// Peak level in dB per bucket (about `n` buckets across the whole buffer), floored at -90 dB, for the gallery's envelope plot.
export function envelopeCurve(x, n = 96) {
  const env = envelope(x), per = Math.max(1, Math.ceil(env.length / n)), out = [];
  for (let i = 0; i < env.length; i += per) { let m = 0; for (let j = i; j < Math.min(env.length, i + per); j++) m = Math.max(m, env[j]); out.push({ t: ((i + per / 2) * HOP) / SR, v: Math.max(-90, db(m)) }); }
  return out;
}

// ---- one buffer -> one record ------------------------------------------------------------------------------------
export function measure(x, { curvePoints = 48 } = {}) {
  const peak = samplePeak(x), nonFinite = x.reduce((n, v) => n + (Number.isFinite(v) ? 0 : 1), 0);
  if (nonFinite) return { nonFinite, silent: false, broken: true };
  const t = timing(x);
  if (t.silent) return { silent: true, nonFinite: 0, samplePeak: peak };
  const tp = truePeak(x), c = crest(x, t), sp = spectral(x, t, curvePoints), clip = clipping(x), nt = noiseToTone(x, t);
  return {
    silent: false, nonFinite: 0, samplePeak: peak, samplePeakDb: db(peak), truePeak: tp, truePeakDb: db(tp), ...clip,
    loudnessLufs: integratedLoudness(x), rms: c.rms, rmsDb: c.rmsDb, crestDb: c.crestDb,
    onsetMs: t.onsetMs, attackMs: t.attackMs, attackSlopeDbPerMs: t.attackSlopeDbPerMs, attackResolutionLimited: t.resolutionLimited, attackWindowLimited: t.attackWindowLimited, peakAtMs: t.peakAtMs,
    activeMs: ((t.activeEndSample - t.activeStartSample) / SR) * 1000, noiseToToneDb: nt.db, noiseToToneADb: nt.dbA, noiseToToneWindow: nt.window, envelopeCurve: envelopeCurve(x), ...sp,
  };
}
