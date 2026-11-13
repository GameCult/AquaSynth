// Shared by build.mjs, the page and test.mjs. CommonJS so build.mjs can inline this file into the page verbatim.
"use strict";
const SCHEMA = "aqua.listen.verdict.v1";
const VERDICTS = ["keep", "fix", "drop"];
const DEVICES = ["headphones", "speakers"];
const AUDIO_EXT = /\.(wav|mp3|ogg|flac)$/i;

// Manifest: { kit: {id, title, note?}, clips: [{id, file, title, feel, group, pair?}] }. Returns a list of problems (empty = valid).
function validateManifest(m) {
  const errs = [];
  const str = (v) => typeof v === "string" && v.trim() !== "";
  if (!m || typeof m !== "object" || Array.isArray(m)) return ["manifest must be an object with `kit` and `clips`"];
  if (!m.kit || !str(m.kit.id) || !/^[A-Za-z0-9._-]+$/.test(m.kit.id)) errs.push("kit.id must be a non-empty string of letters, digits, . _ -");
  if (!m.kit || !str(m.kit.title)) errs.push("kit.title is required");
  if (!Array.isArray(m.clips) || m.clips.length === 0) { errs.push("clips must be a non-empty array"); return errs; }
  const byId = new Map();
  m.clips.forEach((c, i) => {
    const at = `clips[${i}]${c && str(c.id) ? ` (${c.id})` : ""}`;
    if (!c || typeof c !== "object") { errs.push(`${at}: must be an object`); return; }
    for (const k of ["id", "file", "title", "feel", "group"]) if (!str(c[k])) errs.push(`${at}: ${k} is required`);
    if (c.pair !== undefined && !str(c.pair)) errs.push(`${at}: pair must be a clip id`);
    if (str(c.file)) {
      if (!AUDIO_EXT.test(c.file)) errs.push(`${at}: file must be .wav, .mp3, .ogg or .flac`);
      if (/^([A-Za-z]:|[\\/])/.test(c.file) || c.file.split(/[\\/]/).includes("..")) errs.push(`${at}: file must be a relative path inside the clips folder`);
    }
    if (str(c.id)) { if (byId.has(c.id)) errs.push(`${at}: duplicate id`); else byId.set(c.id, c); }
  });
  for (const c of m.clips) {
    if (!c || !str(c.pair) || !str(c.id)) continue;
    const at = `clip ${c.id}`, o = byId.get(c.pair);
    if (c.pair === c.id) errs.push(`${at}: cannot be paired with itself`);
    else if (!o) errs.push(`${at}: pair ${c.pair} is not a clip id`);
    else if (o.pair !== c.id) errs.push(`${at}: pair ${c.pair} does not point back (pairs must be mutual)`);
  }
  return errs;
}

function rng(seed) { // mulberry32
  let a = seed >>> 0;
  return () => { a = (a + 0x6d2b79f5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
function shuffle(xs, r) { const a = xs.slice(); for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(r() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; }

// Presentation order from a seed: singles and A/B pairs are shuffled as units; inside a pair the A/B position is shuffled.
// Returns [{id, slot: 'A'|'B'|null}], a pair's two clips adjacent.
function planOrder(clips, seed) {
  const r = rng(seed), seen = new Set(), units = [];
  for (const c of clips) {
    if (seen.has(c.id)) continue;
    seen.add(c.id);
    if (c.pair) { seen.add(c.pair); units.push([c.id, c.pair]); } else units.push([c.id]);
  }
  const out = [];
  for (const u of shuffle(units, r)) {
    if (u.length === 1) out.push({ id: u[0], slot: null });
    else { const [x, y] = shuffle(u, r); out.push({ id: x, slot: "A" }, { id: y, slot: "B" }); }
  }
  return out;
}

// state: {seed, consent, listener, device, notes, answers: {id: {verdict, reason, landed, device}}, pairs: {"idA|idB": 'A'|'B'|'same'}}
function buildVerdict(manifest, order, state, now) {
  const byId = new Map(manifest.clips.map((c) => [c.id, c]));
  const slot = new Map(order.map((o) => [o.id, o.slot]));
  const clips = order.map((o, i) => {
    const c = byId.get(o.id), a = state.answers[o.id] || {};
    return { position: i + 1, id: c.id, title: c.title, group: c.group, feel: c.feel, file: c.file,
      pair: c.pair || null, slot: o.slot,
      verdict: VERDICTS.includes(a.verdict) ? a.verdict : null, reason: a.reason || "",
      landed: Number.isInteger(a.landed) && a.landed >= 1 && a.landed <= 5 ? a.landed : null,
      device: DEVICES.includes(a.device) ? a.device : null };
  });
  const pairs = [];
  for (const o of order) {
    const c = byId.get(o.id);
    if (o.slot !== "A") continue;
    const key = `${o.id}|${c.pair}`, p = state.pairs[key];
    pairs.push({ a: o.id, b: c.pair, preferred: p === "A" ? o.id : p === "B" ? c.pair : p === "same" ? "no-difference" : null });
  }
  const count = (v) => clips.filter((c) => c.verdict === v).length;
  return { schema: SCHEMA, kit: { id: manifest.kit.id, title: manifest.kit.title }, exportedAt: now, seed: state.seed,
    consent: !!state.consent, listener: state.listener || "", device: state.device || null, notes: state.notes || "",
    summary: { total: clips.length, keep: count("keep"), fix: count("fix"), drop: count("drop"), unanswered: clips.filter((c) => !c.verdict).length },
    clips, pairs };
}

const cell = (s) => String(s ?? "").replace(/\|/g, "\\|").replace(/\r?\n/g, " ");
function verdictMarkdown(v) {
  const L = [`# Listening verdict: ${v.kit.title}`, "",
    `Kit \`${v.kit.id}\`, exported ${v.exportedAt}, seed ${v.seed}. Listener: ${v.listener || "(anonymous)"}. Listening on: ${v.device || "not stated"}.`,
    `Consent given: ${v.consent ? "yes" : "no"}.`, "",
    `**${v.summary.keep} keep, ${v.summary.fix} fix, ${v.summary.drop} drop, ${v.summary.unanswered} unanswered** of ${v.summary.total}.`, "",
    "| # | clip | group | intended feel | verdict | landed (1-5) | device | reason |", "|---|---|---|---|---|---|---|---|"];
  for (const c of v.clips) L.push(`| ${c.position} | ${cell(c.title)} (\`${cell(c.id)}\`)${c.slot ? ` [${c.slot}]` : ""} | ${cell(c.group)} | ${cell(c.feel)} | ${c.verdict ?? "-"} | ${c.landed ?? "-"} | ${c.device ?? "-"} | ${cell(c.reason)} |`);
  if (v.pairs.length) {
    L.push("", "## A/B pairs", "", "| A | B | preferred |", "|---|---|---|");
    for (const p of v.pairs) L.push(`| \`${p.a}\` | \`${p.b}\` | ${p.preferred ?? "-"} |`);
  }
  if (v.notes) L.push("", "## Notes", "", v.notes);
  return L.join("\n") + "\n";
}

// Autosave. The key names the kit by its id and its clip-id set, not by the content hash: a rebuild that fixes a note or
// re-renders a clip is the same kit and keeps the listener's answers; a different clip-id set is a different kit and starts
// fresh (the old entry is left untouched in the browser). Saved state is answers by clip id and pairs by "idA|idB", plus the
// seed; the A/B/same value of a pair is positional (A is the first of the pair in the seed's order), so it stays meaningful
// only while the manifest's clip order is unchanged.
const SAVE_PREFIX = "aqua-listen.v2:";
function saveKey(manifest) { return `${SAVE_PREFIX}${manifest.kit.id}:${manifest.clips.map((c) => c.id).sort().join(",")}`; }

// Earlier pages keyed the save by `aqua-listen:<kitid>:<8-hex content hash>`. Given every localStorage entry as
// [{key, state}] (state already parsed, null if unparseable), returns the key of the legacy entry to adopt, or null:
// same kit id, a seed, and answers/pairs that mention only clip ids of this kit. Fullest entry wins (most answered fields);
// ties go to the later key so the choice is deterministic.
function pickLegacySave(manifest, entries) {
  const ids = new Set(manifest.clips.map((c) => c.id)), prefix = `aqua-listen:${manifest.kit.id}:`;
  let best = null, bestScore = -1;
  for (const { key, state } of entries) {
    if (typeof key !== "string" || !key.startsWith(prefix) || !/^[0-9a-f]{8}$/.test(key.slice(prefix.length))) continue;
    if (!state || typeof state !== "object" || !state.seed || typeof state.answers !== "object" || !state.answers) continue;
    const pairs = state.pairs && typeof state.pairs === "object" ? state.pairs : {};
    if (!Object.keys(state.answers).every((id) => ids.has(id))) continue;
    if (!Object.keys(pairs).every((k) => k.split("|").every((id) => ids.has(id)))) continue;
    const score = Object.values(state.answers).reduce((n, a) => n + (a ? ["verdict", "landed", "reason"].filter((f) => a[f]).length : 0), 0) + Object.keys(pairs).length;
    if (score > bestScore || (score === bestScore && key > best)) { best = key; bestScore = score; }
  }
  return best;
}

// A save as stored -> the state the page runs on, or null. Normalises answers/pairs to plain objects (a hand-edited or old save
// must not crash the page). pairSig records the manifest's clip order the pair picks were made under; a pick's A/B/same is
// relative to that order, so a save with a missing or different pairSig keeps seed and answers and loses its pairs. A legacy
// (hash-keyed) save has no pairSig and cannot be checked: its pairs are kept and stamped with the current signature.
const pairSig = (manifest) => manifest.clips.map((c) => c.id).join(",");
const plain = (o) => (o && typeof o === "object" && !Array.isArray(o) ? o : {});
function restoreSave(manifest, saved, legacy) {
  if (!saved || typeof saved !== "object" || !saved.seed) return null;
  const sig = pairSig(manifest), keep = legacy || saved.pairSig === sig;
  return Object.assign({}, saved, { answers: plain(saved.answers), pairs: keep ? plain(saved.pairs) : {}, pairSig: sig });
}

module.exports = { SCHEMA, VERDICTS, DEVICES, AUDIO_EXT, validateManifest, planOrder, buildVerdict, verdictMarkdown, rng, saveKey, pickLegacySave, pairSig, restoreSave };
