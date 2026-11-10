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
console.log(`ok: ${names.length * MOTIFS.length} clips are the sum of per-note renders; notes are independent of each other's pitch and noise`);
