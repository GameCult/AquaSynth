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
test("markup in a title cannot break the page", () => {
  const m = mf([clip("a", { title: "</script><b>" , feel: "</script>"})]);
  const h = buildKit(m, dir).html;
  assert.equal((h.match(/<\/script>/g) || []).length, 3);
});
console.log(`${n} tests passed`);
