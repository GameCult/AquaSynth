// The comparison native-diff.mjs applies to one source: wasm samples against native samples, and the LIMITS that decide
// whether the difference is acceptable. Pure (no render, no file), so test native-diff.test.mjs can trip each limit with
// known perturbations. Nothing here was heard.
import { measure } from "./metrics.js";

// LIMITS sit between the largest deltas seen across 13 sources and three conditions (|LUFS| 0.017, |true peak| 0.152 dB,
// centroid 0.3 %) and a deliberate +1 dB gain (LUFS and true peak 1.0 dB); the sample bound applies only with --smoother imp3.
export const LIMITS = { lufsDb: 0.1, truePeakDb: 0.5, centroidPct: 1, relRmsErrDbImp3: -60 };

export const pick = (m) => ({ samplePeak: m.samplePeak, truePeakDb: m.truePeakDb, loudnessLufs: m.loudnessLufs, centroidMedianHz: m.centroidMedianHz, overSamples: m.overSamples, rmsDb: m.rmsDb, crestDb: m.crestDb, noiseToToneDb: m.noiseToToneDb, noiseToToneADb: m.noiseToToneADb, lowMotion: m.lowMotion });

// Row for one source, or { error } on a length mismatch (a length change is a failure, not something to trim).
export function compareRenders(id, w, n) {
  if (w.length !== n.length) return { id, error: `length mismatch: wasm ${w.length}, native ${n.length}` };
  const len = w.length; let maxErr = 0, at = 0, sq = 0, sqRef = 0;
  for (let i = 0; i < len; i++) { const d = Math.abs(w[i] - n[i]); if (d > maxErr) { maxErr = d; at = i; } sq += d * d; sqRef += w[i] * w[i]; }
  const mw = measure(w), mn = measure(n);
  return { id, lenWasm: w.length, lenNative: n.length, maxErr, maxErrAt: at, rmsErr: Math.sqrt(sq / len), relRmsErrDb: 10 * Math.log10(sq / Math.max(sqRef, 1e-30)), wasm: pick(mw), native: pick(mn), measures: { wasm: mw, native: mn } };
}

// Names of the LIMITS this row breaks. `mode` is the --smoother mode; the sample bound applies only to "imp3".
export function violationsOf(r, mode) {
  const v = [];
  if (Math.abs(r.native.loudnessLufs - r.wasm.loudnessLufs) > LIMITS.lufsDb) v.push("loudnessLufs");
  if (Math.abs(r.native.truePeakDb - r.wasm.truePeakDb) > LIMITS.truePeakDb) v.push("truePeakDb");
  if (100 * Math.abs(r.native.centroidMedianHz - r.wasm.centroidMedianHz) / r.wasm.centroidMedianHz > LIMITS.centroidPct) v.push("centroidMedianHz");
  if (mode === "imp3" && r.relRmsErrDb > LIMITS.relRmsErrDbImp3) v.push("relRmsErrDb");
  return v;
}
