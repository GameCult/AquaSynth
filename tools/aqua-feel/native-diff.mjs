// Diff aqua-feel's faustwasm render against the daemon's native libfaust render (tools/FaustNativeRender) of the same
// Faust source, at the same params, 48 kHz and length. Nothing here was heard.
// usage: node native-diff.mjs <FaustNativeRender dir|dll> [--tract PATH.dsp] [--out DIR]
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { SR, measure } from "./metrics.js";
import { gate } from "./gate.js";
import { loadRuntime, compile, renderOnce, writeWav, readText, playground } from "./render.js";

const here = dirname(fileURLToPath(import.meta.url)), args = process.argv.slice(2);
const opt = (k) => { const i = args.indexOf(k); return i < 0 ? null : args[i + 1]; };
const bin = resolve(args[0]), out = resolve(opt("--out") ?? join(here, "out-native-diff"));
const low = (n) => readText(join(playground, "lowered", n));
const items = [300, 600, 1200, 4800].map((rpm) => ({ id: `thruster@rpm${rpm}`, group: "patch", file: "thruster.dsp", source: low("thruster.dsp"), params: { rpm }, seconds: 3 }));
items.push({ id: "sfxr-classic-classic-sfxr-abstract-golf", group: "patch", file: "sfxr-classic-classic-sfxr-abstract-golf.dsp", source: low("sfxr-classic-classic-sfxr-abstract-golf.dsp"), params: {}, seconds: 3 });
items.push({ id: "sfxr-explosion", group: "patch", file: "sfxr-explosion.dsp", source: low("sfxr-explosion.dsp"), params: {}, seconds: 3 });
if (opt("--tract")) items.push({ id: "tract/vowel-a", group: "patch", file: "tract-vowel-a.dsp", source: readText(opt("--tract")), params: {}, seconds: 0.72 });
mkdirSync(join(out, "dsp"), { recursive: true });

const readF32Wav = (p) => { const b = readFileSync(p), n = b.readUInt32LE(40) / 4, x = new Float32Array(n); for (let i = 0; i < n; i++) x[i] = b.readFloatLE(44 + i * 4); return x; };
const thresholds = JSON.parse(readText(join(here, "thresholds.json")));
const flagsOf = (id, group, m) => { const g = gate({ items: [{ id, group, metrics: m }] }, thresholds, { partial: true }); return [...g.failures.map((f) => `FAIL ${f.metric}`), ...g.waived.map((f) => `waived ${f.metric}`), ...g.warnings.map((f) => `warn ${f.metric}`)]; };

const rt = await loadRuntime(), rows = [];
for (const it of items) {
  const w = (await renderOnce(await compile(rt, it.id, it.source), it.params, it.seconds)).samples;
  const dsp = join(out, "dsp", it.file); writeFileSync(dsp, it.source);
  const wav = join(out, `${it.id.replace(/[^A-Za-z0-9._@-]+/g, "_")}.native.wav`);
  const r = spawnSync("dotnet", [bin.endsWith(".dll") ? bin : join(bin, "FaustNativeRender.dll"), dsp, wav, String(it.seconds), ...Object.entries(it.params).map(([k, v]) => `${k}=${v}`)], { encoding: "utf8" });
  if (r.status !== 0) { rows.push({ id: it.id, error: (r.stderr || r.stdout).slice(0, 400) }); continue; }
  const n = readF32Wav(wav); writeWav(join(out, `${it.id.replace(/[^A-Za-z0-9._@-]+/g, "_")}.wasm.wav`), w);
  const len = Math.min(w.length, n.length); let maxErr = 0, at = 0, sq = 0, sqRef = 0;
  for (let i = 0; i < len; i++) { const d = Math.abs(w[i] - n[i]); if (d > maxErr) { maxErr = d; at = i; } sq += d * d; sqRef += w[i] * w[i]; }
  const mw = measure(w), mn = measure(n);
  rows.push({ id: it.id, lenWasm: w.length, lenNative: n.length, maxErr, maxErrAt: at, rmsErr: Math.sqrt(sq / len), relRmsErrDb: 10 * Math.log10(sq / Math.max(sqRef, 1e-30)),
    wasm: pick(mw), native: pick(mn), flagsWasm: flagsOf(it.id, it.group, mw), flagsNative: flagsOf(it.id, it.group, mn) });
}
function pick(m) { return { samplePeak: m.samplePeak, truePeakDb: m.truePeakDb, loudnessLufs: m.loudnessLufs, centroidMedianHz: m.centroidMedianHz, overSamples: m.overSamples, rmsDb: m.rmsDb, crestDb: m.crestDb, noiseToToneDb: m.noiseToToneDb, noiseToToneADb: m.noiseToToneADb, lowMotion: m.lowMotion }; }
writeFileSync(join(out, "native-diff.json"), JSON.stringify({ libfaustWasm: rt.version, sampleRate: SR, rows }, null, 1));
console.log(JSON.stringify({ libfaustWasm: rt.version, rows }, null, 1));
