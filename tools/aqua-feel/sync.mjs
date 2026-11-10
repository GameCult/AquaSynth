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

// Frame-diff track: lines of "t,value" (seconds, strictly increasing) or a single "value" column with fps given. Only the first line may be a header.
// Anything else that does not parse is an error: a silently dropped or shifted row would give a plausible wrong lag.
export function readTrack(text, fps) {
  const t = [], v = [];
  text.split(/\r?\n/).forEach((raw, ln) => {
    const line = raw.trim(); if (!line) return;
    const tok = line.split(/[\s,;]+/), f = tok.map(Number);
    if (tok.some((x) => x === "") || f.some((x) => !Number.isFinite(x))) { if (ln === 0 && !t.length && !v.length) return; throw new Error(`track line ${ln + 1} is not numeric: ${raw}`); }
    if (f.length > 2) throw new Error(`track line ${ln + 1} has ${f.length} columns; expected "t,value" or one value per line: ${raw}`);
    if (f.length === 2) { if (t.length && f[0] <= t[t.length - 1]) throw new Error(`track line ${ln + 1}: time ${f[0]} does not increase`); t.push(f[0]); v.push(f[1]); }
    else { if (!fps) throw new Error("single-column track needs --fps"); t.push(v.length / fps); v.push(f[0]); }
  });
  if (v.length < 3) throw new Error("track has fewer than 3 frames");
  return { t: Float64Array.from(t), v: Float64Array.from(v) };
}

// Events: JSON array of numbers or {t,label}, or text lines "t[,label]" in seconds ('#' comments; the first line may be a header). Anything else is an error.
export function readEvents(text) {
  const s = text.trim(), fin = (x, where) => { if (!Number.isFinite(x)) throw new Error(`event ${where} has no numeric time`); return x; };
  let out;
  if (s.startsWith("[") || s.startsWith("{")) {
    const j = JSON.parse(s), a = Array.isArray(j) ? j : j.events;
    if (!Array.isArray(a)) throw new Error("events JSON must be an array or {events:[...]}");
    out = a.map((e, i) => (typeof e === "number" ? { t: fin(e, i), label: `e${i}` } : e && typeof e === "object" ? { t: fin(Number(e.t), i), label: String(e.label ?? `e${i}`) } : fin(NaN, i)));
  } else {
    out = [];
    s.split(/\r?\n/).forEach((raw, ln) => {
      const l = raw.trim(); if (!l || l.startsWith("#")) return;
      const m = l.match(/^([^,\s]+)(?:[,\s]+(.*))?$/), t = Number(m[1]);
      if (!Number.isFinite(t)) { if (ln === 0 && !out.length) return; throw new Error(`events line ${ln + 1} does not start with a time in seconds: ${raw}`); }
      out.push({ t, label: m[2]?.trim() || `e${out.length}` });
    });
  }
  if (!out.length) throw new Error("the events log has no events");
  return out;
}

// Video via ffmpeg: audio as mono f32 at 48 kHz, video as 64x36 gray frames; per-frame mean luminance and mean |frame diff| (motion flux).
// Stream start_time offsets are applied so both clocks are the container's clock; video is resampled to constant frame rate by pts.
export function readVideo(path, { ffmpeg = process.env.FFMPEG ?? "ffmpeg", ffprobe = process.env.FFPROBE ?? "ffprobe", targetFps } = {}) {
  // Stream facts from ffprobe when present, else from `ffmpeg -i` (static ffmpeg builds ship without ffprobe; -i prints them to stderr and exits 1).
  let vs, as;
  try {
    const probe = JSON.parse(execFileSync(ffprobe, ["-v", "error", "-show_streams", "-of", "json", path], { encoding: "utf8" }));
    const sv = probe.streams.find((x) => x.codec_type === "video"), sa = probe.streams.find((x) => x.codec_type === "audio");
    if (sv && sa) { const [n, d] = sv.avg_frame_rate.split("/").map(Number); vs = { index: sv.index, fps: n / d, start: Number(sv.start_time ?? 0) || 0 }; as = { index: sa.index, start: Number(sa.start_time ?? 0) || 0 }; }
  } catch (e) { if (e.code !== "ENOENT") throw new Error(`ffprobe failed: ${e.message}`); }
  if (!vs) {
    let err = ""; try { execFileSync(ffmpeg, ["-hide_banner", "-i", path], { encoding: "utf8", stdio: ["ignore", "ignore", "pipe"] }); } catch (e) { if (e.code === "ENOENT") throw new Error("neither ffprobe nor ffmpeg is on PATH (set FFMPEG/FFPROBE); give --wav and --track instead"); err = String(e.stderr ?? ""); }
    const line = (kind) => err.split("\n").find((l) => new RegExp(`Stream #\\d+:(\\d+).*: ${kind}:`).test(l));
    const vl = line("Video"), al = line("Audio");
    if (!vl || !al) throw new Error("file needs one video and one audio stream");
    const idx = (l) => Number(l.match(/Stream #\d+:(\d+)/)[1]), st = (l) => Number(l.match(/\bstart (\d+(?:\.\d+)?)/)?.[1] ?? 0);
    const fpsm = vl.match(/(\d+(?:\.\d+)?) fps/) ?? vl.match(/(\d+(?:\.\d+)?) tbr/);
    vs = { index: idx(vl), fps: Number(fpsm?.[1] ?? 30), start: st(vl) }; as = { index: idx(al), start: st(al) };
  }
  // Resample to constant frame rate by presentation time (a variable-frame-rate source, e.g. a browser capture, repeats frames), so frame f is at start + f/fps.
  const fps = targetFps ?? vs.fps, W = 64, H = 36;
  const audioRaw = execFileSync(ffmpeg, ["-v", "error", "-i", path, "-map", `0:${as.index}`, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "-"], { maxBuffer: 1 << 30 });
  const videoRaw = execFileSync(ffmpeg, ["-v", "error", "-i", path, "-map", `0:${vs.index}`, "-an", "-vf", `fps=${fps},scale=${W}:${H}:flags=area,format=gray`, "-f", "rawvideo", "-"], { maxBuffer: 1 << 30 });
  const frames = Math.floor(videoRaw.length / (W * H)), t = new Float64Array(frames), lum = new Float64Array(frames), flux = new Float64Array(frames);
  for (let f = 0; f < frames; f++) {
    let s = 0, df = 0; for (let i = 0; i < W * H; i++) { const p = videoRaw[f * W * H + i]; s += p; if (f) df += Math.abs(p - videoRaw[(f - 1) * W * H + i]); }
    t[f] = vs.start + f / fps; lum[f] = s / (W * H) / 255; flux[f] = f ? df / (W * H) / 255 : 0;
  }
  const a = new Float32Array(audioRaw.buffer.slice(audioRaw.byteOffset, audioRaw.byteOffset + audioRaw.length - (audioRaw.length % 4)));
  return { audio: { samples: a, sampleRate: 48000, startS: as.start }, luma: { t, v: lum }, flux: { t, v: flux }, fps };
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
  // The flux frame is only good to a window; refine each onset on the samples. DC and the steady bed are estimated from the stretch before the
  // search window (block peaks of |x - median|, their median, so a previous hit's tail in one block does not count); the onset is the first sample
  // whose deviation exceeds max(10% of the local peak, 2.5x the bed). Search: from one window before the rise start (the window centre can sit up to
  // N/2 early of a click inside it) to one window after the flux peak (over a loud bed the rise start can sit far before the real onset). If nothing clears the bar the flux time stands.
  for (const o of r.onsets) {
    const c = Math.round((o.t - startS) * sr), cp = Math.round((o.peakT - startS) * sr), a0 = Math.max(0, c - N), a1 = Math.min(x.length, cp + N), b0 = Math.max(0, a0 - 8 * 512), blocks = [];
    const dc = median(Array.from(x.subarray(b0, a1)));
    for (let q = b0; q + 512 <= a0; q += 512) { let m = 0; for (let i = q; i < q + 512; i++) m = Math.max(m, Math.abs(x[i] - dc)); blocks.push(m); }
    const bed = blocks.length ? median(blocks) : 0;
    let pk = 0; for (let i = a0; i < a1; i++) pk = Math.max(pk, Math.abs(x[i] - dc));
    const bar = Math.max(0.1 * pk, 2.5 * bed);
    for (let i = a0; i < a1; i++) if (Math.abs(x[i] - dc) >= bar && bar < pk) { o.t = startS + i / sr; break; }
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
// One-to-one greedy matching of onsets to anchors (events, or audio onsets) by distance, within windowS. Returns Map anchorIndex -> onsetIndex.
function match(anchors, onsets, windowS) {
  const edges = [];
  anchors.forEach((a, i) => onsets.forEach((o, j) => { const d = Math.abs(o.t - a); if (d <= windowS) edges.push([d, i, j]); }));
  edges.sort((x, y) => x[0] - y[0]);
  const ai = new Map(), oj = new Set();
  for (const [, i, j] of edges) if (!ai.has(i) && !oj.has(j)) { ai.set(i, j); oj.add(j); }
  return ai;
}
const within = (list, t, windowS) => list.filter((o) => Math.abs(o.t - t) <= windowS).length;

// Frame quantisation: a visual change at time tau first shows in the frame stamped at or after tau, so visual - tau is in [0, framePeriod) and the
// measured lag (audio - visual) is biased LOW by up to a frame period. The truth therefore lies in [lag - audioHop/2, lag + framePeriod + audioHop/2];
// a pair is judged on that interval, not on a point.
export function syncAnalyze({ audio, sampleRate, audioStartS = 0, visual, visualKind = "luma", events, profile = "default", leadMs, lagMs, pairWindowMs = 300, minGapMs, allowUnpaired = false }) {
  if (!PROFILES[profile]) throw new Error(`unknown profile ${profile}`);
  const prof = { ...PROFILES[profile] }, custom = leadMs !== undefined || lagMs !== undefined;
  if (leadMs !== undefined) prof.leadMs = leadMs;
  if (lagMs !== undefined) prof.lagMs = lagMs;
  if (custom) { prof.note = `profile ${profile} with the window overridden on the command line`; }
  for (const [n, x] of [["leadMs", prof.leadMs], ["lagMs", prof.lagMs], ["pairWindowMs", pairWindowMs]]) if (!Number.isFinite(x) || x <= 0) throw new Error(`${n} must be a positive number`);
  const gap = minGapMs === undefined ? {} : { minGapS: minGapMs / 1000 };
  const a = audioOnsets(audio, sampleRate, { startS: audioStartS, ...gap }), v = visualOnsets(visual, { kind: visualKind, ...gap });
  const hopMs = a.resolutionMs, periodMs = v.framePeriodMs, win = pairWindowMs / 1000;
  const classify = (lag) => {
    const lo = lag - hopMs / 2, hi = lag + periodMs + hopMs / 2; // truth interval, ms
    if (lo >= -prof.leadMs && hi <= prof.lagMs) return "inside-window";
    if (lo > prof.lagMs) return "audio-late-detectable";
    if (hi < -prof.leadMs) return "audio-early-detectable";
    return "borderline";
  };
  const pairs = [], usedA = new Set(), usedV = new Set();
  const row = (label, eventS, ao, vo, ambiguous) => {
    const r = { label, eventS, audioS: ao?.t ?? null, visualS: vo?.t ?? null, audioVsEventMs: ao && eventS !== null ? (ao.t - eventS) * 1e3 : null, visualVsEventMs: vo && eventS !== null ? (vo.t - eventS) * 1e3 : null, lagMs: ao && vo ? (ao.t - vo.t) * 1e3 : null };
    if (r.lagMs !== null) { r.lagLowMs = r.lagMs - hopMs / 2; r.lagHighMs = r.lagMs + periodMs + hopMs / 2; }
    r.verdict = r.lagMs === null ? (ao ? "no-visual-onset" : vo ? "no-audio-onset" : "no-onsets") : ambiguous ? "ambiguous-pairing" : classify(r.lagMs);
    return r;
  };
  if (events) {
    const et = events.map((e) => e.t), ma = match(et, a.onsets, win), mv = match(et, v.onsets, win);
    events.forEach((e, i) => {
      const ao = ma.has(i) ? a.onsets[ma.get(i)] : undefined, vo = mv.has(i) ? v.onsets[mv.get(i)] : undefined;
      if (ao) usedA.add(ma.get(i)); if (vo) usedV.add(mv.get(i));
      // another onset of the same train inside the window: the nearest need not be the right one
      pairs.push(row(e.label, e.t, ao, vo, within(a.onsets, e.t, win) > 1 || within(v.onsets, e.t, win) > 1));
    });
  } else {
    const m = match(a.onsets.map((o) => o.t), v.onsets, win);
    a.onsets.forEach((ao, i) => {
      if (!m.has(i)) return;
      const vo = v.onsets[m.get(i)]; usedA.add(i); usedV.add(m.get(i));
      pairs.push(row(`a${i}`, null, ao, vo, within(v.onsets, ao.t, win) > 1 || within(a.onsets, vo.t, win) > 1));
    });
  }
  const lags = pairs.map((p) => p.lagMs).filter((x) => x !== null);
  const count = (f) => pairs.filter(f).length;
  const summary = {
    pairs: lags.length, audioOnsets: a.onsets.length, visualOnsets: v.onsets.length,
    unpairedAudio: a.onsets.length - usedA.size, unpairedVisual: v.onsets.length - usedV.size,
    medianLagMs: lags.length ? median(lags) : null, minLagMs: lags.length ? Math.min(...lags) : null, maxLagMs: lags.length ? Math.max(...lags) : null,
    // Spread of the lags: a constant offset is a latency (fixable by a delay), a wide spread is jitter (not fixable by one number).
    spreadMs: lags.length ? Math.max(...lags) - Math.min(...lags) : null,
    outsideWindow: count((p) => /detectable$/.test(p.verdict)), borderline: count((p) => p.verdict === "borderline"), ambiguous: count((p) => p.verdict === "ambiguous-pairing"),
    missing: count((p) => p.lagMs === null),
  };
  const unpaired = summary.unpairedAudio + summary.unpairedVisual;
  summary.pass = summary.pairs > 0 && summary.outsideWindow === 0 && summary.missing === 0 && summary.ambiguous === 0 && (allowUnpaired || unpaired === 0);
  return {
    schema: "aqua-feel.sync.v1", lagConvention: "lagMs = audio onset - visual onset; positive = audio late, negative = audio early. Truth lies in [lagLowMs, lagHighMs] (visual onsets are quantised up to the next frame)",
    profile: { name: custom ? `${profile}+override` : profile, ...prof }, allowUnpaired, pairWindowMs, visualKind, audioResolutionMs: hopMs, framePeriodMs: periodMs,
    audioOnsets: a.onsets, visualOnsets: v.onsets, pairs, summary,
  };
}

export function formatSync(r) {
  const f = (x, d = 1) => (x === null || x === undefined ? "   -  " : x.toFixed(d).padStart(7));
  const L = [`profile ${r.profile.name}: audio may lead by ${r.profile.leadMs} ms, lag by ${r.profile.lagMs} ms (${r.profile.note})`,
    `lag = audio - visual (+ = audio late). The visual onset is quantised up to the next frame (period ${r.framePeriodMs.toFixed(1)} ms), so the true lag lies in [lag - ${(r.audioResolutionMs / 2).toFixed(1)}, lag + ${(r.framePeriodMs + r.audioResolutionMs / 2).toFixed(1)}] ms`,
    "", "label        event s   audio s  visual s  a-ev ms  v-ev ms   lag ms  verdict"];
  for (const p of r.pairs) L.push(`${String(p.label).padEnd(10).slice(0, 10)} ${f(p.eventS, 3)}  ${f(p.audioS, 3)}  ${f(p.visualS, 3)}  ${f(p.audioVsEventMs)}  ${f(p.visualVsEventMs)}  ${f(p.lagMs)}  ${p.verdict}`);
  const s = r.summary, rng = s.minLagMs === null ? "-" : `${s.minLagMs.toFixed(1)}..${s.maxLagMs.toFixed(1)}`;
  L.push("", `${s.pairs} paired, ${s.unpairedAudio} audio and ${s.unpairedVisual} visual onsets unpaired${r.allowUnpaired ? " (allowed)" : " (fails the check unless --allow-unpaired)"}; median lag ${s.medianLagMs === null ? "-" : s.medianLagMs.toFixed(1)} ms, range ${rng} ms (spread ${s.spreadMs === null ? "-" : s.spreadMs.toFixed(1)} ms)`,
    `${s.outsideWindow} outside the window, ${s.borderline} borderline, ${s.ambiguous} ambiguous (another onset within ${r.pairWindowMs} ms), ${s.missing} without both onsets. ${s.pass ? (s.borderline ? `NOT DETECTABLY OUTSIDE THE WINDOW (${s.borderline} borderline: the interval straddles the edge)` : "WITHIN WINDOW") : "NOT WITHIN WINDOW"} by this metric.`,
    "A metric over two onset lists, not a verdict: a person still has to watch and listen (SYNC.md).");
  return L.join("\n");
}
