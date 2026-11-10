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
function noiseBurst(buf, t0, rand, type, fc, q, atkMs, tau, lenMs, gain) {
  const f = biquad(type, fc, q), len = Math.min(buf.length - t0, at(lenMs)), atk = at(atkMs);
  for (let n = 0; n < len; n++) buf[t0 + n] += gain * run(f, rand()) * env(n, atk, tau, len);
}
function sine(buf, t0, hz0, hz1, glideTau, atkMs, tau, lenMs, gain) {
  const len = Math.min(buf.length - t0, at(lenMs)), atk = at(atkMs), s = Math.min(1, (len / SR) / (4.6 * tau)), fade = Math.min(at(15), len >> 2); let ph = 0;
  for (let n = 0; n < len; n++) { const hz = hz1 + (hz0 - hz1) * Math.exp(-n / SR / glideTau); ph += TAU * hz / SR; buf[t0 + n] += gain * Math.sin(ph) * Math.min(1, (n + 1) / atk) * Math.exp(-n / SR / (tau * s)) * Math.sin(Math.PI / 2 * Math.min(1, (len - n) / fade)) ** 2; }
}

// Eight voices, eight tonal families (the 2026-11-10 family assignment). Voice = hit(buf, t0 sample, deg, i (hit index), n (hits),
// gapMs, rand, motif, room (ms left before the motif's length cap)). Each hit shortens itself to fit `room`. No voice is percussive by design; weksa's pluck and void's tick are the sharpest events.
const fadeOut = at(8);
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
  kiko: { object: "bowed glass", family: "bowed glass", caption: "A rising, shimmering glassy swell that blooms and fades like a flash cooling. Smooth, no click.",
    hit(b, t, d, i, n, gap, r, m, room) { const len = Math.min(b.length - t, at(Math.min(m === "mention" ? 480 : 250, room))), f0 = 1318 * st(d);
      for (let k = 0; k < len; k++) { const p = k / len, f = f0 * (0.96 + 0.07 * p), e = Math.sin(Math.PI * p ** 0.7) ** 2; let y = 0;
        for (const [r2, a] of [[1, 1], [1.004, 0.8], [2.003, 0.3], [3.01, 0.12]]) y += a * Math.sin(TAU * f * r2 * k / SR);
        b[t + k] += 0.45 * y * e * Math.min(1, (len - k) / fadeOut); } } },
  libby: { object: "a small bell", family: "soft bell", caption: "One small bell note with a felt-soft start, no strike, ringing for about a quarter of a second.",
    hit(b, t, d, i, n, gap, r, m, room) { const k = m === "mention" ? 2 : 1; bell(b, t, 784 * st(d), [[1, 1, 0.1 * k], [2.32, 0.22, 0.06 * k], [3.9, 0.06, 0.03 * k]], 26, Math.min(250 * k + (k > 1 ? 20 : 0), room), 0.85); } },
  nibu: { object: "a console waking up", family: "analog synth", caption: "A soft, slightly cold filtered-saw tone that glides up a fifth and settles, with a faint dashboard blip underneath, like a console coming awake. In a mention, just the one note.",
    hit(b, t, d, i, n, gap, r, m, room) { const len = Math.min(b.length - t, at(Math.min(m === "mention" ? 520 : 300, room))), f0 = 220 * st(d), lp = biquad("lp", 1700, 1.1), atk = at(28); let ph = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
      for (let k = 0; k < len; k++) { const f = f0 * (1 + 0.5 * (1 - Math.exp(-k / SR / 0.05))); let y = 0;
        for (let h = 1; h <= 10; h++) { ph[h - 1] += TAU * f * h / SR; y += Math.sin(ph[h - 1]) / h; }
        const p = k / len, e = Math.sin(Math.PI / 2 * Math.min(1, (k + 1) / atk)) ** 2 * (p < 0.6 ? 1 : Math.cos(Math.PI / 2 * (p - 0.6) / 0.4) ** 2);
        b[t + k] += 0.95 * run(lp, y) * e; }
      sine(b, t + at(90), 1760, 1760, 1, 5, 0.02, Math.min(60, Math.max(0, room - 90)), 0.1); } },
  void: { object: "a hollow wooden note", family: "hollow wood", caption: "One round, hollow, almost wooden note that falls a little, finishing on a tiny dry tick. Higher than the horn.",
    hit(b, t, d, i, n, gap, r, m, room) { const f = 300 * st(d); const k = m === "mention" ? 2 : 1; sine(b, t, f * 1.2, f * 0.85, 0.05, 9, 0.085 * k, Math.min(230 * k, room), 0.9); sine(b, t, f * 2.4, f * 1.7, 0.05, 9, 0.03 * k, Math.min(120 * k, room), 0.12);
      noiseBurst(b, t + at(95 * k), r, "bp", 1800, 1.0, 0.5, 0.003, 14, 0.2 * (room > 130 * k ? 1 : 0)); } },
  weksa: { object: "a plucked brass compass", family: "plucked brass", caption: "A small plucked brass note, then a small bright rise: the sound of finding the thing. In a mention, just the one pluck.",
    hit(b, t, d, i, n, gap, r, m, room) { const br = 0.35 + 0.3 * i, k = m === "mention" ? 2 : 1; partials(b, t, 880 * st(d), [[1, 1, 0.06 * k], [2, 0.55, 0.045 * k], [3, br, 0.03 * k], [4, br * 0.6, 0.02 * k]], 3.5, Math.min(m === "mention" ? 480 : 240, room), 0.7); } },
};

export function renderClip(sender, motif) {
  const v = VOICES[sender], onsets = RHYTHM[motif];
  const buf = new Float64Array(at(900)), rand = prng(0x5eed ^ (sender.length * 7919) ^ [...sender].reduce((a, c) => a * 31 + c.charCodeAt(0), 7) >>> 0);
  onsets.forEach(([ms, deg], i) => { const gap = i + 1 < onsets.length ? onsets[i + 1][0] - ms : 160; let room = MAX_MS[motif] - ms; if (motif === "message" && i + 1 < onsets.length) room = Math.min(room, gap); v.hit(buf, at(ms), deg, i, onsets.length, gap, rand, motif, room); });
  let end = buf.length; while (end > 1 && Math.abs(buf[end - 1]) < 1e-5) end--;   // trim silent tail (-100 dB)
  return Float32Array.from(buf.subarray(0, end));
}
