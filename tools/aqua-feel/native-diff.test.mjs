// Checks that each native-diff LIMITS entry trips on a known perturbation and stays quiet on a clean or in-tolerance pair.
// The pair is synthetic (a seeded tone-and-noise burst against a perturbed copy); no render, no dotnet, nothing heard.
// Usage: node native-diff.test.mjs
import assert from "node:assert/strict";
import { SR } from "./metrics.js";
import { LIMITS, compareRenders, violationsOf } from "./native-compare.js";

let n = 0;
const t = (name, fn) => { fn(); n++; console.log(`ok   ${name}`); };

// Tones at 110/330/990 Hz with a slow envelope plus seeded noise: broadband enough that a one-sample shift is far from silent.
const sig = (() => {
  let s = 12345; const r = () => { s = (Math.imul(s, 1664525) + 1013904223) >>> 0; return s / 4294967296 - 0.5; };
  const x = new Float32Array(SR * 3);
  for (let i = 0; i < x.length; i++) { const t = i / SR; x[i] = 0.25 * (0.6 * Math.sin(2 * Math.PI * 110 * t) + 0.3 * Math.sin(2 * Math.PI * 330 * t) + 0.2 * Math.sin(2 * Math.PI * 990 * t)) * (0.7 + 0.3 * Math.sin(2 * Math.PI * 1.3 * t)) + 0.05 * r(); }
  return x;
})();
const gain = (x, db) => x.map((v) => v * Math.pow(10, db / 20));
const shift = (x, k) => { const y = new Float32Array(x.length); y.set(x.subarray(0, x.length - k), k); return y; };
const smooth = (x) => x.map((v, i) => 0.5 * (v + (x[i - 1] ?? 0))); // two-point average: a mild low-pass that moves the spectral centroid
const trips = (native, mode = "library") => violationsOf(compareRenders("probe", sig, native), mode);

t("identical renders trip nothing in either mode", () => {
  assert.deepEqual(trips(sig), []); assert.deepEqual(trips(sig, "imp3"), []);
});

t("a +1 dB gain trips loudness and true peak, and neither the centroid nor (library mode) the sample bound", () => {
  assert.deepEqual(trips(gain(sig, 1)).sort(), ["loudnessLufs", "truePeakDb"]);
});

t("a +1 dB gain also trips the sample bound under imp3 (relative error about -18 dB)", () => {
  assert.deepEqual(trips(gain(sig, 1), "imp3").sort(), ["loudnessLufs", "relRmsErrDb", "truePeakDb"]);
});

t("loudness trips on its own at +0.2 dB, true peak at +0.6 dB: each limit has its own edge", () => {
  assert.deepEqual(trips(gain(sig, 0.2)), ["loudnessLufs"]);
  assert.ok(!trips(gain(sig, 0.05)).includes("loudnessLufs"), "+0.05 dB is inside the 0.1 LU limit");
  assert.ok(trips(gain(sig, 0.6)).includes("truePeakDb"));
  assert.ok(!trips(gain(sig, 0.4)).includes("truePeakDb"), "+0.4 dB is inside the 0.5 dB true-peak limit");
});

t("a spectral change (a two-point low-pass) trips the centroid limit", () => {
  const row = compareRenders("probe", sig, smooth(sig));
  assert.ok(100 * Math.abs(row.native.centroidMedianHz - row.wasm.centroidMedianHz) / row.wasm.centroidMedianHz > LIMITS.centroidPct);
  assert.ok(violationsOf(row, "library").includes("centroidMedianHz"));
});

t("a one-sample shift trips only the sample bound, and only under imp3 (library mode has no sample bound)", () => {
  const sh = shift(sig, 1), row = compareRenders("probe", sig, sh);
  assert.ok(row.relRmsErrDb > LIMITS.relRmsErrDbImp3, `relRmsErrDb ${row.relRmsErrDb}`);
  assert.deepEqual(violationsOf(row, "imp3"), ["relRmsErrDb"]);
  assert.deepEqual(violationsOf(row, "library"), []);
  assert.deepEqual(violationsOf(row, "old"), []);
});

t("an error well inside the sample bound passes under imp3", () => {
  const near = sig.map((v, i) => v + (i % 2 ? 1 : -1) * 1e-5);
  assert.ok(compareRenders("probe", sig, near).relRmsErrDb < LIMITS.relRmsErrDbImp3);
  assert.deepEqual(trips(near, "imp3"), []);
});

t("a length mismatch is an error row, not a trimmed comparison", () => {
  const row = compareRenders("probe", sig, sig.subarray(0, sig.length - 512));
  assert.match(row.error, /length mismatch: wasm 144000, native 143488/);
  assert.equal(row.wasm, undefined);
});

console.log(`${n} passed`);
