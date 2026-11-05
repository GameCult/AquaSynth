// Before/after table and the listening-trial correlation.
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { mean, spearman, permutationP } from "../aqua-trial/analyze.mjs";
import { variants, SECONDS } from "../aqua-trial/logic.js";
import { measure } from "./metrics.js";
import { loadRuntime, compile, renderOnce } from "./render.js";

export const COMPARE_METRICS = ["samplePeak", "truePeakDb", "overSamples", "loudnessLufs", "crestDb", "centroidMedianHz", "noiseToToneDb", "noiseToToneADb", "onsetMs", "attackMs", "lowMotion"];
const f = (v) => (typeof v === "number" ? (Number.isInteger(v) ? String(v) : v.toFixed(2)) : String(v));

export function compareTable(before, after, only) {
  const b = new Map(before.items.map((i) => [i.id, i])), out = [];
  for (const a of after.items) {
    if ((only && !a.id.includes(only)) || !b.has(a.id) || !a.metrics || !b.get(a.id).metrics) continue;
    out.push(`${a.id}`);
    for (const k of COMPARE_METRICS) {
      const x = b.get(a.id).metrics[k], y = a.metrics[k];
      if (x === undefined) continue;
      const d = typeof x === "number" && typeof y === "number" ? (y - x >= 0 ? "+" : "") + (y - x).toFixed(2) : x === y ? "" : "changed";
      out.push(`  ${k.padEnd(18)} ${f(x).padStart(10)} -> ${f(y).padStart(10)}  ${d}`);
    }
  }
  return out.length ? out.join("\n") : "no common items";
}

// Joins trial exports (aqua-trial) to feel metrics by rendering the same four variants, then asks the trial's own question
// of each metric: does it rank-correlate with the ratings (|rho| >= 0.5 and p < 0.05, fixed before any data)? With few
// listeners this is deliberately hard to meet; "no evidence" is the expected answer, and it is not evidence of absence.
export function correlate(exports, trialDir) {
  return correlateAsync(exports, trialDir);
}
async function correlateAsync(exports, trialDir) {
  const rt = await loadRuntime(), entry = await compile(rt, "thruster_trial", readFileSync(join(trialDir, "thruster-trial.dsp"), "utf8"));
  const metricsBy = {};
  for (const v of variants()) metricsBy[v.id] = measure((await renderOnce(entry, v.params, SECONDS)).samples);
  const records = exports.flatMap((e) => e.records);
  const lines = [`${exports.length} listener export(s), ${records.length} ratings; variants rendered here at ${SECONDS} s`, "", "metric              rho      p   n  -> predicts?"];
  for (const k of ["noiseToToneDb", "noiseToToneADb", "loudnessLufs", "samplePeak", "rms", "centroidMedianHz", "fluxMean", "crestDb"]) {
    const xs = records.map((r) => metricsBy[r.variant][k]), ys = records.map((r) => r.rating), rho = spearman(xs, ys), p = permutationP(xs, ys, rho);
    lines.push(`${k.padEnd(18)} ${Number.isFinite(rho) ? rho.toFixed(2).padStart(6) : "   n/a"} ${Number.isFinite(p) ? p.toFixed(3).padStart(6) : "   n/a"} ${String(xs.length).padStart(3)}  -> ${Number.isFinite(rho) && Math.abs(rho) >= 0.5 && p < 0.05 ? "PREDICTS" : "no evidence it predicts"}`);
  }
  lines.push("", `mean rating by variant: ${variants().map((v) => `${v.id} ${mean(records.filter((r) => r.variant === v.id).map((r) => r.rating)).toFixed(2)}`).join(", ")}`);
  lines.push("Four variants are four distinct metric values, so every rank correlation here is really a statement about the variant ordering. Nothing was heard by this script.");
  return lines.join("\n");
}
