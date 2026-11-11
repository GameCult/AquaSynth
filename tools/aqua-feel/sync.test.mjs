// Checks of `aqua-feel sync` on synthetic fixtures with known offsets. No real capture is used. Usage: node sync.test.mjs
import assert from "node:assert/strict";
import { formatSync, syncAnalyze, readWav, readTrack, readEvents, audioOnsets, visualOnsets, sourceFrames, PROFILES } from "./sync.mjs";
import { readFileSync } from "node:fs";
import { wavFloat32 } from "./render.js";
import { makeFixture, FIXTURE_EVENTS_S } from "./sync-fixture.mjs";

let n = 0;
const t = (name, fn) => { fn(); n++; console.log(`ok   ${name}`); };
const run = (o, extra = {}) => { const fx = makeFixture(o); return { fx, r: syncAnalyze({ audio: fx.audio, sampleRate: fx.sampleRate, visual: fx.visual, events: fx.events, ...extra }) }; };

// The truth must lie in the interval each pair reports ([lagLowMs, lagHighMs]); the interval is the claim, a point estimate is not.
const inInterval = (p, truth) => p.lagLowMs - 1e-9 <= truth && truth <= p.lagHighMs + 1e-9;
const OFFGRID = [0.5042, 1.3117, 2.2209, 3.4033, 4.1161, 5.0087]; // events between frame boundaries at 30 and 60 fps

for (const fps of [30, 60]) for (const off of [-60, -20, 0, 40, 80, 150, 190]) {
  t(`${fps} fps, ${off} ms offset: the truth is inside every pair's interval, events on and off the frame grid`, () => {
    for (const events of [FIXTURE_EVENTS_S, OFFGRID]) {
      const { r } = run({ offsetMs: off, fps, events });
      assert.equal(r.pairs.length, events.length);
      for (const p of r.pairs) assert.ok(inInterval(p, off), `${p.label}: [${p.lagLowMs}, ${p.lagHighMs}] vs ${off}`);
      assert.equal(r.summary.unpairedVisual, 0, "no spurious visual onsets"); assert.equal(r.summary.unpairedAudio, 0, "no spurious audio onsets");
    }
  });
}

t("the interval is no wider than one frame period plus one audio hop (it is a measurement, not a shrug)", () => {
  const { r } = run({ offsetMs: 80, fps: 60 }), p = r.pairs[0];
  assert.ok(p.lagHighMs - p.lagLowMs <= r.framePeriodMs + r.audioResolutionMs + 1e-9);
});

t("verdict classes follow the default window (lead 25, lag 100) judged on the interval", () => {
  const v = (off, o = {}) => run({ offsetMs: off, ...o }).r;
  assert.equal(v(0).summary.pass, true);
  assert.equal(v(60).summary.pass, true, "60 ms late is inside the 100 ms lag window");
  assert.equal(v(150).pairs[0].verdict, "audio-late-detectable");
  assert.equal(v(-60).pairs[0].verdict, "audio-early-detectable");
  assert.equal(v(95).pairs[0].verdict, "borderline", "the interval straddles the 100 ms edge");
  assert.equal(v(95).summary.pass, true, "borderline is not detectably outside, so it does not fail the gate");
  assert.match(formatSync(v(95)), /borderline/, "but the report says so");
  assert.match(formatSync(v(0)), /WITHIN WINDOW/); assert.doesNotMatch(formatSync(v(95)), /WITHIN WINDOW/);
  assert.equal(v(85, { fps: 120 }).pairs[0].verdict, "inside-window", "a finer frame clock narrows the interval");
  assert.equal(v(85, { fps: 30 }).pairs[0].verdict, "borderline", "a coarser one widens it");
});

t("the profile changes the verdict, not the measurement", () => {
  const a = run({ offsetMs: 70 }, { profile: "default" }).r, b = run({ offsetMs: 70 }, { profile: "ebu-r37" }).r;
  assert.equal(a.pairs[0].lagMs, b.pairs[0].lagMs);
  assert.equal(a.summary.pass, true); assert.equal(b.summary.pass, false);
  assert.match(run({ offsetMs: 70 }, { lagMs: 200 }).r.profile.note, /overridden/, "an overridden window is not reported as the default profile");
  assert.ok(PROFILES.default.leadMs === 25 && PROFILES.default.lagMs === 100);
});

t("without an events log, onsets pair by proximity and the offset is still recovered", () => {
  const fx = makeFixture({ offsetMs: 60 }), r = syncAnalyze({ audio: fx.audio, sampleRate: fx.sampleRate, visual: fx.visual });
  assert.equal(r.pairs.length, FIXTURE_EVENTS_S.length);
  for (const p of r.pairs) assert.ok(inInterval(p, 60));
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

t("an offset past the 200 ms pairing window is reported as missing, not as a longer lag", () => {
  const { r } = run({ offsetMs: 250 });
  assert.equal(r.pairs[0].verdict, "no-audio-onset"); assert.equal(r.summary.pass, false);
});

t("sparse events (0.7 s or more apart) pair without ambiguity at a +80 ms lag; the same lag at 0.3 s spacing is ambiguous", () => {
  assert.equal(run({ offsetMs: 80 }).r.summary.ambiguous, 0);
  const ev = [0.5, 0.8, 1.1, 1.4, 1.7];
  const { r } = run({ offsetMs: 80, events: ev });
  assert.equal(r.summary.ambiguous, ev.length); assert.equal(r.summary.pass, false);
});

// ---- regressions from the Soul pass on PR #32 ----
t("a noise bed, a steady-tone bed or a DC offset under the hits does not move the audio onset (refinement is relative to the bed)", () => {
  for (const o of [{ noise: 0.03 }, { bed: [[220, 0.05], [330, 0.03]] }, { dc: 0.25 }, { gain: 0.03, noise: 0.003 }, { dc: -0.3, noise: 0.02 }]) {
    const { r } = run({ offsetMs: 50, ...o });
    assert.equal(r.pairs.length, 6, JSON.stringify(o));
    for (const p of r.pairs) assert.ok(inInterval(p, 50), `${JSON.stringify(o)} ${p.label}: ${p.lagMs}`);
  }
});

t("dense hits: events 120 ms apart with a +80 ms lag are not silently paired to the previous hit", () => {
  const ev = [0.5, 0.62, 0.74, 0.86, 0.98, 1.1, 1.22];
  const { r } = run({ offsetMs: 80, events: ev });
  // Either the pairing is right, or it is flagged; it is never a confident wrong lag with a pass.
  for (const p of r.pairs) assert.ok(inInterval(p, 80) || p.verdict === "ambiguous-pairing" || p.lagMs === null, `${p.label}: ${p.lagMs} ${p.verdict}`);
  assert.equal(r.summary.pass, false, "overlapping candidates inside the pairing window cannot pass");
  assert.ok(r.summary.ambiguous > 0);
});

t("an audio onset is never reused by two events", () => {
  const { r } = run({ offsetMs: 200, events: [0.5, 0.58, 0.66, 0.74] });
  const used = r.pairs.map((p) => p.audioS).filter((x) => x !== null);
  assert.equal(new Set(used).size, used.length);
});

t("a narrower --pair-window-ms resolves dense hits that the default calls ambiguous", () => {
  const ev = [0.5, 0.62, 0.74, 0.86, 0.98];
  const { r } = run({ offsetMs: 30, events: ev }, { pairWindowMs: 50 });
  for (const p of r.pairs) assert.ok(inInterval(p, 30), `${p.label}: ${p.lagMs}`);
  assert.equal(r.summary.ambiguous, 0);
});

t("flashes closer than the visual minimum gap merge, and then an event goes unpaired rather than getting another flash's lag", () => {
  const { r } = run({ offsetMs: 0, events: [0.5, 0.56, 0.62, 1.5] });
  assert.equal(r.summary.pass, false);
  for (const p of r.pairs) assert.ok(p.lagMs === null || inInterval(p, 0) || p.verdict === "ambiguous-pairing", `${p.label}: ${p.lagMs} ${p.verdict}`);
});

t("a stray sound or flash fails the check unless --allow-unpaired is given", () => {
  const fx = makeFixture({ offsetMs: 0 }), a = Float32Array.from(fx.audio), click = makeFixture({ offsetMs: 0, events: [2.8] }).audio;
  for (let i = 134000; i < 137500; i++) a[i] += click[i];
  const run2 = (o) => syncAnalyze({ audio: a, sampleRate: 48000, visual: fx.visual, events: fx.events, ...o });
  assert.equal(run2({}).summary.unpairedAudio, 1); assert.equal(run2({}).summary.pass, false);
  assert.equal(run2({ allowUnpaired: true }).summary.pass, true);
});

t("bad options fail loudly: NaN window, unknown profile", () => {
  const fx = makeFixture({});
  assert.throws(() => syncAnalyze({ audio: fx.audio, sampleRate: 48000, visual: fx.visual, lagMs: NaN }), /non-negative/);
  assert.throws(() => syncAnalyze({ audio: fx.audio, sampleRate: 48000, visual: fx.visual, profile: "nope" }), /unknown profile/);
});

t("parsers refuse input that would give a plausible wrong answer", () => {
  assert.throws(() => readEvents("0:00.5\n0:01.2\n"), /does not start with a time/);
  assert.throws(() => readEvents("time,label\nfoo,bar\n"), /line 2/);
  assert.deepEqual(readEvents("time,label\n0.5,a\n1.5,b, loud\n").map((e) => e.label), ["a", "b, loud"], "a header on line 1 is skipped; commas stay in the label");
  assert.throws(() => readEvents("[0.5, null]"), /no numeric time/);
  assert.throws(() => readEvents('[{"time":2}]'), /no numeric time/);
  assert.throws(() => readEvents("# only a comment\n"), /no events/);
  assert.throws(() => readTrack("0,0,0.1\n1,0.1,0.2\n2,0.2,0.3\n"), /3 columns/);
  assert.throws(() => readTrack("0.0,0.1\n0.0167,\n0.0333,0.3\n"), /earlier lines had 2/);
  assert.throws(() => readTrack("0,0.1\n0.5,0.2\n0.4,0.3\n"), /does not increase/);
  assert.throws(() => readEvents('[{"t":null}]'), /no numeric time/); assert.throws(() => readEvents('[{"t":""},1]'), /no numeric time/);
  assert.throws(() => readEvents('[{"t":true}]'), /no numeric time/); assert.throws(() => readEvents("0x10\n"), /does not start with a time/);
  assert.throws(() => readEvents('[{"t":1,"label":{"a":1}}]'), /label must be a string/);
  assert.throws(() => readEvents("0:01.5\n"), /does not start with a time/);
});

t("valid layouts are accepted: header after blank or comment lines, semicolons, trailing commas, CRLF, BOM", () => {
  assert.equal(readTrack("\n\nt,luma\r\n0,0.1,\r\n0.1;0.2\r\n0.2 0.3\r\n").v.length, 3);
  assert.equal(readTrack("\uFEFF0,0.1\n0.1,0.2\n0.2,0.3\n").v.length, 3);
  assert.equal(readEvents("0.5,a\r1.0,b\r").length, 2, "CR-only line ends");
  assert.equal(readTrack("t0,luma_0to1\n0,0.1\n0.1,0.2\n0.2,0.3\n").v.length, 3, "a header may contain digits");
  assert.deepEqual(readEvents("# list\ntime (s);label\n0.5;kick\n1.0;snare, loud\n").map((e) => [e.t, e.label]), [[0.5, "kick"], [1, "snare, loud"]]);
});

t("zero-width windows are expressible: --lag-ms 0 makes any positive lag outside", () => {
  assert.equal(run({ offsetMs: 40 }, { lagMs: 0 }).r.summary.pass, false);
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

t("sourceFrames: the rate is the median frame gap, the stale-frame bound is the 95th-percentile gap", () => {
  const si = (ts) => ts.map((x, i) => `[Parsed_showinfo_0 @ 0x1] n:${i} pts:${x * 1000} pts_time:${x} duration:1`).join("\n");
  const cfr = Array.from({ length: 40 }, (_, i) => i / 50);
  { const c = sourceFrames(si(cfr)); assert.equal(c.fps, 50); assert.ok(Math.abs(c.gapMs - 20) < 1e-6); }
  const vfr = []; let x = 0; for (let i = 0; i < 100; i++) { vfr.push(x); x += i % 10 === 9 ? 0.1 : 0.0167; }  // mostly 60 fps, one 100 ms stall in ten
  const r = sourceFrames(si(vfr)); assert.equal(r.fps, 60); assert.ok(Math.abs(r.gapMs - 100) < 1, `gap ${r.gapMs}`);
  assert.deepEqual(sourceFrames(si([0])), { fps: 30, gapMs: 0 });
  assert.equal(sourceFrames(si([0, 1, 2])).fps, 24); assert.equal(sourceFrames(si([0, 0.001, 0.002])).fps, 120);
});

// The video path needs ffmpeg (FFMPEG=/path or on PATH); skipped, and said so, when there is none.
{
  const { readVideo } = await import("./sync.mjs"), { execFileSync } = await import("node:child_process");
  const ff = process.env.FFMPEG ?? "ffmpeg";
  let have = true; try { execFileSync(ff, ["-version"], { stdio: "ignore" }); } catch { have = false; }
  if (!have) console.log("skip video path: no ffmpeg (set FFMPEG)");
  if (have) for (const [vOff, aOff, vfps] of [[0, 0, 60], [0.06, 0, 60], [0, 0.08, 60], [0.25, 0.1, 60], [0.5, 0.5, 60], [0.1, 0, 15], [0.1, 0, 24], [0, 0.1, 15]]) {
    const { mkdtempSync, writeFileSync } = await import("node:fs"), { tmpdir } = await import("node:os"), { join } = await import("node:path");
    const dir = mkdtempSync(join(tmpdir(), "sync-clip-")), fx = makeFixture({ offsetMs: 0, fps: 60, seconds: 4, events: [0.5, 1.4, 2.3, 3.2] });
    const wavP = join(dir, "a.wav"), rawP = join(dir, "v.gray"), outP = join(dir, "clip.mkv");
    writeFileSync(wavP, wavFloat32(fx.audio));
    writeFileSync(rawP, Buffer.concat(Array.from(fx.visual.v, (x) => Buffer.alloc(64 * 36, Math.round(255 * x)))));
    // encode each stream alone, then mux with -itsoffset and stream copy: that is what puts a start_time on the stream in the file
    const vP = join(dir, "v.mkv"), aP = join(dir, "a.mka");
    execFileSync(ff, ["-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "gray", "-s", "64x36", "-r", "60", "-i", rawP, "-vf", `fps=${vfps}:round=up`, "-c:v", "ffv1", vP]);
    execFileSync(ff, ["-v", "error", "-y", "-i", wavP, "-c:a", "pcm_s16le", aP]);
    execFileSync(ff, ["-v", "error", "-y", "-itsoffset", String(vOff), "-i", vP, "-itsoffset", String(aOff), "-i", aP, "-map", "0:v", "-map", "1:a", "-c", "copy", outP]);
    const v = readVideo(outP, { ffmpeg: ff }), truth = (aOff - vOff) * 1000; // audio = visual shifted by (aOff - vOff)
    // events are on the container clock, which ffmpeg shifts so the earliest stream starts at 0
    const r = syncAnalyze({ audio: v.audio.samples, sampleRate: v.audio.sampleRate, audioStartS: v.audio.startS, visual: v.luma, events: fx.events.map((e) => ({ t: e.t + vOff - Math.min(vOff, aOff), label: e.label })), visualResampled: v.resampled });
    n++; console.log(`ok   ffmpeg clip, video (${vfps} fps) starts ${vOff * 1000} ms, audio ${aOff * 1000} ms: true lag ${truth} ms inside every interval`);
    for (const p of r.pairs) assert.ok(p.lagLowMs - 1e-6 <= truth && truth <= p.lagHighMs + 1e-6, `video@${vOff}/${vfps}fps audio@${aOff}: [${p.lagLowMs}, ${p.lagHighMs}] vs ${truth}`);
    assert.equal(r.pairs.length, 4);
  }
  if (have) t("a real browser capture (VFR vp8+opus webm): every logged hit pairs, and the lag is a constant (spread under one frame)", () => {
    const v = readVideo(new URL("./fixtures/browser-capture.webm", import.meta.url).pathname, { ffmpeg: ff });
    const ev = readEvents(readFileSync(new URL("./fixtures/browser-capture.events.txt", import.meta.url), "utf8"));
    const r = syncAnalyze({ audio: v.audio.samples, sampleRate: v.audio.sampleRate, audioStartS: v.audio.startS, visual: v.luma, events: ev });
    assert.equal(r.pairs.length, 7); assert.equal(r.summary.missing, 0);
    assert.ok(r.summary.spreadMs < 1000 / v.fps, `spread ${r.summary.spreadMs}`);
    assert.equal(r.summary.unpairedAudio + r.summary.unpairedVisual, 0);
    for (const p of r.pairs) assert.ok(p.visualVsEventMs >= 0 && p.visualVsEventMs <= 1000 / v.fps, `flash ${p.visualVsEventMs} ms off its logged instant`);
  });
  if (have) t("a Perlines v2 capture declares no frame rate (1k tbr): the rate comes from its frame times, and the interval is no narrower than its worst frame gaps", () => {
    const v = readVideo(new URL("./fixtures/perlines-aurora-vfr.webm", import.meta.url).pathname, { ffmpeg: ff });
    assert.ok(v.fps >= 50, `resampled at ${v.fps} fps, not the 30 fps fallback`);
    assert.ok(v.sourceGapMs > 1000 / v.fps * 1.5, `source gap ${v.sourceGapMs} ms is a stalled capture, wider than one frame`);
    const r = syncAnalyze({ audio: v.audio.samples, sampleRate: v.audio.sampleRate, audioStartS: v.audio.startS, visual: v.luma, allowUnpaired: true, visualResampled: true, visualGapMs: v.sourceGapMs });
    assert.ok(r.pairs.length >= 3);
    for (const p of r.pairs) assert.ok(p.lagHighMs - p.lagLowMs >= v.sourceGapMs * 1.5, `interval ${p.lagHighMs - p.lagLowMs} ms narrower than the capture's own stalls`);
    assert.match(formatSync(r), /source frames are up to that far apart/);
  });
}

console.log(`${n} sync checks passed. Fixtures only (synthetic, plus one browser capture when ffmpeg is present); nothing was heard or seen.`);
