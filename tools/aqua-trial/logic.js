// Trial logic shared by the browser page (web/trial.js) and the node tests: variants, the blinded pair design,
// render statistics, one-word validation and the export document. No DOM, no Faust import; the renderer is passed in.

export const SCHEMA = "aquasynth.listening-trial.v1";
export const PATCH = "thruster-trial";
export const RPM = 600;
export const NOISE_PERCENTS = [100, 50, 20, 0];
export const SECONDS = 4;

// mix 0 is the current thruster.dsp; mix 1 is the v5 mix as cut at c6561da (before aqua-feel's 2026-11-05 edit). The four
// noise levels run on the current mix; the v5 mix is added at noise 100 so the noise sweep has the old mix to compare against.
export const variants = () => [
  ...NOISE_PERCENTS.map((noise) => ({ id: `noise${noise}`, params: { rpm: RPM, noise, mix: 0 } })),
  { id: "v5noise100", params: { rpm: RPM, noise: 100, mix: 1 } },
];

// mulberry32: small seeded PRNG so a session (and its tests) can be replayed from the seed.
export function rng(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function shuffle(items, rand) {
  const a = [...items];
  for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(rand() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; }
  return a;
}

// Every unordered pair of variants once, pair order shuffled, A/B assignment coin-flipped per pair.
// Each variant therefore appears in n-1 trials, and which one is A is independent of which one is quieter.
export function makeDesign(seed, vs = variants()) {
  const rand = rng(seed);
  const pairs = [];
  for (let i = 0; i < vs.length; i++) for (let j = i + 1; j < vs.length; j++) pairs.push(rand() < 0.5 ? [vs[i], vs[j]] : [vs[j], vs[i]]);
  return shuffle(pairs, rand).map(([a, b]) => ({ A: a, B: b }));
}

export function stats(samples) {
  let peak = 0, sq = 0, bad = 0;
  for (const v of samples) { if (!Number.isFinite(v)) { bad++; continue; } peak = Math.max(peak, Math.abs(v)); sq += v * v; }
  return { peak, rms: Math.sqrt(sq / Math.max(1, samples.length - bad)), nonFinite: bad };
}

// `generator` is a compiled faustwasm FaustMonoDspGenerator; `controls` are controlsOf(...) of its JSON.
export async function renderVariant(generator, controls, variant, sampleRate, seconds = SECONDS) {
  const p = await generator.createOfflineProcessor(sampleRate, 512);
  for (const [label, value] of Object.entries(variant.params)) {
    const c = controls.find((x) => x.label === label);
    if (!c) throw new Error(`patch has no control "${label}"`);
    p.setParamValue(c.address, value);
  }
  const samples = p.render(null, Math.round(sampleRate * seconds))[0];
  return { samples, ...stats(samples) };
}

// One free word: trimmed, no inner whitespace, bounded. Returns the word or null.
export function cleanWord(text) {
  const w = String(text ?? "").trim();
  return w.length > 0 && w.length <= 32 && !/\s/.test(w) ? w : null;
}

export const validRating = (n) => Number.isInteger(n) && n >= 1 && n <= 5;

// answers: [{ A: {rating, word, time, plays}, B: {...} }] aligned with design.
export function buildExport({ seed, design, renders, answers, sampleRate, userAgent, exportedAt }) {
  const records = [];
  design.forEach((trial, t) => {
    for (const pos of ["A", "B"]) {
      const a = answers[t]?.[pos];
      if (!a) continue;
      const v = trial[pos];
      records.push({
        trial: t + 1, position: pos, patch: PATCH, variant: v.id, params: v.params,
        peak: renders[v.id].peak, rms: renders[v.id].rms,
        rating: a.rating, word: a.word, time: a.time, plays: a.plays,
      });
    }
  });
  return { schema: SCHEMA, patch: PATCH, seed, sampleRate, userAgent, exportedAt, seconds: SECONDS, records };
}
