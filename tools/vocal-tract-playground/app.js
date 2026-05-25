"use strict";

const controlSpecs = [
  ["Tongue body", "tongueBody", "tract", 0.56],
  ["Tongue tip", "tongueTip", "tract", 0.42],
  ["Lip aperture", "lipAperture", "tract", 0.72],
  ["Lip rounding", "lipRounding", "tract", 0.18],
  ["Velum", "velum", "tract", 0.18],
  ["Glottal tenseness", "glottalTenseness", "source", 0.48],
  ["Turbulence", "turbulence", "source", 0.14],
  ["Pressure", "pressure", "source", 0.68],
  ["AM depth", "amDepth", "source", 0.08],
  ["FM depth", "fmDepth", "source", 0.05],
  ["LFO rate", "lfoRate", "source", 0.22],
  ["LFO depth", "lfoDepth", "source", 0.06],
  ["Filter cutoff", "filterCutoff", "spectral", 0.58],
  ["Filter resonance", "filterResonance", "spectral", 0.22],
  ["Mel low", "mel0", "spectral", 0.62],
  ["Mel low-mid", "mel1", "spectral", 0.55],
  ["Mel mid", "mel2", "spectral", 0.48],
  ["Mel high-mid", "mel3", "spectral", 0.42],
  ["Mel high", "mel4", "spectral", 0.36],
  ["Mel air", "mel5", "spectral", 0.30]
];

const presets = {
  open: {
    tongueBody: 0.42, tongueTip: 0.30, lipAperture: 0.92, lipRounding: 0.05,
    velum: 0.12, glottalTenseness: 0.42, turbulence: 0.04, pressure: 0.76,
    amDepth: 0.04, fmDepth: 0.04, lfoRate: 0.18, lfoDepth: 0.03,
    filterCutoff: 0.68, filterResonance: 0.20, mel0: 0.78, mel1: 0.68,
    mel2: 0.54, mel3: 0.40, mel4: 0.30, mel5: 0.22
  },
  ee: {
    tongueBody: 0.82, tongueTip: 0.72, lipAperture: 0.46, lipRounding: 0.02,
    velum: 0.10, glottalTenseness: 0.58, turbulence: 0.05, pressure: 0.68,
    amDepth: 0.03, fmDepth: 0.08, lfoRate: 0.26, lfoDepth: 0.04,
    filterCutoff: 0.78, filterResonance: 0.32, mel0: 0.35, mel1: 0.52,
    mel2: 0.82, mel3: 0.72, mel4: 0.58, mel5: 0.36
  },
  oo: {
    tongueBody: 0.30, tongueTip: 0.24, lipAperture: 0.36, lipRounding: 0.88,
    velum: 0.11, glottalTenseness: 0.44, turbulence: 0.03, pressure: 0.70,
    amDepth: 0.03, fmDepth: 0.03, lfoRate: 0.16, lfoDepth: 0.03,
    filterCutoff: 0.36, filterResonance: 0.38, mel0: 0.82, mel1: 0.70,
    mel2: 0.38, mel3: 0.22, mel4: 0.18, mel5: 0.12
  },
  ss: {
    tongueBody: 0.68, tongueTip: 0.86, lipAperture: 0.32, lipRounding: 0.02,
    velum: 0.08, glottalTenseness: 0.10, turbulence: 0.92, pressure: 0.78,
    amDepth: 0.18, fmDepth: 0.12, lfoRate: 0.36, lfoDepth: 0.05,
    filterCutoff: 0.88, filterResonance: 0.62, mel0: 0.12, mel1: 0.18,
    mel2: 0.36, mel3: 0.76, mel4: 0.92, mel5: 0.86
  },
  ma: {
    tongueBody: 0.48, tongueTip: 0.38, lipAperture: 0.28, lipRounding: 0.18,
    velum: 0.78, glottalTenseness: 0.40, turbulence: 0.04, pressure: 0.64,
    amDepth: 0.08, fmDepth: 0.04, lfoRate: 0.20, lfoDepth: 0.05,
    filterCutoff: 0.42, filterResonance: 0.28, mel0: 0.74, mel1: 0.72,
    mel2: 0.58, mel3: 0.40, mel4: 0.30, mel5: 0.22
  }
};

const state = Object.fromEntries(controlSpecs.map(([, id, , value]) => [id, value]));
const controls = new Map();
let audio = null;
let animationFrame = 0;
let phase = 0;

const canvas = document.getElementById("tractCanvas");
const ctx = canvas.getContext("2d");
const controlsRoot = document.getElementById("controls");
const playButton = document.getElementById("playButton");
const panicButton = document.getElementById("panicButton");
const pressureMeter = document.getElementById("pressureMeter");
const noiseMeter = document.getElementById("noiseMeter");
const nasalMeter = document.getElementById("nasalMeter");

function makeControls() {
  for (const [label, id, family, value] of controlSpecs) {
    const row = document.createElement("div");
    row.className = "control";
    row.dataset.family = family;

    const labelEl = document.createElement("label");
    labelEl.htmlFor = id;
    labelEl.textContent = label;

    const valueEl = document.createElement("output");
    valueEl.className = "value";
    valueEl.htmlFor = id;

    const input = document.createElement("input");
    input.id = id;
    input.type = "range";
    input.min = "0";
    input.max = "1";
    input.step = "0.001";
    input.value = value.toString();

    input.addEventListener("input", () => {
      state[id] = Number(input.value);
      valueEl.value = state[id].toFixed(3);
      updateAudio();
    });

    row.append(labelEl, valueEl, input);
    controlsRoot.append(row);
    controls.set(id, { input, valueEl });
  }

  syncControls();
}

function syncControls() {
  for (const [id, pair] of controls) {
    pair.input.value = state[id].toString();
    pair.valueEl.value = state[id].toFixed(3);
  }
  updateMeters();
  draw();
}

function applyPreset(name) {
  Object.assign(state, presets[name]);
  syncControls();
  updateAudio();
}

function createNoiseBuffer(context) {
  const buffer = context.createBuffer(1, context.sampleRate * 2, context.sampleRate);
  const data = buffer.getChannelData(0);
  for (let i = 0; i < data.length; i++) {
    data[i] = Math.random() * 2 - 1;
  }
  return buffer;
}

async function startAudio() {
  if (audio) {
    return;
  }

  const context = new AudioContext();
  const master = context.createGain();
  const voice = context.createOscillator();
  const voiceGain = context.createGain();
  const noise = context.createBufferSource();
  const noiseGain = context.createGain();
  const nasal = context.createBiquadFilter();
  const low = context.createBiquadFilter();
  const mid = context.createBiquadFilter();
  const high = context.createBiquadFilter();
  const air = context.createBiquadFilter();

  voice.type = "sawtooth";
  noise.buffer = createNoiseBuffer(context);
  noise.loop = true;

  nasal.type = "bandpass";
  low.type = "peaking";
  mid.type = "peaking";
  high.type = "peaking";
  air.type = "highpass";

  voice.connect(voiceGain).connect(nasal).connect(low).connect(mid).connect(high).connect(air).connect(master);
  noise.connect(noiseGain).connect(high);
  master.connect(context.destination);

  master.gain.value = 0.0;
  voice.start();
  noise.start();

  audio = { context, master, voice, voiceGain, noiseGain, nasal, low, mid, high, air };
  updateAudio();
  playButton.textContent = "Stop";
  playButton.setAttribute("aria-pressed", "true");
}

async function stopAudio() {
  if (!audio) {
    return;
  }

  const old = audio;
  audio = null;
  old.master.gain.setTargetAtTime(0, old.context.currentTime, 0.02);
  await new Promise(resolve => setTimeout(resolve, 80));
  await old.context.close();
  playButton.textContent = "Play";
  playButton.setAttribute("aria-pressed", "false");
}

function updateAudio() {
  updateMeters();
  if (!audio) {
    return;
  }

  const t = audio.context.currentTime;
  const pitch = 82 + state.pressure * 120 + state.glottalTenseness * 80;
  const wobble = 1 + Math.sin(t * (0.5 + state.lfoRate * 8)) * state.lfoDepth * 0.08;
  const rounded = 1 - state.lipRounding * 0.38;
  const aperture = 0.25 + state.lipAperture * 0.85;

  audio.voice.frequency.setTargetAtTime(pitch * wobble * (1 + state.fmDepth * 0.12), t, 0.015);
  audio.voiceGain.gain.setTargetAtTime((1 - state.turbulence * 0.75) * state.pressure * aperture * 0.24, t, 0.025);
  audio.noiseGain.gain.setTargetAtTime(state.turbulence * state.pressure * (0.05 + state.tongueTip * 0.22), t, 0.02);
  audio.master.gain.setTargetAtTime(0.72, t, 0.03);

  audio.nasal.frequency.setTargetAtTime(220 + state.velum * 260, t, 0.02);
  audio.nasal.Q.setTargetAtTime(0.5 + state.velum * 5, t, 0.02);
  audio.nasal.gain?.setTargetAtTime?.(state.velum * 8, t, 0.02);

  audio.low.frequency.setTargetAtTime((280 + state.tongueBody * 620) * rounded, t, 0.02);
  audio.low.Q.setTargetAtTime(0.7 + state.filterResonance * 8, t, 0.02);
  audio.low.gain.setTargetAtTime((state.mel0 - 0.5) * 18, t, 0.02);

  audio.mid.frequency.setTargetAtTime(780 + state.tongueTip * 1450 + state.lipAperture * 300, t, 0.02);
  audio.mid.Q.setTargetAtTime(0.8 + state.filterResonance * 7, t, 0.02);
  audio.mid.gain.setTargetAtTime(((state.mel1 + state.mel2) * 0.5 - 0.5) * 18, t, 0.02);

  audio.high.frequency.setTargetAtTime(1900 + state.tongueTip * 2600 + state.filterCutoff * 1800, t, 0.02);
  audio.high.Q.setTargetAtTime(0.7 + state.filterResonance * 10, t, 0.02);
  audio.high.gain.setTargetAtTime(((state.mel3 + state.mel4) * 0.5 - 0.5) * 22, t, 0.02);

  audio.air.frequency.setTargetAtTime(3200 + state.filterCutoff * 6500, t, 0.02);
  audio.air.Q.setTargetAtTime(0.7, t, 0.02);
}

function updateMeters() {
  pressureMeter.value = state.pressure;
  noiseMeter.value = state.turbulence;
  nasalMeter.value = state.velum;
}

function draw() {
  const width = canvas.width;
  const height = canvas.height;
  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = "#111714";
  ctx.fillRect(0, 0, width, height);

  const cx = width * 0.5;
  const cy = height * 0.54;
  const time = performance.now() * 0.001;
  phase += 0.018 + state.pressure * 0.02;

  drawGrid(width, height);
  drawTract(cx, cy, width * 0.76, height * 0.42, time);
  drawSpectralBars(width, height);
  drawReadout(width, height);

  animationFrame = requestAnimationFrame(draw);
}

function drawGrid(width, height) {
  ctx.strokeStyle = "rgba(168, 179, 163, 0.11)";
  ctx.lineWidth = 1;
  for (let x = 70; x < width; x += 70) {
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x, height);
    ctx.stroke();
  }
  for (let y = 60; y < height; y += 60) {
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(width, y);
    ctx.stroke();
  }
}

function drawTract(cx, cy, length, scale, time) {
  const pointsTop = [];
  const pointsBottom = [];
  const samples = 72;
  const lipPinch = 1 - state.lipAperture * 0.72;
  const rounding = state.lipRounding * 0.18;
  const body = state.tongueBody;
  const tip = state.tongueTip;
  const velum = state.velum;

  for (let i = 0; i <= samples; i++) {
    const u = i / samples;
    const x = cx - length / 2 + u * length;
    const tongueBody = Math.exp(-Math.pow((u - 0.48) / 0.22, 2)) * body * 0.52;
    const tongueTip = Math.exp(-Math.pow((u - 0.76) / 0.12, 2)) * tip * 0.50;
    const lips = Math.exp(-Math.pow((u - 0.97) / 0.08, 2)) * lipPinch;
    const throat = Math.exp(-Math.pow((u - 0.10) / 0.16, 2)) * (0.12 + state.glottalTenseness * 0.18);
    const pulse = Math.sin(time * (2 + state.lfoRate * 8) + u * 7) * state.amDepth * 0.08;
    const radius = Math.max(0.10, 0.62 - tongueBody - tongueTip - lips - throat + rounding + pulse);
    const yTop = cy - radius * scale * 0.5;
    const yBottom = cy + radius * scale * 0.5;
    pointsTop.push([x, yTop]);
    pointsBottom.push([x, yBottom]);
  }

  const grad = ctx.createLinearGradient(cx - length / 2, cy, cx + length / 2, cy);
  grad.addColorStop(0, "#f47f67");
  grad.addColorStop(0.46, "#f2b84b");
  grad.addColorStop(1, "#7fd18b");

  ctx.beginPath();
  for (let i = 0; i < pointsTop.length; i++) {
    const [x, y] = pointsTop[i];
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  for (let i = pointsBottom.length - 1; i >= 0; i--) {
    const [x, y] = pointsBottom[i];
    ctx.lineTo(x, y);
  }
  ctx.closePath();
  ctx.fillStyle = "rgba(127, 209, 139, 0.17)";
  ctx.fill();
  ctx.strokeStyle = grad;
  ctx.lineWidth = 4;
  ctx.stroke();

  const glottisX = cx - length * 0.52;
  const lipX = cx + length * 0.52;
  drawSource(glottisX, cy, scale);
  drawLips(lipX, cy, scale);
  drawNasalBranch(cx + length * 0.20, cy, scale, velum);
  drawWave(cx, cy, length, scale);
}

function drawSource(x, y, scale) {
  const height = scale * (0.24 + state.glottalTenseness * 0.22);
  ctx.fillStyle = "#f47f67";
  ctx.fillRect(x - 18, y - height / 2, 10, height);
  ctx.fillRect(x + 8, y - height / 2, 10, height);
  ctx.fillStyle = "rgba(244, 127, 103, 0.25)";
  ctx.beginPath();
  ctx.arc(x, y, 28 + state.pressure * 24, 0, Math.PI * 2);
  ctx.fill();
}

function drawLips(x, y, scale) {
  const aperture = 8 + state.lipAperture * 44;
  const round = 18 + state.lipRounding * 36;
  ctx.strokeStyle = "#7fd18b";
  ctx.lineWidth = 5;
  ctx.beginPath();
  ctx.ellipse(x, y, round, aperture, 0, 0, Math.PI * 2);
  ctx.stroke();
}

function drawNasalBranch(x, y, scale, velum) {
  ctx.strokeStyle = `rgba(118, 199, 212, ${0.2 + velum * 0.75})`;
  ctx.lineWidth = 8 + velum * 10;
  ctx.beginPath();
  ctx.moveTo(x, y - scale * 0.16);
  ctx.bezierCurveTo(x + scale * 0.12, y - scale * 0.55, x + scale * 0.40, y - scale * 0.62, x + scale * 0.60, y - scale * 0.38);
  ctx.stroke();
}

function drawWave(cx, cy, length, scale) {
  ctx.strokeStyle = "rgba(240, 244, 237, 0.45)";
  ctx.lineWidth = 2;
  ctx.beginPath();
  for (let i = 0; i <= 160; i++) {
    const u = i / 160;
    const x = cx - length / 2 + u * length;
    const amp = (0.12 + state.pressure * 0.22) * scale * (1 - state.turbulence * 0.35);
    const y = cy + Math.sin(u * 18 + phase) * amp * Math.sin(Math.PI * u);
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.stroke();
}

function drawSpectralBars(width, height) {
  const values = [state.mel0, state.mel1, state.mel2, state.mel3, state.mel4, state.mel5];
  const x0 = width * 0.08;
  const y0 = height * 0.91;
  const barW = Math.max(16, width * 0.035);
  const gap = barW * 0.45;
  for (let i = 0; i < values.length; i++) {
    const h = values[i] * height * 0.20;
    ctx.fillStyle = i < 2 ? "#f2b84b" : i < 4 ? "#7fd18b" : "#76c7d4";
    ctx.fillRect(x0 + i * (barW + gap), y0 - h, barW, h);
  }
}

function drawReadout(width, height) {
  ctx.fillStyle = "#f0f4ed";
  ctx.font = "700 22px system-ui, sans-serif";
  ctx.fillText(`voice patch target vector[20] pressure=${state.pressure.toFixed(2)} noise=${state.turbulence.toFixed(2)} velum=${state.velum.toFixed(2)}`, 28, 42);
  ctx.fillStyle = "#a8b3a3";
  ctx.font = "15px system-ui, sans-serif";
  ctx.fillText("These are the knobs the learned speech driver will predict", 28, height - 24);
}

document.querySelectorAll("[data-preset]").forEach(button => {
  button.addEventListener("click", () => applyPreset(button.dataset.preset));
});

playButton.addEventListener("click", async () => {
  if (audio) {
    await stopAudio();
  } else {
    await startAudio();
  }
});

panicButton.addEventListener("click", stopAudio);

makeControls();
cancelAnimationFrame(animationFrame);
draw();
