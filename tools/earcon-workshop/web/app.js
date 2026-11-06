// Page glue only: state, DOM, AudioContext. Everything audible or exported comes from earcon.js.
// Playback graph follows the playground (tools/aqua-playground/web/app.js): source -> gain -> analyser -> destination, with a peak meter.
import { IDENTITIES, EVENTS, DEFAULT_PARAMS, PARAM_RANGES, renderFamily, buildPack, packFiles, zip, encodeWav, fileName, SR } from "./earcon.js";

const $ = (id) => document.getElementById(id);
const state = { identity: Object.keys(IDENTITIES)[0], chosen: new Set(Object.keys(EVENTS)), params: { ...DEFAULT_PARAMS } };
let ctx, gain, analyser, playing = [], cached;

const status = (t) => { $("status").textContent = t; };
const download = (name, bytes, type) => { const a = document.createElement("a"); a.href = URL.createObjectURL(new Blob([bytes], { type })); a.download = name; a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 5000); };

function build() {
  $("identity").innerHTML = Object.entries(IDENTITIES).map(([k, v]) => `<option value="${k}">${v.label}</option>`).join("");
  $("identity").onchange = () => { state.identity = $("identity").value; refresh(); };
  $("family").onclick = auditionFamily; $("stop").onclick = stop;
  $("reset").onclick = () => { state.params = { ...DEFAULT_PARAMS }; buildControls(); refresh(); };
  $("zip").onclick = () => { const f = family(); download(`earcons-${f.identity}.zip`, zip(packFiles(f)), "application/zip"); };
  $("json").onclick = () => download("pack.json", JSON.stringify(buildPack(family()), null, 2) + "\n", "application/json");
  $("events").innerHTML = Object.entries(EVENTS).map(([k, e]) => `<div class="ev" data-event="${k}">
    <input type="checkbox" checked aria-label="include ${e.label}"><div><b>${e.label}</b><span class="meta">${e.meaning}</span></div>
    <button type="button" data-play>Play</button><button type="button" data-wav>WAV</button><canvas width="600" height="34"></canvas></div>`).join("");
  for (const row of $("events").children) {
    const k = row.dataset.event;
    row.querySelector("input").onchange = (e) => { e.target.checked ? state.chosen.add(k) : state.chosen.delete(k); refresh(); };
    row.querySelector("[data-play]").onclick = () => playOne(k);
    row.querySelector("[data-wav]").onclick = () => { const it = family().items.find((i) => i.event === k); download(fileName(state.identity, k), encodeWav(it.samples), "audio/wav"); };
  }
  buildControls();
}

function buildControls() {
  $("controls").innerHTML = "";
  for (const [k, [lo, hi, step]] of Object.entries(PARAM_RANGES)) {
    const row = document.createElement("div"); row.className = "row";
    const input = Object.assign(document.createElement("input"), { type: "range", min: lo, max: hi, step, value: state.params[k] });
    const out = document.createElement("output"), show = () => { out.textContent = Number(input.value).toFixed(step < 1 ? 2 : 0); };
    input.oninput = () => { state.params[k] = Number(input.value); show(); refresh(); }; show();
    const label = document.createElement("label"); label.textContent = k;
    row.append(label, input, out); $("controls").append(row);
  }
}

// One render per state change, cached until refresh(); that buffer is what is shown, played and exported.
function family() { return (cached ??= renderFamily(state.identity, Object.keys(EVENTS).filter((e) => state.chosen.has(e)), state.params)); }
function refresh() {
  cached = null; const f = family();
  $("idnote").textContent = IDENTITIES[state.identity].note;
  for (const row of $("events").children) {
    const it = f.items.find((i) => i.event === row.dataset.event), cv = row.querySelector("canvas"), g = cv.getContext("2d");
    g.clearRect(0, 0, cv.width, cv.height);
    row.style.opacity = it ? 1 : 0.4;
    if (!it) continue;
    g.fillStyle = "#3fc1c9"; const step = it.samples.length / cv.width;
    for (let x = 0; x < cv.width; x++) { let p = 0; for (let i = Math.floor(x * step); i < Math.floor((x + 1) * step); i++) p = Math.max(p, Math.abs(it.samples[i])); const h = p * cv.height; g.fillRect(x, (cv.height - h) / 2, 1, Math.max(1, h)); }
  }
  $("pack").textContent = JSON.stringify(buildPack(f), null, 2);
  status(`${f.items.length} earcons, peak ${Math.max(0, ...f.items.map((i) => i.peak)).toFixed(2)}, not heard by anyone.`);
}

async function graph() {
  ctx ??= new AudioContext({ latencyHint: "interactive" });
  await ctx.resume();
  if (!gain) { gain = ctx.createGain(); analyser = ctx.createAnalyser(); analyser.fftSize = 1024; gain.connect(analyser); analyser.connect(ctx.destination); meter(); }
}
function schedule(samples, when) {
  const buf = ctx.createBuffer(1, samples.length, SR); buf.copyToChannel(samples, 0);
  const src = ctx.createBufferSource(); src.buffer = buf; src.connect(gain); src.start(when); playing.push(src);
}
function stop() { for (const s of playing) { try { s.stop(); } catch { /* already ended */ } s.disconnect(); } playing = []; }
async function playOne(k) { stop(); await graph(); const it = family().items.find((i) => i.event === k); if (it) schedule(it.samples, ctx.currentTime + 0.02); }
async function auditionFamily() { stop(); await graph(); let t = ctx.currentTime + 0.05; for (const it of family().items) { schedule(it.samples, t); t += it.durationMs / 1000 + 0.35; } }
function meter() {
  const b = new Float32Array(analyser.fftSize); analyser.getFloatTimeDomainData(b);
  $("level").value = b.reduce((m, v) => Math.max(m, Math.abs(v)), 0); requestAnimationFrame(meter);
}

build(); refresh();
