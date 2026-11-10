// Eight sender voices for Ratatoskr dings. Each voice is a different small object; the four motifs share one rhythm
// family (event = rhythm, sender = timbre). Pure JS, deterministic (seeded noise), no dependencies. Built on the
// Earcon Workshop (only its sample rate is imported).
// Output is unit-scale float; build.mjs writes it and finish.py sets loudness, encodes and measures. Nothing here has been heard.
import { SR } from "../../earcon-workshop/web/earcon.js";

const TAU = Math.PI * 2;
export const SENDERS = ["aqua", "eureka", "heimdall", "kiko", "libby", "nibu", "void", "weksa"];
export const MOTIFS = ["mention", "dm", "message", "invite"];

// Shared rhythm family: onset times in ms and a rising scale degree (semitones) per onset. Rhythm tells the event:
// mention = one note, message = quick pair, dm = slow pair, invite = long gap then short (an opening, then in).
export const RHYTHM = {
  mention: [[0, 0]],
  message: [[0, 0], [230, 4]],
  dm: [[0, 0], [240, 5]],
  invite: [[0, 0], [200, 7], [300, 12]],
};
export const MAX_MS = { message: 395, dm: 600, mention: 800, invite: 600 };

function prng(seed) { let a = seed >>> 0; return () => { a = (a + 0x6d2b79f5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296 * 2 - 1; }; }

// RBJ biquads (direct form I). bandpass is constant 0 dB peak gain; lowpass Butterworth-ish at q = 0.707.
function biquad(type, fc, q) {
  const w = TAU * Math.min(fc, 0.45 * SR) / SR, c = Math.cos(w), s = Math.sin(w), al = s / (2 * q);
  let b0, b1, b2;
  if (type === "bp") { b0 = al; b1 = 0; b2 = -al; } else { b0 = (1 - c) / 2; b1 = 1 - c; b2 = (1 - c) / 2; }
  const a0 = 1 + al, a1 = -2 * c / a0, a2 = (1 - al) / a0;
  return { b0: b0 / a0, b1: b1 / a0, b2: b2 / a0, a1, a2, x1: 0, x2: 0, y1: 0, y2: 0 };
}
function run(f, x) { const y = f.b0 * x + f.b1 * f.x1 + f.b2 * f.x2 - f.a1 * f.y1 - f.a2 * f.y2; f.x2 = f.x1; f.x1 = x; f.y2 = f.y1; f.y1 = y; return y; }
const st = (semi) => 2 ** (semi / 12);
const at = (ms) => Math.round(ms * SR / 1000);
// attack-then-exponential-decay envelope; tau seconds to 1/e; short fade so nothing ends on a step.
const env = (n, atk, tau, len) => Math.min(1, (n + 1) / Math.max(1, atk)) * Math.exp(-n / SR / tau) * Math.min(1, (len - n) / at(4));

// additive partials: [ratio, amp, tauSec]. The decay is sped up just enough that the note has fallen ~40 dB by the end of its window
// (so a note shortened to fit a motif ends settled, not cut), then a 15 ms raised-cosine fade closes it.
function partials(buf, t0, f0, parts, atkMs, lenMs, gain = 1) {
  const len = Math.min(buf.length - t0, at(lenMs)), atk = at(atkMs), s = Math.min(1, (len / SR) / (4.6 * Math.max(...parts.map((p) => p[2])))), fade = Math.min(at(15), len >> 2);
  for (let n = 0; n < len; n++) { let y = 0; for (const [r, a, tau] of parts) y += a * Math.exp(-n / SR / (tau * s)) * Math.sin(TAU * f0 * r * n / SR); buf[t0 + n] += gain * y * Math.min(1, (n + 1) / atk) * Math.sin(Math.PI / 2 * Math.min(1, (len - n) / fade)) ** 2; }
}
function sine(buf, t0, hz0, hz1, glideTau, atkMs, tau, lenMs, gain) {
  const len = Math.min(buf.length - t0, at(lenMs)), atk = at(atkMs), s = Math.min(1, (len / SR) / (4.6 * tau)), fade = Math.min(at(15), len >> 2); let ph = 0;
  for (let n = 0; n < len; n++) { const hz = hz1 + (hz0 - hz1) * Math.exp(-n / SR / glideTau); ph += TAU * hz / SR; buf[t0 + n] += gain * Math.sin(ph) * Math.min(1, (n + 1) / atk) * Math.exp(-n / SR / (tau * s)) * Math.sin(Math.PI / 2 * Math.min(1, (len - n) / fade)) ** 2; }
}

// Eight voices, eight tonal families (the 2026-11-10 family assignment). Voice = hit(buf, t0 sample, deg, i (hit index), n (hits),
// gapMs, rand, motif, room (ms left before the motif's length cap)). Each hit shortens itself to fit `room`. No voice is percussive by design; weksa's pluck and void's tick are the sharpest events.
const bell = (b, t, f0, parts, atkMs, lenMs, gain) => partials(b, t, f0, parts, atkMs, lenMs, gain);
export const VOICES = {
  aqua: { object: "a soft water droplet", family: "liquid", caption: "One soft droplet per note: a round, watery 'bloop' whose pitch lifts as it lands. No click.",
    hit(b, t, d, i, n, gap, r, m, room) { const L = m === "mention"; sine(b, t, 600 * st(d), 880 * st(d), 0.025, 6, L ? 0.11 : 0.06, Math.min(L ? 440 : 210, room), 0.95); } },
  eureka: { object: "a pure soft sine", family: "pure tone", caption: "A single soft, clean, steady tone that eases in slowly, holds, and settles away gently. Nothing else.",
    hit(b, t, d, i, n, gap, r, m, room) { const len = Math.min(b.length - t, at(Math.min(m === "mention" ? 400 : 230, room))), f = 330 * st(d), atk = at(70), rel = Math.min(at(110), Math.floor(len * 0.4));
      for (let k = 0; k < len; k++) b[t + k] += 0.95 * Math.sin(TAU * f * k / SR) * Math.sin(Math.PI / 2 * Math.min(1, (k + 1) / atk)) ** 2 * Math.sin(Math.PI / 2 * Math.min(1, (len - k) / rel)) ** 2; } },
  heimdall: { object: "a low horn heard through a door", family: "muted horn", caption: "One low, warm, muffled horn note, as if heard through a closed door. It swells in slowly, holds a steady pitch, and fades away softly.",
    hit(b, t, d, i, n, gap, r, m, room) { const len = Math.min(b.length - t, at(Math.min(m === "mention" ? 600 : 300, room))), f0 = 196 * st(d), door = [biquad("lp", 780, 0.6), biquad("lp", 780, 0.6)];
      for (let k = 0; k < len; k++) { const p = k / len, e = Math.sin(Math.PI * Math.min(1, p / 0.45) / 2) ** 2 * Math.cos(Math.PI / 2 * Math.max(0, (p - 0.55) / 0.45)) ** 2;
        let y = 0; for (let h = 1; h <= 6; h++) y += Math.sin(TAU * f0 * h * k / SR) / (h * h ** 0.5);
        b[t + k] += 1.4 * run(door[1], run(door[0], y)) * e; } } },
  kiko: { object: "struck glass", family: "struck glass", caption: "Hypothesis, unheard: a small glass tapped once. A clean, instant start, then three inharmonic partials that ring and fall away, the upper ones first. No friction noise, no swell, no pitch movement. A note something follows settles before the next lands; the last note rings.",
    hit(b, t, d, i, n, gap, r, m, room) { const last = i === n - 1, k = last ? 1.6 : 1, len = last ? Math.min(480, room - 10) : Math.min(gap, room);
      partials(b, t, 1318 * st(d), [[1, 1, 0.12 * k], [2.76, 0.45, 0.07 * k], [5.4, 0.2, 0.04 * k]], 1, len, 0.8); } },
  libby: { object: "a small bell", family: "soft bell", caption: "One small bell note with a felt-soft start, no strike. A note that something follows is shortened to settle before the next one lands; the last note rings.",
    hit(b, t, d, i, n, gap, r, m, room) { const last = i === n - 1, k = last ? 2 : 1, len = last ? Math.min(520, room - 10) : Math.min(gap, room); bell(b, t, 784 * st(d), [[1, 1, 0.1 * k], [2.32, 0.22, 0.06 * k], [3.9, 0.06, 0.03 * k]], 26, len, 0.85); } },
  void: { object: "a hollow wooden note", family: "hollow wood", caption: "One round, hollow, almost wooden note that falls a little and settles. Higher than the horn. One onset per note: nothing rides the release.",
    // v4: no tick (v3's soft knock 95 ms into every note was the second hit Meta heard), and a note something follows ends by the next onset (as Libby's do), so a note's ring never re-rises under the next attack.
    hit(b, t, d, i, n, gap, r, m, room) { const f = 300 * st(d), k = m === "mention" ? 2 : 1, rm = i + 1 < n ? Math.min(room, gap) : room;
      sine(b, t, f * 1.2, f * 0.85, 0.05, 9, 0.085 * k, Math.min(230 * k, rm), 0.9); sine(b, t, f * 2.4, f * 1.7, 0.05, 9, 0.03 * k, Math.min(120 * k, rm), 0.12); } },
  weksa: { object: "a plucked brass compass", family: "plucked brass", caption: "A small plucked brass note, then a small bright rise: the sound of finding the thing. In a mention, just the one pluck.",
    hit(b, t, d, i, n, gap, r, m, room) { const br = 0.35 + 0.3 * i, k = m === "mention" ? 2 : 1; partials(b, t, 880 * st(d), [[1, 1, 0.06 * k], [2, 0.55, 0.045 * k], [3, br, 0.03 * k], [4, br * 0.6, 0.02 * k]], 3.5, Math.min(m === "mention" ? 480 : 240, room), 0.7); } },
};

// Nibu is the one openly synthetic voice. v2's glided saw was rejected (Meta, 2026-11-10: atrocious), so there are three proposals from three
// families, none with a pitch glide; Nibu and Meta choose. VOICES.nibu is proposal 3 (chosen by Nibu and Meta, 2026-11-10); proposals 1 and 2 stay as non-default alternates.
// shape: raised-cosine in over atk samples, raised-cosine out over the last rel samples.
const shape = (k, len, atk, rel) => Math.sin(Math.PI / 2 * Math.min(1, (k + 1) / atk)) ** 2 * Math.sin(Math.PI / 2 * Math.min(1, (len - k) / rel)) ** 2;
// the held-note length: a note something follows ends by the next onset; the last note rings as long as the motif allows.
const heldLen = (b, t, ring, last, gap, room) => Math.min(b.length - t, at(Math.min(last ? ring : gap, room - 10)));
export const NIBU_PROPOSALS = {
  nibu1: { object: "a held square tone through a fixed filter", family: "static-filter square", caption: "Two held, soft, hollow square-wave tones a fifth apart (in a mention, just the first), filtered dark and not moving. Slow soft attack, long settle, no glide. Plainly synthetic.",
    hit(b, t, d, i, n, gap, r, m, room) { const last = i === n - 1, len = heldLen(b, t, 520, last, gap, room), f0 = 220 * st(d === 4 || d === 5 ? 7 : d), lp = [biquad("lp", 1300, 0.7), biquad("lp", 1300, 0.7)], atk = at(40), rel = Math.floor(len * (last ? 0.55 : 0.4));
      for (let k = 0; k < len; k++) { let y = 0; for (let h = 1; h <= 15; h += 2) y += Math.sin(TAU * f0 * h * k / SR) / h; b[t + k] += 0.9 * run(lp[1], run(lp[0], y)) * shape(k, len, atk, rel); } } },
  nibu2: { object: "a small FM bell, low and plain", family: "low-index FM bell", caption: "A small FM bell in a low register: a harmonic 1:2 pair with a modest, fading index, so it is bright for a moment and then a plain tone. Pitched a tritone from Libby's bell and an octave and a half under it. No glide.",
    hit(b, t, d, i, n, gap, r, m, room) { const last = i === n - 1, len = heldLen(b, t, 520, last, gap, room), f = 277.2 * st(d), tau = last ? 0.16 : 0.09, s = Math.min(1, (len / SR) / (4.6 * tau)), atk = at(8), rel = Math.min(at(15), len >> 2);
      for (let k = 0; k < len; k++) { const e = Math.exp(-k / SR / (tau * s)), idx = 0.2 + 1.8 * Math.exp(-k / SR / (0.07 * s)); b[t + k] += 0.9 * e * Math.sin(TAU * f * k / SR + idx * Math.sin(TAU * f * 2 * k / SR)) * shape(k, len, atk, rel); } } },
  nibu3: { object: "a low-resolution digital tone", family: "sample-rate-reduced sine", caption: "A soft sine that has been through an old converter: held at 8 kHz and 5 bits, then filtered smooth so it is grainy without being harsh. Soft attack, gentle decay, no glide. Synthetic in a different way from the other two.",
    hit(b, t, d, i, n, gap, r, m, room) { const last = i === n - 1, len = heldLen(b, t, 520, last, gap, room), f = 349.2 * st(d), tau = last ? 0.18 : 0.1, s = Math.min(1, (len / SR) / (4.6 * tau)), atk = at(12), rel = Math.min(at(20), len >> 2), lp = [biquad("lp", 2600, 0.7), biquad("lp", 2600, 0.7)]; let held = 0;
      for (let k = 0; k < len; k++) { if (k % 6 === 0) held = Math.round(Math.sin(TAU * f * k / SR) * 16) / 16; b[t + k] += 0.9 * run(lp[1], run(lp[0], held)) * Math.exp(-k / SR / (tau * s)) * shape(k, len, atk, rel); } } },
};
VOICES.nibu = { ...NIBU_PROPOSALS.nibu3 };
const ALL = { ...VOICES, ...NIBU_PROPOSALS };
// Each note renders into its own buffer with its own noise stream (seeded by sender and note index), so no note can read or write
// another's state; a clip is the sum of its notes. A note knows only what follows it (gap, room), never what precedes it.
const noteSeed = (sender, i) => (0x5eed ^ [...sender].reduce((a, c) => a * 31 + c.charCodeAt(0), 7) ^ Math.imul(i + 1, 0x9e3779b1)) >>> 0;
export function renderNote(sender, motif, i) {
  const onsets = RHYTHM[motif], [ms, deg] = onsets[i], buf = new Float64Array(at(900)), gap = i + 1 < onsets.length ? onsets[i + 1][0] - ms : 160;
  let room = MAX_MS[motif] - ms; if (motif === "message" && i + 1 < onsets.length) room = Math.min(room, gap);
  ALL[sender].hit(buf, at(ms), deg, i, onsets.length, gap, prng(noteSeed(sender, i)), motif, room); return buf;
}
export function renderClip(sender, motif) {
  const buf = new Float64Array(at(900));
  RHYTHM[motif].forEach((_, i) => { const note = renderNote(sender, motif, i); for (let k = 0; k < buf.length; k++) buf[k] += note[k]; });
  let end = buf.length; while (end > 1 && Math.abs(buf[end - 1]) < 1e-5) end--;   // trim silent tail (-100 dB)
  return Float32Array.from(buf.subarray(0, end));
}
