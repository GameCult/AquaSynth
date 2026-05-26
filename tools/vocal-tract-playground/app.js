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
  let glottalPhase = 0;
  let noise = 0.1234567;
  let lastConstrictionDiameter = 1.0;
  let transient = 0;
  let dcBlockX = 0;
  let dcBlockY = 0;
  const breathMemory = { x: 0, y: 0 };
  const fricMemory = { x: 0, y: 0 };
  const f1 = makeBandpass();
  const f2 = makeBandpass();
  const f3 = makeBandpass();
  const nasal = makeBandpass();
  const fric = makeBandpass();

  function rand() {
    noise = (noise * 16807) % 2147483647;
    return noise / 1073741823.5 - 1;
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  function makeBandpass() {
    let b0 = 0;
    let b1 = 0;
    let b2 = 0;
    let a1 = 0;
    let a2 = 0;
    let x1 = 0;
    let x2 = 0;
    let y1 = 0;
    let y2 = 0;
    return {
      set(hz, q) {
        const omega = 2 * Math.PI * clamp(hz, 40, sampleRate * 0.45) / sampleRate;
        const sin = Math.sin(omega);
        const alpha = sin / (2 * clamp(q, 0.2, 40));
        const cos = Math.cos(omega);
        const a0 = 1 + alpha;
        b0 = alpha / a0;
        b1 = 0;
        b2 = -alpha / a0;
        a1 = (-2 * cos) / a0;
        a2 = (1 - alpha) / a0;
      },
      process(x) {
        const y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
        x2 = x1;
        x1 = x;
        y2 = y1;
        y1 = y;
        return y;
      }
    };
  }

  function highpass(input, hz, memory) {
    const alpha = 1 / (1 + 2 * Math.PI * hz / sampleRate);
    const y = alpha * (memory.y + input - memory.x);
    memory.x = input;
    memory.y = y;
    return y;
  }

  function glottis(s) {
    glottalPhase += s.frequency / sampleRate;
    glottalPhase -= Math.floor(glottalPhase);
    const phase = glottalPhase;
    const tenseness = clamp(s.tenseness, 0, 1);
    const openPhase = 0.42 + (1 - tenseness) * 0.36;
    const glottalBoundary = clamp((s.glottalReflection + 0.95) / 1.9, 0, 1);
    const pulse = phase < openPhase
      ? Math.sin(Math.PI * phase / openPhase)
      : -0.22 * Math.sin(Math.PI * (phase - openPhase) / Math.max(0.001, 1 - openPhase));
    const bite = Math.sin(4 * Math.PI * phase) * (0.04 + tenseness * 0.20 + glottalBoundary * 0.10);
    return (pulse * (0.72 + glottalBoundary * 0.42) - bite) * s.intensity * (0.13 + tenseness * 0.25);
  }

  function updateTransient(s) {
    const opening = s.constrictionDiameter - lastConstrictionDiameter;
    if (opening > 0.18 && lastConstrictionDiameter < 0.28) {
      transient += opening * s.intensity * (0.18 + s.turbulence * 0.42) * s.burst;
    }
    lastConstrictionDiameter = s.constrictionDiameter;
  }

  function formants(s) {
    const tongue = clamp((s.tongueIndex - 8) / 26, 0, 1);
    const tongueOpen = clamp((s.tongueDiameter - 0.45) / 3.15, 0, 1);
    const lip = clamp((s.lipOpening - 0.35) / 2.15, 0, 1);
    const lipReflection = clamp((-s.lipReflection - 0.1) / 0.88, 0, 1);
    const constrict = clamp((1.15 - s.constrictionDiameter) / 1.15, 0, 1);
    return {
      f1: 240 + tongueOpen * 620 + lip * 80 - constrict * 130,
      f2: 650 + tongue * 1850 + (1 - tongueOpen) * 320 - (1 - lip) * 320 - lipReflection * 120,
      f3: 1850 + tongue * 900 + constrict * 1900 - lipReflection * 260,
      nasal: 240 + clamp(s.velum / 0.4, 0, 1) * 420,
      fric: 1400 + clamp(s.constrictionIndex / 44, 0, 1) * 5200,
      radiation: 0.72 + (1 - lipReflection) * 0.46
    };
  }

  function condition(output) {
    const blocked = output - dcBlockX + 0.995 * dcBlockY;
    dcBlockX = output;
    dcBlockY = blocked;
    return Math.tanh(blocked * 1.15) * 0.72;
  }

  return {
    render(output, s) {
      updateTransient(s);
      for (let i = 0; i < output.length; i++) {
        transient *= 0.995;
        const shape = formants(s);
        const velum = clamp((s.velum - 0.01) / 0.39, 0, 1);
        const constrict = clamp((1.15 - s.constrictionDiameter) / 1.15, 0, 1);
        const openness = clamp((s.constrictionDiameter - 0.18) / 0.9, 0, 1);
        const source = glottis(s);
        const white = rand();
        const breath = highpass(white, 900, breathMemory);
        const fricNoise = highpass(white, 2200, fricMemory);
        const frication = fricNoise * s.turbulence * constrict * openness * (0.35 + s.intensity * 0.65);
        const burst = rand() * transient;

        f1.set(shape.f1, 8 + s.tenseness * 8);
        f2.set(shape.f2, 6 + s.tenseness * 6);
        f3.set(shape.f3, 5 + constrict * 8);
        nasal.set(shape.nasal, 5 + velum * 8);
        fric.set(shape.fric, 2.5 + constrict * 8);

        const vowel = f1.process(source) * 1.8 + f2.process(source) * 1.25 + f3.process(source) * 0.65 * shape.radiation;
        const air = breath * (1 - Math.sqrt(clamp(s.tenseness, 0, 1))) * s.intensity * 0.16;
        const hiss = fric.process(frication + burst * 0.6) * 2.3;
        const nose = nasal.process(source + air) * velum * 0.9;
        output[i] = condition((vowel * (1 - velum * 0.28) + nose + hiss * shape.radiation + air) * 0.42);
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
  ctx.fillText(`source/filter witness freq=${state.frequency.toFixed(1)}Hz tense=${state.tenseness.toFixed(2)} velum=${state.velum.toFixed(2)} burst=${state.burst.toFixed(2)}`, 28, 42);
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
