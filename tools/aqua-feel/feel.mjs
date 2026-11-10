#!/usr/bin/env node
// aqua-feel: headless feel meter. Renders every shipped patch and Ding offline, measures the buffers, writes
// report.json plus a static gallery, and gates the numbers against thresholds.json.
// NOTHING HERE HEARS ANYTHING. These are measurements of rendered buffers; a number is evidence, never a verdict.
// Usage: node feel.mjs run [--out DIR] [--only SUBSTR] [--no-gate]   render, measure, write report + gallery, gate
//        node feel.mjs gate REPORT.json [THRESHOLDS.json] [--partial] re-gate a report (--partial: a subset; skips id-rule and waiver bookkeeping)
//        node feel.mjs compare BEFORE.json AFTER.json [--only SUBSTR]  before/after table
//        node feel.mjs sync --wav A.wav --track T.csv [--events E] | --video V [--fps N] [--kind luma|flux] [--profile P] [--json OUT]   A/V synchrony (SYNC.md)
//        node feel.mjs correlate TRIAL_EXPORT.json ...              feel metrics vs listening-trial ratings
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { measure, SR } from "./metrics.js";
import { loadRuntime, compile, renderOnce, renderDing, writeWav } from "./render.js";
import { allItems } from "./catalog.js";
import { gate, formatGate } from "./gate.js";
import { gallery } from "./gallery.js";
import { compareTable, correlate } from "./report.js";

const here = dirname(fileURLToPath(import.meta.url));
export const CAVEAT = "Nothing here was heard. These are measurements of offline-rendered buffers (faustwasm, mono channel 0); metrics are evidence for a listener to check, not verdicts.";
const args = process.argv.slice(2), cmd = args.shift();
const flag = (n) => { const i = args.indexOf(n); if (i < 0) return undefined; return args.splice(i, i + 2 > args.length ? 1 : 2).pop(); };
const bool = (n) => { const i = args.indexOf(n); if (i < 0) return false; args.splice(i, 1); return true; };
const readJson = (p) => JSON.parse(readFileSync(p, "utf8"));

async function run() {
  const out = resolve(flag("--out") ?? join(here, "out")), only = flag("--only"), noGate = bool("--no-gate");
  const items = allItems().filter((it) => !only || it.id.includes(only));
  if (!items.length) { console.error(`no item matches ${only}`); process.exit(2); }
  const rt = await loadRuntime();
  console.log(`libfaust ${rt.version}; ${items.length} items; ${SR} Hz`);
  const records = [];
  for (const it of items) {
    const t0 = Date.now();
    try {
      const r = it.ding ? await renderDing(rt, it.ding.instrument, it.ding.source, it.ding.event, it.ding) : await renderOnce(await compile(rt, it.id, it.source), it.params, it.seconds);
      const wav = `wav/${it.id.replace(/[^A-Za-z0-9._@-]+/g, "_")}.wav`;
      writeWav(join(out, wav), r.samples);
      records.push({ id: it.id, group: it.group, origin: it.origin, params: it.params ?? {}, seconds: r.samples.length / SR, channels: r.channels, wav, metrics: measure(r.samples) });
      console.log(`ok   ${it.id}  (${Date.now() - t0} ms)`);
    } catch (e) {
      records.push({ id: it.id, group: it.group, origin: it.origin, error: String(e.message ?? e) });
      console.log(`FAIL ${it.id}: ${e.message}`);
    }
  }
  const thresholds = readJson(join(here, "thresholds.json"));
  const report = { schema: "aqua-feel.report.v1", caveat: CAVEAT, libfaust: rt.version, sampleRate: SR, items: records };
  report.gate = gate(report, thresholds, { partial: Boolean(only) });
  mkdirSync(out, { recursive: true });
  writeFileSync(join(out, "report.json"), JSON.stringify(report, null, 1));
  writeFileSync(join(out, "index.html"), gallery(report));
  console.log(`\nwrote ${join(out, "report.json")} and index.html\n${CAVEAT}\n\n${formatGate(report.gate)}`);
  process.exit(!noGate && !report.gate.pass ? 1 : 0);
}

async function sync() {
  const { syncAnalyze, readWav, readTrack, readEvents, readVideo, formatSync } = await import("./sync.mjs");
  const wav = flag("--wav"), trk = flag("--track"), vid = flag("--video"), evs = flag("--events"), fps = Number(flag("--fps") ?? 0) || undefined;
  const kind = flag("--kind") ?? "luma", profile = flag("--profile") ?? "default", json = flag("--json"), noGate = bool("--no-gate");
  const lead = flag("--lead-ms"), lag = flag("--lag-ms");
  let audio, sampleRate, audioStartS = 0, visual;
  try {
    if (vid) { const v = readVideo(vid); audio = v.audio.samples; sampleRate = v.audio.sampleRate; audioStartS = v.audio.startS; visual = kind === "flux" ? v.flux : v.luma; }
    else if (wav && trk) { const w = readWav(readFileSync(wav)); audio = w.samples; sampleRate = w.sampleRate; visual = readTrack(readFileSync(trk, "utf8"), fps); }
    else throw new Error("give --video FILE, or --wav FILE with --track FILE");
    const r = syncAnalyze({ audio, sampleRate, audioStartS, visual, visualKind: kind, profile, events: evs ? readEvents(readFileSync(evs, "utf8")) : undefined,
      leadMs: lead === undefined ? undefined : Number(lead), lagMs: lag === undefined ? undefined : Number(lag) });
    r.caveat = "Nothing here was heard or seen: two onset lists compared. The metric is not the verdict; see SYNC.md.";
    console.log(formatSync(r));
    if (json) writeFileSync(json, JSON.stringify(r, null, 1));
    process.exit(!noGate && !r.summary.pass ? 1 : 0);
  } catch (e) { console.error(`sync: ${e.message}`); process.exit(2); }
}

const cmds = {
  sync,
  run,
  gate() {
    const partial = bool("--partial"), report = readJson(args[0]), g = gate(report, readJson(args[1] ?? join(here, "thresholds.json")), { partial });
    console.log(formatGate(g)); process.exit(g.pass ? 0 : 1);
  },
  compare() { const only = flag("--only"); console.log(compareTable(readJson(args[0]), readJson(args[1]), only)); },
  async correlate() { console.log(await correlate(args.map(readJson), join(here, "../aqua-trial"))); },
};
if (!cmds[cmd]) { console.error(readFileSync(fileURLToPath(import.meta.url), "utf8").split("\n").slice(1, 9).join("\n")); process.exit(2); }
await cmds[cmd]();
