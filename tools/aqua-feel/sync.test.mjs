// Checks of `aqua-feel sync` on synthetic fixtures with known offsets. No real capture is used. Usage: node sync.test.mjs
import assert from "node:assert/strict";
import { syncAnalyze, readWav, readTrack, readEvents, audioOnsets, visualOnsets, PROFILES } from "./sync.mjs";
import { wavFloat32 } from "./render.js";
import { makeFixture, FIXTURE_EVENTS_S } from "./sync-fixture.mjs";

let n = 0;
const t = (name, fn) => { fn(); n++; console.log(`ok   ${name}`); };
const run = (o, extra = {}) => { const fx = makeFixture(o); return { fx, r: syncAnalyze({ audio: fx.audio, sampleRate: fx.sampleRate, visual: fx.visual, events: fx.events, ...extra }) }; };

for (const off of [-60, -20, 0, 40, 80, 150, 250]) {
  t(`recovers a ${off} ms offset at 60 fps within the stated uncertainty, every event`, () => {
    const { r } = run({ offsetMs: off });
    assert.equal(r.pairs.length, FIXTURE_EVENTS_S.length);
    for (const p of r.pairs) assert.ok(Math.abs(p.lagMs - off) <= r.uncertaintyMs, `${p.label}: ${p.lagMs} vs ${off} (+/-${r.uncertaintyMs})`);
    assert.equal(r.summary.unpairedVisual, 0, "no spurious visual onsets");
    assert.equal(r.summary.unpairedAudio, 0, "no spurious audio onsets");
  });
}

t("30 fps: the visual onset lands on a frame at or after the event, so the lag error stays under one frame period", () => {
  for (const off of [0, 80]) {
    const { r } = run({ offsetMs: off, fps: 30 });
    for (const p of r.pairs) assert.ok(Math.abs(p.lagMs - off) <= 1000 / 30, `${p.lagMs} vs ${off}`);
  }
});

t("verdict classes follow the default window (lead 25, lag 100) beyond the uncertainty", () => {
  const verdictOf = (off) => run({ offsetMs: off }).r.summary;
  assert.equal(verdictOf(0).pass, true);
  assert.equal(verdictOf(80).pass, true, "80 ms late is inside the 100 ms lag window");
  assert.equal(verdictOf(150).pass, false);
  assert.equal(run({ offsetMs: 150 }).r.pairs[0].verdict, "audio-late-detectable");
  assert.equal(run({ offsetMs: -60 }).r.pairs[0].verdict, "audio-early-detectable");
  assert.equal(run({ offsetMs: 100 }).r.pairs[0].verdict, "borderline", "the window edge sits inside the uncertainty");
});

t("the profile changes the verdict, not the measurement", () => {
  const a = run({ offsetMs: 70 }, { profile: "default" }).r, b = run({ offsetMs: 70 }, { profile: "ebu-r37" }).r;
  assert.equal(a.pairs[0].lagMs, b.pairs[0].lagMs);
  assert.equal(a.summary.pass, true); assert.equal(b.summary.pass, false);
  assert.ok(PROFILES.default.leadMs === 25 && PROFILES.default.lagMs === 100);
});

t("without an events log, onsets pair by proximity and the offset is still recovered", () => {
  const fx = makeFixture({ offsetMs: 60 }), r = syncAnalyze({ audio: fx.audio, sampleRate: fx.sampleRate, visual: fx.visual });
  assert.equal(r.pairs.length, FIXTURE_EVENTS_S.length);
  for (const p of r.pairs) assert.ok(Math.abs(p.lagMs - 60) <= r.uncertaintyMs);
});

t("jitter shows as spread, not as a median: alternate events 0 and 90 ms late", () => {
  const fx = makeFixture({ offsetMs: 0 }), clean = makeFixture({ offsetMs: 90 }), a = Float32Array.from(fx.audio);
  const s0 = Math.round(FIXTURE_EVENTS_S[1] * 48000) - 100, s1 = Math.round(FIXTURE_EVENTS_S[1] * 48000) + 4800;
  for (let i = s0; i < s1; i++) a[i] = clean.audio[i]; // event 1 comes from the 90 ms-late render
  const r = syncAnalyze({ audio: a, sampleRate: 48000, visual: fx.visual, events: fx.events });
  assert.ok(r.summary.spreadMs > 80 && r.summary.spreadMs < 100, `spread ${r.summary.spreadMs}`);
});

t("an event with no sound is reported, not paired to a neighbour", () => {
  const fx = makeFixture({ offsetMs: 0 }), a = Float32Array.from(fx.audio);
  for (let i = Math.round(2.0 * 48000); i < Math.round(2.6 * 48000); i++) a[i] = 0; // silence the 2.2 s hit
  const r = syncAnalyze({ audio: a, sampleRate: 48000, visual: fx.visual, events: fx.events });
  const p = r.pairs.find((x) => x.eventS === 2.2);
  assert.equal(p.verdict, "no-audio-onset"); assert.equal(p.lagMs, null); assert.equal(r.summary.pass, false);
});

t("an offset past the 300 ms pairing window is reported as missing, not as a 300+ ms lag", () => {
  const { r } = run({ offsetMs: 400 });
  assert.equal(r.pairs[0].verdict, "no-audio-onset"); assert.equal(r.summary.pass, false);
});

t("silence and a flat track yield no onsets and no pass", () => {
  const r = syncAnalyze({ audio: new Float32Array(48000 * 2), sampleRate: 48000, visual: { t: Float64Array.from({ length: 120 }, (_, i) => i / 60), v: new Float64Array(120).fill(0.5) } });
  assert.equal(r.summary.audioOnsets, 0); assert.equal(r.summary.visualOnsets, 0); assert.equal(r.summary.pass, false);
});

t("a sine-bed with a swell rather than a click still gives one audio onset near its start", () => {
  const sr = 48000, x = new Float32Array(sr * 3);
  for (let i = Math.round(1 * sr); i < x.length; i++) { const tt = (i - sr) / sr; x[i] = 0.5 * Math.min(1, tt / 0.01) * Math.sin(2 * Math.PI * 440 * tt); }
  const o = audioOnsets(x, sr).onsets;
  assert.equal(o.length, 1); assert.ok(Math.abs(o[0].t - 1.0) < 0.012, `${o[0].t}`);
});

t("motion-flux track: a step in flux is an onset at its frame", () => {
  const tt = Float64Array.from({ length: 90 }, (_, i) => i / 30), v = new Float64Array(90); v[30] = 0.4; v[31] = 0.3;
  const o = visualOnsets({ t: tt, v }, { kind: "flux" }).onsets;
  assert.equal(o.length, 1); assert.ok(Math.abs(o[0].t - 1.0) < 1e-9);
});

t("readWav reads 16-bit PCM and 32-bit float, mixes stereo to mono, and rejects non-WAV", () => {
  const f = wavFloat32(Float32Array.from([0, 0.5, -0.5])), w = readWav(f);
  assert.equal(w.sampleRate, 48000); assert.deepEqual(Array.from(w.samples), [0, 0.5, -0.5]);
  const b = Buffer.alloc(44 + 8); b.write("RIFF", 0); b.writeUInt32LE(44, 4); b.write("WAVEfmt ", 8); b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(2, 22);
  b.writeUInt32LE(44100, 24); b.writeUInt32LE(176400, 28); b.writeUInt16LE(4, 32); b.writeUInt16LE(16, 34); b.write("data", 36); b.writeUInt32LE(8, 40);
  b.writeInt16LE(16384, 44); b.writeInt16LE(-16384, 46); b.writeInt16LE(8192, 48); b.writeInt16LE(8192, 50);
  const s = readWav(b); assert.equal(s.sampleRate, 44100); assert.deepEqual(Array.from(s.samples), [0, 0.25]);
  assert.throws(() => readWav(Buffer.from("not a wav file at all")));
});

t("readTrack takes header + t,value rows or a single column with fps; readEvents takes lines or JSON", () => {
  const a = readTrack("t,luma\n0,0.1\n0.1,0.2\n0.2,0.3\n"); assert.deepEqual(Array.from(a.t), [0, 0.1, 0.2]);
  const b = readTrack("0.1\n0.2\n0.3\n", 10); assert.ok(Math.abs(b.t[2] - 0.2) < 1e-12);
  assert.throws(() => readTrack("0.1\n0.2\n0.3\n"), /fps/);
  assert.deepEqual(readEvents("# hits\n0.5,kick\n1.25\n").map((e) => e.t), [0.5, 1.25]);
  assert.deepEqual(readEvents('[{"t":2,"label":"x"},3]').map((e) => e.label), ["x", "e1"]);
});

console.log(`${n} sync checks passed. Synthetic fixtures only; nothing was heard or seen.`);
