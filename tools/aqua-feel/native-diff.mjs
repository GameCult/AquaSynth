// Diff aqua-feel's faustwasm render against the daemon's native libfaust render (tools/FaustNativeRender) of the same
// Faust source, at the same params, 48 kHz and length. Nothing here was heard.
// Both sides compile against their own stdlib (faustwasm bundles libfaust's; native reads AQUASYNTH_FAUST_HOME's share/faust), so a
// difference here can be library text, not the host: the report records the identities. The script exits 1
// on a render error, a length mismatch, or a LIMITS violation. LIMITS (native-compare.js; the evidence for their values is there, and native-diff.test.mjs trips each) bound the differences;
// the sample-level bound applies only with --smoother imp3, where the largest error seen is -75.4 dB (dx7-32 is -79.5: phasor increment rounding plus si.smoo text, docs/renders/dx7-algorithm-32-native-diff-cause.md) and a one-sample
// shift reads about -4 dB. With the library smoother the waveforms legitimately diverge (thruster -8 to -29 dB), so no sample bound.
// usage: node native-diff.mjs <FaustNativeRender dir|dll> [--tract PATH.dsp] [--out DIR] [--items core|smoother] [--smoother library|old|imp3]
//   --items smoother: the 12 catalog sources that call si.smooth/si.smoo (thruster at rpm 1200, 7 patches, 4 Ding instruments, one note at 261.6256 Hz for 1.5 s)
//   --smoother old|imp3: rewrite si.smooth/si.smoo in the source text to a local definition (old = `*(1-s) : + ~ *(s)`, imp3 = the error-form
//   recursion), the same text on both sides, so the standard library's own si.smooth no longer decides. Smoothers called inside other
//   library functions are not rewritten.
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { tmpdir } from "node:os";
import { SR } from "./metrics.js";
import { compareRenders, violationsOf } from "./native-compare.js";
import { gate } from "./gate.js";
import { loadRuntime, compile, renderOnce, writeWav, readText, playground } from "./render.js";

const here = dirname(fileURLToPath(import.meta.url)), args = process.argv.slice(2);
const opt = (k) => { const i = args.indexOf(k); return i < 0 ? null : args[i + 1]; };
const bin = resolve(args[0]), out = resolve(opt("--out") ?? join(tmpdir(), "aqua-feel-native-diff"));
const low = (n) => readText(join(playground, "lowered", n));
const mode = opt("--smoother") ?? "library", set = opt("--items") ?? "core";
const DEFS = {
  old: "nd_smooth(s) = *(1.0 - s) : + ~ *(s);",
  imp3: "nd_smooth(s) = \\(x).(x + (fb ~ _ with { fb(e) = s * (e + (x' - x : min(3.4e38))); }));",
};
const pin = (src) => mode === "library" ? src : src.replace(/\bsi\.smoo\b/g, "nd_smoo").replace(/\bsi\.smooth\b/g, "nd_smooth").replace(/import\("stdfaust\.lib"\);/, (m) => `${m}\n${DEFS[mode]}\nnd_smoo = nd_smooth(1 - 44.1/ma.SR);`);
if (!["library", "old", "imp3"].includes(mode) || !["core", "smoother"].includes(set)) { console.error("bad --smoother or --items"); process.exit(2); }
const SMOOTHER_PATCHES = ["thruster", "advanced-alien-multivalve-graph", "advanced-bird-syrinx", "advanced-bird-syrinx-graph", "advanced-syrinx-voice", "advanced-human-larynx-graph", "dx7-algorithm-32-additive-organ", "dx7-algorithm-8-bright-pair"];
function smootherItems() {
  const dir = join(here, "lowered-dings"), d = JSON.parse(readText(join(dir, "dings.json")));
  return [
    ...SMOOTHER_PATCHES.map((n) => ({ id: n === "thruster" ? "thruster@rpm1200" : n, group: "patch", file: `${n}.dsp`, source: low(`${n}.dsp`), params: n === "thruster" ? { rpm: 1200 } : {}, seconds: 3 })),
    ...d.instruments.map((i) => ({ id: `ding-instrument/${i.id}`, group: "ding", file: `ding-${i.id}.dsp`, source: readText(join(dir, i.dsp)), params: { [i.controlPath]: i.rootFrequency }, seconds: d.noteSeconds })),
  ];
}
const items = set === "smoother" ? smootherItems() : [
  ...[300, 600, 1200, 4800].map((rpm) => ({ id: `thruster@rpm${rpm}`, group: "patch", file: "thruster.dsp", source: low("thruster.dsp"), params: { rpm }, seconds: 3 })),
  ...["sfxr-classic-classic-sfxr-abstract-golf", "sfxr-explosion"].map((n) => ({ id: n, group: "patch", file: `${n}.dsp`, source: low(`${n}.dsp`), params: {}, seconds: 3 })),
];
if (opt("--tract")) items.push({ id: "tract/vowel-a", group: "patch", file: "tract-vowel-a.dsp", source: readText(opt("--tract")), params: {}, seconds: 0.72 });
for (const it of items) it.source = pin(it.source);
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
  const row = compareRenders(it.id, w, n);
  if (row.error) { rows.push({ id: it.id, error: row.error }); continue; }
  const { measures, ...rest } = row;
  rows.push({ ...rest, flagsWasm: flagsOf(it.id, it.group, measures.wasm), flagsNative: flagsOf(it.id, it.group, measures.native) });
}
for (const r of rows) if (!r.error) r.violations = violationsOf(r, mode);
writeFileSync(join(out, "native-diff.json"), JSON.stringify({ libfaustWasm: rt.version, sampleRate: SR, items: set, smoother: mode, rows }, null, 1));
if (rows.some((r) => r.error || r.violations.length)) process.exitCode = 1;
console.log(JSON.stringify({ libfaustWasm: rt.version, items: set, smoother: mode, rows }, null, 1));
