// aqua-feel sync: audio-visual synchrony check. Zero npm dependencies (node >= 18); ffmpeg/ffprobe only if you hand it a video.
// NOTHING HERE HEARS OR SEES ANYTHING. It compares two onset lists extracted from signals; see SYNC.md for what that cannot tell you.
// Library: syncAnalyze({ audio, sampleRate, visual:{t,v}, events, profile }) -> report. CLI wiring is in feel.mjs (`node feel.mjs sync ...`).
import { readFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { fft } from "./metrics.js";

// Lag convention: lag = audio onset time - visual onset time. Positive = audio LATE (after the picture); negative = audio EARLY.
// Windows are [leadMs, lagMs]: audio may lead the picture by up to leadMs and trail it by up to lagMs before the offset is "detectable".
// Sources: see SYNC.md. 'default' is the requested conservative profile; the rest are the published ones, secondary-source figures noted there.
export const PROFILES = {
  default: { leadMs: 25, lagMs: 100, note: "conservative; between ATSC IS-191 and ITU-R BT.1359-1" },
  "bt1359": { leadMs: 45, lagMs: 125, note: "ITU-R BT.1359-1 (1998) detectability threshold" },
  "ebu-r37": { leadMs: 40, lagMs: 60, note: "EBU R37 (2007) emission limit, per secondary source" },
  "atsc-is191": { leadMs: 15, lagMs: 45, note: "ATSC IS-191 limit at the encoder input, per secondary source" },
};

// ---------- input readers ----------
export function readWav(buf) {
  if (buf.toString("latin1", 0, 4) !== "RIFF" || buf.toString("latin1", 8, 12) !== "WAVE") throw new Error("not a RIFF/WAVE file");
  let fmt, data;
  for (let p = 12; p + 8 <= buf.length;) {
    const id = buf.toString("latin1", p, p + 4), sz = buf.readUInt32LE(p + 4), body = p + 8;
    if (id === "fmt ") fmt = { tag: buf.readUInt16LE(body), ch: buf.readUInt16LE(body + 2), sr: buf.readUInt32LE(body + 4), bits: buf.readUInt16LE(body + 14) };
    if (id === "data") { data = buf.subarray(body, Math.min(buf.length, body + sz)); break; }
    p = body + sz + (sz & 1);
  }
  if (!fmt || !data) throw new Error("WAV has no fmt or data chunk");
  if (fmt.tag === 0xfffe) fmt.tag = buf.readUInt16LE(buf.indexOf("fmt ") + 8 + 24); // WAVE_FORMAT_EXTENSIBLE: sub-format tag
  const bps = fmt.bits / 8, frames = Math.floor(data.length / (bps * fmt.ch)), out = new Float32Array(frames);
  const get = fmt.tag === 3 && fmt.bits === 32 ? (o) => data.readFloatLE(o) : fmt.tag === 3 && fmt.bits === 64 ? (o) => data.readDoubleLE(o)
    : fmt.tag === 1 && fmt.bits === 16 ? (o) => data.readInt16LE(o) / 32768 : fmt.tag === 1 && fmt.bits === 24 ? (o) => data.readIntLE(o, 3) / 8388608
    : fmt.tag === 1 && fmt.bits === 32 ? (o) => data.readInt32LE(o) / 2147483648 : null;
  if (!get) throw new Error(`unsupported WAV format tag ${fmt.tag} / ${fmt.bits} bit`);
  for (let i = 0; i < frames; i++) { let s = 0; for (let c = 0; c < fmt.ch; c++) s += get((i * fmt.ch + c) * bps); out[i] = s / fmt.ch; } // mono mix
  return { samples: out, sampleRate: fmt.sr, channels: fmt.ch };
}

// Frame-diff track: lines of "t,value" (seconds) or a single "value" column with fps given. A non-numeric first line is a header.
export function readTrack(text, fps) {
  const t = [], v = [];
  for (const line of text.split(/\r?\n/)) {
    if (!line.trim()) continue;
    const f = line.trim().split(/[\s,;]+/).filter(Boolean).map(Number);
    if (f.some(Number.isNaN)) { if (t.length || v.length) throw new Error(`bad track line: ${line}`); continue; }
    if (f.length >= 2) { t.push(f[0]); v.push(f[1]); }
    else { if (!fps) throw new Error("single-column track needs --fps"); t.push(v.length / fps); v.push(f[0]); }
  }
  if (v.length < 3) throw new Error("track has fewer than 3 frames");
  return { t: Float64Array.from(t), v: Float64Array.from(v) };
}

// Events: JSON array of numbers or {t,label}, or text lines "t[,label]" (seconds).
export function readEvents(text) {
  const s = text.trim();
  if (s.startsWith("[") || s.startsWith("{")) {
    const j = JSON.parse(s), a = Array.isArray(j) ? j : j.events;
    return a.map((e, i) => (typeof e === "number" ? { t: e, label: `e${i}` } : { t: Number(e.t), label: e.label ?? `e${i}` }));
  }
  return s.split(/\r?\n/).map((l, i) => l.trim()).filter((l) => l && !l.startsWith("#") && !Number.isNaN(Number(l.split(/[,\s]+/)[0])))
    .map((l, i) => { const [t, ...r] = l.split(/[,\s]+/); return { t: Number(t), label: r.join(" ") || `e${i}` }; });
}

// Video via ffmpeg: audio as mono f32 at 48 kHz, video as 64x36 gray frames; per-frame mean luminance and mean |frame diff| (motion flux).
// Stream start_time offsets are applied so both clocks are the container's clock. Constant frame rate is assumed (avg_frame_rate).
export function readVideo(path, { ffmpeg = "ffmpeg", ffprobe = "ffprobe" } = {}) {
  let probe;
  try { probe = JSON.parse(execFileSync(ffprobe, ["-v", "error", "-show_streams", "-of", "json", path], { encoding: "utf8" })); }
  catch (e) { throw new Error(`ffprobe failed (${e.code === "ENOENT" ? "not on PATH; give --wav and --track instead" : e.message})`); }
  const vs = probe.streams.find((s) => s.codec_type === "video"), as = probe.streams.find((s) => s.codec_type === "audio");
  if (!vs || !as) throw new Error("file needs one video and one audio stream");
  const [n, d] = vs.avg_frame_rate.split("/").map(Number), fps = n / d, W = 64, H = 36;
  const audioRaw = execFileSync(ffmpeg, ["-v", "error", "-i", path, "-map", `0:${as.index}`, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "-"], { maxBuffer: 1 << 30 });
  const videoRaw = execFileSync(ffmpeg, ["-v", "error", "-i", path, "-map", `0:${vs.index}`, "-an", "-vf", `scale=${W}:${H}:flags=area,format=gray`, "-vsync", "passthrough", "-f", "rawvideo", "-"], { maxBuffer: 1 << 30 });
  const frames = Math.floor(videoRaw.length / (W * H)), t = new Float64Array(frames), lum = new Float64Array(frames), flux = new Float64Array(frames);
  const vOff = Number(vs.start_time ?? 0) || 0, aOff = Number(as.start_time ?? 0) || 0;
  for (let f = 0; f < frames; f++) {
    let s = 0, df = 0; for (let i = 0; i < W * H; i++) { const p = videoRaw[f * W * H + i]; s += p; if (f) df += Math.abs(p - videoRaw[(f - 1) * W * H + i]); }
    t[f] = vOff + f / fps; lum[f] = s / (W * H) / 255; flux[f] = f ? df / (W * H) / 255 : 0;
  }
  const a = new Float32Array(audioRaw.buffer, audioRaw.byteOffset, Math.floor(audioRaw.length / 4));
  return { audio: { samples: a, sampleRate: 48000, startS: aOff }, luma: { t, v: lum }, flux: { t, v: flux }, fps };
}

// ---------- onset detection ----------
const median = (a) => { const s = Float64Array.from(a).sort(); const n = s.length; return n ? (n % 2 ? s[(n - 1) / 2] : (s[n / 2 - 1] + s[n / 2]) / 2) : 0; };

// Pick onsets from a nonnegative novelty curve nov[i] at times tt[i]. A peak is a local maximum above max(floor, med + k*1.4826*MAD) and
// at least minGapS from the previous kept peak (the larger wins). The onset is the first sample of the rise that reaches riseFrac of the peak.
function pickOnsets(nov, tt, { k, floor, minGapS, riseFrac }) {
  const med = median(nov), mad = median(nov.map((x) => Math.abs(x - med))), thr = Math.max(floor, med + k * 1.4826 * mad);
  const peaks = [];
  for (let i = 0; i < nov.length; i++) {
    if (nov[i] < thr || (i && nov[i] < nov[i - 1]) || (i + 1 < nov.length && nov[i] <= nov[i + 1])) continue;
    const last = peaks[peaks.length - 1];
    if (last && tt[i] - tt[last] < minGapS) { if (nov[i] > nov[last]) peaks[peaks.length - 1] = i; continue; }
    peaks.push(i);
  }
  return {
    threshold: thr,
    onsets: peaks.map((p) => {
      let i = p; while (i > 0 && nov[i - 1] >= riseFrac * nov[p] && tt[p] - tt[i - 1] < 0.1) i--;
      return { t: tt[i], peakT: tt[p], strength: nov[p] };
    }),
  };
}

// Audio: log-magnitude spectral flux, 1024 window / 128 hop (2.7 ms at 48 kHz), half-wave rectified, summed over 60 Hz..Nyquist.
export function audioOnsets(x, sr, { startS = 0, k = 8, minGapS = 0.05 } = {}) {
  const N = 1024, HOP = 128, win = Float64Array.from({ length: N }, (_, i) => 0.5 - 0.5 * Math.cos((2 * Math.PI * i) / N));
  const lo = Math.ceil((60 * N) / sr), hi = N / 2, frames = Math.max(0, Math.floor((x.length - N) / HOP) + 1);
  const flux = new Float64Array(frames), tt = new Float64Array(frames), prev = new Float64Array(hi + 1), re = new Float64Array(N), im = new Float64Array(N);
  for (let f = 0; f < frames; f++) {
    for (let i = 0; i < N; i++) { re[i] = x[f * HOP + i] * win[i]; im[i] = 0; }
    fft(re, im);
    let s = 0;
    for (let b = lo; b <= hi; b++) { const m = Math.log1p(1e3 * Math.hypot(re[b], im[b]) / N); if (f) s += Math.max(0, m - prev[b]); prev[b] = m; }
    flux[f] = s; tt[f] = startS + (f * HOP + N / 2) / sr; // a frame is stamped at its centre
  }
  // The window centre sits half a window after the first audible sample's frame start; the rise-start pick lands within one window of the true onset.
  const r = pickOnsets(flux, tt, { k, floor: 1e-3 * hi, minGapS, riseFrac: 0.25 });
  // The flux frame is only good to a window; refine each onset on the samples: the first |x| within 20 dB (10%) of the local peak, searched
  // from one window before the flux time to one window after it (the window centre can sit up to N/2 early of a click inside it).
  for (const o of r.onsets) {
    const c = Math.round((o.t - startS) * sr), a0 = Math.max(0, c - N), a1 = Math.min(x.length, c + N);
    let pk = 0; for (let i = a0; i < a1; i++) pk = Math.max(pk, Math.abs(x[i]));
    for (let i = a0; i < a1; i++) if (Math.abs(x[i]) >= 0.1 * pk) { o.t = startS + i / sr; break; }
  }
  r.resolutionMs = (1000 * HOP) / sr; r.windowMs = (1000 * N) / sr;
  return r;
}

// Visual: onset = frame where the track rises. Luminance uses the positive frame-to-frame step; motion flux is already a change measure.
export function visualOnsets(track, { kind = "luma", k = 8, minGapS = 0.1 } = {}) {
  const { t, v } = track, nov = new Float64Array(v.length);
  for (let i = 1; i < v.length; i++) nov[i] = kind === "luma" ? Math.max(0, v[i] - v[i - 1]) : Math.max(0, v[i]);
  const range = Math.max(...v) - Math.min(...v);
  const r = pickOnsets(nov, t, { k, floor: 0.1 * range + 1e-12, minGapS, riseFrac: 0.5 });
  const dts = []; for (let i = 1; i < t.length; i++) dts.push(t[i] - t[i - 1]);
  r.framePeriodMs = 1000 * median(dts);
  return r;
}

// ---------- pairing and verdict ----------
export function syncAnalyze({ audio, sampleRate, audioStartS = 0, visual, visualKind = "luma", events, profile = "default", leadMs, lagMs, pairWindowMs = 300 }) {
  const prof = { ...(PROFILES[profile] ?? (() => { throw new Error(`unknown profile ${profile}`); })()) };
  if (leadMs !== undefined) prof.leadMs = leadMs;
  if (lagMs !== undefined) prof.lagMs = lagMs;
  const a = audioOnsets(audio, sampleRate, { startS: audioStartS }), v = visualOnsets(visual, { kind: visualKind });
  const uncertaintyMs = Math.max(a.resolutionMs, v.framePeriodMs) / 2 + a.resolutionMs / 2; // half a frame period, plus half an audio hop
  const used = new Set(), pairs = [];
  const nearest = (list, t) => { let best = -1, bd = Infinity; list.forEach((o, i) => { const d = Math.abs(o.t - t); if (d < bd) { bd = d; best = i; } }); return best; };
  const classify = (lag) => {
    const excess = lag < 0 ? -lag - prof.leadMs : lag - prof.lagMs; // >0: outside the window by that many ms
    if (excess > uncertaintyMs) return lag < 0 ? "audio-early-detectable" : "audio-late-detectable";
    if (excess > -uncertaintyMs) return "borderline"; // the window edge lies inside the measurement uncertainty
    return "inside-window";
  };
  if (events?.length) {
    for (const e of events) {
      const ai = nearest(a.onsets, e.t), vi = nearest(v.onsets, e.t), ao = a.onsets[ai], vo = v.onsets[vi];
      const okA = ao && Math.abs(ao.t - e.t) * 1e3 <= pairWindowMs, okV = vo && Math.abs(vo.t - e.t) * 1e3 <= pairWindowMs;
      const row = { label: e.label, eventS: e.t, audioS: okA ? ao.t : null, visualS: okV ? vo.t : null, audioVsEventMs: okA ? (ao.t - e.t) * 1e3 : null, visualVsEventMs: okV ? (vo.t - e.t) * 1e3 : null };
      row.lagMs = okA && okV ? (ao.t - vo.t) * 1e3 : null;
      row.verdict = row.lagMs === null ? (okA ? "no-visual-onset" : okV ? "no-audio-onset" : "no-onsets") : classify(row.lagMs);
      if (okA) used.add(`a${ai}`); if (okV) used.add(`v${vi}`);
      pairs.push(row);
    }
  } else {
    a.onsets.forEach((ao, ai) => {
      const vi = nearest(v.onsets, ao.t), vo = v.onsets[vi];
      if (vo && Math.abs(vo.t - ao.t) * 1e3 <= pairWindowMs && !used.has(`v${vi}`)) {
        used.add(`a${ai}`); used.add(`v${vi}`);
        const lagMs = (ao.t - vo.t) * 1e3;
        pairs.push({ label: `a${ai}`, eventS: null, audioS: ao.t, visualS: vo.t, audioVsEventMs: null, visualVsEventMs: null, lagMs, verdict: classify(lagMs) });
      }
    });
  }
  const lags = pairs.map((p) => p.lagMs).filter((x) => x !== null);
  const summary = {
    pairs: lags.length, audioOnsets: a.onsets.length, visualOnsets: v.onsets.length,
    unpairedAudio: a.onsets.filter((_, i) => !used.has(`a${i}`)).length, unpairedVisual: v.onsets.filter((_, i) => !used.has(`v${i}`)).length,
    medianLagMs: lags.length ? median(lags) : null, minLagMs: lags.length ? Math.min(...lags) : null, maxLagMs: lags.length ? Math.max(...lags) : null,
    // Spread of the lags: a constant offset is a latency (fixable by a delay), a wide spread is jitter (not fixable by one number).
    spreadMs: lags.length ? Math.max(...lags) - Math.min(...lags) : null,
    outsideWindow: pairs.filter((p) => /detectable$/.test(p.verdict)).length, borderline: pairs.filter((p) => p.verdict === "borderline").length,
    missing: pairs.filter((p) => p.lagMs === null).length,
  };
  summary.pass = summary.pairs > 0 && summary.outsideWindow === 0 && summary.missing === 0;
  return {
    schema: "aqua-feel.sync.v1", lagConvention: "lagMs = audio onset - visual onset; positive = audio late, negative = audio early",
    profile: { name: profile, ...prof }, uncertaintyMs, visualKind, audioResolutionMs: a.resolutionMs, framePeriodMs: v.framePeriodMs,
    audioOnsets: a.onsets, visualOnsets: v.onsets, pairs, summary,
  };
}

export function formatSync(r) {
  const f = (x, d = 1) => (x === null || x === undefined ? "   -  " : x.toFixed(d).padStart(7));
  const L = [`profile ${r.profile.name}: audio may lead by ${r.profile.leadMs} ms, lag by ${r.profile.lagMs} ms (${r.profile.note ?? "custom"})`,
    `lag = audio - visual (+ = audio late). measurement uncertainty about +/-${r.uncertaintyMs.toFixed(1)} ms (frame period ${r.framePeriodMs.toFixed(1)} ms, audio hop ${r.audioResolutionMs.toFixed(1)} ms)`,
    "", "label        event s   audio s  visual s  a-ev ms  v-ev ms   lag ms  verdict"];
  for (const p of r.pairs) L.push(`${String(p.label).padEnd(10).slice(0, 10)} ${f(p.eventS, 3)}  ${f(p.audioS, 3)}  ${f(p.visualS, 3)}  ${f(p.audioVsEventMs)}  ${f(p.visualVsEventMs)}  ${f(p.lagMs)}  ${p.verdict}`);
  const s = r.summary;
  L.push("", `${s.pairs} paired, ${s.unpairedAudio} audio and ${s.unpairedVisual} visual onsets unpaired; median lag ${s.medianLagMs === null ? "-" : s.medianLagMs.toFixed(1)} ms, range ${s.minLagMs === null ? "-" : `${s.minLagMs.toFixed(1)}..${s.maxLagMs.toFixed(1)}`} ms (spread ${s.spreadMs === null ? "-" : s.spreadMs.toFixed(1)} ms)`,
    `${s.outsideWindow} outside the window, ${s.borderline} borderline, ${s.missing} without both onsets. ${s.pass ? "WITHIN WINDOW" : "NOT WITHIN WINDOW"} by this metric.`,
    "A metric over two onset lists, not a verdict: a person still has to watch and listen (SYNC.md).");
  return L.join("\n");
}
