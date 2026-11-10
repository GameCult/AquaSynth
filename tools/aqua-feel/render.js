// Offline render through the same faustwasm runtime the playground and the listening trial load (not the daemon's
// native libfaust path: same Faust source text but a different compiler build and standard library, so not the same arithmetic; see docs/renders/aqua-feel-native-diff-2026-11-10.md).
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { SR } from "./metrics.js";

const here = dirname(fileURLToPath(import.meta.url));
export const playground = resolve(here, "../aqua-playground");

export async function loadRuntime() {
  const fw = join(playground, "node_modules/@grame/faustwasm");
  const mod = await import(pathToFileURL(join(fw, "dist/esm/index.js")).href);
  const libfaust = new mod.LibFaust(await mod.instantiateFaustModuleFromFile(join(fw, "libfaust-wasm/libfaust-wasm.js")));
  return { mod, libfaust, compiler: new mod.FaustCompiler(libfaust), version: libfaust.version(), cache: new Map() };
}

// Compiled generator per DSP text (a Ding event reuses one compile for all its notes).
export async function compile(rt, name, source) {
  if (rt.cache.has(source)) return rt.cache.get(source);
  const g = new rt.mod.FaustMonoDspGenerator();
  if (!(await g.compile(rt.compiler, name.replace(/[^A-Za-z0-9_]/g, "_"), source, "-ftz 2"))) throw new Error(`compile ${name}: ${rt.compiler.getErrorMessage()}`);
  const controls = [];
  const walk = (items) => { for (const it of items ?? []) { if (it.items) walk(it.items); else if (it.address) controls.push({ label: it.label, address: it.address }); } };
  walk(JSON.parse(g.factory.json).ui);
  const entry = { g, controls };
  rt.cache.set(source, entry);
  return entry;
}

// Render `seconds` of one generator with named controls; returns channel 0 and the channel count.
// Multi-channel output is reported, not mixed, so a metric always describes a buffer someone can hear.
export async function renderOnce(entry, params, seconds) {
  const p = await entry.g.createOfflineProcessor(SR, 512);
  for (const [label, value] of Object.entries(params ?? {})) {
    const bare = (t) => t.replace(/^\//, ""), c = entry.controls.find((x) => bare(x.label) === bare(label) || x.address === label);
    if (!c) throw new Error(`no control "${label}" (has ${entry.controls.map((x) => x.label).join(", ") || "none"})`);
    p.setParamValue(c.address, value);
  }
  const out = p.render(null, Math.round(SR * seconds));
  return { samples: out[0], channels: out.length };
}

// A Ding event is the instrument's note rendered per DingNote at root * 2^(semitones/12), scaled by commandGain * note.gain
// and mixed at the note's delay: DingsPlaybackHost's recipe, minus the audio device.
export async function renderDing(rt, instrument, instrumentSource, event, { noteSeconds, commandGain }) {
  const entry = await compile(rt, `ding_${instrument.id}`, instrumentSource);
  const last = Math.max(...event.notes.map((n) => n.delayMilliseconds));
  const mix = new Float32Array(Math.round(SR * (noteSeconds + last / 1000)));
  for (const n of event.notes) {
    const hz = instrument.rootFrequency * 2 ** (n.semitones / 12);
    const { samples } = await renderOnce(entry, { [instrument.controlPath]: hz }, noteSeconds);
    const off = Math.round((n.delayMilliseconds / 1000) * SR), g = commandGain * n.gain;
    for (let i = 0; i < samples.length && off + i < mix.length; i++) mix[off + i] += samples[i] * g;
  }
  return { samples: mix, channels: 1 };
}

// 32-bit float mono WAV, so overs above 1.0 survive to disk instead of being clamped on the way out.
export function wavFloat32(samples) {
  const dataBytes = samples.length * 4, b = Buffer.alloc(44 + dataBytes);
  b.write("RIFF", 0); b.writeUInt32LE(36 + dataBytes, 4); b.write("WAVEfmt ", 8); b.writeUInt32LE(16, 16);
  b.writeUInt16LE(3, 20); b.writeUInt16LE(1, 22); b.writeUInt32LE(SR, 24); b.writeUInt32LE(SR * 4, 28); b.writeUInt16LE(4, 32); b.writeUInt16LE(32, 34);
  b.write("data", 36); b.writeUInt32LE(dataBytes, 40);
  for (let i = 0; i < samples.length; i++) b.writeFloatLE(samples[i], 44 + i * 4);
  return b;
}
export function writeWav(path, samples) { mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, wavFloat32(samples)); }

export const readText = (p) => readFileSync(p, "utf8");
