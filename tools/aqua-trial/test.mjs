// Node checks of the trial: design, validation, export shape, analyzer maths, and (through the same faustwasm runtime
// the page loads) the trial patch itself. It never runs the page in a browser and never plays or hears anything.
// Usage: (cd ../aqua-playground && npm ci) && node build.mjs && node test.mjs
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { makeDesign, variants, stats, cleanWord, validRating, buildExport, renderVariant, rng, shuffle, SCHEMA, PATCH } from "./logic.js";
import { analyze, format, mean, sd, spearman, ranks, pearson } from "./analyze.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const dist = resolve(process.argv[2] ?? join(here, "dist"));
let n = 0;
const t = async (name, fn) => { await fn(); n++; console.log(`ok   ${name}`); };

await t("design: every unordered pair once, both positions used, replayable from the seed", () => {
  const d = makeDesign(7);
  assert.equal(d.length, 6);
  const keys = d.map((x) => [x.A.id, x.B.id].sort().join("|"));
  assert.equal(new Set(keys).size, 6);
  for (const v of variants()) assert.equal(d.filter((x) => x.A.id === v.id || x.B.id === v.id).length, 3);
  assert.deepEqual(makeDesign(7), d);
  // position is not tied to variant: over many seeds each variant lands in A about half the time
  const inA = Object.fromEntries(variants().map((v) => [v.id, 0]));
  for (let s = 0; s < 2000; s++) for (const x of makeDesign(s)) inA[x.A.id]++;
  for (const c of Object.values(inA)) assert.ok(Math.abs(c / 6000 - 0.5) < 0.05, `A share ${c / 6000}`);
  assert.notDeepEqual(makeDesign(1).map((x) => x.A.id), makeDesign(2).map((x) => x.A.id));
});

await t("variants: rpm 600 and noise 100/50/20/0", () => {
  assert.deepEqual(variants().map((v) => v.params), [100, 50, 20, 0].map((noise) => ({ rpm: 600, noise })));
});

await t("stats: peak, rms, non-finite counted and excluded", () => {
  const s = stats([0, 1, -1, 0]);
  assert.equal(s.peak, 1); assert.ok(Math.abs(s.rms - Math.sqrt(0.5)) < 1e-12);
  assert.equal(stats([1, NaN, Infinity]).nonFinite, 2);
});

await t("one word: trimmed, no inner whitespace, bounded; ratings 1-5 integers", () => {
  assert.equal(cleanWord("  hollow "), "hollow");
  for (const bad of ["", "   ", "two words", "a".repeat(33), null, undefined]) assert.equal(cleanWord(bad), null);
  assert.deepEqual([0, 1, 5, 6, 2.5, "3"].map(validRating), [false, true, true, false, false, false]);
});

const fakeExport = (seed, rate) => {
  const design = makeDesign(seed), rand = rng(seed);
  const renders = { noise100: { peak: 0.19, rms: 0.073 }, noise50: { peak: 0.15, rms: 0.05 }, noise20: { peak: 0.12, rms: 0.04 }, noise0: { peak: 0.1, rms: 0.035 } };
  const answers = design.map(() => Object.fromEntries(["A", "B"].map((p) => [p, { rating: 1 + Math.floor(rand() * 5), word: "w", time: "2026-11-04T00:00:00Z", plays: 1 }])));
  return { design, renders, answers, doc: buildExport({ seed, design, renders, answers, sampleRate: rate, userAgent: "test-ua", exportedAt: "2026-11-04T00:00:00Z" }) };
};

await t("export: one record per heard sound with the required fields", () => {
  const { doc, design, answers } = fakeExport(3, 44100);
  assert.equal(doc.schema, SCHEMA); assert.equal(doc.sampleRate, 44100); assert.equal(doc.userAgent, "test-ua");
  assert.equal(doc.records.length, 12);
  for (const r of doc.records) for (const k of ["patch", "params", "rating", "word", "time", "peak", "rms", "variant", "trial", "position"]) assert.ok(r[k] !== undefined, k);
  const r = doc.records[0];
  assert.equal(r.variant, design[0].A.id); assert.equal(r.rating, answers[0].A.rating); assert.equal(r.patch, PATCH);
  assert.deepEqual(JSON.parse(JSON.stringify(doc)), doc);
});

await t("export: unanswered trials are omitted, not invented", () => {
  const { design, renders, answers } = fakeExport(3, 48000);
  const doc = buildExport({ seed: 3, design, renders, answers: answers.slice(0, 2), sampleRate: 48000, userAgent: "u", exportedAt: "x" });
  assert.equal(doc.records.length, 4);
});

await t("analyze maths: ranks with ties, spearman, pearson, sd", () => {
  assert.deepEqual(ranks([10, 20, 20, 30]), [1, 2.5, 2.5, 4]);
  assert.equal(spearman([1, 2, 3, 4], [10, 20, 30, 40]), 1);
  assert.equal(spearman([1, 2, 3, 4], [4, 3, 2, 1]), -1);
  assert.ok(Number.isNaN(pearson([1, 1, 1], [1, 2, 3])));
  assert.ok(Math.abs(sd([2, 4, 4, 4, 5, 5, 7, 9]) - 2.138089935) < 1e-8);
  assert.equal(mean([1, 2, 3]), 2);
});

const synth = (ratingOf, listeners = 12) => {
  const exports = [];
  for (let l = 0; l < listeners; l++) {
    const e = fakeExport(100 + l, 48000).doc;
    e.records.forEach((r) => { r.rating = ratingOf(r, l); });
    exports.push(e);
  }
  return exports;
};

await t("analyze: ratings that follow rms are reported as predicted by peak and rms", () => {
  const rep = analyze(synth((r) => (r.rms > 0.06 ? 5 : r.rms > 0.045 ? 4 : r.rms > 0.037 ? 2 : 1)));
  assert.equal(rep.records, 144);
  assert.deepEqual(rep.variants.map((v) => v.noise), [100, 50, 20, 0]);
  assert.ok(rep.variants[0].mean > rep.variants[3].mean);
  for (const p of rep.predictors) assert.ok(p.predicts && p.rho > 0.9, `${p.key} rho ${p.rho}`);
  assert.match(format(rep), /PREDICTS/);
});

await t("analyze: ratings unrelated to level are not reported as predicted", () => {
  const rand = rng(99);
  const rep = analyze(synth(() => 1 + Math.floor(rand() * 5)));
  for (const p of rep.predictors) assert.ok(!p.predicts, `${p.key} rho ${p.rho} p ${p.p}`);
  assert.match(format(rep), /no evidence/);
});

await t("analyze: per-variant mean and spread are computed per variant", () => {
  const rep = analyze(synth((r) => (r.params.noise === 100 ? 5 : r.params.noise === 50 ? 3 : 1 + (r.trial % 2) * 2)));
  const by = Object.fromEntries(rep.variants.map((v) => [v.id, v]));
  assert.equal(by.noise100.mean, 5); assert.equal(by.noise100.sd, 0);
  assert.equal(by.noise50.mean, 3);
  assert.ok(by.noise20.sd > 0 && by.noise20.min === 1 && by.noise20.max === 3);
});

await t("analyze CLI: runs on export files and rejects a foreign schema", async () => {
  const { execFileSync, spawnSync } = await import("node:child_process");
  const { mkdtempSync, writeFileSync } = await import("node:fs");
  const { tmpdir } = await import("node:os");
  const d = mkdtempSync(join(tmpdir(), "trial-"));
  const a = join(d, "a.json"), b = join(d, "b.json");
  writeFileSync(a, JSON.stringify(fakeExport(5, 48000).doc));
  writeFileSync(b, JSON.stringify({ ...fakeExport(5, 48000).doc, schema: "other" }));
  assert.match(execFileSync("node", [join(here, "analyze.mjs"), a], { encoding: "utf8" }), /1 listener export/);
  assert.notEqual(spawnSync("node", [join(here, "analyze.mjs"), b]).status, 0);
  assert.equal(spawnSync("node", [join(here, "analyze.mjs")]).status, 2);
});

// --- the patch, through the runtime the page loads ---
const { instantiateFaustModuleFromFile, LibFaust, FaustCompiler, FaustMonoDspGenerator } =
  await import(pathToFileURL(join(dist, "vendor/faustwasm/index.js")).href);
const { controlsOf } = await import(pathToFileURL(join(dist, "ui.js")).href);
const compiler = new FaustCompiler(new LibFaust(await instantiateFaustModuleFromFile(join(dist, "vendor/libfaust-wasm/libfaust-wasm.js"))));
const compile = async (path, name) => {
  const g = new FaustMonoDspGenerator();
  assert.ok(await g.compile(compiler, name, readFileSync(path, "utf8"), "-ftz 2"), compiler.getErrorMessage());
  return { g, controls: controlsOf(JSON.parse(g.factory.json)) };
};
const trial = await compile(join(here, "thruster-trial.dsp"), "thruster_trial");
const base = await compile(join(here, "../../patches/thruster/thruster.dsp"), "thruster");
const SR = 48000;

await t("patch: exposes rpm and noise controls", () => {
  assert.ok(trial.controls.some((c) => c.label === "noise" && c.init === 100));
  assert.ok(trial.controls.some((c) => c.label === "rpm"));
});

await t("patch: noise=100 is sample-identical to patches/thruster/thruster.dsp at rpm 600", async () => {
  const a = await renderVariant(trial.g, trial.controls, { params: { rpm: 600, noise: 100 } }, SR, 2);
  const b = await renderVariant(base.g, base.controls, { params: { rpm: 600 } }, SR, 2);
  assert.equal(a.samples.length, b.samples.length);
  let maxDiff = 0; for (let i = 0; i < a.samples.length; i++) maxDiff = Math.max(maxDiff, Math.abs(a.samples[i] - b.samples[i]));
  assert.ok(maxDiff < 1e-6, `max diff ${maxDiff}`);
});

await t("patch: all four variants finite and non-silent, rendered at 44100 and 48000, noise 0 is deterministic", async () => {
  for (const rate of [44100, 48000]) {
    const seen = [];
    for (const v of variants()) {
      const r = await renderVariant(trial.g, trial.controls, v, rate);
      assert.equal(r.samples.length, rate * 4);
      assert.equal(r.nonFinite, 0); assert.ok(r.peak > 1e-3, `${v.id} peak ${r.peak}`);
      seen.push(r);
    }
    console.log("     " + variants().map((v, i) => `${v.id} peak ${seen[i].peak.toFixed(4)} rms ${seen[i].rms.toFixed(4)}`).join(" | ") + ` @${rate}`);
  }
  const x = await renderVariant(trial.g, trial.controls, variants()[3], SR, 1), y = await renderVariant(trial.g, trial.controls, variants()[3], SR, 1);
  assert.deepEqual(x.samples, y.samples);
});

await t("patch: noise genuinely removes the noise layers (rms falls monotonically 100>50>20>0, rumble remains)", async () => {
  const rms = [];
  for (const v of variants()) rms.push((await renderVariant(trial.g, trial.controls, v, SR)).rms);
  assert.ok(rms[0] > rms[1] && rms[1] > rms[2] && rms[2] > rms[3], rms.join(" > "));
  assert.ok(rms[3] > 1e-3);
});

console.log(`${n} passed`);
