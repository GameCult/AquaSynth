// node polyphony.test.mjs: a clip is the sum of its notes, and no note depends on another note's pitch or noise (per-note state).
// A note may depend on what FOLLOWS it (gap, room) by design; that is the only coupling.
import assert from "node:assert/strict";
import { SENDERS, MOTIFS, RHYTHM, renderNote, renderClip, NIBU_PROPOSALS } from "./voices.mjs";
const SR = 48000, names = [...SENDERS, ...Object.keys(NIBU_PROPOSALS)];
const peak = (x) => x.reduce((a, v) => Math.max(a, Math.abs(v)), 0);
for (const s of names) for (const m of MOTIFS) {
  const n = RHYTHM[m].length, notes = RHYTHM[m].map((_, i) => renderNote(s, m, i)), clip = renderClip(s, m);
  // 1. the clip is the sum of its notes (to float32 rounding)
  for (let k = 0; k < clip.length; k++) { let sum = 0; for (const nt of notes) sum += nt[k]; assert.ok(Math.abs(clip[k] - sum) < 1e-6, `${s} ${m} sum at ${k}`); }
  // 2. changing the pitch of any other note leaves this note bit-identical (noise included)
  for (let i = 0; i < n; i++) for (let j = 0; j < n; j++) if (j !== i) {
    const saved = RHYTHM[m][j][1]; RHYTHM[m][j][1] = saved + 3; const again = renderNote(s, m, i); RHYTHM[m][j][1] = saved;
    assert.deepEqual(again, notes[i], `${s} ${m}: note ${i} changed when note ${j} did`);
  }
}
// 3. Libby: a note something follows has settled (-40 dB re its own peak) when the next one starts; the last note rings longer than any followed one.
for (const m of ["dm", "invite"]) {
  const on = RHYTHM[m], len = (x) => { let e = x.length; while (e > 0 && Math.abs(x[e - 1]) < 1e-3 * peak(x)) e--; return e; };
  for (let i = 0; i < on.length - 1; i++) {
    const x = renderNote("libby", m, i), nextAt = Math.round(on[i + 1][0] * SR / 1000), tail = peak(x.subarray(nextAt - 48, nextAt + 1));
    assert.ok(tail < 0.01 * peak(x), `libby ${m} note ${i} still at ${(20 * Math.log10(tail / peak(x))).toFixed(1)} dB when note ${i + 1} lands`);
    assert.ok(len(renderNote("libby", m, on.length - 1)) - Math.round(on[on.length - 1][0] * SR / 1000) > len(x) - Math.round(on[i][0] * SR / 1000), `libby ${m}: last note rings longer than note ${i}`);
  }
}
// 4. One onset per note. An onset is a rise of a 1 ms peak envelope (4 ms window) of at least ONSET_RISE_DB over the quietest point of the 15 ms before it, above
// -45 dB re that band's peak, in the full band or above 1.5 kHz (a dry tick or click is broadband and sits level with the tone, so only the high band shows it).
// Rises within 20 ms of each other are one onset (a note's own attack takes ~9 ms). A hit riding a release (v3 Void's tick), a re-attack or a ring that swells under
// the next note adds one. Void is checked for every motif: a clip must have exactly as many onsets as notes, at the notes' times.
const ONSET_RISE_DB = 6;
function highpass(x, fc) { // two cascaded RBJ biquads, Q 0.707
  const w = 2 * Math.PI * fc / 48000, c = Math.cos(w), al = Math.sin(w) / (2 * 0.707), a0 = 1 + al, b0 = (1 + c) / 2 / a0, b1 = -(1 + c) / a0, a1 = -2 * c / a0, a2 = (1 - al) / a0;
  let y = Float64Array.from(x); for (let pass = 0; pass < 2; pass++) { const o = new Float64Array(y.length); for (let n = 0; n < y.length; n++) o[n] = b0 * y[n] + b1 * (y[n - 1] || 0) + b0 * (y[n - 2] || 0) - a1 * (o[n - 1] || 0) - a2 * (o[n - 2] || 0); y = o; }
  return y;
}
function riseMs(x, rise) {
  const m = Math.floor(x.length / 48), env = new Float64Array(m);
  for (let i = 0; i < m; i++) { let p = 0; for (let k = Math.max(0, (i - 3) * 48); k < (i + 1) * 48; k++) p = Math.max(p, Math.abs(x[k])); env[i] = p; }
  const top = Math.max(...env), db = (v) => 20 * Math.log10(Math.max(v, 1e-9) / top), out = [];
  for (let i = 1; i < m; i++) { const lo = Math.min(...Array.from(env.subarray(Math.max(0, i - 15), i), db)); if (db(env[i]) > -45 && db(env[i]) - lo >= rise) out.push(i); }
  return out;
}
function onsetsMs(x) {
  const all = [...riseMs(x, ONSET_RISE_DB), ...riseMs(highpass(x, 1500), ONSET_RISE_DB)].sort((p, q) => p - q), out = [];
  for (const i of all) if (!out.length || i - out[out.length - 1].last > 20) out.push({ first: i, last: i }); else out[out.length - 1].last = i;
  return out.map((o) => o.first);
}
for (const m of MOTIFS) {
  const found = onsetsMs(renderClip("void", m)), expect = RHYTHM[m].map(([ms]) => ms);
  assert.equal(found.length, expect.length, `void ${m}: onsets at ${found.join(", ")} ms, notes at ${expect.join(", ")} ms`);
  found.forEach((f, i) => assert.ok(f >= expect[i] && f < expect[i] + 20, `void ${m}: onset ${f} ms is not at note ${i} (${expect[i]} ms)`));
  RHYTHM[m].forEach((_, i) => assert.equal(onsetsMs(renderNote("void", m, i)).length, 1, `void ${m} note ${i} alone has more than one onset`));
}
// 5. Nibu (the 8 kHz 5-bit sine): a note something follows is silent by the next onset (notes do not overlap).
for (const m of ["dm", "message", "invite"]) for (let i = 0; i < RHYTHM[m].length - 1; i++) {
  const x = renderNote("nibu", m, i), nextAt = Math.round(RHYTHM[m][i + 1][0] * 48), after = x.subarray(nextAt).reduce((a, v) => Math.max(a, Math.abs(v)), 0);
  assert.ok(after < 1e-6 * peak(x), `nibu ${m} note ${i} still sounds at ${after / peak(x)} of its peak after note ${i + 1} starts`);
}
console.log(`ok: ${names.length * MOTIFS.length} clips are the sum of per-note renders; notes are independent of each other's pitch and noise; Void has one onset per note; Nibu notes do not overlap`);
