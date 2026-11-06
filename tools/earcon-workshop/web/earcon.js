// Earcon Workshop core. Pure JS, no dependencies, no randomness: the browser page and `node` run this one file, so
// the buffer you audition is the buffer you export. Motif and timbre ideas come from src/AquaSynth.Dings/DingCatalog.cs
// (semitone motifs on a root, FM-bell / triangle / pluck voices, no broadband noise). Nothing here has been heard.

export const SR = 48000;
export const PEAK_CEILING = 0.9; // hard cap: every export peaks at or under this, so < 1.0 with headroom for 16-bit rounding
export const FORMAT = "aquasynth.earcon-pack.v0";

// Motif per event: semitones above the identity's root, onset delay (ms), relative gain. Contours differ on purpose
// (rise, fall, repeat, single, resolve, sparse) so siblings are told apart by shape and rhythm, not timbre alone.
export const EVENTS = {
  success: { label: "Success", meaning: "It worked: rising fifth then octave", notes: [[0, 0, 1], [7, 110, 1], [12, 220, 1]] },
  fail: { label: "Fail", meaning: "It broke: falling minor figure, low landing", notes: [[5, 0, 1], [1, 160, 1], [-4, 320, 1]] },
  warning: { label: "Warning", meaning: "Look at this: tritone zigzag, three quick notes", notes: [[9, 0, 1], [3, 110, 1], [9, 220, 1]] },
  message: { label: "Message", meaning: "Someone spoke: quick two-note lift high in the register", notes: [[12, 0, 0.7], [19, 70, 0.9]] },
  done: { label: "Done", meaning: "Finished: falling resolve onto the root, long tail", notes: [[12, 0, 1], [7, 200, 1], [0, 400, 1.1]] },
  waiting: { label: "Waiting", meaning: "Needs you: quiet, sparse, slow low sway, three soft notes", notes: [[0, 0, 0.55], [5, 450, 0.55], [0, 900, 0.55]] },
};

// Timbre identities. rootHz in the calm mid register; fmRatio/fmIndex/fmDecay shape the bell; harmonics > 0 selects the
// additive triangle; attack is seconds. Values start from the Dings catalog instruments and were not tuned by ear.
export const IDENTITIES = {
  "warm-bell": { label: "Warm bell", note: "soft fundamental, restrained upper partial", rootHz: 261.6256, voice: "fm", fmRatio: 3.01, fmIndex: 2.6, fmDecay: 0.42, attack: 0.004, decay: 0.55, brightness: 0.34 },
  "glass-chime": { label: "Glass chime", note: "clear and precise", rootHz: 329.6276, voice: "fm", fmRatio: 4.12, fmIndex: 4.1, fmDecay: 0.28, attack: 0.002, decay: 0.45, brightness: 0.72 },
  "soft-triangle": { label: "Soft triangle", note: "rounded, calm", rootHz: 261.6256, voice: "tri", harmonics: 5, attack: 0.006, decay: 0.5, brightness: 0.46 },
  "muted-pluck": { label: "Muted pluck", note: "short, dry", rootHz: 293.6648, voice: "tri", harmonics: 7, attack: 0.002, decay: 0.25, brightness: 0.56 },
};

export const DEFAULT_PARAMS = { tempo: 1, transpose: 0, brightness: 0, decay: 1, level: 0.7 };
// brightness is an offset on the identity's own brightness (-0.4..0.4); tempo scales note spacing; decay scales tails;
// level is the family's peak target (0.1..0.9) applied as one factor so relative event loudness survives.
export const PARAM_RANGES = {
  tempo: [0.6, 1.6, 0.01], transpose: [-12, 12, 1], brightness: [-0.25, 0.25, 0.01], decay: [0.5, 1.6, 0.01], level: [0.1, PEAK_CEILING, 0.01],
};

export function clampParams(p = {}) {
  const out = { ...DEFAULT_PARAMS };
  for (const k of Object.keys(DEFAULT_PARAMS)) {
    const v = Number(p[k] ?? DEFAULT_PARAMS[k]);
    const [lo, hi] = PARAM_RANGES[k];
    out[k] = Math.min(hi, Math.max(lo, Number.isFinite(v) ? v : DEFAULT_PARAMS[k]));
  }
  return out;
}

const TAU = Math.PI * 2;

// One note into `out` at sample `start`. Linear attack (<= 6 ms), exponential decay reaching -60 dB at `decaySec`,
// one-pole lowpass whose cutoff follows brightness, 3 ms linear fade at the voice end so no tail ends on a step.
function addNote(out, start, hz, gain, id, decaySec, bright) {
  const len = Math.min(out.length - start, Math.ceil(decaySec * SR));
  if (len <= 0) return;
  const atk = Math.max(1, Math.round(id.attack * SR));
  const tau = decaySec / Math.log(1000);
  const fade = Math.round(0.003 * SR);
  const cutoff = Math.min(9000, 700 * 2 ** (bright * 4)); // 700 Hz .. 9 kHz; two poles keep FM sidebands off Nyquist
  const a = 1 - Math.exp(-TAU * cutoff / SR);
  const idx = id.voice === "fm" ? id.fmIndex * (0.5 + bright) : 0; // brighter -> more sidebands
  const tri = id.voice === "tri" ? Math.max(1, Math.min(id.harmonics, Math.floor((0.45 * SR) / hz / 2))) : 0;
  let z = 0, z2 = 0;
  for (let n = 0; n < len; n++) {
    const t = n / SR;
    const env = Math.min(1, (n + 1) / atk) * Math.exp(-n / SR / tau) * Math.min(1, (len - n) / fade);
    let y;
    if (id.voice === "fm") y = Math.sin(TAU * hz * t + idx * Math.exp(-t / id.fmDecay) * Math.sin(TAU * hz * id.fmRatio * t));
    else { y = 0; for (let h = 0; h < tri; h++) { const k = 2 * h + 1; y += ((h & 1) ? -1 : 1) * Math.sin(TAU * hz * k * t) / (k * k); } y *= 8 / (Math.PI * Math.PI); }
    z += a * (y - z); z2 += a * (z - z2);
    out[start + n] += z2 * env * gain;
  }
}

// Voice gains: fm and tri are both roughly unit-amplitude before the lowpass; the family scale fixes the final level.
export function renderEvent(eventId, identityId, params = {}, scale = 1) {
  const ev = EVENTS[eventId], id = IDENTITIES[identityId];
  if (!ev) throw new Error(`unknown event: ${eventId}`);
  if (!id) throw new Error(`unknown identity: ${identityId}`);
  const p = clampParams(params);
  const decaySec = id.decay * p.decay * (eventId === "done" ? 1.4 : eventId === "message" ? 0.7 : 1);
  const bright = Math.min(1, Math.max(0, id.brightness + p.brightness));
  const lastMs = Math.max(...ev.notes.map((n) => n[1])) * p.tempo;
  const out = new Float32Array(Math.ceil((lastMs / 1000 + decaySec) * SR) + 1);
  for (const [semi, ms, g] of ev.notes) {
    const hz = id.rootHz * 2 ** ((semi + p.transpose) / 12);
    addNote(out, Math.round(ms * p.tempo * SR / 1000), hz, g, id, decaySec, bright);
  }
  for (let i = 0; i < out.length; i++) out[i] *= scale;
  return out;
}

// A family is rendered as one unit: unscaled renders, then ONE factor sets the loudest event to `level`, so relative
// loudness between events (waiting quieter than done) is kept and no event can exceed PEAK_CEILING.
export function renderFamily(identityId, eventIds = Object.keys(EVENTS), params = {}) {
  const p = clampParams(params);
  const raw = eventIds.map((e) => renderEvent(e, identityId, p, 1));
  let max = 0;
  for (const b of raw) for (let i = 0; i < b.length; i++) { const v = Math.abs(b[i]); if (v > max) max = v; }
  const f = max > 0 ? Math.min(p.level, PEAK_CEILING) / max : 0;
  const items = raw.map((b, i) => { for (let j = 0; j < b.length; j++) b[j] *= f; return { event: eventIds[i], samples: b, ...measure(b) }; });
  return { identity: identityId, params: p, sampleRate: SR, items };
}

export function measure(x) {
  let peak = 0, sq = 0;
  for (let i = 0; i < x.length; i++) { const a = Math.abs(x[i]); if (a > peak) peak = a; sq += x[i] * x[i]; }
  let onset = -1;
  for (let i = 0; i < x.length; i++) if (Math.abs(x[i]) >= 0.01) { onset = i; break; } // first sample at or above -40 dBFS
  return { peak, rmsDb: sq > 0 ? 10 * Math.log10(sq / x.length) : -Infinity, onsetMs: onset < 0 ? null : (onset / SR) * 1000, durationMs: (x.length / SR) * 1000 };
}

// ---- WAV (16-bit PCM mono) ----------------------------------------------------------------------------------------
export function encodeWav(x, sr = SR) {
  const buf = new ArrayBuffer(44 + x.length * 2), v = new DataView(buf);
  const s = (o, t) => { for (let i = 0; i < t.length; i++) v.setUint8(o + i, t.charCodeAt(i)); };
  s(0, "RIFF"); v.setUint32(4, 36 + x.length * 2, true); s(8, "WAVEfmt "); v.setUint32(16, 16, true); v.setUint16(20, 1, true);
  v.setUint16(22, 1, true); v.setUint32(24, sr, true); v.setUint32(28, sr * 2, true); v.setUint16(32, 2, true); v.setUint16(34, 16, true);
  s(36, "data"); v.setUint32(40, x.length * 2, true);
  for (let i = 0; i < x.length; i++) v.setInt16(44 + i * 2, Math.round(Math.max(-1, Math.min(1, x[i])) * 32767), true);
  return new Uint8Array(buf);
}

export function decodeWav(bytes) {
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (v.getUint32(0) !== 0x52494646 || v.getUint32(8) !== 0x57415645) throw new Error("not a RIFF/WAVE file");
  const sr = v.getUint32(24, true), n = v.getUint32(40, true) / 2, x = new Float32Array(n);
  for (let i = 0; i < n; i++) x[i] = v.getInt16(44 + i * 2, true) / 32767;
  return { sampleRate: sr, samples: x };
}

// ---- Pack ---------------------------------------------------------------------------------------------------------
const fileName = (identity, event) => `${identity}-${event}.wav`;

export function buildPack(family) {
  return {
    format: FORMAT,
    heard: false, // nobody has listened to this pack; the flag stays false until a person edits it
    sampleRate: family.sampleRate,
    identity: family.identity,
    params: family.params,
    events: Object.fromEntries(family.items.map((it) => [it.event, {
      file: fileName(family.identity, it.event),
      meaning: EVENTS[it.event].meaning,
      durationMs: Math.round(it.durationMs),
      peak: +it.peak.toFixed(4),
      onsetMs: +it.onsetMs.toFixed(2),
    }])),
  };
}

export function packReadme(pack) {
  const ev = Object.entries(pack.events).map(([k, e]) => `| ${k} | \`${e.file}\` | ${e.durationMs} ms | ${e.meaning} |`).join("\n");
  return `# Earcon pack: ${pack.identity}

Generated by the AquaSynth Earcon Workshop. **Nobody has listened to this pack.** Metrics (peak, onset, distinctness) say
the files are not silent, not clipped and differ from each other. They do not say the sounds are pleasant, clear in your
room, or fit your product. Audition them before you ship them.

16-bit PCM mono WAV, ${pack.sampleRate} Hz. \`pack.json\` maps event id to file; \`params\` regenerates the same family in the workshop.

| event | file | length | meaning |
|---|---|---|---|
${ev}

## Using it

**CI** (play on the runner or a dev box; never fail the build on a missing audio device):
\`\`\`sh
play_earcon() { f="$(dirname "$0")/earcons/$1"; { paplay "$f" || aplay -q "$f" || afplay "$f"; } 2>/dev/null || true; }
make test && play_earcon ${pack.events.success?.file ?? "success.wav"} || play_earcon ${pack.events.fail?.file ?? "fail.wav"}
\`\`\`
On a headless runner, attach the file to the job's notification instead (see chat bots).

**Chat bots** (Discord, Slack, Matrix): read \`pack.json\`, look up \`events[eventId].file\`, upload that WAV with the message.
\`\`\`js
const pack = JSON.parse(await fs.readFile("pack.json", "utf8"));
await channel.send({ content: "build finished", files: [path.join(dir, pack.events.done.file)] });
\`\`\`

**Apps** (web): \`new Audio(url).play()\` after a user gesture; browsers block autoplay. Keep one \`Audio\` per event and reset
\`currentTime\` before replaying. Native apps: load the WAV through the platform's short-sound API.

Keep the system volume yours: files peak at or under ${PEAK_CEILING} of full scale (level ${pack.params.level}), which is already loud on headphones.
`;
}

// ---- Minimal stored (uncompressed) ZIP, so the page can hand over one file ------------------------------------------
const CRC = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
export function crc32(b) { let c = 0xffffffff; for (let i = 0; i < b.length; i++) c = CRC[(c ^ b[i]) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; }

export function zip(files) { // files: [{name, bytes}]
  const enc = new TextEncoder(), parts = [], central = [];
  let off = 0;
  for (const f of files) {
    const name = enc.encode(f.name), crc = crc32(f.bytes), h = new DataView(new ArrayBuffer(30));
    h.setUint32(0, 0x04034b50, true); h.setUint16(4, 20, true); h.setUint16(8, 0, true); h.setUint16(12, 0x21, true); h.setUint16(10, 0, true);
    h.setUint32(14, crc, true); h.setUint32(18, f.bytes.length, true); h.setUint32(22, f.bytes.length, true); h.setUint16(26, name.length, true);
    parts.push(new Uint8Array(h.buffer), name, f.bytes);
    const c = new DataView(new ArrayBuffer(46));
    c.setUint32(0, 0x02014b50, true); c.setUint16(4, 20, true); c.setUint16(6, 20, true); c.setUint16(14, 0x21, true);
    c.setUint32(16, crc, true); c.setUint32(20, f.bytes.length, true); c.setUint32(24, f.bytes.length, true); c.setUint16(28, name.length, true); c.setUint32(42, off, true);
    central.push(new Uint8Array(c.buffer), name);
    off += 30 + name.length + f.bytes.length;
  }
  const cdSize = central.reduce((s, p) => s + p.length, 0), end = new DataView(new ArrayBuffer(22));
  end.setUint32(0, 0x06054b50, true); end.setUint16(8, files.length, true); end.setUint16(10, files.length, true); end.setUint32(12, cdSize, true); end.setUint32(16, off, true);
  const all = [...parts, ...central, new Uint8Array(end.buffer)], out = new Uint8Array(all.reduce((s, p) => s + p.length, 0));
  let o = 0; for (const p of all) { out.set(p, o); o += p.length; }
  return out;
}

export function packFiles(family) {
  const pack = buildPack(family), enc = new TextEncoder();
  return [
    ...family.items.map((it) => ({ name: fileName(family.identity, it.event), bytes: encodeWav(it.samples) })),
    { name: "pack.json", bytes: enc.encode(JSON.stringify(pack, null, 2) + "\n") },
    { name: "README.md", bytes: enc.encode(packReadme(pack)) },
  ];
}
export { fileName };
