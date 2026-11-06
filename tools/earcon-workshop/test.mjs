// Automated checks for every identity x event, at default and at the slider extremes. Rendered with the same code the page runs.
// These measure signals: non-silent, onset, peak, clipping, sibling distinctness, determinism, WAV and ZIP round trips.
// They cannot say any earcon sounds good. A human ear is owed.
import { IDENTITIES, EVENTS, renderFamily, packFiles, encodeWav, decodeWav, buildPack, crc32, PARAM_RANGES, PEAK_CEILING, DEFAULT_PARAMS, SR } from "./web/earcon.js";
import { samplePeak, truePeak, clipping, SILENT_PEAK } from "../aqua-feel/metrics.js";

// Spectral distance: log-magnitude STFT (2048 Hann, hop 512) pooled to 32 log-spaced bands 80 Hz..16 kHz, floored at -90 dB
// relative to full scale; mean absolute dB difference over (frame, band) cells where either side is above -75 dB (the shorter buffer is padded with silence).
const ACTIVE = -75; // cells where both spectra sit under this are shared emptiness and do not dilute the mean
const N = 2048, HOP = 512, BANDS = 32, FLOOR = -90;
function fft(re, im) {
  const n = re.length;
  for (let i = 1, j = 0; i < n; i++) { let b = n >> 1; for (; j & b; b >>= 1) j ^= b; j ^= b; if (i < j) { [re[i], re[j]] = [re[j], re[i]]; [im[i], im[j]] = [im[j], im[i]]; } }
  for (let len = 2; len <= n; len <<= 1) {
    const w = (-2 * Math.PI) / len;
    for (let i = 0; i < n; i += len) for (let k = 0; k < len / 2; k++) {
      const c = Math.cos(w * k), s = Math.sin(w * k), a = i + k, b = a + len / 2;
      const xr = re[b] * c - im[b] * s, xi = re[b] * s + im[b] * c;
      re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
    }
  }
}
const win = Float64Array.from({ length: N }, (_, i) => 0.5 - 0.5 * Math.cos((2 * Math.PI * i) / N));
const edges = Array.from({ length: BANDS + 1 }, (_, i) => Math.round(((80 * (16000 / 80) ** (i / BANDS)) / SR) * N));
function bandFrames(x) {
  const frames = [];
  for (let s = 0; s + N <= x.length + N - HOP; s += HOP) {
    const re = new Float64Array(N), im = new Float64Array(N);
    for (let i = 0; i < N; i++) re[i] = (s + i < x.length ? x[s + i] : 0) * win[i];
    fft(re, im);
    frames.push(Float64Array.from({ length: BANDS }, (_, b) => {
      let p = 0; for (let k = edges[b]; k < Math.max(edges[b + 1], edges[b] + 1); k++) p += re[k] ** 2 + im[k] ** 2;
      return Math.max(FLOOR, 10 * Math.log10(p / (N * N / 4) + 1e-12)); // N/4 ~ Hann-scaled unit sine -> 0 dB
    }));
  }
  return frames;
}
export function spectralDistance(a, b) {
  const fa = bandFrames(a), fb = bandFrames(b), n = Math.max(fa.length, fb.length), silent = new Float64Array(BANDS).fill(FLOOR);
  let sum = 0, cells = 0;
  for (let t = 0; t < n; t++) { const x = fa[t] ?? silent, y = fb[t] ?? silent; for (let b = 0; b < BANDS; b++) if (x[b] > ACTIVE || y[b] > ACTIVE) { sum += Math.abs(x[b] - y[b]); cells++; } }
  return cells ? sum / cells : 0;
}

// History, kept because it changes how far to trust the number: the first metric averaged over every band and frame and
// put the closest pair near 3.3 dB at the dark/slow extreme; motifs were reworked, then the mean was restricted to active
// cells (shared silence was diluting it), and the threshold was raised from 4 to 8 dB after seeing a 13.8 dB minimum.
const MIN_DISTANCE_DB = 8;
const names = Object.keys(IDENTITIES), events = Object.keys(EVENTS);
const lo = (k) => PARAM_RANGES[k][0], hi = (k) => PARAM_RANGES[k][1];
const settings = {
  default: {},
  "slow dark long quiet": { tempo: hi("tempo"), brightness: lo("brightness"), decay: hi("decay"), level: lo("level"), transpose: lo("transpose") },
  "fast bright short loud": { tempo: lo("tempo"), brightness: hi("brightness"), decay: lo("decay"), level: hi("level"), transpose: hi("transpose") },
};
let failures = 0, checks = 0;
const check = (ok, msg) => { checks++; if (!ok) { failures++; console.error(`FAIL ${msg}`); } };

let worst = { d: Infinity, at: "" };
for (const [sname, params] of Object.entries(settings)) for (const id of names) {
  const fam = renderFamily(id, undefined, params);
  for (const it of fam.items) {
    const tag = `${id}/${it.event} [${sname}]`, x = it.samples;
    check(x.every(Number.isFinite), `${tag}: NaN or Infinity`);
    check(samplePeak(x) > SILENT_PEAK * 100, `${tag}: silent (peak ${samplePeak(x)})`);
    check(it.onsetMs !== null && it.onsetMs < 20, `${tag}: onset ${it.onsetMs} ms, need < 20`);
    check(it.peak < 1 && it.peak <= PEAK_CEILING + 1e-6, `${tag}: peak ${it.peak}`);
    check(truePeak(x) < 1, `${tag}: true peak ${truePeak(x)} >= 1`);
    const c = clipping(x); check(c.overSamples === 0 && c.flatTopRuns === 0, `${tag}: clipping ${JSON.stringify(c)}`);
    check(it.durationMs < 2600, `${tag}: ${it.durationMs} ms is not short`);
    check(Math.abs(x[x.length - 1]) < 1e-3, `${tag}: ends on a step (${x[x.length - 1]})`);
    const wav = decodeWav(encodeWav(x)); check(wav.samples.length === x.length && wav.sampleRate === SR && samplePeak(wav.samples) < 1, `${tag}: WAV round trip`);
  }
  // siblings: every pair of events inside one identity
  for (let i = 0; i < fam.items.length; i++) for (let j = i + 1; j < fam.items.length; j++) {
    const d = spectralDistance(fam.items[i].samples, fam.items[j].samples);
    if (d < worst.d) worst = { d, at: `${id} ${fam.items[i].event} vs ${fam.items[j].event} [${sname}]` };
    check(d >= MIN_DISTANCE_DB, `siblings ${id} ${fam.items[i].event} vs ${fam.items[j].event} [${sname}]: distance ${d.toFixed(2)} dB < ${MIN_DISTANCE_DB}`);
  }
}
console.log(`closest sibling pair: ${worst.d.toFixed(2)} dB (${worst.at}); threshold ${MIN_DISTANCE_DB} dB`);

// identities are distinct from each other on the same event too
for (const e of events) for (let i = 0; i < names.length; i++) for (let j = i + 1; j < names.length; j++) {
  const d = spectralDistance(renderFamily(names[i], [e]).items[0].samples, renderFamily(names[j], [e]).items[0].samples);
  check(d >= MIN_DISTANCE_DB, `identities ${names[i]} vs ${names[j]} on ${e}: distance ${d.toFixed(2)} dB`);
}

// a self-pair must measure zero, and a silent-vs-signal pair must measure large: the metric can fail both ways
const probe = renderFamily("warm-bell", ["success"]).items[0].samples;
check(spectralDistance(probe, probe) === 0, "metric: self distance not 0");
check(spectralDistance(probe, new Float32Array(probe.length)) > 10, "metric: silence not far from signal");

// negative control: a near-duplicate (2% tempo change) must land under the threshold, so the threshold can say 'too similar'
const near = spectralDistance(renderFamily("warm-bell", ["success"]).items[0].samples, renderFamily("warm-bell", ["success"], { tempo: 1.02 }).items[0].samples);
check(near < MIN_DISTANCE_DB, `negative control: a 2% tempo change measured ${near.toFixed(2)} dB, the threshold would not flag a near-duplicate`);
console.log(`near-duplicate control: ${near.toFixed(2)} dB`);

// determinism: same inputs, same bytes
const a = packFiles(renderFamily("glass-chime", undefined, { tempo: 1.2 })), b = packFiles(renderFamily("glass-chime", undefined, { tempo: 1.2 }));
check(a.every((f, i) => crc32(f.bytes) === crc32(b[i].bytes)), "render is not deterministic");
// sliders do something
const base = renderFamily("warm-bell", ["success"]).items[0], slow = renderFamily("warm-bell", ["success"], { tempo: 1.6 }).items[0], up = renderFamily("warm-bell", ["success"], { transpose: 12 }).items[0];
check(slow.durationMs > base.durationMs, "tempo slider did not lengthen the earcon");
check(spectralDistance(base.samples, up.samples) > 3, "transpose slider did not move the spectrum");
// event subset only renders what was chosen
check(renderFamily("warm-bell", ["fail", "done"]).items.map((i) => i.event).join() === "fail,done", "event selection not honoured");

// pack: ZIP read back with node's own inflate-free parse of stored entries; pack.json lists every file, heard stays false
const files = packFiles(renderFamily("soft-triangle")), z = (await import("./web/earcon.js")).zip(files);
const dv = new DataView(z.buffer, z.byteOffset, z.byteLength), entries = dv.getUint16(z.length - 22 + 10, true), cdOff = dv.getUint32(z.length - 22 + 16, true);
check(entries === files.length, `zip entry count ${entries} != ${files.length}`);
let p = cdOff;
for (const f of files) {
  const csize = dv.getUint32(p + 20, true), crc = dv.getUint32(p + 16, true), nl = dv.getUint16(p + 28, true), lho = dv.getUint32(p + 42, true);
  const name = new TextDecoder().decode(z.subarray(p + 46, p + 46 + nl)), data = z.subarray(lho + 30 + nl, lho + 30 + nl + csize);
  check(name === f.name && crc === crc32(data) && crc === crc32(f.bytes), `zip entry ${f.name} corrupt`);
  p += 46 + nl;
}
const pack = JSON.parse(new TextDecoder().decode(files.find((f) => f.name === "pack.json").bytes));
check(pack.heard === false && Object.values(pack.events).every((e) => files.some((f) => f.name === e.file)), "pack.json does not match its files");
check(/Nobody has listened/.test(new TextDecoder().decode(files.find((f) => f.name === "README.md").bytes)), "pack README lost the unheard notice");

console.log(failures ? `${failures} of ${checks} checks FAILED` : `all ${checks} checks passed`);
process.exit(failures ? 1 : 0);
