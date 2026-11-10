// Synthetic A/V fixture with a known offset: event times, a mono WAV of tone-burst "hits", and a per-frame luminance track of flashes.
// The audio is the visual delayed by offsetMs (positive = audio late), so syncAnalyze must recover lag = offsetMs. Nothing here is a real capture.
// CLI: node sync-fixture.mjs OUTDIR [offsetMs=80] [fps=60]   writes OUTDIR/{fixture.wav,track.csv,events.txt,truth.json}
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { wavFloat32 } from "./render.js";

export const FIXTURE_EVENTS_S = [0.5, 1.3, 2.2, 3.4, 4.1, 5.0];

// bed: steady tones [[hz, amp], ...] under the hits; dc: constant offset; gain: hit amplitude. Events need not sit on frame boundaries.
export function makeFixture({ offsetMs = 80, fps = 60, sampleRate = 48000, seconds = 6, noise = 0.003, events = FIXTURE_EVENTS_S, seed = 7, bed = [], dc = 0, gain = 0.6 } = {}) {
  let s = seed >>> 0; const rand = () => { s = (Math.imul(s, 1664525) + 1013904223) >>> 0; return s / 2 ** 32 - 0.5; };
  const audio = new Float32Array(Math.round(sampleRate * seconds));
  for (let i = 0; i < audio.length; i++) { audio[i] = dc + noise * 2 * rand(); for (const [hz, amp] of bed) audio[i] += amp * Math.sin((2 * Math.PI * hz * i) / sampleRate); }
  for (const e of events) { // 40 ms decaying 880 Hz burst with a 1 ms ramp: a hit with a sharp but not instantaneous attack
    const start = Math.round((e + offsetMs / 1000) * sampleRate);
    for (let j = 0; j < 0.04 * sampleRate && start + j < audio.length; j++) {
      const t = j / sampleRate; audio[start + j] += gain * Math.min(1, t / 0.001) * Math.exp(-t / 0.012) * Math.sin(2 * Math.PI * 880 * t);
    }
  }
  const n = Math.round(fps * seconds), t = new Float64Array(n), v = new Float64Array(n);
  for (let f = 0; f < n; f++) { // frame f is on screen from f/fps; a flash is bright for 4 frames starting at the first frame at or after the event
    t[f] = f / fps; v[f] = 0.1 + 0.01 * 2 * rand();
    for (const e of events) { const f0 = Math.ceil(e * fps - 1e-9); if (f >= f0 && f < f0 + 4) v[f] = 0.9; }
  }
  return { audio, sampleRate, visual: { t, v }, events: events.map((e, i) => ({ t: e, label: `hit${i}` })), offsetMs, fps };
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const [dir, off = "80", fps = "60"] = process.argv.slice(2);
  if (!dir) { console.error("usage: node sync-fixture.mjs OUTDIR [offsetMs=80] [fps=60]"); process.exit(2); }
  const fx = makeFixture({ offsetMs: Number(off), fps: Number(fps) }); mkdirSync(dir, { recursive: true });
  const wav = wavFloat32(fx.audio); wav.writeUInt32LE(fx.sampleRate, 24); wav.writeUInt32LE(fx.sampleRate * 4, 28); // wavFloat32 writes 48 kHz; same rate here
  writeFileSync(join(dir, "fixture.wav"), wav);
  writeFileSync(join(dir, "track.csv"), "t,luma\n" + Array.from(fx.visual.t, (x, i) => `${x.toFixed(6)},${fx.visual.v[i].toFixed(5)}`).join("\n") + "\n");
  writeFileSync(join(dir, "events.txt"), fx.events.map((e) => `${e.t},${e.label}`).join("\n") + "\n");
  writeFileSync(join(dir, "truth.json"), JSON.stringify({ offsetMs: fx.offsetMs, fps: fx.fps, note: "audio = visual delayed by offsetMs; synthetic" }, null, 1));
  console.log(`wrote fixture to ${dir} (audio ${fx.offsetMs} ms late)`);
}
