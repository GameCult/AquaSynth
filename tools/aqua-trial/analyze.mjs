#!/usr/bin/env node
// Dependency-free report over one or more trial exports: per-variant mean/spread of the "does it land?" rating,
// and whether render peak / rms predicted the ratings. Usage: node analyze.mjs export1.json [export2.json ...]
// Pure functions are exported for test.mjs; the CLI runs only when this file is the entry point.
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { rng, shuffle, SCHEMA } from "./logic.js";

export const mean = (xs) => xs.reduce((a, b) => a + b, 0) / xs.length;
export const sd = (xs) => (xs.length < 2 ? NaN : Math.sqrt(xs.reduce((a, x) => a + (x - mean(xs)) ** 2, 0) / (xs.length - 1)));

export function pearson(xs, ys) {
  const mx = mean(xs), my = mean(ys);
  let sxy = 0, sxx = 0, syy = 0;
  xs.forEach((x, i) => { sxy += (x - mx) * (ys[i] - my); sxx += (x - mx) ** 2; syy += (ys[i] - my) ** 2; });
  return sxx === 0 || syy === 0 ? NaN : sxy / Math.sqrt(sxx * syy);
}

// Average ranks (ties share the mean rank), so Spearman is Pearson over ranks.
export function ranks(xs) {
  const order = xs.map((v, i) => [v, i]).sort((a, b) => a[0] - b[0]);
  const r = new Array(xs.length);
  for (let i = 0; i < order.length;) {
    let j = i;
    while (j + 1 < order.length && order[j + 1][0] === order[i][0]) j++;
    for (let k = i; k <= j; k++) r[order[k][1]] = (i + j) / 2 + 1;
    i = j + 1;
  }
  return r;
}
export const spearman = (xs, ys) => pearson(ranks(xs), ranks(ys));

// Two-sided permutation p-value for Spearman rho; seeded so the report is reproducible.
export function permutationP(xs, ys, rho, rounds = 5000) {
  if (!Number.isFinite(rho)) return NaN;
  const rand = rng(12345);
  let hits = 0;
  for (let i = 0; i < rounds; i++) if (Math.abs(spearman(xs, shuffle(ys, rand))) >= Math.abs(rho) - 1e-12) hits++;
  return (hits + 1) / (rounds + 1);
}

export function predictor(records, key) {
  const xs = records.map((r) => r[key]), ys = records.map((r) => r.rating);
  const rho = spearman(xs, ys);
  const p = permutationP(xs, ys, rho);
  // Threshold chosen before any data exists: |rho| >= 0.5 and p < 0.05. With few records this is deliberately hard to meet.
  const predicts = Number.isFinite(rho) && Math.abs(rho) >= 0.5 && p < 0.05;
  return { key, n: records.length, rho, p, predicts };
}

// An export names the sha-256 of the DSP source the page compiled. One without it predates the field (a record from the
// page build before 2026-11-05 would otherwise read as mix 0), so it is rejected, not guessed. `expectedDsp` is the sha-256
// of the thruster-trial.dsp this checkout holds; exports from a different DSP are kept but flagged in the report.
export function analyze(exports, { expectedDsp } = {}) {
  exports.forEach((e, i) => { if (!/^[0-9a-f]{64}$/.test(e.dspSha256 ?? "")) throw new Error(`export ${i + 1} has no dspSha256: it comes from a page build that did not record which DSP it rendered`); });
  const dspHashes = [...new Set(exports.map((e) => e.dspSha256))];
  const dspMismatch = expectedDsp ? dspHashes.filter((h) => h !== expectedDsp) : [];
  const records = exports.flatMap((e, i) => e.records.map((r) => ({ ...r, listener: i })));
  const byVariant = new Map();
  for (const r of records) (byVariant.get(r.variant) ?? byVariant.set(r.variant, []).get(r.variant)).push(r);
  const variants = [...byVariant.entries()].map(([id, rs]) => {
    const ratings = rs.map((r) => r.rating);
    const words = {};
    for (const r of rs) { const w = r.word.toLowerCase(); words[w] = (words[w] ?? 0) + 1; }
    return {
      id, noise: rs[0].params.noise, mix: rs[0].params.mix ?? 0, n: ratings.length, mean: mean(ratings), sd: sd(ratings),
      min: Math.min(...ratings), max: Math.max(...ratings),
      peak: mean(rs.map((r) => r.peak)), rms: mean(rs.map((r) => r.rms)), words,
    };
  }).sort((a, b) => a.mix - b.mix || b.noise - a.noise);
  const predictors = ["peak", "rms"].map((k) => predictor(records, k));
  // Context: the manipulated variable itself, so a peak/rms "prediction" can be told apart from noise-amount doing the work.
  // Only the current mix (mix 0): v5noise100 shares noise 100 with noise100 and would pool two mixes into one noise level.
  const noise = predictor(records.filter((r) => (r.params.mix ?? 0) === 0).map((r) => ({ ...r, noise: r.params.noise })), "noise");
  return { listeners: exports.length, records: records.length, variants, predictors, noise, dspHashes, dspMismatch };
}

const f = (x, d = 3) => (Number.isFinite(x) ? x.toFixed(d) : "n/a");

export function format(rep) {
  const out = [`${rep.listeners} listener export(s), ${rep.records} ratings`, "", "variant       mix noise%    n   mean     sd  min max    peak     rms  words"];
  for (const v of rep.variants) {
    const words = Object.entries(v.words).sort((a, b) => b[1] - a[1]).map(([w, c]) => (c > 1 ? `${w}x${c}` : w)).join(",");
    out.push(`${v.id.padEnd(12)} ${String(v.mix).padStart(3)} ${String(v.noise).padStart(5)} ${String(v.n).padStart(4)} ${f(v.mean, 2).padStart(6)} ${f(v.sd, 2).padStart(6)} ${String(v.min).padStart(4)} ${String(v.max).padStart(3)} ${f(v.peak).padStart(7)} ${f(v.rms).padStart(7)}  ${words}`);
  }
  out.push("", "Did render level predict the rating? (Spearman rho over individual ratings, permutation p; predicts = |rho|>=0.5 and p<0.05)");
  for (const p of rep.predictors) out.push(`  ${p.key.padEnd(5)} rho ${f(p.rho, 2).padStart(6)}  p ${f(p.p, 4)}  n ${p.n}  -> ${p.predicts ? "PREDICTS" : "no evidence it predicts"}`);
  out.push(`  (context) noise% rho ${f(rep.noise.rho, 2)}  p ${f(rep.noise.p, 4)}  n ${rep.noise.n} (mix 0 only)`);
  if (rep.dspHashes.length > 1) out.push("", `WARNING: exports come from ${rep.dspHashes.length} different DSP sources (${rep.dspHashes.map((h) => h.slice(0, 7)).join(", ")}); do not read them as one trial.`);
  if (rep.dspMismatch.length) out.push("", `WARNING: ${rep.dspMismatch.map((h) => h.slice(0, 7)).join(", ")} is not the sha-256 of this checkout's thruster-trial.dsp; the export was rendered from different DSP text.`);
  out.push("", "Peak and rms fall with noise amount here (noise is not loudness-compensated), so a level effect and a noise effect are confounded, and this script does not separate them.");
  return out.join("\n");
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const files = process.argv.slice(2);
  if (!files.length) { console.error("usage: node analyze.mjs export.json [more.json ...]"); process.exit(2); }
  const exports = files.map((p) => {
    const e = JSON.parse(readFileSync(p, "utf8"));
    if (e.schema !== SCHEMA) throw new Error(`${p}: schema ${e.schema}, expected ${SCHEMA}`);
    return e;
  });
  const expectedDsp = createHash("sha256").update(readFileSync(join(dirname(fileURLToPath(import.meta.url)), "thruster-trial.dsp"))).digest("hex");
  console.log(format(analyze(exports, { expectedDsp })));
}
