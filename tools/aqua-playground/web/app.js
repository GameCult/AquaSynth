import { instantiateFaustModuleFromFile, LibFaust, FaustCompiler, FaustMonoDspGenerator } from "./vendor/faustwasm/index.js";
import { controlsOf } from "./ui.js";

const $ = (id) => document.getElementById(id);
const status = (t) => { $("status").textContent = t; };
let compiler, ctx, node, gain, analyser, generator, entry;

async function init() {
  const catalog = await (await fetch("./catalog.json")).json();
  for (const e of catalog) $("patch").add(new Option(`${e.name}${e.kind === "faust" ? " (hand-written Faust)" : ""}`, e.name));
  compiler = new FaustCompiler(new LibFaust(await instantiateFaustModuleFromFile("./vendor/libfaust-wasm/libfaust-wasm.js")));
  window.aquaCatalog = catalog;
  $("patch").onchange = () => load(catalog.find((e) => e.name === $("patch").value));
  $("play").onclick = play; $("stop").onclick = stop;
  $("recompile").onclick = () => compile($("source").value);
  $("volume").oninput = () => { if (gain) gain.gain.value = Number($("volume").value); };
  const start = new URLSearchParams(location.search).get("patch");
  const first = catalog.find((e) => e.name === start) ?? catalog[0];
  $("patch").value = first.name;
  await load(first);
}

async function load(e) {
  stop();
  entry = e;
  $("origin").textContent = `from ${e.origin}`;
  $("source").value = await (await fetch(`./${e.dsp}`)).text();
  await compile($("source").value);
}

async function compile(code) {
  stop();
  status("Compiling Faust to wasm…");
  const t0 = performance.now();
  const g = new FaustMonoDspGenerator();
  const ok = await g.compile(compiler, entry.name.replace(/[^A-Za-z0-9_]/g, "_"), code, "-ftz 2");
  if (!ok) { status(`Compile failed: ${compiler.getErrorMessage()}`); $("play").disabled = true; return; }
  generator = g;
  $("play").disabled = false;
  const controls = controlsOf(JSON.parse(g.factory.json));
  buildControls(controls);
  status(`Compiled in ${Math.round(performance.now() - t0)} ms; ${controls.length} controls. Press Play.`);
}

function buildControls(controls) {
  const box = $("controls"); box.replaceChildren();
  let group = null;
  for (const c of controls) {
    if (c.group !== group) { group = c.group; const h = document.createElement("div"); h.className = "grp"; h.textContent = group; box.append(h); }
    const row = document.createElement("div"); row.className = "row";
    const label = document.createElement("label"); label.textContent = c.label; label.title = c.address;
    const input = document.createElement("input");
    const out = document.createElement("output");
    if (c.type === "checkbox" || c.type === "button") { input.type = "checkbox"; input.checked = c.init > 0; }
    else { input.type = "range"; input.min = c.min; input.max = c.max; input.step = c.step; input.value = c.init; }
    const show = () => { out.textContent = input.type === "checkbox" ? (input.checked ? "on" : "off") : Number(input.value).toFixed(c.step < 0.01 ? 4 : c.step < 1 ? 2 : 0); };
    input.oninput = () => { show(); node?.setParamValue(c.address, input.type === "checkbox" ? Number(input.checked) : Number(input.value)); };
    input.dataset.address = c.address; show();
    row.append(label, input, out); box.append(row);
  }
}

async function play() {
  stop();
  ctx ??= new AudioContext({ latencyHint: "interactive" });
  await ctx.resume();
  // A fresh node each press: one-shot .aqua patches run on a clock that starts at instantiation.
  node = await generator.createNode(ctx);
  if (!node) { status("createNode failed"); return; }
  for (const input of $("controls").querySelectorAll("input")) input.dispatchEvent(new Event("input"));
  gain = ctx.createGain(); gain.gain.value = Number($("volume").value);
  analyser = ctx.createAnalyser(); analyser.fftSize = 1024;
  node.connect(gain); gain.connect(analyser); analyser.connect(ctx.destination);
  status(`Playing ${entry.name} at ${ctx.sampleRate} Hz.`);
  meter();
}

function stop() {
  if (node) { node.disconnect(); node.destroy?.(); node = null; }
  gain?.disconnect(); analyser?.disconnect(); gain = analyser = null;
  $("level").value = 0;
}

function meter() {
  if (!analyser) return;
  const buf = new Float32Array(analyser.fftSize); analyser.getFloatTimeDomainData(buf);
  $("level").value = buf.reduce((m, v) => Math.max(m, Math.abs(v)), 0);
  requestAnimationFrame(meter);
}

init().catch((err) => { status(`Failed to start: ${err}`); console.error(err); });
