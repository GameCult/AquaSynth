// node test.mjs: manifest validation, ordering, verdict export shape, and the built page's isolation. No browser; see README for the browser smoke.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdtempSync, writeFileSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { buildKit } from "./build.mjs";

const L = createRequire(import.meta.url)("./logic.cjs");
let n = 0;
const test = (name, fn) => { fn(); n++; console.log("ok", name); };
const clip = (id, extra = {}) => ({ id, file: `${id}.wav`, title: `Title ${id}`, feel: "calm", group: "g", ...extra });
const mf = (clips) => ({ kit: { id: "k1", title: "Kit" }, clips });
const has = (m, re) => L.validateManifest(m).some((e) => re.test(e));

test("a valid manifest has no problems", () => assert.deepEqual(L.validateManifest(mf([clip("a", { pair: "b" }), clip("b", { pair: "a" }), clip("c")])), []));
test("manifest must be an object with clips", () => { assert.ok(L.validateManifest([]).length); assert.ok(has(mf([]), /non-empty/)); });
test("kit id and title are required", () => { assert.ok(has({ kit: { id: "a b", title: "x" }, clips: [clip("a")] }, /kit\.id/)); assert.ok(has({ kit: { id: "a" }, clips: [clip("a")] }, /kit\.title/)); });
test("every clip field is required", () => { for (const k of ["id", "file", "title", "feel", "group"]) { const c = clip("a"); delete c[k]; assert.ok(has(mf([c]), new RegExp(k + " is required")), k); } });
test("duplicate ids are rejected", () => assert.ok(has(mf([clip("a"), clip("a", { file: "z.wav" })]), /duplicate id/)));
test("files must be audio and stay inside the folder", () => {
  assert.ok(has(mf([clip("a", { file: "a.txt" })]), /must be \.wav/));
  assert.ok(has(mf([clip("a", { file: "../a.wav" })]), /relative path/));
  assert.ok(has(mf([clip("a", { file: "/etc/a.wav" })]), /relative path/));
  assert.ok(has(mf([clip("a", { file: "C:\\a.wav" })]), /relative path/));
});
test("pairs must exist, be mutual, and not be self", () => {
  assert.ok(has(mf([clip("a", { pair: "zz" })]), /not a clip id/));
  assert.ok(has(mf([clip("a", { pair: "b" }), clip("b")]), /does not point back/));
  assert.ok(has(mf([clip("a", { pair: "b" }), clip("b", { pair: "c" }), clip("c", { pair: "b" })]), /does not point back/));
  assert.ok(has(mf([clip("a", { pair: "a" })]), /itself/));
});

const clips = Array.from({ length: 12 }, (_, i) => clip("c" + i, i < 4 ? { pair: i % 2 ? "c" + (i - 1) : "c" + (i + 1) } : {}));
test("order is deterministic per seed, covers every clip once, and varies by seed", () => {
  const a = L.planOrder(clips, 7), b = L.planOrder(clips, 7);
  assert.deepEqual(a, b);
  assert.deepEqual(a.map((o) => o.id).sort(), clips.map((c) => c.id).sort());
  assert.notDeepEqual(L.planOrder(clips, 8).map((o) => o.id), a.map((o) => o.id));
});
test("a pair is adjacent, slotted A then B, and both clips take position A across seeds", () => {
  const firstA = new Set();
  for (let s = 1; s <= 40; s++) {
    const o = L.planOrder(clips, s);
    o.forEach((x, i) => { if (x.slot === "A") { assert.equal(o[i + 1].slot, "B"); assert.equal(clips.find((c) => c.id === x.id).pair, o[i + 1].id); if (x.id === "c0" || x.id === "c1") firstA.add(x.id); } else if (x.slot === null) assert.ok(!clips.find((c) => c.id === x.id).pair); });
  }
  assert.deepEqual([...firstA].sort(), ["c0", "c1"]);
});

const manifest = mf([clip("a", { pair: "b", title: "Pipe | one" }), clip("b", { pair: "a" }), clip("c")]);
test("verdict.json has the exported shape", () => {
  const order = [{ id: "c", slot: null }, { id: "b", slot: "A" }, { id: "a", slot: "B" }];
  const v = L.buildVerdict(manifest, order, { seed: 5, consent: true, listener: "Pat", device: "headphones", notes: "hi",
    answers: { c: { verdict: "keep", reason: "clean", landed: 5, device: "headphones" }, a: { verdict: "bogus", landed: 9 } }, pairs: { "b|a": "B" } }, "2026-01-01T00:00:00Z");
  assert.equal(v.schema, L.SCHEMA);
  assert.deepEqual(Object.keys(v), ["schema", "kit", "exportedAt", "seed", "consent", "listener", "device", "notes", "summary", "clips", "pairs"]);
  assert.deepEqual(v.summary, { total: 3, keep: 1, fix: 0, drop: 0, unanswered: 2 });
  assert.deepEqual(Object.keys(v.clips[0]), ["position", "id", "title", "group", "feel", "file", "pair", "slot", "verdict", "reason", "landed", "device"]);
  assert.deepEqual(v.clips.map((c) => c.id), ["c", "b", "a"]);
  assert.equal(v.clips[0].verdict, "keep"); assert.equal(v.clips[0].landed, 5);
  assert.equal(v.clips[2].verdict, null, "an unknown verdict is blank, not passed through"); assert.equal(v.clips[2].landed, null, "landed outside 1-5 is blank");
  assert.deepEqual(v.pairs, [{ a: "b", b: "a", preferred: "a" }], "pair choice B means the clip shown second");
  JSON.parse(JSON.stringify(v));
});
test("verdict.md lists every clip, escapes pipes, and states the totals", () => {
  const order = L.planOrder(manifest.clips, 3);
  const v = L.buildVerdict(manifest, order, { seed: 3, consent: true, answers: { c: { verdict: "drop", reason: "line1\nline2 | x", landed: 1 } }, pairs: {} }, "t");
  const md = L.verdictMarkdown(v);
  assert.match(md, /^# Listening verdict: Kit/);
  assert.match(md, /0 keep, 0 fix, 1 drop, 2 unanswered\*\* of 3/);
  assert.match(md, /Pipe \\\| one/); assert.match(md, /line1 line2 \\\| x/);
  assert.equal(md.split("\n").filter((l) => /^\| \d+ \|/.test(l)).length, 3);
  assert.match(md, /## A\/B pairs/);
});

const dir = mkdtempSync(join(tmpdir(), "aqua-listen-"));
const wav = Buffer.alloc(44 + 400); wav.write("RIFF", 0); wav.write("WAVE", 8);
for (const id of ["a", "b", "c"]) writeFileSync(join(dir, id + ".wav"), wav);
test("the built page is one file with no network reach", () => {
  const r = buildKit(manifest, dir), h = r.html;
  assert.match(h, /Content-Security-Policy" content="default-src 'none'/);
  assert.doesNotMatch(h, /connect-src|img-src|<link |<script[^>]*\ssrc=|<img |<iframe|@import|url\(/);
  assert.doesNotMatch(h, /\b(fetch|XMLHttpRequest|WebSocket|sendBeacon)\b/);
  assert.equal((h.match(/<script/g) || []).length, 3);
  const data = JSON.parse(/<script id="kit-data" type="application\/json">(.*?)<\/script>/s.exec(h)[1]);
  assert.equal(Buffer.from(data.audio.a.b64, "base64").compare(wav), 0);
  assert.equal(data.hash, r.hash);
});
test("the page shows the consent note first and skips it only for a returning, consenting listener", () => {
  const h = buildKit(manifest, dir).html;
  assert.ok(h.indexOf("function consent()") < h.indexOf("function main()"));
  assert.ok(h.includes("if (load() && load().consent) start(); else consent();"));
  assert.ok(h.includes("sends nothing anywhere"));
});
test("the content hash changes when a clip changes", () => {
  const h1 = buildKit(manifest, dir).hash; writeFileSync(join(dir, "c.wav"), Buffer.concat([wav, Buffer.from([1])]));
  assert.notEqual(buildKit(manifest, dir).hash, h1);
});
test("build refuses a missing file and a manifest escape, naming them", () => {
  assert.throws(() => buildKit(mf([clip("zz")]), dir), /file not found: zz\.wav/);
  assert.throws(() => buildKit(mf([clip("a", { file: "../x.wav" })]), dir), /relative path/);
  assert.throws(() => buildKit(mf([clip("a", { title: "" })]), dir), /title is required/);
});
test("pictures ride in a collapsed block only for clips that have both, and only then does the policy admit data: images", () => {
  const pics = mkdtempSync(join(tmpdir(), "aqua-listen-img-")), png = Buffer.from("89504e470d0a1a0a", "hex");
  writeFileSync(join(pics, "a.spec.png"), png); writeFileSync(join(pics, "a.wave.png"), png); writeFileSync(join(pics, "b.spec.png"), png);
  const withPics = buildKit(manifest, dir, pics), data = JSON.parse(/<script id="kit-data" type="application\/json">(.*?)<\/script>/s.exec(withPics.html)[1]);
  assert.deepEqual(Object.keys(data.images), ["a"]);
  assert.deepEqual(data.images.a.map((p) => p.label), ["spectrogram", "waveform"]);
  assert.match(withPics.html, /media-src blob:; img-src data:/);
  const bare = buildKit(manifest, dir);
  assert.doesNotMatch(bare.html, /img-src/); assert.deepEqual(JSON.parse(/<script id="kit-data" type="application\/json">(.*?)<\/script>/s.exec(bare.html)[1]).images, {});
  assert.notEqual(withPics.hash, bare.hash);
});
test("markup in a title cannot break the page", () => {
  const m = mf([clip("a", { title: "</script><b>" , feel: "</script>"})]);
  const h = buildKit(m, dir).html;
  assert.equal((h.match(/<\/script>/g) || []).length, 3);
});
const st = (answers, pairs = {}) => ({ seed: 7, answers, pairs, consent: true });
test("autosave key is stable across a content-hash change and moves with the clip-id set", () => {
  const m = mf([clip("b"), clip("a")]);
  assert.equal(L.saveKey(m), L.saveKey(mf([clip("a"), clip("b", { title: "reworded", feel: "other" })])), "title/feel edits and clip order do not move the key");
  assert.notEqual(L.saveKey(m), L.saveKey(mf([clip("a"), clip("b"), clip("c")])));
  assert.notEqual(L.saveKey(m), L.saveKey(mf([clip("a"), clip("z")])));
  assert.notEqual(L.saveKey(m), L.saveKey({ kit: { id: "k2", title: "Kit" }, clips: m.clips }));
  assert.ok(!/^aqua-listen:/.test(L.saveKey(m)), "new keys are never mistaken for hash-keyed legacy entries");
  const dir2 = mkdtempSync(join(tmpdir(), "aqlk-")); writeFileSync(join(dir2, "a.wav"), Buffer.alloc(50)); writeFileSync(join(dir2, "b.wav"), Buffer.alloc(50));
  const b1 = buildKit(m, dir2); writeFileSync(join(dir2, "b.wav"), Buffer.alloc(51)); const b2 = buildKit(m, dir2);
  assert.notEqual(b1.hash, b2.hash);
  assert.ok(b1.html.includes("saveKey") && !b1.html.includes("${kit.hash}"), "the built page keys on saveKey, not the hash");
});
test("legacy migration adopts the hash-keyed entry of the same kit", () => {
  const m = mf([clip("a"), clip("b")]);
  const old = { key: "aqua-listen:k1:93312816", state: st({ a: { verdict: "keep", landed: 3 } }) };
  assert.equal(L.pickLegacySave(m, [{ key: "unrelated", state: null }, old]), old.key);
  const fuller = { key: "aqua-listen:k1:00000001", state: st({ a: { verdict: "keep", landed: 3, reason: "x" }, b: { verdict: "fix" } }) };
  assert.equal(L.pickLegacySave(m, [old, fuller]), fuller.key, "the fuller entry wins");
});
test("legacy migration ignores other kits, foreign clip ids, malformed keys and empty state", () => {
  const m = mf([clip("a"), clip("b")]), ans = { a: { verdict: "keep" } };
  for (const e of [
    { key: "aqua-listen:k2:93312816", state: st(ans) },                         // another kit
    { key: "aqua-listen:k1:93312816", state: st({ zz: { verdict: "keep" } }) },  // foreign clip id in answers
    { key: "aqua-listen:k1:93312816", state: st(ans, { "a|zz": "A" }) },          // foreign clip id in pairs
    { key: "aqua-listen:k1:nothex", state: st(ans) }, { key: "aqua-listen:k1:93312816:x", state: st(ans) },
    { key: "aqua-listen:k1:93312816", state: null }, { key: "aqua-listen:k1:93312816", state: { answers: ans } }])
    assert.equal(L.pickLegacySave(m, [e]), null, JSON.stringify(e.key) + JSON.stringify(e.state));
});
test("restoreSave: reordered clips keep answers and seed but drop pair picks; same order keeps them", () => {
  const m = mf([clip("a", { pair: "b" }), clip("b", { pair: "a" }), clip("c")]), rev = mf([...m.clips].reverse());
  const saved = { ...st({ a: { verdict: "keep" }, c: { verdict: "fix" } }, { "a|b": "A" }), pairSig: L.pairSig(m) };
  assert.deepEqual(L.restoreSave(m, saved, false).pairs, { "a|b": "A" });
  const r = L.restoreSave(rev, saved, false);
  assert.deepEqual(r.pairs, {}); assert.equal(r.seed, 7); assert.equal(r.answers.a.verdict, "keep"); assert.equal(r.pairSig, L.pairSig(rev));
  assert.deepEqual(L.restoreSave(m, st({}, { "a|b": "A" }), false).pairs, {}, "no signature: pairs dropped");
  assert.deepEqual(L.restoreSave(m, st({}, { "a|b": "A" }), true).pairs, { "a|b": "A" }, "legacy adoption keeps pairs");
  assert.equal(L.restoreSave(m, st({}, { "a|b": "A" }), true).pairSig, L.pairSig(m), "and stamps the current signature");
  assert.equal(L.restoreSave(m, { answers: {} }, false), null);
});
test("restoreSave: a save without pairs or with array answers is normalised, not left to crash the page", () => {
  const m = mf([clip("a")]);
  for (const bad of [{ seed: 3 }, { seed: 3, answers: [], pairs: null }, { seed: 3, answers: "x", pairs: [] }]) {
    const r = L.restoreSave(m, bad, true); assert.deepEqual(r.answers, {}); assert.deepEqual(r.pairs, {});
    L.buildVerdict(m, L.planOrder(m.clips, r.seed), r, "now");
  }
});
test("the built page keys its save through saveKey and never builds a hash key", () => {
  const h = buildKit(mf([clip("a")]), dir).html;
  assert.ok(h.includes("KEY = L.saveKey(kit.manifest)"));
  assert.ok(!/kit\.hash/.test(h) && !/KEY = [`"']/.test(h) && !/localStorage\.(set|get)Item\(`/.test(h), "no hash-form or string-built key in the page source");
});
console.log(`${n} tests passed`);
