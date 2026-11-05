// Checks of the meter itself on synthetic signals with known answers, the gate's waiver bookkeeping, the WAV writer, the
// gallery, and the catalog. It renders no patch; `node feel.mjs run` does that. Nothing here hears anything.
// Usage: node test.mjs
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { SR, measure, truePeak, samplePeak, integratedLoudness, clipping, fft, timing } from "./metrics.js";
import { gate } from "./gate.js";
import { gallery } from "./gallery.js";
import { wavFloat32 } from "./render.js";
import { patchItems, dingItems } from "./catalog.js";

const here = dirname(fileURLToPath(import.meta.url));
let n = 0;
const t = (name, fn) => { fn(); n++; console.log(`ok   ${name}`); };
const sine = (hz, amp, sec, ph = 0) => Float32Array.from({ length: Math.round(SR * sec) }, (_, i) => amp * Math.sin(2 * Math.PI * hz * i / SR + ph));
let seed = 1; const rand = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 2 ** 32 - 0.5; };
const noise = (amp, sec) => Float32Array.from({ length: Math.round(SR * sec) }, () => amp * 2 * rand());
const decay = (x, tau) => x.map((v, i) => v * Math.exp(-i / SR / tau));
const near = (a, b, tol, msg) => assert.ok(Math.abs(a - b) <= tol, `${msg}: ${a} vs ${b} (+/-${tol})`);

t("fft: a bin-centred cosine lands in its bin", () => {
  const re = Float64Array.from({ length: 256 }, (_, i) => Math.cos(2 * Math.PI * 16 * i / 256)), im = new Float64Array(256);
  fft(re, im); near(Math.hypot(re[16], im[16]), 128, 1e-9, "bin 16"); near(Math.hypot(re[17], im[17]), 0, 1e-9, "bin 17");
});

t("levels: a 0.5 sine reads peak 0.5, rms -9.03 dB, crest 3.01 dB, loudness -9.7 LUFS (BS.1770)", () => {
  const m = measure(sine(440, 0.5, 2));
  near(m.samplePeak, 0.5, 1e-6, "peak"); near(m.rmsDb, -9.03, 0.05, "rms"); near(m.crestDb, 3.01, 0.05, "crest"); near(m.loudnessLufs, -9.7, 0.2, "LUFS");
});

t("loudness: -6 dB is -6 LU; silence is null", () => {
  near(integratedLoudness(sine(1000, 0.5, 2)) - integratedLoudness(sine(1000, 0.25, 2)), 6.02, 0.01, "6 dB step");
  assert.equal(integratedLoudness(new Float32Array(SR)), null);
});

t("loudness: gating drops the silent tail (the naive 3 s mean would read 7.8 dB lower); block-edge dilution stays under 2 dB", () => {
  const x = new Float32Array(SR * 3); x.set(sine(1000, 0.5, 0.5));
  near(integratedLoudness(x), integratedLoudness(sine(1000, 0.5, 0.5)), 2, "gated vs bare");
});

t("true peak: a tone sampled between its crests exceeds the sample peak; a sample-aligned one does not", () => {
  const x = sine(12000, 0.9, 0.1, Math.PI / 4); // samples sit 45 degrees off the crests
  assert.ok(samplePeak(x) < 0.7 && truePeak(x) > 0.85 && truePeak(x) < 0.95, `${samplePeak(x)} ${truePeak(x)}`);
  const y = sine(1000, 0.5, 0.1); near(truePeak(y), 0.5, 0.01, "oversampling must not invent peaks on a slow tone");
});

t("clipping: counts samples over 1.0 and flat-top runs, ignores a clean 0.99", () => {
  const x = sine(100, 2, 0.1).map((v) => Math.max(-1.2, Math.min(1.2, v)));
  const c = clipping(x); assert.ok(c.overSamples > 100 && c.flatTopRuns > 0);
  const y = clipping(sine(100, 0.99, 0.1)); assert.equal(y.overSamples, 0); assert.equal(y.flatTopRuns, 0);
});

t("timing: onset latency is where the sound starts, attack is the first-note rise", () => {
  const x = new Float32Array(SR); x.set(sine(1000, 0.5, 0.5).map((v, i) => v * Math.min(1, i / (0.01 * SR))), Math.round(0.02 * SR));
  const tm = timing(x); near(tm.onsetMs, 20 + 1, 1.5, "onset"); near(tm.attackMs, 8, 1.5, "10-90% of a 10 ms ramp is 8 ms");
});

t("timing: a two-note event reports the first note's attack, even when a louder note follows 200 ms later before the first has decayed", () => {
  const x = new Float32Array(SR * 2);
  x.set(decay(sine(500, 0.3, 0.6), 0.5)); const b = decay(sine(800, 0.9, 0.5), 0.1); for (let i = 0; i < b.length; i++) x[Math.round(0.2 * SR) + i] += b[i];
  assert.ok(timing(x).attackMs < 3, `${timing(x).attackMs}`);
});

t("timing: a swell longer than the 150 ms attack window is flagged window-limited", () => {
  assert.equal(timing(sine(220, 0.5, 2).map((v, i) => v * i / (2 * SR))).attackWindowLimited, true);
  assert.equal(timing(decay(sine(220, 0.5, 1), 0.2)).attackWindowLimited, false);
});

t("attack: a low tone's ramp is not mistaken for its zero crossings (261 Hz, 30 ms ramp; 220 Hz, 2 s swell)", () => {
  const ramp = (hz, ms) => { const x = sine(hz, 0.5, 1); return x.map((v, i) => v * Math.min(1, i / (ms / 1000 * SR))); };
  for (const hz of [130, 261.6, 523, 1000]) near(timing(ramp(hz, 30)).attackMs, 24, 3, `${hz} Hz, 30 ms ramp`);
  assert.ok(timing(sine(220, 0.5, 2).map((v, i) => v * i / (2 * SR))).attackMs > 100, "a 2 s swell is not a click");
});

t("silent: a -80 dBFS tone is silent, a -60 dBFS tone is not", () => {
  assert.equal(measure(sine(1000, 1e-4, 1)).silent, true); assert.equal(measure(sine(1000, 1e-3, 1)).silent, false);
});

t("noise-to-tone: a sine reads far tonal, white noise far noisy, and the A-weighted ratio sees a quiet 1 kHz hiss under a loud 40 Hz tone", () => {
  assert.ok(measure(sine(440, 0.5, 2)).noiseToToneDb < -30);
  assert.ok(measure(noise(0.3, 2)).noiseToToneDb > 20);
  const low = sine(40, 0.5, 3), hiss = noise(0.02, 3), x = low.map((v, i) => v + hiss[i]), m = measure(x);
  assert.ok(m.noiseToToneADb > m.noiseToToneDb + 8, `flat ${m.noiseToToneDb}, A-weighted ${m.noiseToToneADb}`);
});

t("noise-to-tone: a noise-excited resonance is not called a tone, a steady sine is (temporal smoothness)", () => {
  // two-pole resonator at 600 Hz, Q~25, fed with noise: spectrally narrow but randomly modulated
  const w = 2 * Math.PI * 600 / SR, r = 1 - w / 50, a1 = -2 * r * Math.cos(w), a2 = r * r, e = noise(1, 3), y = new Float32Array(e.length);
  for (let i = 2; i < y.length; i++) y[i] = e[i] - a1 * y[i - 1] - a2 * y[i - 2];
  const peak = samplePeak(y); const ring = y.map((v) => (0.4 * v) / peak);
  assert.ok(measure(ring).noiseToToneDb > measure(sine(600, 0.4, 3)).noiseToToneDb + 25, "ringing noise must read noisier than a sine");
});

t("low motion: a held tone and held noise are flagged; a decaying bell is not; under 1 s is not judged", () => {
  assert.equal(measure(sine(440, 0.5, 2)).lowMotion, true);
  assert.equal(measure(noise(0.3, 2)).lowMotion, true);
  assert.equal(measure(decay(sine(880, 0.5, 2), 0.25)).lowMotion, false);
  assert.equal(measure(sine(440, 0.5, 0.6)).lowMotion, false);
});

t("centroid: follows a sweep upward", () => {
  const x = Float32Array.from({ length: SR * 2 }, (_, i) => 0.5 * Math.sin(2 * Math.PI * (200 * i / SR + 1000 * (i / SR) ** 2 / 2)));
  const c = measure(x).centroidCurve; assert.ok(c.at(-2).v > c[1].v * 3, `${c[1].v} -> ${c.at(-2).v}`);
});

t("silence and non-finite: reported, not crashed on", () => {
  assert.equal(measure(new Float32Array(SR)).silent, true);
  const x = sine(440, 0.5, 0.1); x[10] = NaN; const m = measure(x); assert.equal(m.broken, true); assert.equal(m.nonFinite, 1);
});

const item = (id, group, metrics) => ({ id, group, metrics: { nonFinite: 0, silent: false, overSamples: 0, truePeakDb: -6, lowMotion: false, ...metrics } });
const rules = [{ group: "all", metric: "overSamples", equals: 0, why: "clips" }, { group: "ding", metric: "onsetMs", max: 5, why: "late" }, { group: "all", metric: "truePeakDb", max: -1, level: "warn", why: "headroom" }];

t("gate: failing rule fails; group scoping respected; warn does not fail", () => {
  const g = gate({ items: [item("a", "patch", { overSamples: 3, onsetMs: 50 }), item("b", "ding", { onsetMs: 50 }), item("c", "patch", { truePeakDb: -0.5 })] }, { rules });
  assert.equal(g.pass, false); assert.deepEqual(g.failures.map((f) => `${f.id}:${f.metric}`), ["a:overSamples", "b:onsetMs"]); assert.equal(g.warnings.length, 1);
});

t("gate: a waiver downgrades a failure, a stale waiver fails, partial runs skip the stale check", () => {
  const rep = { items: [item("a", "patch", { overSamples: 3 }), item("c", "patch", {})] };
  const waivers = [{ id: "a", metric: "overSamples", reason: "r" }];
  assert.equal(gate(rep, { rules, waivers }).pass, true); assert.equal(gate(rep, { rules, waivers }).waived.length, 1);
  const stale = gate(rep, { rules, waivers: [...waivers, { id: "c", metric: "overSamples", reason: "r" }] });
  assert.equal(stale.pass, false); assert.match(stale.failures[0].why, /stale waiver/);
  assert.equal(gate(rep, { rules, waivers: [{ id: "c", metric: "overSamples", reason: "r" }] }, { partial: true }).pass, false); // a still fails
});

t("gate: a waiver covers only up to its stated bound; a numeric waiver without a bound covers nothing", () => {
  const r = [{ group: "all", metric: "truePeakDb", max: 0, why: "tp" }];
  const mk = (tp) => ({ items: [item("g", "patch", { truePeakDb: tp })] });
  const w = [{ id: "g", metric: "truePeakDb", max: 1, reason: "r" }];
  assert.equal(gate(mk(0.9), { rules: r, waivers: w }).pass, true);
  assert.equal(gate(mk(6), { rules: r, waivers: w }).pass, false);
  assert.equal(gate(mk(0.9), { rules: r, waivers: [{ id: "g", metric: "truePeakDb", reason: "r" }] }).pass, false);
  assert.equal(gate(mk(null), { rules: r, waivers: w }).pass, false);
});

t("gate: a rule whose id matches no item fails a full run (disarmed guard) and is skipped on a partial run; a missing metric fails", () => {
  const th = { rules: [{ group: "patch", id: "thruster@rpm600", metric: "truePeakDb", max: 0, why: "w" }] };
  const rep = { items: [item("thr@rpm600", "patch", {})] };
  assert.equal(gate(rep, th).pass, false); assert.match(gate(rep, th).failures[0].detail, /matches no item/);
  assert.equal(gate(rep, th, { partial: true }).pass, true);
  assert.equal(gate({ items: [item("a", "patch", {})] }, { rules: [{ group: "all", metric: "lowMotion", equals: false, why: "w" }] }).pass, true);
  const gone = item("a", "patch", {}); delete gone.metrics.lowMotion;
  assert.equal(gate({ items: [gone] }, { rules: [{ group: "all", metric: "lowMotion", equals: false, why: "w" }] }).pass, false);
});

t("gate: a silent item fails once and does not also trip every other rule", () => {
  const g = gate({ items: [{ id: "s", group: "patch", metrics: { silent: true, nonFinite: 0, samplePeak: 0 } }] }, { rules: [{ group: "patch", metric: "onsetMs", max: 5, why: "w" }] });
  assert.equal(g.failures.length, 1); assert.equal(g.failures[0].metric, "silent");
});

t("gate: a render error, NaN output, or glob rule id is handled", () => {
  const g = gate({ items: [{ id: "x", group: "patch", error: "boom" }, { id: "y", group: "patch", metrics: { broken: true, nonFinite: 2 } }] }, { rules });
  assert.equal(g.failures.length, 2);
  const g2 = gate({ items: [item("thruster@rpm300", "patch", { onsetMs: 1, ntr: 9 })] }, { rules: [{ group: "patch", id: "thruster@*", metric: "ntr", max: 5, why: "w" }] });
  assert.equal(g2.pass, false);
});

t("thresholds.json: every rule names a metric and a why; every waiver names a reason and a follow-up", () => {
  const th = JSON.parse(readFileSync(join(here, "thresholds.json"), "utf8"));
  for (const r of th.rules) { assert.ok(r.metric && r.why && ["all", "patch", "ding"].includes(r.group), JSON.stringify(r)); assert.ok(["min", "max", "equals"].some((k) => k in r)); }
  for (const w of th.waivers) assert.ok(w.id && w.metric && w.reason && /^aqua-body:follow_up:/.test(w.followUp), JSON.stringify(w));
  for (const w of th.waivers) {
    const rules = th.rules.filter((r) => r.metric === w.metric && (r.group === "all" || r.group === "patch"));
    assert.ok(rules.every((r) => r.equals !== undefined || "max" in w || "min" in w), `numeric waiver without a bound: ${JSON.stringify(w)}`);
  }
});

t("wav: 32-bit float mono header, length, and samples above 1.0 survive", () => {
  const b = wavFloat32(Float32Array.from([0, 2.5, -1.5]));
  assert.equal(b.toString("ascii", 0, 4), "RIFF"); assert.equal(b.readUInt16LE(20), 3); assert.equal(b.readUInt16LE(34), 32); assert.equal(b.readUInt32LE(24), SR);
  assert.equal(b.readUInt32LE(40), 12); assert.equal(b.readFloatLE(48), 2.5); assert.equal(b.length, 44 + 12);
});

t("catalog: the playground patches with the thruster fanned to four rpm, and 9 events on 4 instruments (36 Dings)", () => {
  const p = patchItems(), d = dingItems();
  assert.equal(p.filter((i) => i.id.startsWith("thruster")).length, 4); assert.ok(p.length >= 41 && p.every((i) => i.source.includes("process")));
  assert.equal(d.length, 36); assert.ok(new Set(d.map((i) => i.id)).size === 36);
});

t("gallery: says on its face that nothing was heard, escapes ids, marks failing metrics", () => {
  const rep = { sampleRate: SR, libfaust: "x", gate: { pass: false, failures: [{ id: "a<b", metric: "overSamples" }], waived: [], warnings: [] }, items: [{ id: "a<b", group: "patch", wav: "wav/a.wav", metrics: { ...measure(sine(440, 0.5, 0.3)), overSamples: 9 } }, { id: "z", group: "ding", error: "no" }] };
  const html = gallery(rep);
  assert.match(html, /Nothing here was heard/); assert.ok(!html.includes("a<b")); assert.match(html, /class="fail"/); assert.match(html, /did not render/);
});

console.log(`${n} passed`);
