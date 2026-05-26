"use strict";

const controlSpecs = [
  ["Frequency", "frequency", "source", 140, 60, 420, "Hz"],
  ["Intensity", "intensity", "source", 0.72, 0, 1, ""],
  ["Tenseness", "tenseness", "source", 0.60, 0, 1, ""],
  ["Tongue index", "tongueIndex", "tract", 12.9, 8, 34, "cell"],
  ["Tongue diameter", "tongueDiameter", "tract", 2.43, 0.45, 3.6, "diam"],
  ["Velum", "velum", "tract", 0.01, 0.01, 0.4, "diam"],
  ["Constriction index", "constrictionIndex", "tract", 32, 2, 43, "cell"],
  ["Constriction diameter", "constrictionDiameter", "tract", 1.0, -0.7, 3.4, "diam"],
  ["Turbulence", "turbulence", "source", 0.18, 0, 1, ""],
  ["Burst", "burst", "source", 0.25, 0, 1, ""],
  ["Lip opening", "lipOpening", "tract", 1.5, 0.35, 2.5, "diam"],
  ["Glottal reflection", "glottalReflection", "spectral", 0.75, -0.95, 0.95, ""],
  ["Lip reflection", "lipReflection", "spectral", -0.85, -0.98, 0.1, ""],
  ["Gain", "gain", "source", 0.7, 0, 1, ""]
];

const presets = {
  open: {
    frequency: 140, intensity: 0.72, tenseness: 0.58, tongueIndex: 13,
    tongueDiameter: 2.7, velum: 0.01, constrictionIndex: 32,
    constrictionDiameter: 1.4, turbulence: 0.08, lipOpening: 1.7,
    burst: 0.25, glottalReflection: 0.75, lipReflection: -0.85, gain: 0.72
  },
  ee: {
    frequency: 165, intensity: 0.68, tenseness: 0.72, tongueIndex: 27,
    tongueDiameter: 1.05, velum: 0.01, constrictionIndex: 34,
    constrictionDiameter: 1.2, turbulence: 0.04, lipOpening: 1.1,
    burst: 0.25, glottalReflection: 0.75, lipReflection: -0.85, gain: 0.68
  },
  oo: {
    frequency: 128, intensity: 0.72, tenseness: 0.55, tongueIndex: 12,
    tongueDiameter: 2.9, velum: 0.01, constrictionIndex: 38,
    constrictionDiameter: 1.5, turbulence: 0.03, lipOpening: 0.55,
    burst: 0.25, glottalReflection: 0.75, lipReflection: -0.9, gain: 0.74
  },
  ss: {
    frequency: 130, intensity: 0.48, tenseness: 0.22, tongueIndex: 28,
    tongueDiameter: 0.75, velum: 0.01, constrictionIndex: 34,
    constrictionDiameter: 0.35, turbulence: 0.95, lipOpening: 1.2,
    burst: 0.25, glottalReflection: 0.65, lipReflection: -0.82, gain: 0.78
  },
  ma: {
    frequency: 132, intensity: 0.66, tenseness: 0.52, tongueIndex: 14,
    tongueDiameter: 2.2, velum: 0.33, constrictionIndex: 18,
    constrictionDiameter: 0.8, turbulence: 0.12, lipOpening: 1.35,
    burst: 0.25, glottalReflection: 0.78, lipReflection: -0.84, gain: 0.72
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
  for (const [label, id, family, value, min, max, unit] of controlSpecs) {
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
    input.min = min.toString();
    input.max = max.toString();
    input.step = max - min > 20 ? "0.01" : "0.001";
    input.value = value.toString();

    input.addEventListener("input", () => {
      state[id] = Number(input.value);
      valueEl.value = formatValue(id, state[id], unit);
      updateAudio();
    });

    row.append(labelEl, valueEl, input);
    controlsRoot.append(row);
    controls.set(id, { input, valueEl, unit });
  }

  syncControls();
}

function formatValue(id, value, unit) {
  const digits = id.includes("Index") || id === "frequency" ? 1 : 3;
  return `${value.toFixed(digits)}${unit ? ` ${unit}` : ""}`;
}

function syncControls() {
  for (const [id, pair] of controls) {
    pair.input.value = state[id].toString();
    pair.valueEl.value = formatValue(id, state[id], pair.unit);
  }
  updateMeters();
  updateAudio();
  draw();
}

function applyPreset(name) {
  Object.assign(state, presets[name]);
  syncControls();
}

async function startAudio() {
  if (audio) return;

  const context = new AudioContext();
  const master = context.createGain();
  const processor = context.createScriptProcessor(1024, 0, 1);
  const synth = createTractSynth(context.sampleRate);

  processor.onaudioprocess = event => {
    const output = event.outputBuffer.getChannelData(0);
    synth.render(output, state);
  };

  master.gain.value = 0.0;
  processor.connect(master).connect(context.destination);
  audio = { context, master, processor, synth };
  updateAudio();
  playButton.textContent = "Stop";
  playButton.setAttribute("aria-pressed", "true");
}

async function stopAudio() {
  if (!audio) return;

  const old = audio;
  audio = null;
  old.master.gain.setTargetAtTime(0, old.context.currentTime, 0.02);
  old.processor.disconnect();
  await new Promise(resolve => setTimeout(resolve, 80));
  await old.context.close();
  playButton.textContent = "Play";
  playButton.setAttribute("aria-pressed", "false");
}

function updateAudio() {
  updateMeters();
  if (!audio) return;

  audio.master.gain.setTargetAtTime(state.gain, audio.context.currentTime, 0.03);
}

function updateMeters() {
  pressureMeter.value = state.intensity;
  noiseMeter.value = state.turbulence;
  nasalMeter.value = (state.velum - 0.01) / 0.39;
}

function createTractSynth(sampleRate) {
  const sections = 44;
  const noseSections = 28;
  const right = new Float32Array(sections);
  const left = new Float32Array(sections);
  const junctionRight = new Float32Array(sections);
  const junctionLeft = new Float32Array(sections + 1);
  const reflection = new Float32Array(sections);
  const diameter = new Float32Array(sections);
  const targetDiameter = new Float32Array(sections);
  const noseRight = new Float32Array(noseSections);
  const noseLeft = new Float32Array(noseSections);
  const noseJunctionRight = new Float32Array(noseSections);
  const noseJunctionLeft = new Float32Array(noseSections + 1);
  const noseReflection = new Float32Array(noseSections);
  const noseDiameter = new Float32Array(noseSections);
  let glottalPhase = 0;
  let noise = 0.1234567;
  let lastConstrictionDiameter = 1.0;
  let transient = 0;
  let dcBlockX = 0;
  let dcBlockY = 0;
  let low1 = 0;
  let low2 = 0;
  let mid1 = 0;
  let mid2 = 0;
  let high1 = 0;
  let high2 = 0;
  let nasal1 = 0;
  let nasal2 = 0;

  initRest();
  updateNose(0.01);

  function rand() {
    noise = (noise * 16807) % 2147483647;
    return noise / 1073741823.5 - 1;
  }

  function initRest() {
    for (let i = 0; i < sections; i++) {
      const base = i < 7 ? 0.6 : i < 10 ? 1.1 : 1.5;
      diameter[i] = base;
      targetDiameter[i] = base;
    }
  }

  function updateTargets(s) {
    for (let i = 0; i < sections; i++) {
      let base = i < 7 ? 0.6 : i < 10 ? 1.1 : 1.5;
      if (i > 10 && i < 39) {
        const angle = 1.1 * Math.PI * (s.tongueIndex - i) / 22;
        const fixedTongueDiameter = 2 + (s.tongueDiameter - 2) / 1.5;
        let curve = (1.5 - fixedTongueDiameter + 1.7) * Math.cos(angle);
        if (i === 8 || i === 38) curve *= 0.8;
        if (i === 10 || i === 37) curve *= 0.94;
        base = 1.5 - curve;
      }
      targetDiameter[i] = Math.max(0, base);
    }

    applyConstriction(s.constrictionIndex, s.constrictionDiameter);
    targetDiameter[sections - 1] = Math.max(0.05, s.lipOpening);
  }

  function applyConstriction(position, constrictionDiameter) {
    const newDiameter = Math.max(0, constrictionDiameter - 0.3);
    const range = position < 25 ? 10 : 5;
    const lower = Math.max(0, Math.floor(position - range - 1));
    const upper = Math.min(sections - 1, Math.ceil(position + range + 1));
    for (let i = lower; i <= upper; i++) {
      const offset = Math.abs(i - position) - 0.5;
      let scale;
      if (offset <= 0) scale = 0;
      else if (offset > range) scale = 1;
      else scale = 0.5 * (1 - Math.cos(Math.PI * offset / range));
      const difference = targetDiameter[i] - newDiameter;
      if (difference > 0) targetDiameter[i] = newDiameter + difference * scale;
    }
  }

  function slewDiameters() {
    for (let i = 0; i < sections; i++) {
      const speed = i < 17 ? 0.00035 : i < 32 ? 0.00045 : 0.0007;
      diameter[i] += Math.max(-speed, Math.min(speed * 2, targetDiameter[i] - diameter[i]));
    }
  }

  function updateNose(velum) {
    for (let i = 0; i < noseSections; i++) {
      const d = 2 * (i / noseSections);
      let value = i === 0 ? velum : d < 1 ? 0.4 + 1.6 * d : 0.5 + 1.5 * (2 - d);
      noseDiameter[i] = Math.min(value, 1.9);
    }
    for (let i = 1; i < noseSections; i++) {
      const a0 = Math.max(1e-6, noseDiameter[i - 1] * noseDiameter[i - 1]);
      const a1 = Math.max(1e-6, noseDiameter[i] * noseDiameter[i]);
      noseReflection[i] = (a0 - a1) / (a0 + a1);
    }
  }

  function updateReflection() {
    for (let i = 1; i < sections; i++) {
      const a0 = Math.max(1e-6, diameter[i - 1] * diameter[i - 1]);
      const a1 = Math.max(1e-6, diameter[i] * diameter[i]);
      reflection[i] = ((a0 - a1) / (a0 + a1)) * 0.74;
    }
  }

  function glottis(s) {
    glottalPhase += s.frequency / sampleRate;
    glottalPhase -= Math.floor(glottalPhase);
    const t = glottalPhase;
    const tenseness = Math.max(0, Math.min(1, s.tenseness));
    const openPhase = 0.55 + 0.32 * (1 - tenseness);
    const pulse = t < openPhase
      ? Math.sin(Math.PI * t / openPhase)
      : -0.28 * Math.sin(Math.PI * (t - openPhase) / (1 - openPhase));
    const harmonicBite = (0.12 + tenseness * 0.62) * Math.sin(4 * Math.PI * t);
    const aspiration = rand() * s.intensity * (1 - Math.sqrt(tenseness)) * 0.18;
    return (pulse - harmonicBite * 0.72 + aspiration) * s.intensity * (0.16 + 0.34 * Math.pow(tenseness, 0.35));
  }

  function injectTurbulence(s) {
    const thinness = Math.max(0, Math.min(1, 8 * (0.7 - s.constrictionDiameter)));
    const openness = Math.max(0, Math.min(1, 30 * (s.constrictionDiameter - 0.3)));
    const frontLift = 0.35 + 0.65 * Math.max(0, Math.min(1, s.constrictionIndex / 44));
    const pressure = Math.max(s.turbulence, s.burst);
    const amount = rand() * s.turbulence * pressure * thinness * openness * s.intensity * frontLift * 1.8;
    const i = Math.floor(s.constrictionIndex);
    const delta = s.constrictionIndex - i;
    if (i + 1 < sections) {
      right[i + 1] += amount * (1 - delta) * 0.5;
      left[i + 1] += amount * (1 - delta) * 0.5;
    }
    if (i + 2 < sections) {
      right[i + 2] += amount * delta * 0.5;
      left[i + 2] += amount * delta * 0.5;
    }
  }

  function step(input, s) {
    injectTurbulence(s);
    junctionRight[0] = left[0] * s.glottalReflection * 0.72 + input;
    junctionLeft[sections] = right[sections - 1] * s.lipReflection * 0.72;

    for (let i = 1; i < sections; i++) {
      const w = reflection[i] * (right[i - 1] + left[i]);
      junctionRight[i] = right[i - 1] - w;
      junctionLeft[i] = left[i] + w;
    }

    const noseStart = sections - noseSections + 1;
    const velumArea = Math.max(1e-6, s.velum * s.velum);
    const leftArea = Math.max(1e-6, diameter[noseStart] * diameter[noseStart]);
    const rightArea = Math.max(1e-6, diameter[noseStart + 1] * diameter[noseStart + 1]);
    const sum = leftArea + rightArea + velumArea;
    const rl = (2 * leftArea - sum) / sum;
    const rr = (2 * rightArea - sum) / sum;
    const rn = (2 * velumArea - sum) / sum;
    junctionLeft[noseStart] = rl * right[noseStart - 1] + (1 + rl) * (noseLeft[0] + left[noseStart]);
    junctionRight[noseStart] = rr * left[noseStart] + (1 + rr) * (right[noseStart - 1] + noseLeft[0]);
    noseJunctionRight[0] = rn * noseLeft[0] + (1 + rn) * (left[noseStart] + right[noseStart - 1]);

    for (let i = 0; i < sections; i++) {
      right[i] = junctionRight[i] * 0.985;
      left[i] = junctionLeft[i + 1] * 0.985;
    }
    const lipOutput = right[sections - 1];

    noseJunctionLeft[noseSections] = noseRight[noseSections - 1] * s.lipReflection * 0.68;
    for (let i = 1; i < noseSections; i++) {
      const w = noseReflection[i] * (noseRight[i - 1] + noseLeft[i]);
      noseJunctionRight[i] = noseRight[i - 1] - w;
      noseJunctionLeft[i] = noseLeft[i] + w;
    }
    for (let i = 0; i < noseSections; i++) {
      noseRight[i] = noseJunctionRight[i] * 0.982;
      noseLeft[i] = noseJunctionLeft[i + 1] * 0.982;
    }

    return lipOutput * (0.85 + s.lipOpening * 0.28) + noseRight[noseSections - 1] * Math.max(0, Math.min(1, s.velum / 0.4));
  }

  function updateTransient(s) {
    const opening = s.constrictionDiameter - lastConstrictionDiameter;
    if (opening > 0.18 && lastConstrictionDiameter < 0.28) {
      transient += opening * s.intensity * (0.18 + s.turbulence * 0.42) * s.burst;
    }
    lastConstrictionDiameter = s.constrictionDiameter;
  }

  function condition(output) {
    const blocked = output - dcBlockX + 0.995 * dcBlockY;
    dcBlockX = output;
    dcBlockY = blocked;
    return Math.tanh(blocked * 0.85);
  }

  function resonator(input, hz, radius, a, b) {
    const omega = 2 * Math.PI * Math.max(40, Math.min(sampleRate * 0.45, hz)) / sampleRate;
    const next = input + 2 * radius * Math.cos(omega) * a - radius * radius * b;
    return [next, a];
  }

  function tractColor(input, s) {
    const tongue = Math.max(0, Math.min(1, (s.tongueIndex - 8) / 26));
    const tongueOpen = Math.max(0, Math.min(1, s.tongueDiameter / 3.4));
    const lip = Math.max(0, Math.min(1, (s.lipOpening - 0.35) / 2.15));
    const constrict = Math.max(0, Math.min(1, (1.2 - s.constrictionDiameter) / 1.2));
    const velum = Math.max(0, Math.min(1, (s.velum - 0.01) / 0.39));

    const f1 = 260 + tongueOpen * 520 - (1 - lip) * 140 - constrict * 90;
    const f2 = 760 + tongue * 1500 + (1 - tongueOpen) * 380 - (1 - lip) * 260;
    const f3 = 1800 + tongue * 1100 + constrict * 1600;
    const nasalF = 240 + velum * 460;

    [low1, low2] = resonator(input, f1, 0.985, low1, low2);
    [mid1, mid2] = resonator(input, f2, 0.972, mid1, mid2);
    [high1, high2] = resonator(input, f3, 0.948, high1, high2);
    [nasal1, nasal2] = resonator(input, nasalF, 0.986, nasal1, nasal2);

    const vowel = low1 * (0.22 + tongueOpen * 0.16) + mid1 * (0.14 + tongue * 0.16) + high1 * constrict * 0.06;
    const nasal = nasal1 * velum * 0.16;
    return input * 0.22 + vowel + nasal;
  }

  return {
    render(output, s) {
      updateTargets(s);
      updateNose(s.velum);
      updateReflection();
      updateTransient(s);
      for (let i = 0; i < output.length; i++) {
        slewDiameters();
        transient *= 0.995;
        const g = glottis(s) + rand() * transient;
        const a = step(g, s);
        const b = step(g, s);
        output[i] = condition(tractColor((a + b) * 0.18, s));
      }
    }
  };
}

function draw() {
  const width = canvas.width;
  const height = canvas.height;
  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = "#111714";
  ctx.fillRect(0, 0, width, height);

  const cx = width * 0.5;
  const cy = height * 0.54;
  phase += 0.018 + state.intensity * 0.02;

  drawGrid(width, height);
  drawTract(cx, cy, width * 0.76, height * 0.42);
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

function drawTract(cx, cy, length, scale) {
  const pointsTop = [];
  const pointsBottom = [];
  const samples = 72;
  for (let i = 0; i <= samples; i++) {
    const cell = i / samples * 44;
    const u = i / samples;
    const x = cx - length / 2 + u * length;
    let diameter = cell < 7 ? 0.6 : cell < 10 ? 1.1 : 1.5;
    if (cell > 10 && cell < 39) {
      const angle = 1.1 * Math.PI * (state.tongueIndex - cell) / 22;
      const fixedTongueDiameter = 2 + (state.tongueDiameter - 2) / 1.5;
      diameter = 1.5 - (1.5 - fixedTongueDiameter + 1.7) * Math.cos(angle);
    }
    const distance = Math.abs(cell - state.constrictionIndex);
    const range = state.constrictionIndex < 25 ? 10 : 5;
    if (distance < range) {
      const narrowed = Math.max(0, state.constrictionDiameter - 0.3);
      const mix = 0.5 * (1 + Math.cos(Math.PI * distance / range));
      diameter = Math.min(diameter, diameter * (1 - mix) + narrowed * mix);
    }
    if (i === samples) diameter = state.lipOpening;
    const radius = Math.max(0.04, diameter / 3.2);
    pointsTop.push([x, cy - radius * scale * 0.5]);
    pointsBottom.push([x, cy + radius * scale * 0.5]);
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

  drawSource(cx - length * 0.52, cy, scale);
  drawLips(cx + length * 0.52, cy, scale);
  drawNasalBranch(cx + length * 0.20, cy, scale, state.velum);
  drawConstriction(cx - length / 2 + (state.constrictionIndex / 44) * length, cy, scale);
  drawWave(cx, cy, length, scale);
}

function drawSource(x, y, scale) {
  const height = scale * (0.20 + state.tenseness * 0.26);
  ctx.fillStyle = "#f47f67";
  ctx.fillRect(x - 18, y - height / 2, 10, height);
  ctx.fillRect(x + 8, y - height / 2, 10, height);
  ctx.fillStyle = "rgba(244, 127, 103, 0.25)";
  ctx.beginPath();
  ctx.arc(x, y, 24 + state.intensity * 28, 0, Math.PI * 2);
  ctx.fill();
}

function drawLips(x, y, scale) {
  const aperture = 8 + state.lipOpening * 24;
  ctx.strokeStyle = "#7fd18b";
  ctx.lineWidth = 5;
  ctx.beginPath();
  ctx.ellipse(x, y, 28, aperture, 0, 0, Math.PI * 2);
  ctx.stroke();
}

function drawNasalBranch(x, y, scale, velum) {
  const open = (velum - 0.01) / 0.39;
  ctx.strokeStyle = `rgba(118, 199, 212, ${0.18 + open * 0.78})`;
  ctx.lineWidth = 5 + open * 14;
  ctx.beginPath();
  ctx.moveTo(x, y - scale * 0.16);
  ctx.bezierCurveTo(x + scale * 0.12, y - scale * 0.55, x + scale * 0.40, y - scale * 0.62, x + scale * 0.60, y - scale * 0.38);
  ctx.stroke();
}

function drawConstriction(x, y, scale) {
  const close = Math.max(0, Math.min(1, (1.2 - state.constrictionDiameter) / 1.2));
  ctx.strokeStyle = `rgba(242, 184, 75, ${0.25 + close * 0.7})`;
  ctx.lineWidth = 3 + close * 9;
  ctx.beginPath();
  ctx.moveTo(x, y - scale * 0.28);
  ctx.lineTo(x, y + scale * 0.28);
  ctx.stroke();
}

function drawWave(cx, cy, length, scale) {
  ctx.strokeStyle = "rgba(240, 244, 237, 0.45)";
  ctx.lineWidth = 2;
  ctx.beginPath();
  for (let i = 0; i <= 160; i++) {
    const u = i / 160;
    const x = cx - length / 2 + u * length;
    const amp = (0.10 + state.intensity * 0.22) * scale * (1 - state.turbulence * 0.25);
    const y = cy + Math.sin(u * 18 + phase) * amp * Math.sin(Math.PI * u);
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.stroke();
}

function drawReadout(width, height) {
  ctx.fillStyle = "#f0f4ed";
  ctx.font = "700 22px system-ui, sans-serif";
  ctx.fillText(`tract graph witness freq=${state.frequency.toFixed(1)}Hz tense=${state.tenseness.toFixed(2)} velum=${state.velum.toFixed(2)} burst=${state.burst.toFixed(2)}`, 28, 42);
  ctx.fillStyle = "#a8b3a3";
  ctx.font = "15px system-ui, sans-serif";
  ctx.fillText("Aqua DSL tract voice controls: source, tongue, velum, constriction, turbulence, radiation", 28, height - 24);
}

document.querySelectorAll("[data-preset]").forEach(button => {
  button.addEventListener("click", () => applyPreset(button.dataset.preset));
});

playButton.addEventListener("click", async () => {
  if (audio) await stopAudio();
  else await startAudio();
});

panicButton.addEventListener("click", stopAudio);

makeControls();
cancelAnimationFrame(animationFrame);
draw();

window.AquaTractPlayground = {
  state,
  presets,
  createTractSynth
};
