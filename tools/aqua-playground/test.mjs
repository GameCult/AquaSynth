// Headless check of the built site (dist/): every catalog patch compiles to wasm through the same
// faustwasm runtime the browser loads, exposes controls, and renders audible, finite samples.
// It measures non-silence and NaN only; it cannot hear anything. Usage: node build.mjs && node test.mjs
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const dist = resolve(process.argv[2] ?? join(dirname(fileURLToPath(import.meta.url)), "dist"));
const { instantiateFaustModuleFromFile, LibFaust, FaustCompiler, FaustMonoDspGenerator } =
  await import(pathToFileURL(join(dist, "vendor/faustwasm/index.js")).href);
const { controlsOf } = await import(pathToFileURL(join(dist, "ui.js")).href);

const libfaust = new LibFaust(await instantiateFaustModuleFromFile(join(dist, "vendor/libfaust-wasm/libfaust-wasm.js")));
const compiler = new FaustCompiler(libfaust);
const catalog = JSON.parse(readFileSync(join(dist, "catalog.json"), "utf8"));
const SR = 48000, SECONDS = 2;
let failed = 0;
console.log(`libfaust ${libfaust.version()}; ${catalog.length} patches; ${SECONDS} s at ${SR} Hz`);

function stats(out) {
  let peak = 0, bad = 0;
  for (const ch of out) for (const v of ch) { if (!Number.isFinite(v)) bad++; else peak = Math.max(peak, Math.abs(v)); }
  return { peak, bad };
}

for (const e of catalog) {
  const g = new FaustMonoDspGenerator();
  const ok = await g.compile(compiler, e.name.replace(/[^A-Za-z0-9_]/g, "_"), readFileSync(join(dist, e.dsp), "utf8"), "-ftz 2");
  if (!ok) { failed++; console.log(`FAIL ${e.name}: compile: ${compiler.getErrorMessage()}`); continue; }
  const controls = controlsOf(JSON.parse(g.factory.json));
  const { peak, bad } = stats((await g.createOfflineProcessor(SR, 512)).render(null, SR * SECONDS));
  const problems = [];
  if (bad) problems.push(`${bad} non-finite samples`);
  if (!(peak > 1e-4)) problems.push(`silent (peak ${peak})`);
  if (e.name === "thruster") {
    if (!controls.some((c) => c.label === "rpm")) problems.push("no rpm control in the DSP UI");
    for (const rpm of [600, 4800]) {
      const p = await g.createOfflineProcessor(SR, 512);
      p.setParamValue(controls.find((c) => c.label === "rpm").address, rpm);
      const s = stats(p.render(null, SR * SECONDS));
      console.log(`  thruster rpm=${rpm}: peak ${s.peak.toFixed(4)}, non-finite ${s.bad}`);
      if (s.bad || !(s.peak > 1e-4)) problems.push(`rpm ${rpm}: peak ${s.peak}, non-finite ${s.bad}`);
    }
  }
  failed += problems.length ? 1 : 0;
  console.log(`${problems.length ? "FAIL" : "ok  "} ${e.name}: peak ${peak.toFixed(4)}${peak > 1 ? " (CLIPS)" : ""}, ${controls.length} controls${problems.length ? " :: " + problems.join("; ") : ""}`);
}
console.log(failed ? `${failed} FAILED` : "all passed");
process.exit(failed ? 1 : 0);
