// Browser glue for the listening trial. The decisions (design, statistics, validation, export shape) live in
// ../logic.js and are tested in node; this file only touches the DOM, the AudioContext and the download.
// Nothing here makes a network request after the page assets load, and nothing is written to storage.
import { instantiateFaustModuleFromFile, LibFaust, FaustCompiler, FaustMonoDspGenerator } from "./vendor/faustwasm/index.js";
import { controlsOf } from "./ui.js";
import { variants, makeDesign, renderVariant, cleanWord, validRating, buildExport, sha256Hex, PATCH } from "./logic.js";

const $ = (s, root = document) => root.querySelector(s);
const status = (t) => { $("#status").textContent = t; };
const seed = crypto.getRandomValues(new Uint32Array(1))[0];
const design = makeDesign(seed);
const answers = [];
const renders = {};   // variant id -> { buffer, peak, rms }
let ctx, source, dspSha256, current = 0, plays = { A: 0, B: 0 };

$("#agree").onchange = () => { $("#start").disabled = !$("#agree").checked; };
$("#start").onclick = start;
$("#stop").onclick = stopSound;
$("#next").onclick = next;
$("#export").onclick = exportJson;
for (const fs of document.querySelectorAll("fieldset")) {
  const stars = $(".stars", fs);
  for (let n = 1; n <= 5; n++) {
    const label = document.createElement("label");
    const input = Object.assign(document.createElement("input"), { type: "radio", name: `rate-${fs.dataset.pos}`, value: n });
    input.onchange = refresh;
    label.append(input, String(n));
    stars.append(label);
  }
  $(".word", fs).oninput = refresh;
  $(".play", fs).onclick = () => play(fs.dataset.pos);
}

async function start() {
  $("#start").disabled = true; $("#agree").disabled = true;
  try {
    ctx = new AudioContext();   // created inside the click so the browser allows playback
    status("Loading the Faust compiler…");
    const compiler = new FaustCompiler(new LibFaust(await instantiateFaustModuleFromFile("./vendor/libfaust-wasm/libfaust-wasm.js")));
    const g = new FaustMonoDspGenerator();
    const dspText = await (await fetch(`./${PATCH}.dsp`)).text();
    dspSha256 = await sha256Hex(dspText);   // the exact source compiled, so an export names the DSP that produced it
    if (!await g.compile(compiler, "thruster_trial", dspText, "-ftz 2")) throw new Error(compiler.getErrorMessage());
    const controls = controlsOf(JSON.parse(g.factory.json));
    for (const v of variants()) {
      status(`Rendering ${Object.keys(renders).length + 1} of ${variants().length}…`);
      const r = await renderVariant(g, controls, v, ctx.sampleRate);
      if (r.nonFinite || !(r.peak > 1e-4)) throw new Error(`render ${v.id} is silent or not finite`);
      const buffer = ctx.createBuffer(1, r.samples.length, ctx.sampleRate);
      buffer.copyToChannel(r.samples, 0);
      renders[v.id] = { buffer, peak: r.peak, rms: r.rms };
    }
  } catch (err) { status(`Could not start: ${err}`); console.error(err); return; }
  $("#consent").hidden = true; $("#trial").hidden = false;
  show();
}

function show() {
  plays = { A: 0, B: 0 };
  $("#progress").textContent = `Comparison ${current + 1} of ${design.length}`;
  for (const fs of document.querySelectorAll("fieldset")) {
    for (const i of fs.querySelectorAll(".stars input")) i.checked = false;
    $(".word", fs).value = "";
  }
  refresh();
}

function read(pos) {
  const fs = $(`fieldset[data-pos="${pos}"]`);
  const checked = $(".stars input:checked", fs);
  return { rating: checked ? Number(checked.value) : null, word: cleanWord($(".word", fs).value) };
}

function refresh() {
  const a = read("A"), b = read("B");
  $("#next").disabled = !(validRating(a.rating) && a.word && validRating(b.rating) && b.word && plays.A && plays.B);
}

function play(pos) {
  stopSound();
  ctx.resume();
  const { buffer } = renders[design[current][pos].id];
  source = ctx.createBufferSource(); source.buffer = buffer;
  // 10 ms ramps so the start and stop of the file are not a click that colours the rating.
  const g = ctx.createGain(), t = ctx.currentTime, end = t + buffer.duration;
  g.gain.setValueAtTime(0, t); g.gain.linearRampToValueAtTime(0.8, t + 0.01);
  g.gain.setValueAtTime(0.8, end - 0.01); g.gain.linearRampToValueAtTime(0, end);
  source.connect(g).connect(ctx.destination); source.start(t);
  plays[pos]++; refresh();
}

function stopSound() { try { source?.stop(); } catch {} source = null; }

function next() {
  stopSound();
  const time = new Date().toISOString();
  answers[current] = {};
  for (const pos of ["A", "B"]) answers[current][pos] = { ...read(pos), time, plays: plays[pos] };
  if (++current < design.length) return show();
  $("#trial").hidden = true; $("#done").hidden = false;
}

function exportJson() {
  const doc = buildExport({
    seed, design, answers, dspSha256, sampleRate: ctx.sampleRate, userAgent: navigator.userAgent, exportedAt: new Date().toISOString(),
    renders: Object.fromEntries(Object.entries(renders).map(([id, r]) => [id, { peak: r.peak, rms: r.rms }])),
  });
  const url = URL.createObjectURL(new Blob([JSON.stringify(doc, null, 2)], { type: "application/json" }));
  const a = Object.assign(document.createElement("a"), { href: url, download: `aquasynth-trial-${seed.toString(16)}.json` });
  a.click(); URL.revokeObjectURL(url);
}
